using System.ComponentModel.DataAnnotations;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.Account;

public class ForgotPasswordModel : PageModel
{
    private readonly PasswordResetService _reset;
    private readonly IClinicalEmailSender _email;
    private readonly ClinicalAppUrls _urls;

    public ForgotPasswordModel(PasswordResetService reset, IClinicalEmailSender email, ClinicalAppUrls urls)
    {
        _reset = reset;
        _email = email;
        _urls = urls;
    }

    [BindProperty]
    public ForgotInput Input { get; set; } = new();

    public bool Submitted { get; private set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var payload = await _reset.CreateTokenForEmailAsync(Input.Email);
        if (payload is not null)
        {
            var link = _urls.BuildPageUrl("Account/ResetPassword", new Dictionary<string, string?>
            {
                ["token"] = payload.PlainToken
            });

            var body = $"""
                <p>Hello {payload.FullName},</p>
                <p>We received a request to reset your password for A to Z Clinical.</p>
                <p><a href="{link}">Reset your password</a></p>
                <p>If you did not request this, you can ignore this email.</p>
                <p>This link expires in {PasswordResetService.DefaultExpiryMinutes} minutes for security.</p>
                """;

            try
            {
                await _email.SendAsync(payload.Email, "Reset your A to Z Clinical password", body);
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
        [Required, EmailAddress, Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;
    }
}
