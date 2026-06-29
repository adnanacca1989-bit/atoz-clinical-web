using System.ComponentModel.DataAnnotations;
using AtoZClinical.Infrastructure.Identity;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace AtoZClinical.Web.Pages.Account;

[DisableRateLimiting]
public class ResendConfirmationModel : PageModel
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly RegistrationEmailService _registrationEmail;
    private readonly IClinicalEmailSender _email;
    private readonly ILogger<ResendConfirmationModel> _logger;

    public ResendConfirmationModel(
        UserManager<ApplicationUser> users,
        RegistrationEmailService registrationEmail,
        IClinicalEmailSender email,
        ILogger<ResendConfirmationModel> logger)
    {
        _users = users;
        _registrationEmail = registrationEmail;
        _email = email;
        _logger = logger;
    }

    [BindProperty]
    public ResendInput Input { get; set; } = new();

    public bool Submitted { get; private set; }
    public bool EmailDeliveryFailed { get; private set; }
    public bool EmailNotConfigured { get; private set; }
    public string UserErrorMessage { get; private set; } = SmtpEmailDiagnostics.UserFriendlyFailureMessage;
    public string AdminSetupMessage { get; private set; } =
        "Email is not configured on the server. Set SMTP_HOST, SMTP_USER, SMTP_PASS, and FROM_EMAIL in Render environment variables, then redeploy.";

    public void OnGet(string? username)
    {
        if (!string.IsNullOrWhiteSpace(username))
            Input.Username = username.Trim();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        if (!_email.IsConfigured)
        {
            EmailNotConfigured = true;
            EmailDeliveryFailed = true;
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
}
