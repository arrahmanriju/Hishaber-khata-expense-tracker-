using ExpenseTracker.Data;
using ExpenseTracker.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace ExpenseTracker.Tests;

public sealed class PostgreSqlFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly NpgsqlConnectionStringBuilder _connection = new(
        Environment.GetEnvironmentVariable("EXPENSETRACKER_TEST_POSTGRESQL")
        ?? throw new InvalidOperationException("Set EXPENSETRACKER_TEST_POSTGRESQL to an isolated PostgreSQL test server."));

    private readonly string _database = $"expense_tracker_test_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = ConnectionString()
            }));
    }

    public async Task InitializeAsync()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.False(context.Database.HasPendingModelChanges());

        context.Transactions.Add(new Transaction
        {
            Type = "Expense",
            Category = "Food",
            Amount = 9.25m,
            Description = "Phase 1 legacy transaction",
            Date = DateTime.Today,
            IsFixed = true
        });
        context.UserSettings.Add(new UserSettings
        {
            UserName = "Phase 1 legacy settings",
            MonthlyBudget = 100m,
            Currency = "USD",
            CurrencySymbol = "$"
        });
        await context.SaveChangesAsync();
    }

    Task IAsyncLifetime.DisposeAsync() => base.DisposeAsync().AsTask();

    internal ApplicationDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(ConnectionString(), postgres =>
                postgres.MigrationsHistoryTable(ApplicationDbContext.PostgreSqlHistoryTable))
            .Options, new Microsoft.AspNetCore.Http.HttpContextAccessor());

    private string ConnectionString() =>
        new NpgsqlConnectionStringBuilder(_connection.ConnectionString) { Database = _database }.ConnectionString;
}
