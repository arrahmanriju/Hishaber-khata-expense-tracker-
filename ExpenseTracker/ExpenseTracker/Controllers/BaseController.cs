using ExpenseTracker.Data;
using ExpenseTracker.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Controllers
{
    [Authorize]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public abstract class BaseController : Controller
    {
        private readonly ApplicationDbContext _context;

        public BaseController(ApplicationDbContext context)
        {
            _context = context;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            base.OnActionExecuting(context);
            
            // Get currency settings and pass to all views
            var settings = _context.UserSettings.AsNoTracking().SingleOrDefault();
            if (settings != null)
            {
                ViewBag.CurrencySymbol = settings.CurrencySymbol;
                ViewBag.Currency = settings.Currency;
            }
            else
            {
                ViewBag.CurrencySymbol = "৳";
                ViewBag.Currency = "BDT";
            }
        }

        protected ApplicationDbContext Context => _context;

        protected string OwnerId => _context.CurrentUserId
            ?? throw new InvalidOperationException("An authenticated user ID is required.");

        protected bool IsValidPeriod(int month, int year) =>
            ModelState.IsValid && month is >= 1 and <= 12 && year is >= 1001 and <= 9998;

        protected void ValidateTransaction(TransactionInput input, string type, string? originalCategory = null)
        {
            var categories = type == "Income" ? TransactionInput.IncomeCategories : TransactionInput.ExpenseCategories;
            if (input.Category != originalCategory && !categories.Contains(input.Category))
                ModelState.AddModelError(nameof(input.Category), "Choose a supported category.");

            if (input.Date is { } date && (date.Year < 1001 || date.Year > 9998))
                ModelState.AddModelError(nameof(input.Date), "Enter a date in a supported year.");

            if (input.Amount is { } amount && decimal.Round(amount, 2) != amount)
                ModelState.AddModelError(nameof(input.Amount), "Enter up to two decimal places.");
        }

        protected static void ApplyTransactionInput(Transaction transaction, TransactionInput input)
        {
            transaction.Category = input.Category;
            transaction.Amount = decimal.Round(input.Amount!.Value, 2);
            transaction.Description = input.Description;
            transaction.Date = input.Date!.Value.Date;
            transaction.IsFixed = transaction.Type == "Expense" && input.IsFixed;
        }

        protected IActionResult ReturnToPeriod(string action, string controller, int? month, int? year) =>
            month is >= 1 and <= 12 && year is >= 1001 and <= 9998
                ? RedirectToAction(action, controller, new { month, year })
                : RedirectToAction(action, controller);
    }
}
