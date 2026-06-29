using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Web.Pages.Reports;

public class TrialBalanceModel : PageModel
{
    private readonly JournalReportService _journal;
    private readonly ClinicalDbContext _db;
    private readonly ClinicContextService _clinicContext;

    public TrialBalanceModel(
        JournalReportService journal,
        ClinicalDbContext db,
        ClinicContextService clinicContext)
    {
        _journal = journal;
        _db = db;
        _clinicContext = clinicContext;
    }

    [BindProperty(SupportsGet = true)]
    public DateTime AsOfDate { get; set; } = DateTime.Today;

    [BindProperty(SupportsGet = true)]
    public bool NonZeroOnly { get; set; }

    public List<JournalReportService.TrialBalanceRow> Results { get; private set; } = [];
    public decimal TotalDebit { get; private set; }
    public decimal TotalCredit { get; private set; }
    public decimal TotalLiquidCash { get; private set; }
    public bool IsBalanced { get; private set; }
    public JournalReportService.JournalIntegrityReport? Integrity { get; private set; }
    public string? WarningMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync() => await RunAsync();
    public Task<IActionResult> OnPostRunAsync() => RunAsync();
    public IActionResult OnPostClearAsync() => RedirectToPage();

    private async Task<IActionResult> RunAsync()
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();

        var id = clinicId.Value;
        Integrity = await _journal.ValidateIntegrityAsync(id);
        Results = await _journal.GetTrialBalanceAsync(id, AsOfDate);
        if (NonZeroOnly)
            Results = Results.Where(r => r.Balance != 0).ToList();

        var chartAccounts = await _db.ChartAccounts.ForClinic(id).AsNoTracking().ToListAsync();
        var liquidAccounts = FinancialStatementBuilder.ResolveLiquidAccounts("All", chartAccounts);
        TotalLiquidCash = FinancialStatementBuilder.SumLiquidBalance(Results, liquidAccounts);

        TotalDebit = Results.Sum(r => r.DisplayDebit);
        TotalCredit = Results.Sum(r => r.DisplayCredit);
        IsBalanced = Math.Abs(TotalDebit - TotalCredit) < 0.01m && Integrity.IsTrialBalanceBalanced;

        if (Integrity.UnbalancedEntries.Count > 0)
        {
            var first = Integrity.UnbalancedEntries[0];
            WarningMessage =
                $"Found {Integrity.UnbalancedEntries.Count} unbalanced journal entry(ies). " +
                $"Example: #{first.EntryNo} on {first.EntryDate:d} ({first.SourceType}) — debit {first.TotalDebit:N2} ≠ credit {first.TotalCredit:N2}.";
        }
        else if (!IsBalanced)
        {
            WarningMessage = $"Trial balance is out of balance by {Math.Abs(TotalDebit - TotalCredit):N2}.";
        }
        else if (Results.Any(r => r.AccountNo == 0 && r.Balance != 0))
        {
            var orphanCount = Results.Count(r => r.AccountNo == 0 && r.Balance != 0);
            WarningMessage =
                $"{orphanCount} account(s) appear in the journal but are missing from the chart of accounts. Add them under Chart Accounts.";
        }

        return Page();
    }
}
