using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Web.Pages.Portal;

[DisableRateLimiting]
public class ReportsModel : PageModel
{
    private readonly PatientPortalSession _session;
    private readonly PatientPrintBundleService _bundleService;
    private readonly ClinicalDbContext _db;

    public ReportsModel(
        PatientPortalSession session,
        PatientPrintBundleService bundleService,
        ClinicalDbContext db)
    {
        _session = session;
        _bundleService = bundleService;
        _db = db;
    }

    public PatientPrintBundle Bundle { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var session = _session.Get(HttpContext);
        if (session is null)
            return RedirectToPage("/Portal/Login");

        HttpContext.Items[HttpContextClinicProvider.TenantClinicIdKey] = session.ClinicId;

        var patient = await _db.Patients.AsNoTracking()
            .ForClinic(session.ClinicId)
            .FirstOrDefaultAsync(p => p.Id == session.PatientId);
        if (patient is null)
        {
            _session.SignOut(HttpContext);
            return RedirectToPage("/Portal/Login");
        }

        Bundle = await _bundleService.BuildAsync(
            session.ClinicId,
            patient.FullName,
            patient.PatientNo,
            patient.DoctorName);

        return Page();
    }
}
