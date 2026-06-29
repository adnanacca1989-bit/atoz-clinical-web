using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AtoZClinical.Web.Pages.Reports;

public class PlStatementModel : PageModel
{
    private readonly ClinicalDbContext _db;
    private readonly ClinicContextService _clinicContext;
    private readonly ClinicalJournalSyncService _journalSync;
    private readonly JournalReportService _journal;
    private readonly ILogger<PlStatementModel> _logger;

    public PlStatementModel(
        ClinicalDbContext db,
        ClinicContextService clinicContext,
        ClinicalJournalSyncService journalSync,
        JournalReportService journal,
        ILogger<PlStatementModel> logger)
    {
        _db = db;
        _clinicContext = clinicContext;
        _journalSync = journalSync;
        _journal = journal;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)]
    public DateTime FromDate { get; set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    [BindProperty(SupportsGet = true)]
    public DateTime ToDate { get; set; } = DateTime.Today;

    [BindProperty(SupportsGet = true)]
    public string? DoctorName { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? PatientName { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool NonZero { get; set; }

    public FinancialStatementBuilder.ProfitAndLossSnapshot? Snapshot { get; private set; }
    public decimal JournalIncomeCredits { get; private set; }
    public decimal JournalExpenseDebits { get; private set; }

    public async Task<IActionResult> OnGetAsync() => await RunAsync();
    public Task<IActionResult> OnPostRunAsync() => RunAsync();
    public IActionResult OnPostClearAsync() => RedirectToPage();

    private async Task<IActionResult> RunAsync()
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();

        var id = clinicId.Value;
        await _journalSync.EnsureClinicalJournalsAsync(id);

        var periodActivity = await _journal.GetPeriodActivityAsync(
            id, FromDate, ToDate, PatientName, DoctorName);

        Snapshot = FinancialStatementBuilder.BuildProfitAndLoss(periodActivity, NonZero);

        JournalIncomeCredits = periodActivity
            .Where(r => r.AccountCategory.Equals("Income", StringComparison.OrdinalIgnoreCase))
            .Sum(r => r.PeriodCredit);
        JournalExpenseDebits = periodActivity
            .Where(r => r.AccountCategory.Equals("Expense", StringComparison.OrdinalIgnoreCase))
            .Sum(r => r.PeriodDebit);

        _logger.LogDebug(
            "P&L clinic {ClinicId} {From:d}-{To:d}: income={Income}, cogs={Cogs}, expenses={Expenses}, net={Net}",
            id, FromDate, ToDate,
            Snapshot.TotalIncome, Snapshot.TotalCostOfGoodsSold, Snapshot.TotalExpenses, Snapshot.NetIncome);

        return Page();
    }

    public async Task<IActionResult> OnPostExportAsync()
    {
        await RunAsync();
        if (Snapshot is null)
            return RedirectToPage();

        var exportRows = new List<object?[]>();
        foreach (var line in Snapshot.Income)
            exportRows.Add(["Income", line.Account, line.Amount]);
        exportRows.Add(["Income", "Total Income", Snapshot.TotalIncome]);
        exportRows.Add(["COGS", "Cost of Goods Sold", Snapshot.TotalCostOfGoodsSold]);
        exportRows.Add(["Summary", "Gross Profit", Snapshot.GrossProfit]);
        foreach (var line in Snapshot.Expenses)
            exportRows.Add(["Expense", line.Account, line.Amount]);
        exportRows.Add(["Summary", "Total Expenses", Snapshot.TotalExpenses]);
        exportRows.Add(["Summary", "Net Income", Snapshot.NetIncome]);

        var bytes = ReportExcelService.Export("PL Statement",
            ["Section", "Account / Detail", "Amount"],
            exportRows);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"PLStatement_{DateTime.Now:yyyyMMdd}.xlsx");
    }
}
