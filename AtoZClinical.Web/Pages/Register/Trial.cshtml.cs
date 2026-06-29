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
    public string? DeliveryMessage { get; private set; }
    public string? ClinicName { get; private set; }
    public string? ClinicCode { get; private set; }
    public string? AdminUsername { get; private set; }
    public string TrialShareUrl { get; private set; } = "";
    public bool OtpDeliveredViaLog { get; private set; }
    public bool UsesLogOnlyOtp { get; private set; }
    public bool EmailDeliveryAvailable { get; private set; }
    public bool SmsDeliveryAvailable { get; private set; }
    public bool WhatsAppDeliveryAvailable { get; private set; }

    public void OnGet(string? verify)
    {
        TrialShareUrl = BuildTrialUrl();
        LoadDeliveryStatus();
        if (!string.IsNullOrWhiteSpace(verify))
            Input.AdminUsername = verify.Trim();
    }

    public async Task<IActionResult> OnPostRegisterAsync()
    {
        TrialShareUrl = BuildTrialUrl();
        LoadDeliveryStatus();
        ValidateContactMethod();
        if (!ModelState.IsValid) return Page();
        if (!await ValidateCaptchaAsync(_captcha)) return Page();

        try
        {
            var (channel, destination) = ResolveRegistrationChannel();
            var (clinic, admin, _) = await _vendor.RegisterTrialClinicAsync(new TrialClinicRegistrationRequest
            {
                ClinicName = Input.ClinicName,
                AdminUsername = Input.AdminUsername,
                AdminPassword = Input.AdminPassword,
                Email = Input.UseEmail ? Input.Email?.Trim() : null,
                Phone = Input.UseEmail ? null : Input.Mobile?.Trim()
            });

            ClinicName = clinic.Name;
            ClinicCode = clinic.ClinicCode;
            AdminUsername = admin.UserName;
            PendingUserId = admin.Id;

            if (admin.EmailConfirmed)
            {
                Registered = true;
                ReadyToSignIn = true;
                return Page();
            }

            var sendOutcome = await _verification.SendCodeAsync(admin, channel, destination);
            ApplySendOutcome(sendOutcome);

            if (sendOutcome.Result == VerificationCodeSendResult.Sent)
            {
                AwaitingVerification = true;
                return Page();
            }

            Registered = true;
            CodeSendFailed = true;
            CodeSendErrorMessage = sendOutcome.ErrorMessage ?? "Verification code could not be sent.";
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
        LoadDeliveryStatus();
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
        LoadDeliveryStatus();
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

            var (channel, destination) = ResolveRegistrationChannel();
            var sendOutcome = await _verification.SendCodeAsync(user, channel, destination);
            AwaitingVerification = true;
            ApplySendOutcome(sendOutcome);

            if (sendOutcome.Result == VerificationCodeSendResult.Sent)
            {
                CodeResent = true;
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

    private void ApplySendOutcome(VerificationCodeSendOutcome sendOutcome)
    {
        MaskedDestination = sendOutcome.MaskedDestination;
        OtpDeliveredViaLog = sendOutcome.DeliveredViaLog;
        DeliveryMessage = OtpDeliveryConfiguration.BuildSentMessage(sendOutcome.DeliveryMethod, sendOutcome.MaskedDestination);
    }

    private (RegistrationVerificationChannel Channel, string Destination) ResolveRegistrationChannel()
    {
        if (Input.UseEmail)
            return (RegistrationVerificationChannel.Email, Input.Email!.Trim());

        var channel = OtpDeliveryConfiguration.ResolveMobileChannel(_config, Input.ContactMethod);
        return (channel, Input.Mobile!.Trim());
    }

    private void LoadDeliveryStatus()
    {
        EmailDeliveryAvailable = OtpDeliveryConfiguration.IsEmailAvailable(_config);
        SmsDeliveryAvailable = OtpDeliveryConfiguration.IsSmsAvailable(_config);
        WhatsAppDeliveryAvailable = OtpDeliveryConfiguration.IsWhatsAppAvailable(_config);
        UsesLogOnlyOtp = OtpDeliveryConfiguration.UsesServerLogFallback(_config);

        if (!Input.UseEmail && !SmsDeliveryAvailable && WhatsAppDeliveryAvailable)
            Input.ContactMethod = "whatsapp";
        else if (!Input.UseEmail && SmsDeliveryAvailable && !WhatsAppDeliveryAvailable)
            Input.ContactMethod = "sms";
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
