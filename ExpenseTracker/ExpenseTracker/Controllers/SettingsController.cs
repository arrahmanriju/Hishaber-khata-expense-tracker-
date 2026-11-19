using ExpenseTracker.Data;
using ExpenseTracker.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ExpenseTracker.Controllers;

public class SettingsController(ApplicationDbContext context) : BaseController(context)
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var settings = await Context.UserSettings.AsNoTracking().SingleOrDefaultAsync();
        var input = settings is null ? new SettingsInput() : new SettingsInput
        {
            UserName = settings.UserName,
            Email = settings.Email,
            MonthlyBudget = settings.MonthlyBudget,
            Currency = settings.Currency
        };

        return await SettingsView(input, input.MonthlyBudget);
    }

    [HttpPost]
    public async Task<IActionResult> UpdateSettings(SettingsInput input)
    {
        var settings = await Context.UserSettings.SingleOrDefaultAsync();
        if (decimal.Round(input.MonthlyBudget, 2) != input.MonthlyBudget)
            ModelState.AddModelError(nameof(input.MonthlyBudget), "Enter up to two decimal places.");

        if (!ModelState.IsValid)
            return await SettingsView(input, settings?.MonthlyBudget ?? 0);

        if (settings is null)
        {
            settings = new UserSettings { UserId = OwnerId };
            Context.UserSettings.Add(settings);
        }

        ApplySettings(settings, input);
        try
        {
            await Context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (Context.Entry(settings).State == EntityState.Added
            && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_UserSettings_UserId" })
        {
            // Another request created the settings row while this request was saving.
            Context.Entry(settings).State = EntityState.Detached;
            var existing = await Context.UserSettings.SingleAsync();
            ApplySettings(existing, input);
            await Context.SaveChangesAsync();
        }

        TempData["SuccessMessage"] = "Settings updated successfully!";
        return RedirectToAction(nameof(Index));
    }

    private async Task<IActionResult> SettingsView(SettingsInput input, decimal savedBudget)
    {
        var today = DateTime.Today;
        var monthlyExpense = await Context.Transactions
            .Where(t => t.Type == "Expense" && t.Date.Month == today.Month && t.Date.Year == today.Year)
            .SumAsync(t => t.Amount);

        ViewBag.MonthlyExpense = monthlyExpense;
        ViewBag.SavedBudget = savedBudget;
        ViewBag.BudgetRemaining = savedBudget - monthlyExpense;
        ViewBag.BudgetPercentage = savedBudget > 0 ? monthlyExpense / savedBudget * 100 : 0m;
        return View("Index", input);
    }

    private static void ApplySettings(UserSettings settings, SettingsInput input)
    {
        settings.UserName = input.UserName;
        settings.Email = input.Email;
        settings.MonthlyBudget = decimal.Round(input.MonthlyBudget, 2);
        settings.Currency = input.Currency;
        settings.CurrencySymbol = input.Currency switch
        {
            "USD" => "$",
            "EUR" => "€",
            "GBP" => "£",
            "INR" => "₹",
            _ => "৳"
        };
        settings.UpdatedAt = DateTime.Now;
    }
}
