using System.ComponentModel.DataAnnotations;
using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Identity;
using AtoZClinical.Infrastructure.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace AtoZClinical.Web.Pages.Account;

[DisableRateLimiting]
public class VerifyAccountModel : PageModel
{
    private readonly ApplicationUserLookup _userLookup;
    private readonly TrialRegistrationVerificationService _verification;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<VerifyAccountModel> _logger;

    public VerifyAccountModel(
        ApplicationUserLookup userLookup,
        TrialRegistrationVerificationService verification,
        IConfiguration config,
        IWebHostEnvironment env,
        ILogger<VerifyAccountModel> logger)
    {
        _userLookup = userLookup;
        _verification = verification;
        _config = config;
        _env = env;
        _logger = logger;
    }

    [BindProperty]
    public VerifyInput Input { get; set; } = new();

    [BindProperty]
    public string? VerificationCode { get; set; }

    [BindProperty]
    public string? PendingUserId { get; set; }

    public bool CodeSent { get; private set; }
    public bool Verified { get; private set; }
    public bool ShowCodeForm { get; private set; }
    public bool OtpDeliveredViaLog { get; private set; }
    public bool ShowDevelopmentOtpHints => _env.IsDevelopment();
    public string? DeliveryMessage { get; private set; }
    public string? ErrorMessage { get; private set; }

    public void OnGet(string? username)
    {
        if (!string.IsNullOrWhiteSpace(username))
            Input.Username = username.Trim();
    }

    public async Task<IActionResult> OnPostSendCodeAsync()
    {
        if (string.IsNullOrWhiteSpace(Input.Username))
        {
            ModelState.AddModelError(nameof(Input.Username), "Username is required.");
            return Page();
        }

        var user = await _userLookup.FindByUsernameOrEmailAsync(Input.Username.Trim());
        if (user is null)
        {
            CodeSent = true;
            ShowCodeForm = true;
            return Page();
        }

        if (user.EmailConfirmed)
        {
            Verified = true;
            return Page();
        }

        PendingUserId = user.Id;
        var (channel, destination) = ResolveChannel(user, _config);
        if (string.IsNullOrWhiteSpace(destination))
        {
            ErrorMessage = "This account has no email or mobile on file.";
            return Page();
        }

        try
        {
            var outcome = await _verification.SendCodeAsync(user, channel, destination);
            if (outcome.Result == VerificationCodeSendResult.Sent)
            {
                CodeSent = true;
                ShowCodeForm = true;
                OtpDeliveredViaLog = outcome.DeliveredViaLog;
                DeliveryMessage = OtpDeliveryConfiguration.BuildUserVerificationPrompt(
                    outcome.DeliveryMethod,
                    outcome.Channel,
                    outcome.MaskedDestination);
                return Page();
            }

            ErrorMessage = outcome.ErrorMessage ?? "Could not send verification code.";
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Verify account send code failed for {Username}", Input.Username);
            ErrorMessage = "Could not send verification code.";
            return Page();
        }
    }

    public async Task<IActionResult> OnPostVerifyAsync()
    {
        if (string.IsNullOrWhiteSpace(PendingUserId) && !string.IsNullOrWhiteSpace(Input.Username))
        {
            var user = await _userLookup.FindByUsernameOrEmailAsync(Input.Username.Trim());
            PendingUserId = user?.Id;
        }

        if (string.IsNullOrWhiteSpace(PendingUserId) || string.IsNullOrWhiteSpace(VerificationCode))
        {
            ShowCodeForm = true;
            ModelState.AddModelError(string.Empty, "Enter the 4-digit verification code.");
            return Page();
        }

        var outcome = await _verification.VerifyCodeAsync(PendingUserId, VerificationCode);
        if (outcome.Result == VerificationCodeVerifyResult.Verified
            || outcome.Result == VerificationCodeVerifyResult.AlreadyVerified)
        {
            Verified = true;
            return Page();
        }

        ShowCodeForm = true;
        ErrorMessage = outcome.ErrorMessage ?? "Verification failed.";
        return Page();
    }

    internal static (RegistrationVerificationChannel Channel, string Destination) ResolveChannel(
        ApplicationUser user,
        IConfiguration config)
    {
        if (!string.IsNullOrWhiteSpace(user.Email))
            return (RegistrationVerificationChannel.Email, user.Email.Trim());

        if (!string.IsNullOrWhiteSpace(user.PhoneNumber))
        {
            var channel = OtpDeliveryConfiguration.ResolveMobileChannel(config, null);
            return (channel, user.PhoneNumber.Trim());
        }

        return (RegistrationVerificationChannel.Email, string.Empty);
    }

    public sealed class VerifyInput
    {
        [Required, Display(Name = "Username")]
        public string Username { get; set; } = string.Empty;
    }
}
