using System.ComponentModel.DataAnnotations;
using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Identity;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace AtoZClinical.Web.Pages.Register;

[EnableRateLimiting("register")]
public class TrialModel : CaptchaPageModel
{
    private readonly VendorClinicService _vendor;
    private readonly CaptchaService _captcha;
    private readonly TrialRegistrationVerificationService _verification;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IConfiguration _config;
    private readonly ILogger<TrialModel> _logger;

    public TrialModel(
        VendorClinicService vendor,
        CaptchaService captcha,
        TrialRegistrationVerificationService verification,
        UserManager<ApplicationUser> users,
        IConfiguration config,
        ILogger<TrialModel> logger)
    {
        _vendor = vendor;
        _captcha = captcha;
        _verification = verification;
        _users = users;
        _config = config;
        _logger = logger;
    }

    [BindProperty]
    public TrialInput Input { get; set; } = new();

    [BindProperty]
    public string? VerificationCode { get; set; }

    [BindProperty]
    public string? PendingUserId { get; set; }

    public bool AwaitingVerification { get; private set; }
    public bool Registered { get; private set; }
    public bool Verified { get; private set; }
    public bool ReadyToSignIn { get; private set; }
    public bool CodeSendFailed { get; private set; }
    public bool VerificationFailed { get; private set; }
    public bool CodeResent { get; private set; }
    public string? CodeSendErrorMessage { get; private set; }
    public string? VerificationErrorMessage { get; private set; }
    public string? MaskedDestination { get; private set; }
    public string VerificationChannelLabel { get; private set; } = "email";
    public string? ClinicName { get; private set; }
    public string? ClinicCode { get; private set; }
    public string? AdminUsername { get; private set; }
    public string TrialShareUrl { get; private set; } = "";
    public bool OtpDeliveredViaLog { get; private set; }
    public bool UsesLogOnlyOtp { get; private set; }

    public void OnGet(string? verify)
    {
        TrialShareUrl = BuildTrialUrl();
        LoadChannelAvailability();
        if (!string.IsNullOrWhiteSpace(verify))
            Input.AdminUsername = verify.Trim();
    }

