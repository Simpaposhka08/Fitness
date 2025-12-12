using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

public class LoginModel : PageModel
{
    private readonly FitnessDbContext _context;

    public LoginModel(FitnessDbContext context)
    {
        _context = context;
    }

    [BindProperty]
    public string PhoneNumber { get; set; }

    [BindProperty]
    public string Password { get; set; }

    public string ErrorMessage { get; set; }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrEmpty(PhoneNumber) || string.IsNullOrEmpty(Password))
        {
            ModelState.AddModelError(string.Empty, "Логин и пароль обязательны");
            return Page();
        }

        // Проверяем, существует ли пользователь с указанным номером телефона
        var user = await _context.Users.FirstOrDefaultAsync(u => u.PhoneNumber == PhoneNumber);
        if (user == null)
        {
            ModelState.AddModelError(string.Empty, "Неверный номер");
            return Page();
        }

        // Проверяем хэш пароля
        if (user.PasswordHash != HashPassword(Password))
        {
            ModelState.AddModelError(string.Empty, "Неверный пароль");
            return Page();
        }

        // Установка куки для аутентификации
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, user.Name),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim("PhoneNumber", user.PhoneNumber),
            new Claim("IsAdmin", user.IsAdmin.ToString()) // Храним роль администратора
        };

        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = true, // Кука будет сохраняться между сессиями
            ExpiresUtc = DateTime.UtcNow.AddDays(1) // Срок действия куки — 1 дней
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(claimsIdentity),
            authProperties);

        return RedirectToPage("/Index");
    }

    private string HashPassword(string password)
    {
        using (var sha256 = SHA256.Create())
        {
            var bytes = Encoding.UTF8.GetBytes(password);
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }
    }
}
