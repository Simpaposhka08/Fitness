using System.ComponentModel.DataAnnotations;// для валидации (Required, Range и т.д.)
using System.Security.Claims;// чтобы получить ID пользователя
using System.Text.Json;// для сохранения данных в JSON (в сессии)
using Fitness.Services;// сервисы (AI и ML)
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;// работа с БД

//backend логика страницы AI Coach
public class AICoachModel : PageModel
{   //ключи для хранения данных в браузерной сессии (временная память)
    private const string ChatSessionKey = "ai-coach-chat";
    private const string RecommendedIdsSessionKey = "ai-coach-recommended-ids";
    private const string ProfileSessionKey = "ai-coach-profile";

    // зависимости
    private readonly FitnessDbContext _context;
    private readonly AiCoachService _aiCoachService;
    private readonly MlRecommendationService _mlRecommendationService;

    public AICoachModel(
        FitnessDbContext context,
        AiCoachService aiCoachService,
        MlRecommendationService mlRecommendationService)
    {
        _context = context;
        _aiCoachService = aiCoachService;
        _mlRecommendationService = mlRecommendationService;
    }

    // Данные из формы (сообщение + профиль)
    [BindProperty]
    public ChatInputModel Input { get; set; } = new();

    public string? ErrorMessage { get; set; }// текст ошибки
    public List<ChatMessage> Messages { get; set; } = new();// история чата
    public List<WorkoutCardModel> RecommendedWorkouts { get; set; } = new();// рекомендации

