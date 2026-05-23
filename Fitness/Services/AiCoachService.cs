using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Fitness.Services;

// Основной сервис AI-тренера
public class AiCoachService
{
    // Список дней недели на русском (используется для парсинга и сравнения)
    private static readonly string[] RussianWeekdays =
    {
        "понедельник",
        "вторник",
        "среда",
        "четверг",
        "пятница",
        "суббота",
        "воскресенье"
    };

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public AiCoachService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    // Главный метод генерации ответа AI
    public async Task<AiCoachReply> GenerateChatReplyAsync(
        string level,
        int workoutsPerWeek,
        IReadOnlyCollection<WorkoutPromptItem> availableWorkouts,
        IReadOnlyCollection<MlWorkoutScore>? mlRecommendations,
        IReadOnlyCollection<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        // Получение API ключа
        var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")
            ?? _configuration["AiCoach:ApiKey"];

        // Если ключа нет — fallback на локальную логику
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return BuildLocalReply(workoutsPerWeek, availableWorkouts, mlRecommendations, messages);
        }

        // Настройки модели
        var model = _configuration["AiCoach:Model"] ?? "deepseek/deepseek-r1:free";
        var siteUrl = _configuration["AiCoach:SiteUrl"];
        var siteName = _configuration["AiCoach:SiteName"] ?? "Fitness Club";
        var maxTokens = GetMaxTokens();

        // Формируем HTTP-запрос к OpenRouter
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        // Дополнительные заголовки
        if (!string.IsNullOrWhiteSpace(siteUrl))
        {
            request.Headers.Add("HTTP-Referer", siteUrl);
        }

        request.Headers.Add("X-Title", siteName);
        _httpClient.Timeout = TimeSpan.FromSeconds(90);

        // Преобразуем список тренировок в текст для AI
        var workoutsText = string.Join(
            "\n",
            availableWorkouts.Select(w =>
                $"- id={w.Id}; title={w.Title}; trainer={w.Trainer}; date={w.StartTimeLocal:dd.MM.yyyy}; time={w.StartTimeLocal:HH:mm}; weekday={GetRussianDayOfWeek(w.StartTimeLocal)}; full_datetime={w.StartTimeLocal:dd.MM.yyyy HH:mm}; price={w.Price:0.##}"));

        // Подсказка от ML
        var mlHintText = BuildMlHintText(mlRecommendations);

        // Системный prompt для модели
        var systemPrompt = $@"
Ты персональный фитнес-ассистент.
Отвечай только на русском языке.
Веди ответ в формате живого чата.
Используй только тренировки из переданного списка.
Не придумывай тренировки, которых нет в списке.
Учитывай пожелания пользователя буквально.
Если в истории чата есть уточнения, они важнее первоначального сообщения.
Если пользователь запрещает определенные дни недели, ты обязан полностью исключить тренировки в эти дни.
Считай оценки ML-сервиса вспомогательной подсказкой, но не нарушай ограничения пользователя.

Перед тем как вернуть recommendedWorkoutIds, сделай внутреннюю проверку:
1. Нет ли среди выбранных тренировок запрещенных дней.
2. Соответствует ли выбор цели пользователя.
3. Совпадают ли id с реально существующими тренировками из списка.
4. Не предлагай все подряд: выбирай только самые релевантные под запрос и уровень пользователя.

Верни JSON строго по схеме:
{{
  ""message"": ""текст ответа пользователю"",
  ""recommendedWorkoutIds"": [1, 2, 3]
}}

В поле message дай короткий, естественный ответ.
Не перечисляй весь список тренировок и не копируй расписание целиком.
В message указывай максимум 1-2 примера тренировок, даже если рекомендаций больше.
В recommendedWorkoutIds укажи только id тренировок из списка, которые действительно рекомендуешь.
Если не хочешь рекомендовать тренировку, не включай ее id.
Если подходящих вариантов мало, честно скажи об этом в message.
Когда перечисляешь тренировки в message, указывай дату и день недели.
Не оборачивай JSON в markdown.

Уровень подготовки пользователя: {level}
Желаемое количество тренировок в неделю: {workoutsPerWeek}
Сегодняшняя дата: {DateTime.Now:dd.MM.yyyy}

Доступные тренировки из базы:
{workoutsText}

Подсказки от ML-сервиса:
{mlHintText}";

