using System.ComponentModel.DataAnnotations;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace AtoZClinical.Web.Pages.Register;

[EnableRateLimiting("register")]
public class TrialModel : CaptchaPageModel
{
    private readonly VendorClinicService _vendor;
    private readonly CaptchaService _captcha;
    private readonly IConfiguration _config;
    private readonly ILogger<TrialModel> _logger;

    public TrialModel(
        VendorClinicService vendor,
        CaptchaService captcha,
        IConfiguration config,
        ILogger<TrialModel> logger)
    {
        _vendor = vendor;
        _captcha = captcha;
        _config = config;
        _logger = logger;
    }

    [BindProperty]
    public TrialInput Input { get; set; } = new();

    public bool Registered { get; private set; }
    public string? ClinicName { get; private set; }
    public string? ClinicCode { get; private set; }
    public string? AdminUsername { get; private set; }
    public string TrialShareUrl { get; private set; } = "";

    public void OnGet(string? verify)
    {
        TrialShareUrl = BuildTrialUrl();
        if (!string.IsNullOrWhiteSpace(verify))
            Input.AdminUsername = verify.Trim();
    }

    public async Task<IActionResult> OnPostRegisterAsync()
    {
        TrialShareUrl = BuildTrialUrl();
        if (!ModelState.IsValid) return Page();
        if (!await ValidateCaptchaAsync(_captcha)) return Page();

        try
        {
            var (clinic, admin, _) = await _vendor.RegisterTrialClinicAsync(new TrialClinicRegistrationRequest
            {
                ClinicName = Input.ClinicName,
                AdminUsername = Input.AdminUsername,
                AdminPassword = Input.AdminPassword,
                Email = Input.Email.Trim()
            });

            ClinicName = clinic.Name;
            ClinicCode = clinic.ClinicCode;
            AdminUsername = admin.UserName;
            Registered = true;
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Trial registration failed");
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
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

        [Required, EmailAddress, Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Range(typeof(bool), "true", "true", ErrorMessage = "You must accept the Terms of Service and Privacy Policy.")]
        [Display(Name = "I agree to the Terms of Service and Privacy Policy")]
        public bool AcceptTerms { get; set; }
    }
}
