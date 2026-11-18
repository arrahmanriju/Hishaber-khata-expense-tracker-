using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Models;

public class LoginInput
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = "";

    [Required, DataType(DataType.Password), StringLength(128)]
    public string Password { get; set; } = "";

    public string? ReturnUrl { get; set; }
}

public class RegisterInput : LoginInput
{
    [Required, DataType(DataType.Password), Compare(nameof(Password))]
    [Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = "";
}
