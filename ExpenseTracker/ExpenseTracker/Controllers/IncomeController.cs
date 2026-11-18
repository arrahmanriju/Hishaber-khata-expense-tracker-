using ExpenseTracker.Data;
using ExpenseTracker.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Controllers
{
    public class IncomeController : BaseController
    {
        private readonly ApplicationDbContext _context;

        public IncomeController(ApplicationDbContext context) : base(context)
        {
            _context = context;
        }

        // GET: Income Page
        [HttpGet]
        public async Task<IActionResult> Index(int? month, int? year)
        {
            if (!IsValidPeriod(month ?? DateTime.Now.Month, year ?? DateTime.Now.Year))
                return BadRequest("Invalid report period.");

            return await LoadIndex(month, year, new TransactionInput { Date = DateTime.Today });
        }

        private async Task<IActionResult> LoadIndex(int? month, int? year, TransactionInput input)
        {
            // Default to current month and year if not provided
            int selectedMonth = month ?? DateTime.Now.Month;
            int selectedYear = year ?? DateTime.Now.Year;
            if (selectedMonth is < 1 or > 12 || selectedYear is < 1001 or > 9998)
                return BadRequest("Invalid report period.");

            // Filter incomes for the selected month and year
            var incomes = await _context.Transactions
                .Where(t => t.Type == "Income"
                    && t.Date.Month == selectedMonth
                    && t.Date.Year == selectedYear)
                .OrderByDescending(t => t.Date)
                .ToListAsync();

            // Group by category and sum
            var incomeByCategory = incomes
                .GroupBy(t => t.Category)
                .Select(g => new
                {
                    Category = g.Key,
                    Total = g.Sum(t => t.Amount),
                    Count = g.Count()
                })
                .OrderByDescending(x => x.Total)
                .ToList();

            ViewBag.IncomeByCategory = incomeByCategory;
            ViewBag.TotalIncome = incomes.Sum(t => t.Amount);
            ViewBag.SelectedMonth = selectedMonth;
            ViewBag.SelectedYear = selectedYear;

            ViewBag.Incomes = incomes;
            return View("Index", input);
        }


        [HttpPost]
        public async Task<IActionResult> AddIncome(TransactionInput input, int? month, int? year)
        {
            ValidateTransaction(input, "Income");
            if (!ModelState.IsValid)
                return await LoadIndex(month, year, input);

            var income = new Transaction { Type = "Income", UserId = OwnerId };
            ApplyTransactionInput(income, input);
            _context.Transactions.Add(income);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Income added successfully!";
            return ReturnToPeriod(nameof(Index), "Income", income.Date.Month, income.Date.Year);
        }

        [HttpPost]
        public async Task<IActionResult> DeleteIncome(int id, int? month, int? year)
        {
            var ownerId = OwnerId;
            var deleted = await _context.Transactions
                .Where(t => t.UserId == ownerId && t.TransactionId == id && t.Type == "Income")
                .ExecuteDeleteAsync();
            if (deleted == 0)
                return NotFound();
            TempData["SuccessMessage"] = "Income deleted successfully!";
            return ReturnToPeriod(nameof(Index), "Income", month, year);
        }
    }
}
