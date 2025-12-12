using System;
using System.ComponentModel.DataAnnotations;

public class PurchasedWorkout
{
    public int Id { get; set; }

    // Внешний ключ на пользователя
    [Required]
    public int UserId { get; set; }
    public User User { get; set; }

    // Внешний ключ на тренировку
    [Required]
    public int WorkoutId { get; set; }
    public Workout Workout { get; set; }

    // Дата записи на тренировку
    [Required]
    public DateTime PurchaseDate { get; set; }
}