        // Формируем список сообщений для AI
        var chatMessages = new List<object>
        {
            new
            {
                role = "system",
                content = systemPrompt
            }
        };

        chatMessages.AddRange(messages.Select(m => new
        {
            role = m.Role,
            content = m.Content
        }));

        // Тело запроса
        var payload = new
        {
            model,
            messages = chatMessages,
            temperature = 0.2,
            max_tokens = maxTokens,
            response_format = new
            {
                type = "json_object"
            }
        };

        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        // Отправка запроса
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        // Если ошибка — fallback
        if (!response.IsSuccessStatusCode)
        {
            return BuildLocalReply(workoutsPerWeek, availableWorkouts, mlRecommendations, messages);
        }

        JsonDocument jsonDocument;
        try
        {
            jsonDocument = JsonDocument.Parse(responseContent);
        }
        catch
        {
            return BuildLocalReply(workoutsPerWeek, availableWorkouts, mlRecommendations, messages);
        }

        // Парсим ответ модели
        using (jsonDocument)
        {
            if (jsonDocument.RootElement.TryGetProperty("choices", out var choices) &&
                choices.GetArrayLength() > 0 &&
                choices[0].TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var contentElement))
            {
                var payloadText = contentElement.GetString() ?? string.Empty;
                AiCoachReply? reply;

                try
                {
                    reply = JsonSerializer.Deserialize<AiCoachReply>(payloadText, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
                catch
                {
                    return BuildLocalReply(workoutsPerWeek, availableWorkouts, mlRecommendations, messages);
                }

                if (reply is not null)
                {
                    // Применяем ограничения по дням
                    var dayConstraints = ExtractDayConstraints(messages);

                    // Объединяем рекомендации AI + ML
                    var preferredIds = MergeMlRecommendations(reply.RecommendedWorkoutIds ?? new List<int>(), mlRecommendations);

                    // Фильтрация
                    var filteredIds = FilterRecommendedIdsByDayConstraints(
                        preferredIds,
                        availableWorkouts,
                        dayConstraints,
                        workoutsPerWeek);

                    // fallback если ничего не осталось
                    if (filteredIds.Count == 0)
                    {
                        filteredIds = availableWorkouts
                            .OrderBy(workout => workout.StartTimeLocal)
                            .Take(Math.Max(1, workoutsPerWeek))
                            .Select(workout => workout.Id)
                            .ToList();
                    }

                    // Финальное сообщение
                    var finalMessage = BuildConsistentAssistantMessage(
                        string.IsNullOrWhiteSpace(reply.Message) ? "Не удалось получить текст ответа." : reply.Message,
                        filteredIds,
                        availableWorkouts,
                        workoutsPerWeek);

                    return new AiCoachReply(
                        finalMessage,
                        filteredIds);
                }
            }
        }

        return BuildLocalReply(workoutsPerWeek, availableWorkouts, mlRecommendations, messages);
    }

    // Получение max tokens из конфига
    private int GetMaxTokens()
    {
        const int defaultMaxTokens = 1200;
        var configuredValue = _configuration["AiCoach:MaxTokens"];

        if (int.TryParse(configuredValue, out var parsed) && parsed > 0)
        {
            return parsed;
        }

        return defaultMaxTokens;
    }

    // Объединение AI и ML рекомендаций
    private static IEnumerable<int> MergeMlRecommendations(
        IEnumerable<int> replyIds,
        IReadOnlyCollection<MlWorkoutScore>? mlRecommendations)
    {
        var result = new List<int>();
        result.AddRange(replyIds);

        if (mlRecommendations is not null)
        {
            result.AddRange(mlRecommendations.OrderByDescending(item => item.Score).Select(item => item.Id));
        }

        return result;
    }

    // Формирование текста подсказки ML
    private static string BuildMlHintText(IReadOnlyCollection<MlWorkoutScore>? mlRecommendations)
    {
        if (mlRecommendations is null || mlRecommendations.Count == 0)
        {
            return "ML-сервис не вернул оценки.";
        }

        return string.Join(
            "\n",
            mlRecommendations
                .OrderByDescending(item => item.Score)
                .Take(5)
                .Select(item => $"- id={item.Id}; score={item.Score:F3}"));
    }

