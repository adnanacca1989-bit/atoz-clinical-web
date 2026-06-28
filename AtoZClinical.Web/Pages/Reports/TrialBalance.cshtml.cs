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

    public async Task<IActionResult> OnGetAsync() => await RunAsync();
    public Task<IActionResult> OnPostRunAsync() => RunAsync();
    public IActionResult OnPostClearAsync() => RedirectToPage();

    private async Task<IActionResult> RunAsync()
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();

        var id = clinicId.Value;
        Results = await _journal.GetTrialBalanceAsync(id, AsOfDate);
        if (NonZeroOnly)
            Results = Results.Where(r => r.Balance != 0).ToList();

        var chartAccounts = await _db.ChartAccounts.ForClinic(id).AsNoTracking().ToListAsync();
        var liquidAccounts = FinancialStatementBuilder.ResolveLiquidAccounts("All", chartAccounts);
        TotalLiquidCash = FinancialStatementBuilder.SumLiquidBalance(Results, liquidAccounts);

        TotalDebit = Results.Sum(r => r.TotalDebit);
        TotalCredit = Results.Sum(r => r.TotalCredit);
        return Page();
    }
}
