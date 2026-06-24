using System.ComponentModel.DataAnnotations;
using AtoZClinical.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.Register;

public class TrialModel : PageModel
{
    private readonly VendorClinicService _vendor;
    private readonly IConfiguration _config;

    public TrialModel(VendorClinicService vendor, IConfiguration config)
    {
        _vendor = vendor;
        _config = config;
    }

    [BindProperty]
    public TrialInput Input { get; set; } = new();

    public bool Registered { get; private set; }
    public string? ClinicName { get; private set; }
    public string? AdminUsername { get; private set; }
    public string TrialShareUrl { get; private set; } = "";

    public void OnGet()
    {
        TrialShareUrl = BuildTrialUrl();
    }

    public async Task<IActionResult> OnPostRegisterAsync()
    {
        TrialShareUrl = BuildTrialUrl();
        if (!ModelState.IsValid) return Page();

        try
        {
            var (clinic, admin, _) = await _vendor.RegisterTrialClinicAsync(new TrialClinicRegistrationRequest
            {
                ClinicName = Input.ClinicName,
                AdminUsername = Input.AdminUsername,
                AdminPassword = Input.AdminPassword,
                Email = Input.Email
            });

            Registered = true;
            ClinicName = clinic.Name;
            AdminUsername = admin.UserName;
            return Page();
        }
        catch (Exception ex)
        {
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

        [Required, MinLength(6), DataType(DataType.Password), Display(Name = "Password")]
        public string AdminPassword { get; set; } = string.Empty;

        [Required, EmailAddress, Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;
    }
}