    // Локальная логика без AI
    private static AiCoachReply BuildLocalReply(
        int workoutsPerWeek,
        IReadOnlyCollection<WorkoutPromptItem> availableWorkouts,
        IReadOnlyCollection<MlWorkoutScore>? mlRecommendations,
        IReadOnlyCollection<ChatMessage> messages)
    {
        var dayConstraints = ExtractDayConstraints(messages);

        var orderedIds = (mlRecommendations ?? Array.Empty<MlWorkoutScore>())
            .OrderByDescending(item => item.Score)
            .Select(item => item.Id);

        var filteredIds = FilterRecommendedIdsByDayConstraints(
            orderedIds,
            availableWorkouts,
            dayConstraints,
            workoutsPerWeek);

        var selectedWorkouts = availableWorkouts
            .Where(workout => filteredIds.Contains(workout.Id))
            .OrderBy(workout => workout.StartTimeLocal)
            .ToList();

        if (selectedWorkouts.Count == 0)
        {
            return new AiCoachReply("Пока не вижу подходящих тренировок в ближайшем расписании.", new List<int>());
        }

        var message = "Подобрал варианты по расписанию и оценке локального ML-сервиса:\n"
                      + "Учел ваш запрос и уровень подготовки. Подходящие варианты добавлены в карточки рекомендаций справа.";

        return new AiCoachReply(
            BuildFinalMessage(message, selectedWorkouts.Count, workoutsPerWeek),
            filteredIds);
    }

    // Получение дня недели
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

    // Фильтрация тренировок по ограничениям
    private static List<int> FilterRecommendedIdsByDayConstraints(
        IEnumerable<int> recommendedIds,
        IReadOnlyCollection<WorkoutPromptItem> availableWorkouts,
        DayConstraints constraints,
        int desiredCount)
    {
        var workoutById = availableWorkouts.ToDictionary(w => w.Id);

        var selectedIds = recommendedIds
            .Distinct()
            .Where(id => workoutById.ContainsKey(id))
            .Where(id => IsWorkoutAllowedByConstraints(workoutById[id], constraints))
            .ToList();

        if (selectedIds.Count >= desiredCount)
        {
            return selectedIds.Take(desiredCount).ToList();
        }

        // fallback — добираем из расписания
        var fallbackIds = availableWorkouts
            .Where(w => IsWorkoutAllowedByConstraints(w, constraints))
            .OrderBy(w => w.StartTimeLocal)
            .Select(w => w.Id)
            .Where(id => !selectedIds.Contains(id))
            .Take(Math.Max(0, desiredCount - selectedIds.Count))
            .ToList();

        selectedIds.AddRange(fallbackIds);
        return selectedIds;
    }

    // Проверка допустимости дня
    private static bool IsWorkoutAllowedByConstraints(WorkoutPromptItem workout, DayConstraints constraints)
    {
        var weekday = GetRussianDayOfWeek(workout.StartTimeLocal);

        if (constraints.AllowedDays.Count > 0)
        {
            return constraints.AllowedDays.Contains(weekday);
        }

        if (constraints.BlockedDays.Count > 0)
        {
            return !constraints.BlockedDays.Contains(weekday);
        }

        return true;
    }

