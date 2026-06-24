using System.ComponentModel.DataAnnotations;
using AtoZClinical.Infrastructure;
using AtoZClinical.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.Register;

public class ClinicModel : PageModel
{
    private readonly VendorClinicService _vendor;

    public ClinicModel(VendorClinicService vendor) => _vendor = vendor;

    [BindProperty]
    public RegisterInput Input { get; set; } = new();

    public bool Registered { get; private set; }
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

    public sealed class RegisterInput
    {
        [Required, Display(Name = "Clinic Name")]
        public string ClinicName { get; set; } = string.Empty;

        [Required, Display(Name = "Contact Person")]
        public string ContactPerson { get; set; } = string.Empty;

        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? City { get; set; }
        public string? Country { get; set; }

        [Required, Display(Name = "Admin Username")]
        public string AdminUsername { get; set; } = string.Empty;

        [Required, MinLength(6), Display(Name = "Admin Password")]
        public string AdminPassword { get; set; } = string.Empty;

        public List<string> EnabledModuleGroups { get; set; } = [];
    }
}
