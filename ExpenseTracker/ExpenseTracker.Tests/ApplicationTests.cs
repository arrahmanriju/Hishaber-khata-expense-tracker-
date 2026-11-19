using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace ExpenseTracker.Tests;

public sealed class ApplicationTests(PostgreSqlFixture app) : IClassFixture<PostgreSqlFixture>
{
    private static readonly string Password = CreatePassword();
    private static readonly DateTime ReportDate = DateTime.Today;
    private static string Period => $"month={ReportDate.Month}&year={ReportDate.Year}";
    private static string Monthly => $"/MonthlyOverview?{Period}";

    [Fact]
    public async Task AccountFlowProtectsAppAndAllowsAnonymousHealthCheck()
    {
        using var client = NewClient();
        await VerifyStatus(client.GetAsync("/health"), HttpStatusCode.OK);
        foreach (var path in new[] { "/", "/Income", "/Expense/AddExpense", Monthly, "/Settings",
                     $"/MonthlyOverview/ExportToExcel?{Period}" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.StartsWith("/Account/Login", new Uri(client.BaseAddress!, response.Headers.Location!).AbsolutePath);
        }

        var email = await Register(client);
        using (var overview = await VerifyStatus(client.GetAsync("/"), HttpStatusCode.OK))
        {
            var html = await overview.Content.ReadAsStringAsync();
            Assert.Contains("class=\"account-menu\"", html);
            Assert.Contains(email, html);
        }
        await VerifyStatus(client.GetAsync("/Account/Logout"), HttpStatusCode.MethodNotAllowed);
        await VerifyStatus(client.PostAsync("/Account/Logout", null), HttpStatusCode.BadRequest);
        await VerifyStatus(Post(client, "/Account/Logout", "/"), HttpStatusCode.Redirect);
        await VerifyStatus(client.GetAsync("/"), HttpStatusCode.Redirect);

        using var invalidLogin = await VerifyStatus(Post(client, "/Account/Login", "/Account/Login",
            ("Email", email), ("Password", CreatePassword())), HttpStatusCode.OK);
        Assert.Contains("Unable to sign in", await invalidLogin.Content.ReadAsStringAsync());
        using var login = await VerifyStatus(Post(client, "/Account/Login", "/Account/Login",
            ("Email", email), ("Password", Password), ("ReturnUrl", "https://example.com")),
            HttpStatusCode.Redirect);
        Assert.Equal("/", login.Headers.Location!.OriginalString);
        var cookie = Assert.Single(SetCookieHeaderValue.ParseList(login.Headers.GetValues("Set-Cookie").ToList()),
            value => value.Name.Value == ".AspNetCore.Identity.Application");
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);
        Assert.Equal(SameSiteMode.Lax, cookie.SameSite);
        await VerifyStatus(client.GetAsync("/"), HttpStatusCode.OK);
    }

    [Fact]
    public async Task RegistrationRejectsInvalidAndDuplicateAccountsAndStoresAPasswordHash()
    {
        using var client = NewClient();
        var email = $"test-{Guid.NewGuid():N}@example.com";
        var fields = new[] { ("Email", email), ("Password", Password), ("ConfirmPassword", Password) };

        using (var noToken = await VerifyStatus(client.PostAsync("/Account/Register", null), HttpStatusCode.BadRequest))
        {
            Assert.Null(noToken.Headers.Location);
        }

        using (var invalid = await VerifyStatus(Post(client, "/Account/Register", "/Account/Register",
            ("Email", "invalid-email"), ("Password", Password), ("ConfirmPassword", CreatePassword())),
            HttpStatusCode.OK))
        {
            var html = await invalid.Content.ReadAsStringAsync();
            Assert.Contains("not a valid e-mail address", html);
            Assert.Contains("do not match", html);
        }

        // Meets the existing complexity rules, but falls short of the new minimum length.
        var shortPassword = $"Aa!{Random.Shared.Next(10000, 99999)}z";
        using (var tooShort = await VerifyStatus(Post(client, "/Account/Register", "/Account/Register",
            ("Email", email), ("Password", shortPassword), ("ConfirmPassword", shortPassword)), HttpStatusCode.OK))
        {
            Assert.Contains("Passwords must be at least 12 characters.", await tooShort.Content.ReadAsStringAsync());
        }

        await using var context = app.CreateContext();
        Assert.False(await context.Users.AnyAsync(user => user.NormalizedEmail == email.ToUpperInvariant()));

        using var registered = await VerifyStatus(
            Post(client, "/Account/Register", "/Account/Register", fields), HttpStatusCode.Redirect);
        using var duplicateClient = NewClient();
        using var duplicate = await VerifyStatus(Post(duplicateClient, "/Account/Register", "/Account/Register",
            ("Email", email.ToUpperInvariant()), ("Password", Password), ("ConfirmPassword", Password)),
            HttpStatusCode.OK);
        Assert.Contains("Unable to register with this email address.", await duplicate.Content.ReadAsStringAsync());

        var account = await context.Users.SingleAsync(user => user.NormalizedEmail == email.ToUpperInvariant());
        Assert.NotNull(account.PasswordHash);
        Assert.NotEqual(Password, account.PasswordHash);
        Assert.Equal(PasswordVerificationResult.Success,
            new PasswordHasher<IdentityUser>().VerifyHashedPassword(account, account.PasswordHash, Password));
    }

