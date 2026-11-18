using ExpenseTracker.Data;
using ExpenseTracker.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

var builder = WebApplication.CreateBuilder(args);

var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
{
    builder.WebHost.UseUrls($"http://+:{port}");
}

builder.Services.AddDbContext<ApplicationDbContext>((services, options) =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("DefaultConnection")
        ?? Environment.GetEnvironmentVariable("MYSQL_URL")
        ?? Environment.GetEnvironmentVariable("DATABASE_URL");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException("Configure ConnectionStrings:DefaultConnection or MYSQL_URL.");
    }

    if (Uri.TryCreate(connectionString, UriKind.Absolute, out var uri) && uri.Scheme == "mysql")
    {
        var credentials = uri.UserInfo.Split(':', 2);
        connectionString = new MySqlConnectionStringBuilder
        {
            Server = uri.Host,
            Port = (uint)(uri.IsDefaultPort ? 3306 : uri.Port),
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            UserID = Uri.UnescapeDataString(credentials[0]),
            Password = credentials.Length == 2 ? Uri.UnescapeDataString(credentials[1]) : ""
        }.ConnectionString;
    }

    options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0)));
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = RegisterInput.MinimumPasswordLength;
}).AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddControllersWithViews(options =>
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddHealthChecks();

var app = builder.Build();

// Validate database configuration during startup without opening a connection.
using (var scope = app.Services.CreateScope())
{
    _ = scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

public partial class Program { }
