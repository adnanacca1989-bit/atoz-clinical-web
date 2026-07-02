using System.ComponentModel.DataAnnotations;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;

namespace AtoZClinical.Web.Pages.Account;

public class ForgotPasswordModel : PageModel
{
    private readonly PasswordResetService _reset;
    private readonly IClinicalEmailSender _email;
    private readonly ClinicalAppUrls _urls;
    private readonly IConfiguration _config;
    private readonly ILogger<ForgotPasswordModel> _logger;

    public ForgotPasswordModel(
        PasswordResetService reset,
        IClinicalEmailSender email,
        ClinicalAppUrls urls,
        IConfiguration config,
        ILogger<ForgotPasswordModel> logger)
    {
        _reset = reset;
        _email = email;
        _urls = urls;
        _config = config;
        _logger = logger;
    }

    [BindProperty]
    public ForgotInput Input { get; set; } = new();

    public bool Submitted { get; private set; }
    public bool EmailSent { get; private set; }
    public bool ShowGenericAcknowledgment { get; private set; }
    public bool EmailDeliveryFailed { get; private set; }
    public string? EmailConfigurationWarningHtml { get; private set; }
    public string UserErrorMessage { get; private set; } = SmtpEmailDiagnostics.UserFriendlyFailureMessage;

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        try
        {
            if (!SmtpEmailConfiguration.IsEmailConfigured(_config))
            {
                var missing = SmtpEmailConfiguration.GetMissingVariables(_config);
                _logger.LogError(
                    "Password reset blocked: SMTP not configured. Missing: {Missing}",
                    string.Join(", ", missing));
                EmailDeliveryFailed = true;
                EmailConfigurationWarningHtml = SmtpEmailConfiguration.FormatMissingVariablesHtml(missing);
                UserErrorMessage = new ClinicalEmailNotConfiguredException(missing).Message;
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
                    <p>This link expires in {_config.GetValue("PasswordReset:ExpiryMinutes", PasswordResetService.DefaultExpiryMinutes)} minutes for security.</p>
                    """;

                var result = await _email.SendAsync(payload.Email, "Reset your A to Z Clinical password", body);
                if (!result.Success)
                {
                    _logger.LogError(
                        "Password reset email failed for {Email}: {Reason}",
                        payload.Email, result.Message);
                    EmailDeliveryFailed = true;
                    UserErrorMessage = result.Message;
                    return Page();
                }

                EmailSent = true;
                Submitted = true;
                _logger.LogInformation("Password reset email sent successfully to {Email}", payload.Email);
                return Page();
            }

            ShowGenericAcknowledgment = true;
            Submitted = true;
            return Page();
        }
        catch (ClinicalEmailNotConfiguredException ex)
        {
            _logger.LogError(ex, "Password reset blocked: SMTP not configured");
            EmailDeliveryFailed = true;
            EmailConfigurationWarningHtml = SmtpEmailConfiguration.FormatMissingVariablesHtml(ex.MissingVariables);
            UserErrorMessage = ex.Message;
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Password reset failed for email {Email}", Input.Email);
            EmailDeliveryFailed = true;
            UserErrorMessage = SmtpEmailDiagnostics.ClassifyFailure(ex);
            return Page();
        }
    }

    public sealed class ForgotInput
    {
        [Required, EmailAddress, Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;
    }
}
