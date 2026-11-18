using ExpenseTracker.Data;
using ExpenseTracker.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Controllers;

public class TransactionsController(ApplicationDbContext context) : BaseController(context)
{
    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var transaction = await FindTransaction(id);
        if (transaction is null)
            return NotFound();

        return EditView(transaction, new TransactionInput
        {
            Category = transaction.Category,
            Amount = transaction.Amount,
            Description = transaction.Description,
            Date = transaction.Date,
            IsFixed = transaction.IsFixed
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, TransactionInput input)
    {
        var transaction = await FindTransaction(id);
        if (transaction is null)
            return NotFound();

        ValidateTransaction(input, transaction.Type, transaction.Category);
        if (!ModelState.IsValid)
            return EditView(transaction, input);

        ApplyTransactionInput(transaction, input);
        try
        {
            await Context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return NotFound();
        }

        TempData["SuccessMessage"] = "Transaction updated successfully!";
        return ReturnToPeriod("Index", "MonthlyOverview", transaction.Date.Month, transaction.Date.Year);
    }

    private Task<Transaction?> FindTransaction(int id)
    {
        var ownerId = OwnerId;
        return Context.Transactions.SingleOrDefaultAsync(t => t.TransactionId == id
            && t.UserId == ownerId && (t.Type == "Income" || t.Type == "Expense"));
    }

    private IActionResult EditView(Transaction transaction, TransactionInput input)
    {
        ViewBag.TransactionId = transaction.TransactionId;
        ViewBag.TransactionType = transaction.Type;
        ViewBag.OriginalCategory = transaction.Category;
        return View("Edit", input);
    }
}
