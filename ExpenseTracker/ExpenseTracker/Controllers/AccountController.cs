using ExpenseTracker.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.Controllers;

public class AccountController(UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn) : Controller
{
    [AllowAnonymous, HttpGet]
    public IActionResult Register(string? returnUrl) => View(new RegisterInput { ReturnUrl = returnUrl });

    [AllowAnonymous, HttpPost]
    public async Task<IActionResult> Register(RegisterInput input)
    {
        if (!ModelState.IsValid)
            return View(input);

        var user = new IdentityUser { UserName = input.Email, Email = input.Email };
        var result = await users.CreateAsync(user, input.Password);
        if (result.Succeeded)
        {
            await signIn.SignInAsync(user, isPersistent: false);
            return ReturnToApp(input.ReturnUrl);
        }

        foreach (var error in result.Errors)
        {
            var message = error.Code is "DuplicateEmail" or "DuplicateUserName"
                ? "Unable to register with this email address."
                : error.Description;
            ModelState.AddModelError(string.Empty, message);
        }

        return View(input);
    }

    [AllowAnonymous, HttpGet]
    public IActionResult Login(string? returnUrl) => View(new LoginInput { ReturnUrl = returnUrl });

    [AllowAnonymous, HttpPost]
    public async Task<IActionResult> Login(LoginInput input)
    {
        if (!ModelState.IsValid)
            return View(input);

        var result = await signIn.PasswordSignInAsync(input.Email, input.Password,
            isPersistent: false, lockoutOnFailure: true);
        if (result.Succeeded)
            return ReturnToApp(input.ReturnUrl);

        ModelState.AddModelError(string.Empty, "Unable to sign in with these credentials.");
        return View(input);
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    private IActionResult ReturnToApp(string? returnUrl) =>
        Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction("Index", "Home");
}
