using System.ComponentModel.DataAnnotations;
using System.Text;
using AtoZClinical.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;

namespace AtoZClinical.Web.Pages.Account;

[DisableRateLimiting]
public class ResetPasswordModel : PageModel
{
    private readonly UserManager<ApplicationUser> _users;

    public ResetPasswordModel(UserManager<ApplicationUser> users) => _users = users;

    [BindProperty]
    public ResetInput Input { get; set; } = new();

    public bool Succeeded { get; private set; }

    public IActionResult OnGet(string? userId, string? code)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(code))
            return RedirectToPage("/Account/ForgotPassword");

        Input.UserId = userId;
        Input.Code = code;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var user = await _users.FindByIdAsync(Input.UserId);
        if (user is null)
        {
            ModelState.AddModelError(string.Empty, "Invalid reset request.");
            return Page();
        }

        string token;
        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(Input.Code));
        }
        catch
        {
            ModelState.AddModelError(string.Empty, "Invalid or expired reset link.");
            return Page();
        }

        var result = await _users.ResetPasswordAsync(user, token, Input.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return Page();
        }

        Succeeded = true;
        return Page();
    }

    public sealed class ResetInput
    {
        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        public string Code { get; set; } = string.Empty;

        [Required, MinLength(12), DataType(DataType.Password), Display(Name = "New password")]
        public string Password { get; set; } = string.Empty;

        [Required, DataType(DataType.Password), Display(Name = "Confirm password"), Compare(nameof(Password))]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
