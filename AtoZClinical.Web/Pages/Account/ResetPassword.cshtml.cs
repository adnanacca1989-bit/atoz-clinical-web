using System.ComponentModel.DataAnnotations;
using AtoZClinical.Infrastructure.Identity;
using AtoZClinical.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace AtoZClinical.Web.Pages.Account;

[DisableRateLimiting]
public class ResetPasswordModel : PageModel
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly PasswordResetService _reset;

    public ResetPasswordModel(UserManager<ApplicationUser> users, PasswordResetService reset)
    {
        _users = users;
        _reset = reset;
    }

    [BindProperty]
    public ResetInput Input { get; set; } = new();

    public bool Succeeded { get; private set; }
    public bool TokenValid { get; private set; }

    public async Task<IActionResult> OnGet(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return RedirectToPage("/Account/ForgotPassword");

        var row = await _reset.FindValidTokenAsync(token);
        if (row is null)
        {
            TokenValid = false;
            return Page();
        }

        Input.Token = token;
        TokenValid = true;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var row = await _reset.FindValidTokenAsync(Input.Token);
        if (row is null)
        {
            ModelState.AddModelError(string.Empty, "Invalid or expired reset link. Please request a new one.");
            return Page();
        }

        if (!ModelState.IsValid)
        {
            TokenValid = true;
            return Page();
        }

        var user = await _users.FindByIdAsync(row.UserId);
        if (user is null)
        {
            ModelState.AddModelError(string.Empty, "Invalid reset request.");
            return Page();
        }

        var identityToken = await _users.GeneratePasswordResetTokenAsync(user);
        var result = await _users.ResetPasswordAsync(user, identityToken, Input.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            TokenValid = true;
            return Page();
        }

        await _reset.MarkUsedAsync(row.Id);
        Succeeded = true;
        return Page();
    }

    public sealed class ResetInput
    {
        [Required]
        public string Token { get; set; } = string.Empty;

        [Required, MinLength(12), DataType(DataType.Password), Display(Name = "New password")]
        public string Password { get; set; } = string.Empty;

        [Required, DataType(DataType.Password), Display(Name = "Confirm password"), Compare(nameof(Password))]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
