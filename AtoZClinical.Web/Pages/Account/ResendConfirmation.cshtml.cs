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
        var user = await _users.FindByNameAsync(username)
            ?? await _users.FindByEmailAsync(username);

        if (user is not null
            && !string.IsNullOrWhiteSpace(user.Email)
            && !user.EmailConfirmed)
        {
            var result = await _registrationEmail.SendEmailConfirmationAsync(user, user.Email);
            if (result == EmailConfirmationSendResult.NotConfigured)
            {
                var missing = SmtpEmailConfiguration.GetMissingVariables(_config);
                EmailConfigurationWarningHtml = SmtpEmailConfiguration.FormatMissingVariablesHtml(missing);
                _logger.LogWarning(
                    "Resend confirmation skipped (not configured). Missing: {Missing}",
                    string.Join(", ", missing));
            }
            else if (result == EmailConfirmationSendResult.Failed)
            {
                EmailDeliveryFailed = true;
                return Page();
            }
            else if (result == EmailConfirmationSendResult.Sent && _env.IsDevelopment())
            {
                _logger.LogInformation("Development mode: confirmation email sent to {Email}", user.Email);
            }
        }

        Submitted = true;
        return Page();
    }

    public sealed class ResendInput
    {
        [Required, Display(Name = "Username or Email")]
        public string Username { get; set; } = string.Empty;
    }
}
