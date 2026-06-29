using System.ComponentModel.DataAnnotations;
using AtoZClinical.Infrastructure.Identity;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;

namespace AtoZClinical.Web.Pages.Account;

[DisableRateLimiting]
public class ResendConfirmationModel : PageModel
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly RegistrationEmailService _registrationEmail;
    private readonly IConfiguration _config;
    private readonly ILogger<ResendConfirmationModel> _logger;

    public ResendConfirmationModel(
        UserManager<ApplicationUser> users,
        RegistrationEmailService registrationEmail,
        IConfiguration config,
        ILogger<ResendConfirmationModel> logger)
    {
        _users = users;
        _registrationEmail = registrationEmail;
        _config = config;
        _logger = logger;
    }

    [BindProperty]
    public ResendInput Input { get; set; } = new();

    public bool Submitted { get; private set; }
    public bool EmailSent { get; private set; }
    public bool EmailNotConfigured { get; private set; }
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
            var user = await _users.FindByNameAsync(username)
                ?? await _users.FindByEmailAsync(username);

            if (user is not null
                && !string.IsNullOrWhiteSpace(user.Email)
                && !user.EmailConfirmed)
            {
                var outcome = await _registrationEmail.SendEmailConfirmationAsync(user, user.Email);
                switch (outcome.Result)
                {
                    case EmailConfirmationSendResult.Sent:
                        EmailSent = true;
                        break;
                    case EmailConfirmationSendResult.NotConfigured:
                        EmailNotConfigured = true;
                        EmailConfigurationWarningHtml = SmtpEmailConfiguration.FormatMissingVariablesHtml(
                            SmtpEmailConfiguration.GetMissingVariables(_config));
                        break;
                    case EmailConfirmationSendResult.Failed:
                        EmailDeliveryFailed = true;
                        UserErrorMessage = outcome.ErrorMessage ?? SmtpEmailDiagnostics.UserFriendlyFailureMessage;
                        return Page();
                }
            }

            Submitted = true;
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