    [Fact]
    public async Task RepeatedFailedLoginsLockTheAccount()
    {
        using var owner = NewClient();
        var email = await Register(owner);
        using var attacker = NewClient();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var failed = await VerifyStatus(Post(attacker, "/Account/Login", "/Account/Login",
                ("Email", email), ("Password", CreatePassword())), HttpStatusCode.OK);
            Assert.Contains("Unable to sign in with these credentials", await failed.Content.ReadAsStringAsync());
        }

        using var locked = await VerifyStatus(Post(attacker, "/Account/Login", "/Account/Login",
            ("Email", email), ("Password", Password)), HttpStatusCode.OK);
        Assert.Contains("Unable to sign in with these credentials", await locked.Content.ReadAsStringAsync());
        using var forbidden = await VerifyStatus(attacker.GetAsync("/"), HttpStatusCode.Redirect);

        await using var context = app.CreateContext();
        var account = await context.Users.SingleAsync(user => user.NormalizedEmail == email.ToUpperInvariant());
        Assert.True(account.LockoutEnd > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task OwnersCanEditAndDeleteOnlyTheirEligibleTransactions()
    {
        using var owner = NewClient();
        using var other = NewClient();
        await Register(owner);
        await Register(other);
        const string income = "Phase 1 owner income";
        const string fixedExpense = "Phase 1 owner fixed expense";
        const string regularExpense = "Phase 1 owner regular expense";
        await Create(owner, "/Income/AddIncome", "/Income", "Salary", income);
        var incomeEdit = await EditLink(owner, $"/Income?{Period}");
        await Create(owner, "/Expense/AddExpense", "/Expense/AddExpense", "Food", fixedExpense, isFixed: true);
        var fixedEdit = await EditLink(owner, $"/Expense/FixedExpense?{Period}");
        await Create(owner, "/Expense/AddExpense", "/Expense/AddExpense", "Food", regularExpense);
        var regularEdit = await EditLink(owner, Monthly, regularExpense);
        var deleteIncome = $"/Income/DeleteIncome/{GetId(incomeEdit)}?{Period}";
        var deleteFixed = $"/Expense/DeleteFixedExpense/{GetId(fixedEdit)}?{Period}";

        await VerifyStatus(other.GetAsync(incomeEdit), HttpStatusCode.NotFound);
        await VerifyStatus(Post(other, incomeEdit, "/Income", TransactionFields("Salary", "Changed")),
            HttpStatusCode.NotFound);
        await VerifyStatus(Post(other, deleteIncome, "/Income"), HttpStatusCode.NotFound);
        await VerifyStatus(Post(other, deleteFixed, "/Income"), HttpStatusCode.NotFound);
        await VerifyStatus(owner.GetAsync(deleteIncome), HttpStatusCode.MethodNotAllowed);
        await VerifyStatus(owner.GetAsync(deleteFixed), HttpStatusCode.MethodNotAllowed);
        await VerifyStatus(owner.PostAsync(deleteIncome, null), HttpStatusCode.BadRequest);
        await VerifyStatus(owner.PostAsync(regularEdit, null), HttpStatusCode.BadRequest);
        await VerifyStatus(owner.PostAsync("/Expense/AddExpense", null), HttpStatusCode.BadRequest);
        await VerifyStatus(Post(owner, $"/Income/DeleteIncome/{GetId(fixedEdit)}", "/Income"), HttpStatusCode.NotFound);
        await VerifyStatus(Post(owner, $"/Expense/DeleteFixedExpense/{GetId(regularEdit)}", "/Income"), HttpStatusCode.NotFound);

        await VerifyStatus(Post(owner, incomeEdit, incomeEdit, TransactionFields("Salary", "Edited income")),
            HttpStatusCode.Redirect);
        await VerifyStatus(Post(owner, regularEdit, regularEdit, TransactionFields("Food", "Edited expense")),
            HttpStatusCode.Redirect);
        var html = await owner.GetStringAsync(Monthly);
        Assert.Contains("Edited income", html);
        Assert.Contains("Edited expense", html);

        await VerifyStatus(Post(owner, deleteIncome, "/Income"), HttpStatusCode.Redirect);
        await VerifyStatus(Post(owner, deleteFixed, "/Income"), HttpStatusCode.Redirect);
        await VerifyStatus(owner.GetAsync(incomeEdit), HttpStatusCode.NotFound);
        await VerifyStatus(owner.GetAsync(fixedEdit), HttpStatusCode.NotFound);
        Assert.Contains("Edited expense", await owner.GetStringAsync(Monthly));
    }

    [Fact]
    public async Task ReportsAndSettingsIsolateAccountsAndPreserveLegacyRows()
    {
        using var owner = NewClient();
        using var other = NewClient();
        await Register(owner);
        await Register(other);
        const string description = "Phase 1 owner-only report entry";
        await Create(owner, "/Expense/AddExpense", "/Expense/AddExpense", "Food", description);
        await VerifyStatus(Post(owner, "/Settings/UpdateSettings", "/Settings",
            ("UserName", "Owner-only profile"), ("MonthlyBudget", "500"), ("Currency", "EUR")),
            HttpStatusCode.Redirect);

        var ownerDashboard = await owner.GetStringAsync($"/?{Period}");
        var otherDashboard = await other.GetStringAsync($"/?{Period}");
        Assert.Contains(description, ownerDashboard);
        Assert.DoesNotContain(description, otherDashboard);
        Assert.Equal(42.25m, TrendExpense(ownerDashboard));
        Assert.Equal(0m, TrendExpense(otherDashboard));
        Assert.Contains("Owner-only profile", await owner.GetStringAsync("/Settings"));
        Assert.DoesNotContain("Owner-only profile", await other.GetStringAsync("/Settings"));
        Assert.DoesNotContain("Phase 1 legacy settings", await other.GetStringAsync("/Settings"));

        Assert.Contains(description, await ExportDescriptions(owner));
        var otherExport = await ExportDescriptions(other);
        Assert.DoesNotContain(description, otherExport);
        Assert.DoesNotContain("Phase 1 legacy transaction", otherExport);

        await using var context = app.CreateContext();
        Assert.Empty(await context.Transactions.ToListAsync());
        Assert.Empty(await context.UserSettings.ToListAsync());
        var legacy = await context.Transactions.IgnoreQueryFilters()
            .SingleAsync(t => t.Description == "Phase 1 legacy transaction");
        Assert.Null(legacy.UserId);
        Assert.Equal(9.25m, legacy.Amount);
        Assert.True(legacy.IsFixed);
        var settings = await context.UserSettings.IgnoreQueryFilters()
            .SingleAsync(s => s.UserName == "Phase 1 legacy settings");
        Assert.Null(settings.UserId);
        Assert.Equal(100m, settings.MonthlyBudget);
    }

    [Fact]
    public async Task InvalidFormsAndPeriodsReturnUsableErrors()
    {
        using var client = NewClient();
        await Register(client);
        using var income = await VerifyStatus(Post(client, "/Income/AddIncome", "/Income",
            ("Amount", "-1"), ("Category", "Food"), ("Date", "invalid")), HttpStatusCode.OK);
        Assert.Contains("Choose a supported category", await income.Content.ReadAsStringAsync());
        using var settings = await VerifyStatus(Post(client, "/Settings/UpdateSettings", "/Settings",
            ("UserName", "Invalid"), ("MonthlyBudget", "-1"), ("Currency", "unsupported")), HttpStatusCode.OK);
        Assert.Contains("Choose a supported currency", await settings.Content.ReadAsStringAsync());
        Assert.DoesNotContain("value=\"Invalid\"", await client.GetStringAsync("/Settings"));
        foreach (var path in new[] { "/", "/Income", "/Expense/FixedExpense", "/MonthlyOverview",
                     "/MonthlyOverview/ExportToExcel" })
            await VerifyStatus(client.GetAsync($"{path}?month=13&year={ReportDate.Year}"), HttpStatusCode.BadRequest);
    }

    private static string CreatePassword() => $"{Guid.NewGuid():N}Aa!7";

    private HttpClient NewClient() => app.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
    });

    private static async Task<string> Register(HttpClient client)
    {
        var email = $"test-{Guid.NewGuid():N}@example.com";
        await VerifyStatus(Post(client, "/Account/Register", "/Account/Register",
            ("Email", email), ("Password", Password), ("ConfirmPassword", Password)), HttpStatusCode.Redirect);
        return email;
    }

    private static async Task Create(HttpClient client, string action, string form, string category,
        string description, bool isFixed = false)
    {
        await VerifyStatus(Post(client, action, form, TransactionFields(category, description, isFixed)),
            HttpStatusCode.Redirect);
    }

    private static (string, string)[] TransactionFields(string category, string description, bool isFixed = false) =>
    [
        ("Category", category), ("Amount", "42.25"), ("Description", description),
        ("Date", ReportDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ("IsFixed", isFixed.ToString())
    ];

    private static async Task<HttpResponseMessage> Post(HttpClient client, string action, string formPath,
        params (string Name, string Value)[] fields)
    {
        var html = await client.GetStringAsync(formPath);
        var token = Regex.Match(html, """name="__RequestVerificationToken"[^>]*value="([^"]+)" """.Trim());
        Assert.True(token.Success, $"No form token at {formPath}");
        using var body = new FormUrlEncodedContent(fields
            .Append((Name: "__RequestVerificationToken", Value: WebUtility.HtmlDecode(token.Groups[1].Value)))
            .Select(field => KeyValuePair.Create(field.Name, field.Value)));
        return await client.PostAsync(action, body);
    }

    private static async Task<string> EditLink(HttpClient client, string path, string? description = null)
    {
        var html = await client.GetStringAsync(path);
        if (description is not null)
        {
            var index = html.IndexOf(description, StringComparison.Ordinal);
            Assert.True(index >= 0, "Transaction description must be shown");
            html = html[index..];
        }
        var link = Regex.Match(html, """href="(/Transactions/Edit/\d+)" """.Trim());
        Assert.True(link.Success, $"No edit link at {path}");
        return link.Groups[1].Value;
    }

    private static string GetId(string edit) => edit[(edit.LastIndexOf('/') + 1)..];

    private static decimal TrendExpense(string html)
    {
        var chart = Regex.Match(html, @"const monthlyData = (\[.*?\]);");
        Assert.True(chart.Success, "Dashboard should render the monthly trend data");
        using var data = JsonDocument.Parse(chart.Groups[1].Value);
        var month = data.RootElement.EnumerateArray().Last();
        return month.GetProperty("expense").GetDecimal();
    }

    private static async Task<string[]> ExportDescriptions(HttpClient client)
    {
        using var response = await VerifyStatus(client.GetAsync($"/MonthlyOverview/ExportToExcel?{Period}"),
            HttpStatusCode.OK);
        await using var file = await response.Content.ReadAsStreamAsync();
        using var workbook = new XLWorkbook(file);
        return workbook.Worksheet("All Transactions").RowsUsed().Skip(1)
            .Select(row => row.Cell(4).GetString()).ToArray();
    }

    private static async Task<HttpResponseMessage> VerifyStatus(Task<HttpResponseMessage> request, HttpStatusCode expected)
    {
        var response = await request;
        Assert.Equal(expected, response.StatusCode);
        return response;
    }
}
