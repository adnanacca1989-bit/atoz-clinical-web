using System.ComponentModel.DataAnnotations;

using AtoZClinical.Infrastructure;

using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;

namespace AtoZClinical.Web.Pages.Register;

[EnableRateLimiting("register")]
public class ClinicModel : CaptchaPageModel
{
    private readonly VendorClinicService _vendor;
    private readonly CaptchaService _captcha;
    private readonly RegistrationEmailService _registrationEmail;
    private readonly IConfiguration _config;
    private readonly ILogger<ClinicModel> _logger;

    public ClinicModel(
        VendorClinicService vendor,
        CaptchaService captcha,
        RegistrationEmailService registrationEmail,
        IConfiguration config,
        ILogger<ClinicModel> logger)
    {
        _vendor = vendor;
        _captcha = captcha;
        _registrationEmail = registrationEmail;
        _config = config;
        _logger = logger;
    }



    [BindProperty]

    public RegisterInput Input { get; set; } = new();



    public bool Registered { get; private set; }
    public bool EmailConfirmationSent { get; private set; }
    public bool EmailConfirmationFailed { get; private set; }
    public string? EmailConfirmationErrorMessage { get; private set; }
    public string? ClinicName { get; private set; }
    public string? AdminUsername { get; private set; }



    public static ClinicalModuleCatalog.ModuleGroup[] ModuleGroups => ClinicalModuleCatalog.Groups;



    public void OnGet()

    {

        if (Input.EnabledModuleGroups.Count == 0)

            Input.EnabledModuleGroups = ModuleGroups.Select(g => g.Key).ToList();

    }



    public async Task<IActionResult> OnPostAsync()

    {

        if (!ModelState.IsValid) return Page();

        if (!await ValidateCaptchaAsync(_captcha)) return Page();

        if (!string.IsNullOrWhiteSpace(Input.Email) && !SmtpEmailConfiguration.IsEmailConfigured(_config))
        {
            var missing = SmtpEmailConfiguration.GetMissingVariables(_config);
            _logger.LogError(
                "Clinic registration blocked: SMTP not configured. Missing: {Missing}",
                string.Join(", ", missing));
            ModelState.AddModelError(string.Empty, new ClinicalEmailNotConfiguredException(missing).Message);
            return Page();
        }



        try

        {

            var (clinic, admin, _) = await _vendor.RegisterPublicClinicAsync(new PublicClinicRegistrationRequest

            {

                ClinicName = Input.ClinicName,

                ContactPerson = Input.ContactPerson,

                Email = Input.Email,

                Phone = Input.Phone,

                City = Input.City,

                Country = Input.Country,

                AdminUsername = Input.AdminUsername,

                AdminPassword = Input.AdminPassword,

                EnabledModuleGroups = Input.EnabledModuleGroups.ToArray()

            });



            if (!string.IsNullOrWhiteSpace(Input.Email) && !admin.EmailConfirmed)
            {
                var sendOutcome = await _registrationEmail.SendEmailConfirmationAsync(admin, Input.Email);
                EmailConfirmationSent = sendOutcome.Result == EmailConfirmationSendResult.Sent;
                EmailConfirmationFailed = sendOutcome.Result == EmailConfirmationSendResult.Failed;
                EmailConfirmationErrorMessage = sendOutcome.ErrorMessage;

                if (EmailConfirmationSent)
                    _logger.LogInformation("Clinic registration confirmation email sent for admin {UserId}", admin.Id);
                else if (EmailConfirmationFailed)
                    _logger.LogError(
                        "Clinic registration confirmation email failed for admin {UserId}: {Reason}",
                        admin.Id, EmailConfirmationErrorMessage);
            }

            Registered = true;

            ClinicName = clinic.Name;

            AdminUsername = admin.UserName;

            return Page();

        }

        catch (ClinicalEmailNotConfiguredException ex)
        {
            _logger.LogError(ex, "Clinic registration confirmation blocked: SMTP not configured");
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }
        catch (Exception ex)

        {

            ModelState.AddModelError(string.Empty, ex.Message);

            return Page();

        }

    }



    public sealed class RegisterInput

    {

        [Required, Display(Name = "Clinic Name")]

        public string ClinicName { get; set; } = string.Empty;



        [Required, Display(Name = "Contact Person")]

        public string ContactPerson { get; set; } = string.Empty;



        [EmailAddress]

        public string? Email { get; set; }

        public string? Phone { get; set; }

        public string? City { get; set; }

        public string? Country { get; set; }



        [Required, Display(Name = "Admin Username")]

        public string AdminUsername { get; set; } = string.Empty;



        [Required, MinLength(12), Display(Name = "Admin Password")]

        public string AdminPassword { get; set; } = string.Empty;



        public List<string> EnabledModuleGroups { get; set; } = [];

        [Range(typeof(bool), "true", "true", ErrorMessage = "You must accept the Terms of Service and Privacy Policy.")]
        [Display(Name = "I agree to the Terms of Service and Privacy Policy")]
        public bool AcceptTerms { get; set; }
    }

}


