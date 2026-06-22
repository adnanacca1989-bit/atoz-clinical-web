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
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(ClinicContextService context, ClinicalDbContext db, ILogger<IndexModel> logger)
    {
        _context = context;
        _db = db;
        _logger = logger;
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
        try
        {
            var clinic = await _context.GetCurrentClinicAsync();
            ClinicName = clinic?.Name ?? "Clinic";
            if (clinic is null)
                return Page();

            var from = FromDate.Date;
            var to = ToDate.Date;
            if (to < from)
                (from, to) = (to, from);

            var doctors = await _db.Doctors
                .AsNoTracking()
                .Where(d => d.ClinicId == clinic.Id)
                .Select(d => d.Status)
                .ToListAsync();

            ActiveDoctorCount = doctors.Count(status =>
                string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase));

            var patientRows = await _db.Patients
                .AsNoTracking()
                .Where(p => p.ClinicId == clinic.Id)
                .Select(p => new { p.Status, p.AppointmentDate })
                .ToListAsync();

            var statusesInRange = patientRows
                .Where(p => p.AppointmentDate.HasValue &&
                            p.AppointmentDate.Value.Date >= from &&
                            p.AppointmentDate.Value.Date <= to)
                .Select(p => p.Status);

            PendingPatients = CountStatus(statusesInRange, PatientVisitStatuses.Pending);
            CancelledPatients = CountStatus(statusesInRange, PatientVisitStatuses.Cancelled);
            ConfirmedPatients = CountStatus(statusesInRange, PatientVisitStatuses.Confirmed);
            UnderProcessPatients = CountStatus(statusesInRange, PatientVisitStatuses.UnderProcess);
            CompletedPatients = CountStatus(statusesInRange, PatientVisitStatuses.Completed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dashboard failed for request {TraceId}", HttpContext.TraceIdentifier);
            throw;
        }

        return Page();
    }

    private static int CountStatus(IEnumerable<string?> statuses, string status) =>
        statuses.Count(value => PatientVisitStatuses.Normalize(value)
            .Equals(status, StringComparison.OrdinalIgnoreCase));
}
