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

    /// <summary>today = focus on today; period = custom date range.</summary>
    [BindProperty(SupportsGet = true)]
    public string Scope { get; set; } = "period";

    public string ClinicName { get; private set; } = string.Empty;
    public bool IsTodayScope => Scope.Equals("today", StringComparison.OrdinalIgnoreCase);

    public int ActiveDoctorCount { get; private set; }
    public int NewRegistrations { get; private set; }

    public int TodayPending { get; private set; }
    public int TodayUnderProcess { get; private set; }
    public int TodayCompleted { get; private set; }
    public int TodayConfirmed { get; private set; }

    public int PeriodPending { get; private set; }
    public int PeriodCancelled { get; private set; }
    public int PeriodConfirmed { get; private set; }
    public int PeriodUnderProcess { get; private set; }
    public int PeriodCompleted { get; private set; }

    public decimal InvoiceTotal { get; private set; }
    public decimal CashReceived { get; private set; }
    public decimal OutstandingAr { get; private set; }

    public async Task<IActionResult> OnGetAsync() => await RunAsync();

    public Task<IActionResult> OnPostRunAsync() => RunAsync();

    public IActionResult OnPostRefreshAsync() => RedirectToPage(new { FromDate, ToDate, Scope });

    private async Task<IActionResult> RunAsync()
    {
        try
        {
            var clinic = await _context.GetCurrentClinicAsync();
            ClinicName = clinic?.Name ?? "Clinic";
            if (clinic is null)
                return Page();

            var today = DateTime.Today;
            var from = IsTodayScope ? today : FromDate.Date;
            var to = IsTodayScope ? today : ToDate.Date;
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
                .Select(p => new { p.Status, p.AppointmentDate, p.CreatedAt })
                .ToListAsync();

            var todayStatuses = patientRows
                .Where(p => p.AppointmentDate.HasValue && p.AppointmentDate.Value.Date == today)
                .Select(p => p.Status);

            TodayPending = CountStatus(todayStatuses, PatientVisitStatuses.Pending);
            TodayConfirmed = CountStatus(todayStatuses, PatientVisitStatuses.Confirmed);
            TodayUnderProcess = CountStatus(todayStatuses, PatientVisitStatuses.UnderProcess);
            TodayCompleted = CountStatus(todayStatuses, PatientVisitStatuses.Completed);

            var periodStatuses = patientRows
                .Where(p => p.AppointmentDate.HasValue &&
                            p.AppointmentDate.Value.Date >= from &&
                            p.AppointmentDate.Value.Date <= to)
                .Select(p => p.Status);

            PeriodPending = CountStatus(periodStatuses, PatientVisitStatuses.Pending);
            PeriodCancelled = CountStatus(periodStatuses, PatientVisitStatuses.Cancelled);
            PeriodConfirmed = CountStatus(periodStatuses, PatientVisitStatuses.Confirmed);
            PeriodUnderProcess = CountStatus(periodStatuses, PatientVisitStatuses.UnderProcess);
            PeriodCompleted = CountStatus(periodStatuses, PatientVisitStatuses.Completed);

            NewRegistrations = patientRows.Count(p =>
                p.CreatedAt.Date >= from && p.CreatedAt.Date <= to);

            var invoices = await _db.Invoices
                .AsNoTracking()
                .Where(i => i.ClinicId == clinic.Id)
                .Select(i => new { i.InvoiceDate, i.TotalAmount, i.BalanceDue })
                .ToListAsync();

            var inPeriod = invoices.Where(i => i.InvoiceDate.Date >= from && i.InvoiceDate.Date <= to).ToList();
            InvoiceTotal = inPeriod.Sum(i => i.TotalAmount);
            OutstandingAr = invoices.Sum(i => i.BalanceDue);

            var receipts = await _db.CashReceipts
                .AsNoTracking()
                .Where(r => r.ClinicId == clinic.Id)
                .Select(r => new { r.ReceiptDate, r.Amount })
                .ToListAsync();

            CashReceived = receipts
                .Where(r => r.ReceiptDate.Date >= from && r.ReceiptDate.Date <= to)
                .Sum(r => r.Amount);
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
