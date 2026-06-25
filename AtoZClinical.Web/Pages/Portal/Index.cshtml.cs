using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Web.Pages.Portal;

public class IndexModel : PageModel
{
    private readonly PatientPortalSession _session;
    private readonly PatientPortalService _portal;
    private readonly ClinicalDbContext _db;

    public IndexModel(PatientPortalSession session, PatientPortalService portal, ClinicalDbContext db)
    {
        _session = session;
        _portal = portal;
        _db = db;
    }

    public Patient? Patient { get; private set; }
    public List<Appointment> UpcomingAppointments { get; private set; } = [];
    public List<Prescription> RecentPrescriptions { get; private set; } = [];
    public List<Invoice> RecentInvoices { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        var session = _session.Get(HttpContext);
        if (session is null)
            return RedirectToPage("/Portal/Login");

        Patient = await _db.Patients.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ClinicId == session.ClinicId && p.Id == session.PatientId);
        if (Patient is null)
        {
            _session.SignOut(HttpContext);
            return RedirectToPage("/Portal/Login");
        }

        UpcomingAppointments = await _portal.GetUpcomingAppointmentsAsync(session.ClinicId, session.PatientId);
        RecentPrescriptions = await _portal.GetRecentPrescriptionsAsync(session.ClinicId, Patient.FullName);
        RecentInvoices = await _portal.GetRecentInvoicesAsync(session.ClinicId, Patient.FullName);
        return Page();
    }
}
