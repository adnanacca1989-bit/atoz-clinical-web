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
    public bool EmailDeliveryFailed { get; private set; }
    public bool EmailNotConfigured { get; private set; }
    public string UserErrorMessage { get; private set; } = SmtpEmailDiagnostics.UserFriendlyFailureMessage;
    public string AdminSetupMessage { get; private set; } = SmtpEmailConfiguration.NotConfiguredUserMessage;

    public void OnGet(string? username)
    {
        if (!string.IsNullOrWhiteSpace(username))
            Input.Username = username.Trim();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        if (!SmtpEmailConfiguration.IsEmailConfigured(_config))
        {
            var missing = SmtpEmailConfiguration.GetMissingVariables(_config);
            _logger.LogError(
                "Resend confirmation requested but SMTP not configured. Missing: {Missing}",
                string.Join(", ", missing));
            EmailNotConfigured = true;
            EmailDeliveryFailed = true;
            AdminSetupMessage = BuildNotConfiguredMessage(missing);
            ModelState.AddModelError(string.Empty, AdminSetupMessage);
            return Page();
        }

        var username = Input.Username.Trim();
        var user = await _users.FindByNameAsync(username)
            ?? await _users.FindByEmailAsync(username);

        if (user is not null
            && !string.IsNullOrWhiteSpace(user.Email)
            && !user.EmailConfirmed)
        {
            var result = await _registrationEmail.SendEmailConfirmationAsync(user, user.Email);
            if (result is EmailConfirmationSendResult.Failed or EmailConfirmationSendResult.NotConfigured)
            {
                EmailDeliveryFailed = true;
                ModelState.AddModelError(string.Empty, UserErrorMessage);
                return Page();
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

    private static string BuildNotConfiguredMessage(IReadOnlyList<string> missing) =>
        missing.Count == 0
            ? SmtpEmailConfiguration.NotConfiguredUserMessage
            : $"Email is not configured on the server. Missing on Render: {string.Join(", ", missing)}. "
              + "Open the atoz-clinical web service → Environment, add them, save, and wait for redeploy.";
}
