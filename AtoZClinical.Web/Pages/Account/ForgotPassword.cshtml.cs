using System.ComponentModel.DataAnnotations;
using System.Text;
using AtoZClinical.Infrastructure.Identity;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;

namespace AtoZClinical.Web.Pages.Account;

[DisableRateLimiting]
public class ForgotPasswordModel : PageModel
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly IClinicalEmailSender _email;
    private readonly ClinicalAppUrls _urls;

    public ForgotPasswordModel(UserManager<ApplicationUser> users, IClinicalEmailSender email, ClinicalAppUrls urls)
    {
        _users = users;
        _email = email;
        _urls = urls;
    }

    [BindProperty]
    public ForgotInput Input { get; set; } = new();

    public bool Submitted { get; private set; }

    public void OnGet() { }

    [EnableRateLimiting("auth")]
    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var username = Input.Username.Trim();
        var user = await _users.FindByNameAsync(username)
            ?? await _users.FindByEmailAsync(username);

        if (user is not null && !string.IsNullOrWhiteSpace(user.Email))
        {
            var token = await _users.GeneratePasswordResetTokenAsync(user);
            var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var link = _urls.BuildPageUrl("Account/ResetPassword", new Dictionary<string, string?>
            {
                ["userId"] = user.Id,
                ["code"] = encoded
            });

            var body = $"""
                <p>Hello {user.FullName},</p>
                <p>We received a request to reset your password for A to Z Clinical.</p>
                <p><a href="{link}">Reset your password</a></p>
                <p>If you did not request this, you can ignore this email.</p>
                <p>This link expires after a short period for security.</p>
                """;

            try
            {
                await _email.SendAsync(user.Email, "Reset your A to Z Clinical password", body);
            }
            catch
            {
                if (!_email.IsConfigured)
                {
                    ModelState.AddModelError(string.Empty,
                        "Password reset email is not configured. Contact your system vendor.");
                    return Page();
                }

                throw;
            }
        }

        Submitted = true;
        return Page();
    }

    public sealed class ForgotInput
    {
        [Required, Display(Name = "Username or Email")]
        public string Username { get; set; } = string.Empty;
    }
}
