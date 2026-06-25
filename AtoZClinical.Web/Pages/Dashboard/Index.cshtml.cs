using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.Dashboard;

public class IndexModel : PageModel
{
    private readonly ClinicContextService _context;
    private readonly DashboardService _dashboard;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(ClinicContextService context, DashboardService dashboard, ILogger<IndexModel> logger)
    {
        _context = context;
        _dashboard = dashboard;
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

            var summary = await _dashboard.GetSummaryAsync(clinic.Id, from, to, IsTodayScope);

            ActiveDoctorCount = summary.ActiveDoctorCount;
            NewRegistrations = summary.NewRegistrations;
            TodayPending = summary.TodayPending;
            TodayUnderProcess = summary.TodayUnderProcess;
            TodayCompleted = summary.TodayCompleted;
            TodayConfirmed = summary.TodayConfirmed;
            PeriodPending = summary.PeriodPending;
            PeriodCancelled = summary.PeriodCancelled;
            PeriodConfirmed = summary.PeriodConfirmed;
            PeriodUnderProcess = summary.PeriodUnderProcess;
            PeriodCompleted = summary.PeriodCompleted;
            InvoiceTotal = summary.InvoiceTotal;
            CashReceived = summary.CashReceived;
            OutstandingAr = summary.OutstandingAr;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dashboard failed for request {TraceId}", HttpContext.TraceIdentifier);
            throw;
        }

        return Page();
    }
}