    // Извлечение ограничений по дням из сообщений
    private static DayConstraints ExtractDayConstraints(IReadOnlyCollection<ChatMessage> messages)
    {
        var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string>? allowed = null;

        foreach (var message in messages.Where(m => m.Role == "user"))
        {
            var text = Normalize(message.Content);

            if (string.IsNullOrWhiteSpace(text))
                continue;

            var daysInMessage = ExtractMentionedDays(text);

            if (daysInMessage.Count == 0)
                continue;

            var hasOnlyIntent = HasOnlyDayIntent(text);
            var hasExcludeIntent = HasExcludeDayIntent(text);
            var hasCanIntent = HasCanDayIntent(text);
            var hasSpecificDayRequest = HasSpecificDayRequestIntent(text);

            if (hasOnlyIntent)
            {
                allowed = new HashSet<string>(daysInMessage, StringComparer.OrdinalIgnoreCase);
                blocked.Clear();
                continue;
            }

            if (hasSpecificDayRequest && !hasExcludeIntent)
            {
                allowed = new HashSet<string>(daysInMessage, StringComparer.OrdinalIgnoreCase);
                blocked.Clear();
                continue;
            }

            if (hasExcludeIntent && allowed is null)
            {
                foreach (var day in daysInMessage)
                {
                    blocked.Add(day);
                }
            }

            if (hasCanIntent)
            {
                if (allowed is null)
                {
                    allowed = new HashSet<string>(daysInMessage, StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    allowed.UnionWith(daysInMessage);
                }
            }
        }

        return new DayConstraints(
            blocked,
            allowed ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    // Проверки intent'ов через regex
    private static bool HasOnlyDayIntent(string text)
    {
        return Regex.IsMatch(text, @"\b(только|лишь|исключительно)\b", RegexOptions.IgnoreCase)
               || Regex.IsMatch(text, @"\b(могу|получается|удобно)\b.{0,20}\b(только|лишь)\b", RegexOptions.IgnoreCase);
    }

    private static bool HasExcludeDayIntent(string text)
    {
        return Regex.IsMatch(text, @"\b(не могу|не получается|нельзя|неудобно|кроме|исключая|без)\b", RegexOptions.IgnoreCase);
    }

    private static bool HasCanDayIntent(string text)
    {
        return Regex.IsMatch(text, @"\b(могу|получается|удобно|подходит)\b", RegexOptions.IgnoreCase)
               && !Regex.IsMatch(text, @"\bне\b.{0,12}\b(могу|получается|удобно|подходит)\b", RegexOptions.IgnoreCase);
    }

    private static bool HasSpecificDayRequestIntent(string text)
    {
        return Regex.IsMatch(text, @"\b(нужн\w*|хочу|подбери|давай|ищу|нужно)\b", RegexOptions.IgnoreCase)
               && Regex.IsMatch(
                   text,
                   @"\b(в|на)\s+(понедельник\w*|вторник\w*|сред\w*|четверг\w*|пятниц\w*|суббот\w*|воскресень\w*)\b",
                   RegexOptions.IgnoreCase);
    }

    // Извлечение упомянутых дней недели
    private static HashSet<string> ExtractMentionedDays(string text)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var day in RussianWeekdays)
        {
            var stem = day[..Math.Max(3, day.Length - 2)];

            if (Regex.IsMatch(text, $@"\b{Regex.Escape(day)}\w*\b", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(text, $@"\b{Regex.Escape(stem)}\w*\b", RegexOptions.IgnoreCase))
            {
                result.Add(day);
            }
        }

        return result;
    }

    // Нормализация текста
    private static string Normalize(string text)
    {
        return text
            .ToLowerInvariant()
            .Replace('ё', 'е');
    }

    // Добавление информации о нехватке тренировок
    private static string BuildFinalMessage(string baseMessage, int actualCount, int desiredCount)
    {
        if (actualCount >= desiredCount)
        {
            return baseMessage;
        }

        return $"{baseMessage}\n\nПодходящих тренировок в доступном списке: {actualCount} из {desiredCount}.";
    }

    // Финальное сообщение AI
    private static string BuildConsistentAssistantMessage(
        string baseMessage,
        IReadOnlyCollection<int> selectedIds,
        IReadOnlyCollection<WorkoutPromptItem> availableWorkouts,
        int desiredCount)
    {
        var selected = availableWorkouts
            .Where(w => selectedIds.Contains(w.Id))
            .OrderBy(w => w.StartTimeLocal)
            .ToList();

        if (selected.Count == 0)
        {
            return BuildFinalMessage(
                $"{baseMessage}\n\nСейчас в расписании нет подходящих тренировок под ваш запрос.",
                0,
                desiredCount);
        }

        return BuildFinalMessage(baseMessage, selected.Count, desiredCount);
    }
}

// Ограничения по дням
public sealed record DayConstraints(
    HashSet<string> BlockedDays,
    HashSet<string> AllowedDays);

// DTO тренировки для prompt
public sealed record WorkoutPromptItem(
    int Id,
    string Title,
    string Trainer,
    DateTime StartTimeLocal,
    decimal Price);

// Сообщение чата
public sealed record ChatMessage(string Role, string Content);

// Ответ AI
public sealed record AiCoachReply(string Message, List<int> RecommendedWorkoutIds);