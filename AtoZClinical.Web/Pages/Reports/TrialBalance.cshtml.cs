using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.Reports;

public class TrialBalanceModel : PageModel
{
    private readonly JournalReportService _journal;
    private readonly ClinicContextService _clinicContext;

    public TrialBalanceModel(JournalReportService journal, ClinicContextService clinicContext)
    {
        _journal = journal;
        _clinicContext = clinicContext;
    }

    [BindProperty(SupportsGet = true)]
    public DateTime AsOfDate { get; set; } = DateTime.Today;

    [BindProperty(SupportsGet = true)]
    public bool NonZeroOnly { get; set; }

    public List<JournalReportService.TrialBalanceRow> Results { get; private set; } = [];
    public decimal TotalDebit { get; private set; }
    public decimal TotalCredit { get; private set; }

    public async Task<IActionResult> OnGetAsync() => await RunAsync();
    public Task<IActionResult> OnPostRunAsync() => RunAsync();
    public IActionResult OnPostClearAsync() => RedirectToPage();

    private async Task<IActionResult> RunAsync()
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();

        Results = await _journal.GetTrialBalanceAsync(clinicId.Value, AsOfDate);
        if (NonZeroOnly)
            Results = Results.Where(r => r.Balance != 0).ToList();
        TotalDebit = Results.Sum(r => r.TotalDebit);
        TotalCredit = Results.Sum(r => r.TotalCredit);
        return Page();
    }
}
