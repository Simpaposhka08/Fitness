using System.ComponentModel.DataAnnotations;

public class Workout
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Название обязательно")]
    [StringLength(100, ErrorMessage = "Название не может быть длиннее 100 символов")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Описания обязательно")]
    [StringLength(500, ErrorMessage = "Описание не может быть длиннее 500 символов")]
    public string Description { get; set; } = string.Empty;

    [Required(ErrorMessage = "Цена обязательна")]
    [Range(0, 10000, ErrorMessage = "Цена должна быть от 0 до 10 000")]
    public decimal Price { get; set; }

    [Required(ErrorMessage = "Тренер обязателен")]
    public string Trainer { get; set; } = string.Empty;

    [Required(ErrorMessage = "Дата начала обязательна")]
    public DateTime StartTime { get; set; }

    [Required(ErrorMessage = "Дата окончания обязательна")]
    [DataType(DataType.DateTime, ErrorMessage = "Некорректная дата")]
    public DateTime EndTime { get; set; }

    // Slug генерируется автоматически, если не указан вручную
    [StringLength(100, ErrorMessage = "Название на английском не может быть длиннее 100 символов")]
    public string Slug { get; set; } = string.Empty; // Название тренировки на английском языке, например, "yoga", "basketball-dribbling"

    // Ссылка на видео (необязательное поле)
    public string? VideoUrl { get; set; } // Ссылка на видео тренировки (например, на YouTube)
}
