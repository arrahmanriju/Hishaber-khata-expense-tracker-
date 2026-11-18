using ExpenseTracker.Data;
using ExpenseTracker.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Controllers
{
    public class ExpenseController : BaseController
    {
        private readonly ApplicationDbContext _context;

        public ExpenseController(ApplicationDbContext context) : base(context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult AddExpense() => View(new TransactionInput { Date = DateTime.Today });

        [HttpPost]
        public async Task<IActionResult> AddExpense(TransactionInput input)
        {
            ValidateTransaction(input, "Expense");
            if (!ModelState.IsValid)
                return View(input);

            var expense = new Transaction { Type = "Expense", UserId = OwnerId };
            ApplyTransactionInput(expense, input);
            _context.Transactions.Add(expense);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Expense added successfully!";
            return ReturnToPeriod(nameof(HomeController.Index), "Home", expense.Date.Month, expense.Date.Year);
        }

        // GET: Fixed Expense Page
        public async Task<IActionResult> FixedExpense(int? month, int? year)
        {
            // Default to current month and year if not provided
            int selectedMonth = month ?? DateTime.Now.Month;
            int selectedYear = year ?? DateTime.Now.Year;
            if (!IsValidPeriod(selectedMonth, selectedYear))
                return BadRequest("Invalid report period.");

            // Filter fixed expenses for the selected month and year
            var fixedExpenses = await _context.Transactions
                .Where(t => t.Type == "Expense"
                    && t.IsFixed == true
                    && t.Date.Month == selectedMonth
                    && t.Date.Year == selectedYear)
                .OrderByDescending(t => t.Date)
                .ToListAsync();

            // Calculate total for the month
            var totalFixed = fixedExpenses.Sum(t => t.Amount);

            // Group by category
            var expenseByCategory = fixedExpenses
                .GroupBy(t => t.Category)
                .Select(g => new
                {
                    Category = g.Key,
                    Total = g.Sum(t => t.Amount),
                    Count = g.Count()
                })
                .OrderByDescending(x => x.Total)
                .ToList();

            ViewBag.ExpenseByCategory = expenseByCategory;
            ViewBag.TotalFixedExpense = totalFixed;
            ViewBag.SelectedMonth = selectedMonth;
            ViewBag.SelectedYear = selectedYear;

            return View(fixedExpenses);
        }


        [HttpPost]
        public async Task<IActionResult> DeleteFixedExpense(int id, int? month, int? year)
        {
            var ownerId = OwnerId;
            var deleted = await _context.Transactions
                .Where(t => t.UserId == ownerId && t.TransactionId == id && t.Type == "Expense" && t.IsFixed)
                .ExecuteDeleteAsync();
            if (deleted == 0)
                return NotFound();
            TempData["SuccessMessage"] = "Fixed expense deleted successfully!";
            return ReturnToPeriod(nameof(FixedExpense), "Expense", month, year);
        }
    }
}
