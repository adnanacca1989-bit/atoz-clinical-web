using System.ComponentModel.DataAnnotations;
using AtoZClinical.Infrastructure.Identity;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace AtoZClinical.Web.Pages.Account;

[DisableRateLimiting]
public class ResendConfirmationModel : PageModel
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly RegistrationEmailService _registrationEmail;
    private readonly IConfiguration _config;
    private readonly IHostEnvironment _env;
    private readonly ILogger<ResendConfirmationModel> _logger;

    public ResendConfirmationModel(
        UserManager<ApplicationUser> users,
        RegistrationEmailService registrationEmail,
        IConfiguration config,
        IHostEnvironment env,
        ILogger<ResendConfirmationModel> logger)
    {
        _users = users;
        _registrationEmail = registrationEmail;
        _config = config;
        _env = env;
        _logger = logger;
    }

    [BindProperty]
    public ResendInput Input { get; set; } = new();

    public bool Submitted { get; private set; }
    public bool EmailDeliveryFailed { get; private set; }
    public string? UserFacingMessage { get; private set; }
    public string? EmailConfigurationWarningHtml { get; private set; }
    public string UserErrorMessage { get; private set; } = SmtpEmailConfiguration.EmailServiceUnavailableUserMessage;

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
                var result = await _registrationEmail.SendEmailConfirmationAsync(user, user.Email);
                if (result == EmailConfirmationSendResult.NotConfigured)
                {
                    UserFacingMessage = SmtpEmailConfiguration.EmailServiceUnavailableUserMessage;
                    if (_env.IsDevelopment())
                    {
                        var missing = SmtpEmailConfiguration.GetMissingVariables(_config);
                        EmailConfigurationWarningHtml = SmtpEmailConfiguration.FormatMissingVariablesHtml(missing);
                    }
                }
                else if (result == EmailConfirmationSendResult.Failed)
                {
                    if (!SmtpEmailConfiguration.IsEmailConfigured(_config))
                    {
                        UserFacingMessage = SmtpEmailConfiguration.EmailServiceUnavailableUserMessage;
                    }
                    else
                    {
                        EmailDeliveryFailed = true;
                        UserErrorMessage = SmtpEmailConfiguration.EmailServiceUnavailableUserMessage;
                        return Page();
                    }
                }
            }

            Submitted = true;
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Resend confirmation failed for username {Username}", username);
            if (!SmtpEmailConfiguration.IsEmailConfigured(_config))
            {
                UserFacingMessage = SmtpEmailConfiguration.EmailServiceUnavailableUserMessage;
                Submitted = true;
                return Page();
            }
            EmailDeliveryFailed = true;
            UserErrorMessage = SmtpEmailConfiguration.EmailServiceUnavailableUserMessage;
            return Page();
        }
    }

    public sealed class ResendInput
    {
        [Required, Display(Name = "Username or Email")]
        public string Username { get; set; } = string.Empty;
    }
}
