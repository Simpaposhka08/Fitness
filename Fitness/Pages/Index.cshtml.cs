using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

public class IndexModel : PageModel
{
    private readonly FitnessDbContext _context;

    public IndexModel(FitnessDbContext context)
    {
        _context = context;
    }

    public List<Workout> Workouts { get; set; } = new List<Workout>();
    public DateTime CurrentDate { get; set; }
    public DateTime MinDate { get; private set; }
    public DateTime MaxDate { get; private set; }
    public bool ShowAddModal { get; private set; }

    [BindProperty]
    public Workout? NewWorkout { get; set; }

    public async Task OnGetAsync(string date)
    {
        // Используем UTC для всех операций с датами
        var todayUtc = DateTime.UtcNow.Date;
        MinDate = todayUtc.AddYears(-5);
        MaxDate = todayUtc.AddYears(5);
        
        // Парсим дату и явно указываем UTC
        if (string.IsNullOrEmpty(date))
        {
            // Если дата не указана, используем сегодняшнюю дату
            CurrentDate = todayUtc;
        }
        else
        {
            var parsedDate = DateTime.Parse(date).Date;
            // Явно указываем, что это UTC дата
            CurrentDate = new DateTime(parsedDate.Year, parsedDate.Month, parsedDate.Day, 0, 0, 0, DateTimeKind.Utc);
        }

        // Ограничиваем дату допустимыми пределами
        if (CurrentDate < MinDate)
        {
            CurrentDate = MinDate;
        }
        else if (CurrentDate > MaxDate)
        {
            CurrentDate = MaxDate;
        }
        
        // Если текущая дата слишком старая (раньше чем 30 дней назад), устанавливаем на сегодня
        // Это предотвращает отображение старых дат, когда нет тренировок
        if (CurrentDate < todayUtc.AddDays(-30))
        {
            CurrentDate = todayUtc;
        }

        var userId = User.Identity?.IsAuthenticated == true ? User.FindFirstValue(ClaimTypes.NameIdentifier) : null;

        // Используем UTC для сравнения с данными из БД
        var nowUtc = DateTime.UtcNow;

        // Сравниваем даты, используя UTC
        var query = _context.Workouts
            .Where(w => w.StartTime.Date == CurrentDate && w.StartTime > nowUtc);

        if (userId != null)
        {
            if (int.TryParse(userId, out var parsedUserId))
            {
                query = query.Where(w => !_context.PurchasedWorkouts
                    .Any(pw => pw.UserId == parsedUserId && pw.WorkoutId == w.Id));
            }
        }

        Workouts = await query
            .OrderBy(w => w.StartTime)
            .ToListAsync();

        // Очищаем ошибки валидации и инициализируем NewWorkout при открытии страницы (не при отправке формы)
        if (Request.Method == "GET")
        {
            ModelState.Clear();
            NewWorkout = new Workout();
        }

        ViewData["ShowAddModal"] = ShowAddModal;
    }

