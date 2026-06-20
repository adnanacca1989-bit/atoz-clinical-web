using AtoZClinical.Core.Entities;
using AtoZClinical.Core.Enums;
using AtoZClinical.Infrastructure.Identity;
using AtoZClinical.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.Vendor;

public class DetailsModel : PageModel
{
    private readonly VendorClinicService _vendor;

    public DetailsModel(VendorClinicService vendor) => _vendor = vendor;

    public Clinic? Clinic { get; private set; }
    public List<ApplicationUser> Users { get; private set; } = [];
    public string? NewUsername { get; private set; }
    public string? NewPassword { get; private set; }
    public bool Created { get; private set; }
    public string? Message { get; private set; }

    [BindProperty]
    public RenewInput Renew { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(Guid id, bool created = false)
    {
        Clinic = await _vendor.GetClinicAsync(id);
        if (Clinic is null) return NotFound();

        Users = await _vendor.GetClinicUsersAsync(id);
        Created = created;
        NewUsername = TempData["NewUsername"]?.ToString();
        NewPassword = TempData["NewPassword"]?.ToString();
        Message = TempData["Message"]?.ToString();
        Renew = RenewInput.FromClinic(Clinic);
        return Page();
    }

    public async Task<IActionResult> OnPostRenewAsync(Guid id)
    {
        Clinic = await _vendor.GetClinicAsync(id);
        if (Clinic is null) return NotFound();

        await _vendor.RenewLicenseAsync(id, Renew.LicenseExpires, Renew.PlanName, Renew.MaxUsers);
        TempData["Message"] = "License renewed and clinic activated.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostActivateAsync(Guid id)
    {
        await _vendor.ActivateClinicAsync(id);
        TempData["Message"] = "Clinic activated.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostSuspendAsync(Guid id)
    {
        await _vendor.SuspendClinicAsync(id);
        TempData["Message"] = "Clinic suspended.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostSetExpiredAsync(Guid id)
    {
        await _vendor.UpdateClinicStatusAsync(id, ClinicStatus.Expired);
        TempData["Message"] = "Clinic marked as expired.";
        return RedirectToPage(new { id });
    }

    public sealed class RenewInput
    {
        public DateTime LicenseExpires { get; set; } = DateTime.Today.AddYears(1);
        public string PlanName { get; set; } = "Standard";
        public int MaxUsers { get; set; } = 25;

        public static RenewInput FromClinic(Clinic clinic) => new()
        {
            LicenseExpires = clinic.LicenseExpires?.Date ?? DateTime.Today.AddYears(1),
            PlanName = clinic.PlanName,
            MaxUsers = clinic.MaxUsers
        };
    }
}
