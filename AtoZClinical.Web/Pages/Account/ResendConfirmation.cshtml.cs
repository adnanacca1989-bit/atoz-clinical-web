using System.ComponentModel.DataAnnotations;
using AtoZClinical.Infrastructure.Identity;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;

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
            EmailDeliveryFailed = true;
            ModelState.AddModelError(string.Empty,
                "Email is not configured on this server. Contact your system administrator.");
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
                _logger.LogError("Resend confirmation failed for user {UserId}", user.Id);
                EmailDeliveryFailed = true;
                ModelState.AddModelError(string.Empty,
                    "We could not send the confirmation email. Please try again later.");
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
