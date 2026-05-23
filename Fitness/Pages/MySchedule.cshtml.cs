using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

public class MyScheduleModel : PageModel
{
    private readonly FitnessDbContext _context;

    public MyScheduleModel(FitnessDbContext context)
    {
        _context = context;
    }

    public List<PurchasedWorkout> PurchasedWorkouts { get; set; } = new();

    public async Task OnGetAsync()
    {
        // �������� ������������� �������� ������������
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userId, out var parsedUserId))
        {
            PurchasedWorkouts = new List<PurchasedWorkout>();
            return;
        }

        // ��������� ��� ����������, �� ������� ������� ������������
        PurchasedWorkouts = await _context.PurchasedWorkouts
            .Where(pw => pw.UserId == parsedUserId)
            .Include(pw => pw.Workout)  // �������� ��������� ������ � ����������
            .OrderBy(pw => pw.Workout.StartTime)
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostCancelAsync(int id)
    {
        // ���� ������ � PurchasedWorkouts �� ID
        var purchasedWorkout = await _context.PurchasedWorkouts
            .FirstOrDefaultAsync(pw => pw.Id == id);

        if (purchasedWorkout != null)
        {
            // ������� ������ � ����������
            _context.PurchasedWorkouts.Remove(purchasedWorkout);
            await _context.SaveChangesAsync();
        }

        return RedirectToPage(); // ����� �������� ������������ �� �������� ����������
    }
}
