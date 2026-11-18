using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Models;

public class SettingsInput
{
    [Required, StringLength(100)]
    public string UserName { get; set; } = "User";

    [EmailAddress, StringLength(256)]
    public string? Email { get; set; }

    [Range(typeof(decimal), "0", "999999999999999.99")]
    public decimal MonthlyBudget { get; set; }

    [Required, RegularExpression("BDT|USD|EUR|GBP|INR", ErrorMessage = "Choose a supported currency.")]
    public string Currency { get; set; } = "BDT";
}
