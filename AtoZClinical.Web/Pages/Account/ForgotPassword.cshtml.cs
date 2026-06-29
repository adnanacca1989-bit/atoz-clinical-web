using System.ComponentModel.DataAnnotations;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AtoZClinical.Web.Pages.Account;

public class ForgotPasswordModel : PageModel
{
    private readonly PasswordResetService _reset;
    private readonly IClinicalEmailSender _email;
    private readonly ClinicalAppUrls _urls;
    private readonly ILogger<ForgotPasswordModel> _logger;

    public ForgotPasswordModel(
        PasswordResetService reset,
        IClinicalEmailSender email,
        ClinicalAppUrls urls,
        ILogger<ForgotPasswordModel> logger)
    {
        _reset = reset;
        _email = email;
        _urls = urls;
        _logger = logger;
    }

    [BindProperty]
    public ForgotInput Input { get; set; } = new();

    public bool Submitted { get; private set; }
    public bool EmailDeliveryFailed { get; private set; }
    public bool EmailNotConfigured { get; private set; }
    public string UserErrorMessage { get; private set; } = SmtpEmailDiagnostics.UserFriendlyFailureMessage;
    public string AdminSetupMessage { get; private set; } =
        "Email is not configured on the server. Set SMTP_HOST, SMTP_USER, SMTP_PASS, and FROM_EMAIL in Render environment variables, then redeploy.";

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        if (!_email.IsConfigured)
        {
            var reason = SmtpEmailSettings.From(HttpContext.RequestServices.GetRequiredService<IConfiguration>()).DescribeReadiness();
            _logger.LogError("Password reset requested but SMTP not configured: {Reason}", reason);
            EmailNotConfigured = true;
            EmailDeliveryFailed = true;
            return Page();
        }

        var payload = await _reset.CreateTokenForEmailAsync(Input.Email);
        if (payload is not null)
        {
            var link = _urls.BuildPageUrl("reset-password", new Dictionary<string, string?>
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
                _logger.LogInformation("Password reset email delivered to {Email}", payload.Email);
            }
            catch (ClinicalEmailSendException ex)
            {
                _logger.LogError(ex, "Password reset email failed for {Email}: {Reason}", payload.Email, ex.FailureReason);
                EmailDeliveryFailed = true;
                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Password reset email failed for {Email}: {Reason}",
                    payload.Email, SmtpEmailDiagnostics.ClassifyFailure(ex));
                EmailDeliveryFailed = true;
                return Page();
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