    // При открытии страницы
    public async Task OnGetAsync()
    {
        Messages = LoadMessages();// загружаем чат
        RecommendedWorkouts = await LoadRecommendedWorkoutsAsync();// загружаем тренировки
    }
    // Сброс чата и рекомендаций
    public IActionResult OnPostReset()
    {
        HttpContext.Session.Remove(ChatSessionKey);
        HttpContext.Session.Remove(RecommendedIdsSessionKey);
        HttpContext.Session.Remove(ProfileSessionKey);
        return RedirectToPage();// перезагрузка страницы
    }
    // Запись на тренировку
    public async Task<IActionResult> OnPostEnrollAsync(int workoutId)
    {
        // Если пользователь не вошёл — отправляем на логин
        if (!User.Identity?.IsAuthenticated ?? true)
        {
            TempData["Message"] = "Сначала войдите в аккаунт, чтобы записаться на тренировку.";
            return RedirectToPage("/Login");
        }
        // Получаем ID пользователя
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdValue, out var userId))
        {
            ErrorMessage = "Не удалось определить пользователя.";
            Messages = LoadMessages();
            RecommendedWorkouts = await LoadRecommendedWorkoutsAsync();
            return Page();
        }
        // Проверяем — уже купил?
        var alreadyPurchased = await _context.PurchasedWorkouts
            .AnyAsync(pw => pw.UserId == userId && pw.WorkoutId == workoutId);
        // Если нет — покупаем
        if (!alreadyPurchased)
        {
            var workout = await _context.Workouts.FindAsync(workoutId);

            _context.PurchasedWorkouts.Add(new PurchasedWorkout
            {
                UserId = userId,
                WorkoutId = workoutId,
                PurchaseDate = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
            // Отправляем feedback в ML (это была хорошая рекомендация)
            var profile = LoadProfile();
            if (profile is not null && workout is not null)
            {
                await _mlRecommendationService.SendPositiveFeedbackAsync(
                    profile.Level,
                    profile.WorkoutsPerWeek,
                    workout,
                    HttpContext.RequestAborted);
            }
        }

        return RedirectToPage();
    }
    // Основной метод — когда пользователь отправляет сообщение
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Messages = LoadMessages();// загружаем чат
        var previousMessages = Messages.ToList();
        // Проверяем — изменился ли профиль
        var profileChanged = HasProfileChanged(Input.Level, Input.WorkoutsPerWeek);
        if (profileChanged)
        {
            Messages.Clear();// очищаем чат
            SaveMessages(Messages);
            SaveRecommendedIds(new List<int>());// очищаем рекомендации
        }
        // Берём сообщение пользователя
        var userMessage = (Input.Message ?? string.Empty).Trim();
        // Если сообщение пустое
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            if (profileChanged)
            {
                userMessage = previousMessages
                    .LastOrDefault(m => m.Role == "user")
                    ?.Content
                    ?.Trim() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(userMessage))
                {
                    ModelState.AddModelError(nameof(Input.Message), "Напишите сообщение для AI Coach.");
                }
            }
            else
            {
                ModelState.AddModelError(nameof(Input.Message), "Напишите сообщение для AI Coach.");
            }
        }

        if (!ModelState.IsValid)
        {
            RecommendedWorkouts = await LoadRecommendedWorkoutsAsync();
            return Page();
        }
        // Берём тренировки на ближайшие 7 дней (полная неделя),
        var today = DateTime.UtcNow.Date;
        var planningHorizonDays = 7;
        var horizonEnd = today.AddDays(planningHorizonDays);

        var workouts = await _context.Workouts
            .Where(w => w.StartTime >= today && w.StartTime < horizonEnd)
            .OrderBy(w => w.StartTime)
            .Select(w => new WorkoutPromptItem(
                w.Id,
                w.Title,
                w.Trainer,
                w.StartTime.ToLocalTime(),
                w.Price))
            .ToListAsync(cancellationToken);
        // Если нет тренировок — ошибка
        if (workouts.Count == 0)
        {
            ErrorMessage = $"На ближайшие {planningHorizonDays} дней нет доступных тренировок для анализа.";
            RecommendedWorkouts = await LoadRecommendedWorkoutsAsync();
            return Page();
        }
        // Добавляем сообщение пользователя в чат
        if (profileChanged)
        {
            Messages.Add(new ChatMessage("user", $"Обновил профиль: уровень {Input.Level}, тренировок в неделю {Input.WorkoutsPerWeek}. Пересчитай рекомендации с учетом этих параметров."));
        }

        Messages.Add(new ChatMessage("user", userMessage));

        try
        {
            // ML рекомендует тренировки
            var mlRecommendations = await _mlRecommendationService.PredictAsync(
                Input.Level,
                Input.WorkoutsPerWeek,
                workouts,
                cancellationToken);
            // AI генерирует ответ
            var reply = await _aiCoachService.GenerateChatReplyAsync(
                Input.Level,
                Input.WorkoutsPerWeek,
                workouts,
                mlRecommendations,
                Messages,
                cancellationToken);
            // Добавляем ответ AI
            Messages.Add(new ChatMessage("assistant", reply.Message));
            // Сохраняем всё
            SaveMessages(Messages);
            SaveRecommendedIds(reply.RecommendedWorkoutIds);
            SaveProfile(Input.Level, Input.WorkoutsPerWeek);
            RecommendedWorkouts = await LoadRecommendedWorkoutsAsync();
            Input.Message = string.Empty;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            RecommendedWorkouts = await LoadRecommendedWorkoutsAsync();
        }

        return Page();
    }
    //Работа с Session
    private List<ChatMessage> LoadMessages()
    {
        var json = HttpContext.Session.GetString(ChatSessionKey);
        return string.IsNullOrWhiteSpace(json)
            ? new List<ChatMessage>()
            : JsonSerializer.Deserialize<List<ChatMessage>>(json) ?? new List<ChatMessage>();
    }

    private void SaveMessages(List<ChatMessage> messages)
    {
        HttpContext.Session.SetString(ChatSessionKey, JsonSerializer.Serialize(messages));
    }

    private List<int> LoadRecommendedIds()
    {
        var json = HttpContext.Session.GetString(RecommendedIdsSessionKey);
        return string.IsNullOrWhiteSpace(json)
            ? new List<int>()
            : JsonSerializer.Deserialize<List<int>>(json) ?? new List<int>();
    }

    private void SaveRecommendedIds(List<int> ids)
    {
        HttpContext.Session.SetString(RecommendedIdsSessionKey, JsonSerializer.Serialize(ids));
    }
    // Загружаем тренировки по ID
    private async Task<List<WorkoutCardModel>> LoadRecommendedWorkoutsAsync()
    {
        var ids = LoadRecommendedIds();
        if (ids.Count == 0)
        {
            return new List<WorkoutCardModel>();
        }

        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var parsedUserId = int.TryParse(userIdValue, out var currentUserId) ? currentUserId : (int?)null;

        var workouts = await _context.Workouts
            .Where(w => ids.Contains(w.Id))
            .OrderBy(w => w.StartTime)
            .ToListAsync();

        var purchasedIds = parsedUserId.HasValue
            ? await _context.PurchasedWorkouts
                .Where(pw => pw.UserId == parsedUserId.Value && ids.Contains(pw.WorkoutId))
                .Select(pw => pw.WorkoutId)
                .ToListAsync()
            : new List<int>();

        return workouts
            .Select(w => new WorkoutCardModel
            {
                Id = w.Id,
                Title = w.Title,
                Trainer = w.Trainer,
                StartTime = w.StartTime.ToLocalTime(),
                EndTime = w.EndTime.ToLocalTime(),
                Price = w.Price,
                Description = w.Description,
                IsAlreadyBooked = purchasedIds.Contains(w.Id),
                DayOfWeekLabel = GetRussianDayOfWeek(w.StartTime.ToLocalTime())
            })
            .ToList();
    }
    // Проверка изменения профиля
    private bool HasProfileChanged(string level, int workoutsPerWeek)
    {
        var saved = LoadProfile();
        if (saved is null)
        {
            return false;
        }

        return !string.Equals(saved.Level, level, StringComparison.Ordinal) ||
               saved.WorkoutsPerWeek != workoutsPerWeek;
    }

    private AiCoachProfile? LoadProfile()
    {
        var json = HttpContext.Session.GetString(ProfileSessionKey);
        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<AiCoachProfile>(json);
    }

    private void SaveProfile(string level, int workoutsPerWeek)
    {
        var profile = new AiCoachProfile(level, workoutsPerWeek);
        HttpContext.Session.SetString(ProfileSessionKey, JsonSerializer.Serialize(profile));
    }

    private static string GetRussianDayOfWeek(DateTime dateTime)
    {
        return dateTime.DayOfWeek switch
        {
            DayOfWeek.Monday => "понедельник",
            DayOfWeek.Tuesday => "вторник",
            DayOfWeek.Wednesday => "среда",
            DayOfWeek.Thursday => "четверг",
            DayOfWeek.Friday => "пятница",
            DayOfWeek.Saturday => "суббота",
            DayOfWeek.Sunday => "воскресенье",
            _ => string.Empty
        };
    }

    public class ChatInputModel
    {
        [Display(Name = "Сообщение")]
        [StringLength(700, ErrorMessage = "Сделайте сообщение короче, до 700 символов.")]
        public string Message { get; set; } = string.Empty;

        [Required(ErrorMessage = "Укажите уровень подготовки.")]
        [Display(Name = "Уровень подготовки")]
        public string Level { get; set; } = "Новичок";

        [Range(1, 7, ErrorMessage = "Укажите число от 1 до 7.")]
        [Display(Name = "Сколько тренировок в неделю")]
        public int WorkoutsPerWeek { get; set; } = 3;
    }

    public class WorkoutCardModel
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Trainer { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public decimal Price { get; set; }
        public string Description { get; set; } = string.Empty;
        public bool IsAlreadyBooked { get; set; }
        public string DayOfWeekLabel { get; set; } = string.Empty;
    }
}

public sealed record AiCoachProfile(string Level, int WorkoutsPerWeek);
