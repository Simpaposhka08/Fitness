using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Threading.Tasks;
using System;

public class WorkoutModel : PageModel
{
    private readonly FitnessDbContext _context;

    public WorkoutModel(FitnessDbContext context)
    {
        _context = context;
    }

    public Workout? Workout { get; set; }

    public async Task<IActionResult> OnGetAsync(string slug)
    {
        Workout = await _context.Workouts
            .FirstOrDefaultAsync(w => w.Slug == slug);

        if (Workout == null)
        {
            return NotFound();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostComeWorkoutAsync(int workoutId)
    {
        if (!User.Identity.IsAuthenticated)
        {
            TempData["Message"] = "Вы должны войти в систему, чтобы записаться на тренировку.";
            return RedirectToPage("/Login");
        }

        var userId = User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(userId) || !int.TryParse(userId, out var parsedUserId))
        {
            TempData["Message"] = "Ошибка аутентификации. Пожалуйста, войдите снова.";
            return RedirectToPage("/Login");
        }

        var workout = await _context.Workouts.FindAsync(workoutId);

        if (workout == null)
        {
            TempData["Message"] = "Тренировка не найдена.";
            return RedirectToPage("/Index");
        }

        // Проверка, не записан ли уже пользователь на эту тренировку
        var existingPurchase = await _context.PurchasedWorkouts
            .FirstOrDefaultAsync(pw => pw.UserId == parsedUserId && pw.WorkoutId == workoutId);

        if (existingPurchase != null)
        {
            TempData["Message"] = "Вы уже записаны на эту тренировку!";
            return RedirectToPage("/Index");
        }

        // Проверка, не прошла ли уже тренировка (используем UTC)
        if (workout.StartTime < DateTime.UtcNow)
        {
            TempData["Message"] = "Нельзя записаться на прошедшую тренировку.";
            return RedirectToPage("/Index");
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
        }
        catch (Exception ex)
        {
            TempData["Message"] = $"Ошибка при записи на тренировку: {ex.Message}";
        }

        return RedirectToPage("/Index");
    }
}
