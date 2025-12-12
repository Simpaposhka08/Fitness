using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

// ���������� ��������� ���� ������ � �������� �����������
// Подключение базы данных PostgreSQL
builder.Services.AddDbContext<FitnessDbContext>(options => 
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
    .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
               .EnableSensitiveDataLogging());


// ���������� Razor Pages ��� ������ � �����������
builder.Services.AddRazorPages();

// ���������� ��������������
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login";
        options.LogoutPath = "/Logout";
    });

var app = builder.Build();

// ��������� middleware ��� �������������� � �����������
app.UseAuthentication();
app.UseAuthorization();

app.UseRouting();
app.UseStaticFiles();  // �������� ��������� ����������� ������, ����� ��� CSS, JavaScript, �����������.

app.MapRazorPages();

app.Run();
