using AtoZClinical.Core.Entities;
using AtoZClinical.Core.Enums;
using AtoZClinical.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.Vendor;

public class ClientsModel : PageModel
{
    private readonly VendorClinicService _vendor;

    public ClientsModel(VendorClinicService vendor) => _vendor = vendor;

    [BindProperty(SupportsGet = true)]
    public string? Filter { get; set; }

    public List<Clinic> Clinics { get; private set; } = [];

    public static bool IsTrialPlan(Clinic clinic) =>
        string.Equals(clinic.PlanName, "Trial", StringComparison.OrdinalIgnoreCase);

    public static int? TrialRemainingDays(Clinic clinic)
    {
        if (!IsTrialPlan(clinic) || !clinic.LicenseExpires.HasValue)
            return null;

        return (clinic.LicenseExpires.Value.Date - DateTime.UtcNow.Date).Days;
    }

    public static int? TrialDaysUsed(Clinic clinic)
    {
        if (!IsTrialPlan(clinic) || !clinic.LicenseExpires.HasValue)
            return null;

        var totalDays = Math.Max(1, (clinic.LicenseExpires.Value.Date - clinic.CreatedAt.Date).Days);
        var remaining = TrialRemainingDays(clinic) ?? 0;
        return Math.Clamp(totalDays - Math.Max(0, remaining), 0, totalDays);
    }

    public static int? TrialTotalDays(Clinic clinic)
    {
        if (!IsTrialPlan(clinic) || !clinic.LicenseExpires.HasValue)
            return null;

        return Math.Max(1, (clinic.LicenseExpires.Value.Date - clinic.CreatedAt.Date).Days);
    }

    public async Task OnGetAsync()
    {
        Clinics = await _vendor.ListClinicsAsync();
        if (!string.IsNullOrWhiteSpace(Filter) && Filter != "All")
        {
            if (string.Equals(Filter, "Trial", StringComparison.OrdinalIgnoreCase))
                Clinics = Clinics.Where(c => string.Equals(c.PlanName, "Trial", StringComparison.OrdinalIgnoreCase)).ToList();
            else if (Enum.TryParse<ClinicStatus>(Filter, true, out var status))
                Clinics = Clinics.Where(c => c.Status == status).ToList();
        }
    }

    public async Task<IActionResult> OnPostSetPendingAsync(Guid id)
    {
        await _vendor.UpdateClinicStatusAsync(id, ClinicStatus.Pending);
        return RedirectToPage(new { Filter });
    }

    public async Task<IActionResult> OnPostSetActiveAsync(Guid id)
    {
        await _vendor.ActivateClinicAsync(id);
        return RedirectToPage(new { Filter });
    }

    public async Task<IActionResult> OnPostSetSuspendedAsync(Guid id)
    {
        await _vendor.SuspendClinicAsync(id);
        return RedirectToPage(new { Filter });
    }

    public async Task<IActionResult> OnPostSetExpiredAsync(Guid id)
    {
        await _vendor.UpdateClinicStatusAsync(id, ClinicStatus.Expired);
        return RedirectToPage(new { Filter });
    }

    public async Task<IActionResult> OnPostRenewAsync(Guid id, DateTime licenseExpires, string? planName)
    {
        await _vendor.RenewLicenseAsync(id, licenseExpires, planName);
        return RedirectToPage(new { Filter });
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        try
        {
            await _vendor.DeleteClinicAsync(id);
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToPage(new { Filter });
    }
}
