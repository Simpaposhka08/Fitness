using System.ComponentModel.DataAnnotations;

public class User
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Имя обязательно")]
    [StringLength(50, ErrorMessage = "Имя не может быть длиннее 50 символов")]
    public string Name { get; set; }

    [Required(ErrorMessage = "Номер телефона обязателен")]
    [RegularExpression(@"^\+?\d{10,15}$", ErrorMessage = "Введите корректный номер телефона")]
    public string PhoneNumber { get; set; }

    [Required(ErrorMessage = "Пароль обязателен")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Пароль должен быть от 6 до 100 символов")]
    public string PasswordHash { get; set; }
    public bool IsAdmin { get; set; } = false;

    // Связь с купленными тренировками
    public ICollection<PurchasedWorkout> PurchasedWorkouts { get; set; }
}
