using ExpenseTracker.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Xunit;

namespace ExpenseTracker.Tests;

public sealed class MySqlFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MySqlConnectionStringBuilder _connection = new(
        Environment.GetEnvironmentVariable("EXPENSETRACKER_TEST_MYSQL")
        ?? throw new InvalidOperationException("Set EXPENSETRACKER_TEST_MYSQL to an isolated MySQL test server."));

    private readonly string _schema = $"expense_tracker_test_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = ConnectionString(_schema)
            }));
    }

    public async Task InitializeAsync()
    {
        await using var legacy = CreateContext(_schema);
        await legacy.GetService<IMigrator>().MigrateAsync("20260118182451_AddUserSettings");
        await legacy.Database.ExecuteSqlRawAsync("""
            INSERT INTO Transactions (Type, Category, Amount, Description, Date, CreatedAt, IsFixed)
            VALUES ('Expense', 'Food', 9.25, 'Phase 1 legacy transaction', CURRENT_DATE, CURRENT_TIMESTAMP, 1)
            """);
        await legacy.Database.ExecuteSqlRawAsync("""
            INSERT INTO UserSettings (UserName, MonthlyBudget, Currency, CurrencySymbol, UpdatedAt)
            VALUES ('Phase 1 legacy settings', 100, 'USD', '$', CURRENT_TIMESTAMP)
            """);
        await legacy.Database.MigrateAsync();

        await using var fresh = CreateContext(_schema + "_new");
        await fresh.Database.MigrateAsync();
        Assert.Empty(await fresh.Database.GetPendingMigrationsAsync());
        Assert.False(fresh.Database.HasPendingModelChanges());
    }

    Task IAsyncLifetime.DisposeAsync() => base.DisposeAsync().AsTask();

    internal ApplicationDbContext CreateContext() => CreateContext(_schema);

    private ApplicationDbContext CreateContext(string schema) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseMySql(ConnectionString(schema), new MySqlServerVersion(new Version(8, 0)))
            .Options, new Microsoft.AspNetCore.Http.HttpContextAccessor());

    private string ConnectionString(string schema) =>
        new MySqlConnectionStringBuilder(_connection.ConnectionString) { Database = schema }.ConnectionString;
}
