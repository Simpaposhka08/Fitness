using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

public class RegisterModel : PageModel
{
    private readonly FitnessDbContext _context;

    public RegisterModel(FitnessDbContext context)
    {
        _context = context;
    }

    [BindProperty]
    [Required(ErrorMessage = "Имя обязательно")]
    public string Name { get; set; }

    [BindProperty]
    [Required(ErrorMessage = "Номер телефона обязателен")]
    [RegularExpression(@"^\+?\d{10,15}$", ErrorMessage = "Введите корректный номер телефона")]
    public string PhoneNumber { get; set; }

    [BindProperty]
    [Required(ErrorMessage = "Пароль обязателен")]  
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Пароль должен быть от 6 до 100 символов")]
    public string Password { get; set; }

    [BindProperty]
    [Required(ErrorMessage = "Подтвердите пароль")]
    [Compare("Password", ErrorMessage = "Пароли не совпадают")]
    public string ConfirmPassword { get; set; }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        // Проверка: существует ли пользователь с таким номером телефона
        var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.PhoneNumber == PhoneNumber);
        if (existingUser != null)
        {
            // Пользователь с таким номером телефона уже существует
            ModelState.AddModelError(string.Empty, "Пользователь с таким номером телефона уже зарегистрирован.");
            return Page();
        }

        // Создание нового пользователя
        var newUser = new User
        {
            Name = Name,
            PhoneNumber = PhoneNumber,
            PasswordHash = HashPassword(Password),
            IsAdmin = false
        };

        // Добавление пользователя в базу данных
        _context.Users.Add(newUser);
        await _context.SaveChangesAsync();

        // Автоматическая авторизация после регистрации
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, newUser.Name),
            new Claim(ClaimTypes.NameIdentifier, newUser.Id.ToString()),
            new Claim("PhoneNumber", newUser.PhoneNumber),
            new Claim("IsAdmin", newUser.IsAdmin.ToString())
        };

        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = true, // Сохранять куки между сессиями
            ExpiresUtc = DateTime.UtcNow.AddDays(1) // Действие куки — 7 дней
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(claimsIdentity),
            authProperties);

        // Перенаправление на главную страницу
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
