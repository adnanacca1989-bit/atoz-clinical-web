using System.ComponentModel.DataAnnotations;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;

namespace AtoZClinical.Web.Pages.Account;

[DisableRateLimiting]
public class ResendConfirmationModel : PageModel
{
    private readonly ApplicationUserLookup _userLookup;
    private readonly RegistrationEmailService _registrationEmail;
    private readonly IConfiguration _config;
    private readonly ILogger<ResendConfirmationModel> _logger;

    public ResendConfirmationModel(
        ApplicationUserLookup userLookup,
        RegistrationEmailService registrationEmail,
        IConfiguration config,
        ILogger<ResendConfirmationModel> logger)
    {
        _userLookup = userLookup;
        _registrationEmail = registrationEmail;
        _config = config;
        _logger = logger;
    }

    [BindProperty]
    public ResendInput Input { get; set; } = new();

    public bool Submitted { get; private set; }
    public bool EmailSent { get; private set; }
    public bool ShowGenericAcknowledgment { get; private set; }
    public bool EmailDeliveryFailed { get; private set; }
    public string? EmailConfigurationWarningHtml { get; private set; }
    public string UserErrorMessage { get; private set; } = SmtpEmailDiagnostics.UserFriendlyFailureMessage;

    public void OnGet(string? username)
    {
        if (!string.IsNullOrWhiteSpace(username))
            Input.Username = username.Trim();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var username = Input.Username.Trim();
        try
        {
            if (!SmtpEmailConfiguration.IsEmailConfigured(_config))
            {
                var missing = SmtpEmailConfiguration.GetMissingVariables(_config);
                _logger.LogError(
                    "Resend confirmation blocked: SMTP not configured. Missing: {Missing}",
                    string.Join(", ", missing));
                EmailDeliveryFailed = true;
                EmailConfigurationWarningHtml = SmtpEmailConfiguration.FormatMissingVariablesHtml(missing);
                UserErrorMessage = new ClinicalEmailNotConfiguredException(missing).Message;
                return Page();
            }

            var user = await _userLookup.FindByUsernameOrEmailAsync(username);
            if (user is not null
                && !string.IsNullOrWhiteSpace(user.Email)
                && !user.EmailConfirmed)
            {
                var outcome = await _registrationEmail.SendEmailConfirmationAsync(user, user.Email);
                if (outcome.Result == EmailConfirmationSendResult.Sent)
                {
                    EmailSent = true;
                    Submitted = true;
                    _logger.LogInformation(
                        "Resend confirmation succeeded for user {UserId} email {Email}",
                        user.Id, user.Email);
                    return Page();
                }

                if (outcome.Result == EmailConfirmationSendResult.Failed)
                {
                    EmailDeliveryFailed = true;
                    UserErrorMessage = outcome.ErrorMessage ?? SmtpEmailDiagnostics.UserFriendlyFailureMessage;
                    _logger.LogError(
                        "Resend confirmation failed for user {UserId}: {Reason}",
                        user.Id, UserErrorMessage);
                    return Page();
                }
            }

            ShowGenericAcknowledgment = true;
            Submitted = true;
            return Page();
        }
        catch (ClinicalEmailNotConfiguredException ex)
        {
            _logger.LogError(ex, "Resend confirmation blocked: SMTP not configured");
            EmailDeliveryFailed = true;
            EmailConfigurationWarningHtml = SmtpEmailConfiguration.FormatMissingVariablesHtml(ex.MissingVariables);
            UserErrorMessage = ex.Message;
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Resend confirmation failed for username {Username}", username);
            EmailDeliveryFailed = true;
            UserErrorMessage = SmtpEmailDiagnostics.ClassifyFailure(ex);
            return Page();
        }
    }

    public sealed class ResendInput
    {
        [Required, Display(Name = "Username or Email")]
        public string Username { get; set; } = string.Empty;
    }
}
