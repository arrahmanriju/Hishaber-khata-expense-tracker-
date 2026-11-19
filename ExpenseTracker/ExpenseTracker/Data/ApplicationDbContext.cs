using System.Security.Claims;
using ExpenseTracker.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Data;

public class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options,
    IHttpContextAccessor httpContextAccessor) : IdentityDbContext<IdentityUser>(options)
{
    public const string PostgreSqlHistoryTable = "__EFMigrationsHistoryPostgreSql";

    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();

    internal string? CurrentUserId
    {
        get
        {
            var user = httpContextAccessor.HttpContext?.User;
            return user?.Identity?.IsAuthenticated == true
                ? user.FindFirstValue(ClaimTypes.NameIdentifier)
                : null;
        }
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Transaction>(entity =>
        {
            entity.HasQueryFilter(transaction => CurrentUserId != null && transaction.UserId == CurrentUserId);
            // Preserve the input scale so PostgreSQL does not pad amounts beyond System.Decimal.
            entity.Property(transaction => transaction.Amount).HasColumnType("numeric");
            entity.Property(transaction => transaction.Date).HasColumnType("date");
            entity.Property(transaction => transaction.CreatedAt).HasColumnType("timestamp without time zone");
            entity.HasIndex(transaction => new { transaction.UserId, transaction.Date });
            entity.HasOne<IdentityUser>().WithMany().HasForeignKey(transaction => transaction.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<UserSettings>(entity =>
        {
            entity.HasQueryFilter(settings => CurrentUserId != null && settings.UserId == CurrentUserId);
            entity.Property(settings => settings.MonthlyBudget).HasColumnType("numeric");
            entity.Property(settings => settings.UpdatedAt).HasColumnType("timestamp without time zone");
            entity.HasIndex(settings => settings.UserId).IsUnique();
            entity.HasOne<IdentityUser>().WithMany().HasForeignKey(settings => settings.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
