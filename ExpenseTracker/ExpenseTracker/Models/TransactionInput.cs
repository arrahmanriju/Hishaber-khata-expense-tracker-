using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Models;

public class TransactionInput
{
    internal static readonly string[] IncomeCategories =
        ["Salary", "Business", "Freelance", "Investment", "Gift", "Bonus", "Other"];
    internal static readonly string[] ExpenseCategories =
        ["Food", "Transport", "Shopping", "Entertainment", "Health", "Education", "Bills", "Rent", "Other"];

    [Required]
    public string Category { get; set; } = "";

    [Required, Range(typeof(decimal), "0.01", "999999999999999.99")]
    public decimal? Amount { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }

    [Required, DataType(DataType.Date)]
    public DateTime? Date { get; set; }

    [Display(Name = "Fixed expense")]
    public bool IsFixed { get; set; }
}
