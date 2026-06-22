using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Web.Pages.Dashboard;

public class IndexModel : PageModel
{
    private readonly ClinicContextService _context;
    private readonly ClinicalDbContext _db;

    public IndexModel(ClinicContextService context, ClinicalDbContext db)
    {
        _context = context;
        _db = db;
    }

    [BindProperty(SupportsGet = true)]
    public DateTime FromDate { get; set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    [BindProperty(SupportsGet = true)]
    public DateTime ToDate { get; set; } = DateTime.Today;

    public string ClinicName { get; private set; } = string.Empty;
    public int ActiveDoctorCount { get; private set; }
    public int PendingPatients { get; private set; }
    public int CancelledPatients { get; private set; }
    public int ConfirmedPatients { get; private set; }
    public int UnderProcessPatients { get; private set; }
    public int CompletedPatients { get; private set; }

    public async Task<IActionResult> OnGetAsync() => await RunAsync();

    public Task<IActionResult> OnPostRunAsync() => RunAsync();

    public IActionResult OnPostRefreshAsync() => RedirectToPage(new { FromDate, ToDate });

    private async Task<IActionResult> RunAsync()
    {
        var clinic = await _context.GetCurrentClinicAsync();
        ClinicName = clinic?.Name ?? "Clinic";
        if (clinic is null) return Forbid();

        ActiveDoctorCount = await _db.Doctors.CountAsync(d =>
            d.ClinicId == clinic.Id && d.Status == "Active");

        var from = FromDate.Date;
        var to = ToDate.Date;
        var patients = await _db.Patients
            .Where(p => p.ClinicId == clinic.Id &&
                        p.AppointmentDate != null &&
                        p.AppointmentDate >= from &&
                        p.AppointmentDate <= to)
            .ToListAsync();

        PendingPatients = CountStatus(patients, PatientVisitStatuses.Pending);
        CancelledPatients = CountStatus(patients, PatientVisitStatuses.Cancelled);
        ConfirmedPatients = CountStatus(patients, PatientVisitStatuses.Confirmed);
        UnderProcessPatients = CountStatus(patients, PatientVisitStatuses.UnderProcess);
        CompletedPatients = CountStatus(patients, PatientVisitStatuses.Completed);

        return Page();
    }

    private static int CountStatus(IEnumerable<Core.Entities.Patient> patients, string status) =>
        patients.Count(p => PatientVisitStatuses.Normalize(p.Status)
            .Equals(status, StringComparison.OrdinalIgnoreCase));
}