    public async Task<IActionResult> OnPostComeWorkoutAsync(int workoutId)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            TempData["Message"] = "Вы должны войти в систему, чтобы записаться на тренировку.";
            await OnGetAsync(string.Empty);
            return Page();
        }

        var userId = User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(userId) || !int.TryParse(userId, out var parsedUserId))
        {
            TempData["Message"] = "Ошибка аутентификации. Пожалуйста, войдите снова.";
            await OnGetAsync(string.Empty);
            return Page();
        }

        var workout = await _context.Workouts.FindAsync(workoutId);

        if (workout == null)
        {
            TempData["Message"] = "Тренировка не найдена.";
            await OnGetAsync(string.Empty);
            return Page();
        }

        // Сохраняем дату тренировки для обновления списка
        var workoutDate = workout.StartTime.Date.ToString("yyyy-MM-dd");

        // Проверка, не записан ли уже пользователь на эту тренировку
        var existingPurchase = await _context.PurchasedWorkouts
            .FirstOrDefaultAsync(pw => pw.UserId == parsedUserId && pw.WorkoutId == workoutId);

        if (existingPurchase != null)
        {
            TempData["Message"] = "Вы уже записаны на эту тренировку!";
            await OnGetAsync(workoutDate);
            return Page();
        }

        // Проверка, не прошла ли уже тренировка (используем UTC)
        if (workout.StartTime < DateTime.UtcNow)
        {
            TempData["Message"] = "Нельзя записаться на прошедшую тренировку.";
            await OnGetAsync(workoutDate);
            return Page();
        }

        var purchasedWorkout = new PurchasedWorkout
        {
            UserId = parsedUserId,
            WorkoutId = workout.Id,
            PurchaseDate = DateTime.UtcNow
        };

        try
        {
            _context.PurchasedWorkouts.Add(purchasedWorkout);
            await _context.SaveChangesAsync();
            TempData["Message"] = "Вы успешно записались на тренировку!";
            
            // Обновляем список тренировок, чтобы скрыть записанную
            await OnGetAsync(workoutDate);
        }
        catch (Exception ex)
        {
            TempData["Message"] = $"Ошибка при записи на тренировку: {ex.Message}";
            await OnGetAsync(workoutDate);
        }

        return RedirectToPage(new { date = workoutDate });
    }

    public async Task<IActionResult> OnPostAddWorkoutAsync()
    {
        if (User.Identity?.IsAuthenticated != true || User.FindFirst(c => c.Type == "IsAdmin")?.Value != "True")
        {
            TempData["Message"] = "Недостаточно прав для добавления тренировки.";
            return RedirectToPage();
        }

        // Выбираем дату для возврата (если есть введённая тренировка)
        var targetDate = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");
        
        if (NewWorkout == null)
        {
            TempData["Message"] = "Ошибка: данные тренировки не получены.";
            await OnGetAsync(targetDate);
            ShowAddModal = true;
            ViewData["ShowAddModal"] = true;
            return Page();
        }

        // Конвертируем введённое время в UTC перед сохранением
        if (NewWorkout.StartTime.Kind == DateTimeKind.Local || NewWorkout.StartTime.Kind == DateTimeKind.Unspecified)
        {
            NewWorkout.StartTime = NewWorkout.StartTime.ToUniversalTime();
        }
        if (NewWorkout.EndTime.Kind == DateTimeKind.Local || NewWorkout.EndTime.Kind == DateTimeKind.Unspecified)
        {
            NewWorkout.EndTime = NewWorkout.EndTime.ToUniversalTime();
        }
        
        targetDate = NewWorkout.StartTime.Date.ToString("yyyy-MM-dd");

        // Обработка необязательных полей
        if (string.IsNullOrWhiteSpace(NewWorkout.VideoUrl))
        {
            NewWorkout.VideoUrl = null;
        }
        else
        {
            // Валидация URL только если поле заполнено
            if (!Uri.TryCreate(NewWorkout.VideoUrl, UriKind.Absolute, out var uri) || 
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                ModelState.AddModelError("NewWorkout.VideoUrl", "Некорректный URL для видео");
            }
        }

        if (string.IsNullOrWhiteSpace(NewWorkout.Slug))
        {
            NewWorkout.Slug = await GenerateSlugAsync(NewWorkout.Slug, NewWorkout.Title, NewWorkout.StartTime);
        }

        // Проверка времени окончания
        if (NewWorkout.EndTime <= NewWorkout.StartTime)
        {
            ModelState.AddModelError("NewWorkout.EndTime", "Время окончания должно быть позже времени начала");
        }

        // Удаляем VideoUrl и Slug из валидации перед повторной валидацией
        // Это позволяет им быть необязательными
        ModelState.Remove("NewWorkout.VideoUrl");
        ModelState.Remove("NewWorkout.Slug");

        // Повторная валидация модели (VideoUrl и Slug уже удалены из ModelState)
        TryValidateModel(NewWorkout, nameof(NewWorkout));

        if (!ModelState.IsValid)
        {
            await OnGetAsync(targetDate);
            ShowAddModal = true;
            ViewData["ShowAddModal"] = true;
            return Page();
        }

        try
        {
            _context.Workouts.Add(NewWorkout);
            await _context.SaveChangesAsync();
            TempData["Message"] = "Тренировка успешно добавлена!";
            return RedirectToPage(new { date = targetDate });
        }
        catch (Exception ex)
        {
            TempData["Message"] = $"Ошибка при добавлении тренировки: {ex.Message}";
            await OnGetAsync(targetDate);
            ShowAddModal = true;
            ViewData["ShowAddModal"] = true;
            return Page();
        }
    }

    public async Task<IActionResult> OnPostLogoutAsync()
    {
        await HttpContext.SignOutAsync();
        return RedirectToPage("/Index");
    }

    private async Task<string> GenerateSlugAsync(string requestedSlug, string title, DateTime startTime)
    {
        var baseSlug = NormalizeSlug(!string.IsNullOrWhiteSpace(requestedSlug)
            ? requestedSlug
            : $"{title ?? "workout"}-{startTime:yyyyMMddHHmm}");

        if (string.IsNullOrWhiteSpace(baseSlug))
        {
            baseSlug = $"workout-{startTime:yyyyMMddHHmm}";
        }

        var slug = baseSlug;
        var suffix = 1;
        while (await _context.Workouts.AnyAsync(w => w.Slug == slug))
        {
            slug = $"{baseSlug}-{suffix++}";
        }

        return slug;
    }

    private string NormalizeSlug(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.ToLowerInvariant();
        normalized = Regex.Replace(normalized, @"[^a-z0-9]+", "-");
        normalized = normalized.Trim('-');
        return normalized;
    }
}
