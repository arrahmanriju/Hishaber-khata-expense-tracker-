using System.Globalization;
using ExpenseTracker.Data;
using ExpenseTracker.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

var builder = WebApplication.CreateBuilder(args);

var runningOnRender = builder.Configuration.GetValue<bool>("RENDER");
var configuredPort = builder.Configuration["PORT"];
if (runningOnRender || !string.IsNullOrWhiteSpace(configuredPort)
    || string.IsNullOrWhiteSpace(builder.Configuration[WebHostDefaults.ServerUrlsKey]))
{
    var port = 10000;
    if (!string.IsNullOrWhiteSpace(configuredPort)
        && (!int.TryParse(configuredPort, NumberStyles.None, CultureInfo.InvariantCulture, out port)
            || port is < 1 or > 65535))
    {
        throw new InvalidOperationException("PORT must be an integer between 1 and 65535.");
    }

    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

builder.Services.AddDbContext<ApplicationDbContext>((services, options) =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var connection = ReadMySqlConnection(configuration);
    options.UseMySql(connection.ConnectionString, new MySqlServerVersion(new Version(8, 0)));
});
if (builder.Configuration["DataProtection:KeyPath"] is { Length: > 0 } keyPath)
{
    if (!Path.IsPathFullyQualified(keyPath))
    {
        throw new InvalidOperationException("DataProtection:KeyPath must be an absolute path.");
    }

    builder.Services.AddDataProtection()
        .SetApplicationName("HishaberKhata")
        .PersistKeysToFileSystem(new DirectoryInfo(keyPath));
}

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

if (runningOnRender)
{
    // Render terminates HTTPS before forwarding to the container. Restore only the scheme.
    // The container's public HTTP port must be reachable solely through Render's ingress.
    var forwardedHeaders = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedProto,
        ForwardLimit = 1
    };
    forwardedHeaders.KnownNetworks.Clear();
    forwardedHeaders.KnownProxies.Clear();
    app.UseForwardedHeaders(forwardedHeaders);
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

static MySqlConnectionStringBuilder ReadMySqlConnection(IConfiguration configuration)
{
    var value = configuration.GetConnectionString("DefaultConnection")
        ?? configuration["MYSQL_URL"]
        ?? configuration["DATABASE_URL"];
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException("Configure ConnectionStrings:DefaultConnection or MYSQL_URL.");
    }

    MySqlConnectionStringBuilder connection;
    if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "mysql")
    {
        var credentials = uri.UserInfo.Split(':', 2);
        connection = new MySqlConnectionStringBuilder
        {
            Server = uri.Host,
            Port = (uint)(uri.IsDefaultPort ? 3306 : uri.Port),
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            UserID = Uri.UnescapeDataString(credentials[0]),
            Password = credentials.Length == 2 ? Uri.UnescapeDataString(credentials[1]) : ""
        };
    }
    else
    {
        connection = new MySqlConnectionStringBuilder(value);
    }

    var caPath = configuration["Database:CaCertificatePath"];
    if (!string.IsNullOrWhiteSpace(caPath))
    {
        if (!Path.IsPathFullyQualified(caPath) || !File.Exists(caPath))
        {
            throw new InvalidOperationException("Database:CaCertificatePath must point to an existing absolute PEM file.");
        }

        connection.SslCa = caPath;
    }

    if (configuration.GetValue<bool>("RENDER") || configuration.GetValue<bool>("Database:RequireTls")
        || !string.IsNullOrWhiteSpace(caPath))
    {
        connection.SslMode = MySqlSslMode.VerifyFull;
        if (string.IsNullOrWhiteSpace(connection.Database))
        {
            throw new InvalidOperationException("Specify the MySQL database name in the connection string.");
        }
    }

    return connection;
}

public partial class Program { }
