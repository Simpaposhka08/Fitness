using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Fitness.Services;

public class MlRecommendationService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public MlRecommendationService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    // Основной метод: получает список тренировок и возвращает скоринг от ML модели
    public async Task<List<MlWorkoutScore>> PredictAsync(
        string level,
        int workoutsPerWeek,
        IReadOnlyCollection<WorkoutPromptItem> workouts,
        CancellationToken cancellationToken = default)
    {
        // Если тренировок нет — сразу возвращаем пустой список
        if (workouts.Count == 0)
        {
            return new List<MlWorkoutScore>();
        }

        // Если ML отключен — используем fallback алгоритм
        if (!IsMlEnabled())
        {
            return BuildFallbackScores(workouts);
        }

        var baseUrl = _configuration["MlService:BaseUrl"] ?? "http://127.0.0.1:8008";
        var scores = new List<MlWorkoutScore>(workouts.Count);

        // Для каждой тренировки запрашиваем ML предсказание
        foreach (var workout in workouts)
        {
            var score = await TryPredictForWorkoutAsync(
                baseUrl,
                level,
                workoutsPerWeek,
                workout,
                cancellationToken);

            scores.Add(new MlWorkoutScore(workout.Id, score));
        }

        // Сортируем по убыванию "полезности" тренировки
        return scores
            .OrderByDescending(item => item.Score)
            .ToList();
    }

    // Отправка позитивного фидбэка в ML сервис (обучение модели)
    public async Task SendPositiveFeedbackAsync(
        string level,
        int workoutsPerWeek,
        Workout workout,
        CancellationToken cancellationToken = default)
    {
        if (!IsMlEnabled())
        {
            return; // если ML выключен — ничего не отправляем
        }

        var baseUrl = _configuration["MlService:BaseUrl"] ?? "http://127.0.0.1:8008";
        var url = $"{baseUrl.TrimEnd('/')}/feedback";

        // Формируем payload для обучения модели
        var payload = new
        {
            level,
            workouts_per_week = workoutsPerWeek,
            title = workout.Title,
            trainer = workout.Trainer,
            weekday = GetRussianDayOfWeek(workout.StartTime.ToLocalTime()),
            hour = workout.StartTime.ToLocalTime().Hour,
            price = Convert.ToDouble(workout.Price),
            label = 1 // позитивный пример (понравилось)
        };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json")
            };

            _httpClient.Timeout = TimeSpan.FromSeconds(10);

            // Отправляем запрос (best-effort — не критично если упадёт)
            using var response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch
        {
            // Игнорируем ошибки, чтобы не ломать основной флоу приложения
        }
    }

    // Запрос предсказания для одной тренировки
    private async Task<double> TryPredictForWorkoutAsync(
        string baseUrl,
        string level,
        int workoutsPerWeek,
        WorkoutPromptItem workout,
        CancellationToken cancellationToken)
    {
        var url = $"{baseUrl.TrimEnd('/')}/predict";

        // Данные для ML модели
        var payload = new
        {
            level,
            workouts_per_week = workoutsPerWeek,
            title = workout.Title,
            trainer = workout.Trainer,
            weekday = GetRussianDayOfWeek(workout.StartTimeLocal),
            hour = workout.StartTimeLocal.Hour,
            price = Convert.ToDouble(workout.Price)
        };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json")
            };

            _httpClient.Timeout = TimeSpan.FromSeconds(10);

            var response = await _httpClient.SendAsync(request, cancellationToken);

            // Если API упал — используем fallback
            if (!response.IsSuccessStatusCode)
            {
                return BuildFallbackScore(workout);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(json);

            // Достаём score из ответа ML сервиса
            if (document.RootElement.TryGetProperty("score", out var scoreElement) &&
                scoreElement.TryGetDouble(out var scoreValue))
            {
                return NormalizeScore(scoreValue);
            }
        }
        catch
        {
            // Любая ошибка → fallback
        }

        return BuildFallbackScore(workout);
    }

    // Проверка включён ли ML сервис
    private bool IsMlEnabled()
    {
        return bool.TryParse(_configuration["MlService:Enabled"], out var enabled) && enabled;
    }

    // Fallback скоринг для списка тренировок (если ML недоступен)
    private static List<MlWorkoutScore> BuildFallbackScores(IReadOnlyCollection<WorkoutPromptItem> workouts)
    {
        return workouts
            .Select(workout => new MlWorkoutScore(workout.Id, BuildFallbackScore(workout)))
            .OrderByDescending(item => item.Score)
            .ToList();
    }

    // Fallback скоринг для одной тренировки (эвристика)
    private static double BuildFallbackScore(WorkoutPromptItem workout)
    {
        var now = DateTime.Now;

        // Насколько тренировка близка по дате (чем ближе — тем выше score)
        var daysDistance = Math.Abs((workout.StartTimeLocal.Date - now.Date).TotalDays);
        var closenessFactor = Math.Max(0.0, 1.0 - (daysDistance / 7.0));

        // Насколько время близко к "идеальному" (18:00)
        var targetHour = 18.0;
        var hourDistance = Math.Abs(workout.StartTimeLocal.Hour - targetHour);
        var hourFactor = Math.Max(0.0, 1.0 - (hourDistance / 12.0));

        // Чем дешевле — тем лучше
        var priceFactor = Math.Max(0.0, 1.0 - (double)workout.Price / 3000.0);

        return NormalizeScore(closenessFactor * 0.45 + hourFactor * 0.35 + priceFactor * 0.2);
    }

    // Нормализация score в диапазон 0..1
    private static double NormalizeScore(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0.0;
        }

        return Math.Clamp(value, 0.0, 1.0);
    }

    // Перевод даты в русское название дня недели
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
            _ => CultureInfo.InvariantCulture.DateTimeFormat.GetDayName(dateTime.DayOfWeek)
        };
    }
}

// DTO результата скоринга тренировки
public sealed record MlWorkoutScore(int Id, double Score);