    public async Task<IActionResult> OnPostRegisterAsync()
    {
        TrialShareUrl = BuildTrialUrl();
        LoadChannelAvailability();
        ValidateContactMethod();
        if (!ModelState.IsValid) return Page();
        if (!await ValidateCaptchaAsync(_captcha)) return Page();

        try
        {
            var useEmail = Input.UseEmail;
            var (clinic, admin, _) = await _vendor.RegisterTrialClinicAsync(new TrialClinicRegistrationRequest
            {
                ClinicName = Input.ClinicName,
                AdminUsername = Input.AdminUsername,
                AdminPassword = Input.AdminPassword,
                Email = useEmail ? Input.Email?.Trim() : null,
                Phone = useEmail ? null : Input.Mobile?.Trim()
            });

            ClinicName = clinic.Name;
            ClinicCode = clinic.ClinicCode;
            AdminUsername = admin.UserName;
            PendingUserId = admin.Id;

            if (admin.EmailConfirmed)
            {
                Registered = true;
                ReadyToSignIn = true;
                _logger.LogInformation(
                    "Trial clinic {ClinicId} registered — admin {UserId} auto-confirmed.",
                    clinic.Id, admin.Id);
                return Page();
            }

            var channel = useEmail
                ? RegistrationVerificationChannel.Email
                : RegistrationVerificationChannel.Sms;
            var destination = useEmail ? Input.Email!.Trim() : Input.Mobile!.Trim();
            var sendOutcome = await _verification.SendCodeAsync(admin, channel, destination);

            if (sendOutcome.Result == VerificationCodeSendResult.Sent)
            {
                AwaitingVerification = true;
                MaskedDestination = sendOutcome.MaskedDestination;
                OtpDeliveredViaLog = sendOutcome.DeliveredViaLog;
                VerificationChannelLabel = channel == RegistrationVerificationChannel.Email ? "email" : "mobile";
                return Page();
            }

            Registered = true;
            CodeSendFailed = true;
            CodeSendErrorMessage = sendOutcome.ErrorMessage
                ?? "Verification code could not be sent.";
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Trial registration failed");
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostVerifyAsync()
    {
        TrialShareUrl = BuildTrialUrl();
        LoadChannelAvailability();
        if (string.IsNullOrWhiteSpace(PendingUserId) || string.IsNullOrWhiteSpace(VerificationCode))
        {
            ModelState.AddModelError(string.Empty, "Enter the 4-digit verification code.");
            AwaitingVerification = true;
            return Page();
        }

        var outcome = await _verification.VerifyCodeAsync(PendingUserId, VerificationCode);
        if (outcome.Result == VerificationCodeVerifyResult.Verified
            || outcome.Result == VerificationCodeVerifyResult.AlreadyVerified)
        {
            Registered = true;
            Verified = true;
            return Page();
        }

        AwaitingVerification = true;
        VerificationFailed = true;
        VerificationErrorMessage = outcome.ErrorMessage ?? "Verification failed.";
        return Page();
    }

    public async Task<IActionResult> OnPostResendCodeAsync()
    {
        TrialShareUrl = BuildTrialUrl();
        LoadChannelAvailability();
        if (string.IsNullOrWhiteSpace(PendingUserId))
        {
            ModelState.AddModelError(string.Empty, "Session expired. Please register again.");
            return Page();
        }

        var user = await _users.FindByIdAsync(PendingUserId);
        if (user is null)
        {
            ModelState.AddModelError(string.Empty, "Account not found. Please register again.");
            return Page();
        }

        try
        {
            ValidateContactMethod();
            if (!ModelState.IsValid)
            {
                AwaitingVerification = true;
                return Page();
            }

            var channel = Input.UseEmail
                ? RegistrationVerificationChannel.Email
                : RegistrationVerificationChannel.Sms;
            var destination = Input.UseEmail ? Input.Email!.Trim() : Input.Mobile!.Trim();
            var sendOutcome = await _verification.SendCodeAsync(user, channel, destination);

            AwaitingVerification = true;
            VerificationChannelLabel = channel == RegistrationVerificationChannel.Email ? "email" : "mobile";

            if (sendOutcome.Result == VerificationCodeSendResult.Sent)
            {
                CodeResent = true;
                MaskedDestination = sendOutcome.MaskedDestination;
                OtpDeliveredViaLog = sendOutcome.DeliveredViaLog;
                return Page();
            }

            VerificationFailed = true;
            VerificationErrorMessage = sendOutcome.ErrorMessage ?? "Could not resend code.";
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Resend verification code failed for user {UserId}", PendingUserId);
            AwaitingVerification = true;
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }
    }

    private void LoadChannelAvailability()
    {
        UsesLogOnlyOtp = AccountVerificationPolicy.UsesLogOnlyDelivery(_config);
    }

    private void ValidateContactMethod()
    {
        if (Input.UseEmail)
        {
            if (string.IsNullOrWhiteSpace(Input.Email))
                ModelState.AddModelError(nameof(Input.Email), "Email is required.");
            else if (!new EmailAddressAttribute().IsValid(Input.Email))
                ModelState.AddModelError(nameof(Input.Email), "Enter a valid email address.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(Input.Mobile))
                ModelState.AddModelError(nameof(Input.Mobile), "Mobile number is required.");
            else if (!PhoneNumberNormalizer.TryNormalize(Input.Mobile, out _))
                ModelState.AddModelError(nameof(Input.Mobile), "Enter a valid mobile number (e.g. 07xx xxx xxxx).");
        }
    }

    private string BuildTrialUrl()
    {
        var configured = _config["App:PublicBaseUrl"]?.Trim().TrimEnd('/');
        var baseUrl = !string.IsNullOrWhiteSpace(configured)
            ? configured
            : $"{Request.Scheme}://{Request.Host}";
        return $"{baseUrl}/Register/Trial";
    }

    public sealed class TrialInput
    {
        [Required, Display(Name = "Clinical Name")]
        public string ClinicName { get; set; } = string.Empty;

        [Required, Display(Name = "Username")]
        public string AdminUsername { get; set; } = string.Empty;

        [Required, MinLength(12), DataType(DataType.Password), Display(Name = "Password")]
        public string AdminPassword { get; set; } = string.Empty;

        [Display(Name = "Contact method")]
        public string ContactMethod { get; set; } = "email";

        [EmailAddress, Display(Name = "Email")]
        public string? Email { get; set; }

        [Phone, Display(Name = "Mobile number")]
        public string? Mobile { get; set; }

        public bool UseEmail => string.Equals(ContactMethod, "email", StringComparison.OrdinalIgnoreCase);

        [Range(typeof(bool), "true", "true", ErrorMessage = "You must accept the Terms of Service and Privacy Policy.")]
        [Display(Name = "I agree to the Terms of Service and Privacy Policy")]
        public bool AcceptTerms { get; set; }
    }
}
