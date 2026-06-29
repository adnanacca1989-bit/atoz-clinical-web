using System.ComponentModel.DataAnnotations;
using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Identity;
using AtoZClinical.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace AtoZClinical.Web.Pages.Account;

[DisableRateLimiting]
public class VerifyAccountModel : PageModel
{
    private readonly ApplicationUserLookup _userLookup;
    private readonly TrialRegistrationVerificationService _verification;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IConfiguration _config;
    private readonly ILogger<VerifyAccountModel> _logger;

    public VerifyAccountModel(
        ApplicationUserLookup userLookup,
        TrialRegistrationVerificationService verification,
        UserManager<ApplicationUser> users,
        IConfiguration config,
        ILogger<VerifyAccountModel> logger)
    {
        _userLookup = userLookup;
        _verification = verification;
        _users = users;
        _config = config;
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
    public string? MaskedDestination { get; private set; }
    public string VerificationChannelLabel { get; private set; } = "email";
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
        var channel = !string.IsNullOrWhiteSpace(user.Email) && AccountVerificationPolicy.CanVerifyViaEmail(_config)
            ? RegistrationVerificationChannel.Email
            : RegistrationVerificationChannel.Sms;
        var destination = channel == RegistrationVerificationChannel.Email
            ? user.Email!
            : user.PhoneNumber ?? "";

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
                MaskedDestination = outcome.MaskedDestination;
                VerificationChannelLabel = channel == RegistrationVerificationChannel.Email ? "email" : "mobile";
                return Page();
            }

            ErrorMessage = outcome.ErrorMessage ?? "Could not send verification code.";
            return Page();
        }
        catch (ClinicalEmailNotConfiguredException ex)
        {
            ErrorMessage = ex.Message;
            return Page();
        }
        catch (ClinicalSmsNotConfiguredException ex)
        {
            ErrorMessage = ex.Message;
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

    public sealed class VerifyInput
    {
        [Required, Display(Name = "Username")]
        public string Username { get; set; } = string.Empty;
    }
}
