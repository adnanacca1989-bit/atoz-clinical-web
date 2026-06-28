using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Web.Pages.Reports;

public class BalanceSheetModel : PageModel
{
    private readonly ClinicalDbContext _db;
    private readonly ClinicContextService _clinicContext;
    private readonly ArReportService _ar;
    private readonly ClinicalJournalSyncService _journalSync;
    private readonly JournalReportService _journal;

    public BalanceSheetModel(
        ClinicalDbContext db,
        ClinicContextService clinicContext,
        ArReportService ar,
        ClinicalJournalSyncService journalSync,
        JournalReportService journal)
    {
        _db = db;
        _clinicContext = clinicContext;
        _ar = ar;
        _journalSync = journalSync;
        _journal = journal;
    }

    [BindProperty(SupportsGet = true)]
    public DateTime FromDate { get; set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    [BindProperty(SupportsGet = true)]
    public DateTime ToDate { get; set; } = DateTime.Today;

    public List<BsRow> Assets { get; private set; } = [];
    public List<BsRow> Liabilities { get; private set; } = [];
    public List<BsRow> Equity { get; private set; } = [];
    public decimal TotalAssets { get; private set; }
    public decimal TotalLiabilities { get; private set; }
    public decimal TotalEquity { get; private set; }
    public decimal TotalLiquidCash { get; private set; }
    public bool IsBalanced { get; private set; }
    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync() => await RunAsync();
    public Task<IActionResult> OnPostRunAsync() => RunAsync();
    public IActionResult OnPostClearAsync() => RedirectToPage();

    private async Task<IActionResult> RunAsync()
    {
        try
        {
            var clinicId = await _clinicContext.GetClinicIdAsync();
            if (clinicId is null) return Forbid();

            var id = clinicId.Value;
            var asOf = ToDate.Date;

            await _journalSync.EnsureClinicalJournalsAsync(id);

            var chartAccounts = await _db.ChartAccounts.ForClinic(id).AsNoTracking().ToListAsync();
            var trialBalance = await _journal.GetTrialBalanceAsync(id, asOf);
            var arReport = await _ar.BuildAsync(id, null, asOf, null, null, null);
            var openAr = Math.Max(0m, arReport.TotalEndingBalance);

            var snapshot = FinancialStatementBuilder.BuildBalanceSheet(trialBalance, openAr);
            var liquidAccounts = FinancialStatementBuilder.ResolveLiquidAccounts("All", chartAccounts);

            Assets = snapshot.Assets.Select(r => new BsRow(r.Account, r.Amount)).ToList();
            Liabilities = snapshot.Liabilities.Select(r => new BsRow(r.Account, r.Amount)).ToList();
            Equity = snapshot.Equity.Select(r => new BsRow(r.Account, r.Amount)).ToList();
            TotalAssets = snapshot.TotalAssets;
            TotalLiabilities = snapshot.TotalLiabilities;
            TotalEquity = snapshot.TotalEquity;
            TotalLiquidCash = FinancialStatementBuilder.SumLiquidBalance(trialBalance, liquidAccounts);
            IsBalanced = snapshot.IsBalanced;
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not load balance sheet data: {ex.Message}";
            Assets = [];
            Liabilities = [];
            Equity = [];
            TotalAssets = TotalLiabilities = TotalEquity = TotalLiquidCash = 0;
            IsBalanced = false;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostExportAsync()
    {
        await RunAsync();
        var rows = Assets.Select(r => new object?[] { "Asset", r.Account, r.Amount })
            .Concat(Liabilities.Select(r => new object?[] { "Liability", r.Account, r.Amount }))
            .Concat(Equity.Select(r => new object?[] { "Equity", r.Account, r.Amount }));
        var bytes = ReportExcelService.Export("Balance Sheet",
            ["Section", "Account", "Amount"],
            rows);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"BalanceSheet_{DateTime.Now:yyyyMMdd}.xlsx");
    }

    public sealed record BsRow(string Account, decimal Amount);
}
