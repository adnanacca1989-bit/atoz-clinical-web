using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.Reports;

public class GeneralLedgerModel : PageModel
{
    private readonly JournalReportService _journal;
    private readonly ChartAccountService _chartService;
    private readonly ClinicContextService _clinicContext;

    public GeneralLedgerModel(
        JournalReportService journal,
        ChartAccountService chartService,
        ClinicContextService clinicContext)
    {
        _journal = journal;
        _chartService = chartService;
        _clinicContext = clinicContext;
    }

    [BindProperty(SupportsGet = true)]
    public DateTime FromDate { get; set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    [BindProperty(SupportsGet = true)]
    public DateTime ToDate { get; set; } = DateTime.Today;

    [BindProperty(SupportsGet = true)]
    public string? AccountName { get; set; }

    public List<JournalReportService.GeneralLedgerRow> Results { get; private set; } = [];
    public List<ChartAccount> ChartAccounts { get; private set; } = [];
    public decimal TotalDebit { get; private set; }
    public decimal TotalCredit { get; private set; }
    public bool ShowRunningBalance => !string.IsNullOrWhiteSpace(AccountName);

    public async Task<IActionResult> OnGetAsync() => await RunAsync();
    public Task<IActionResult> OnPostRunAsync() => RunAsync();
    public IActionResult OnPostClearAsync() => RedirectToPage();

    private async Task<IActionResult> RunAsync()
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();

        ChartAccounts = await _chartService.ListAsync(clinicId.Value);
        Results = await _journal.GetGeneralLedgerAsync(clinicId.Value, FromDate, ToDate, AccountName);
        TotalDebit = Results.Sum(r => r.Debit);
        TotalCredit = Results.Sum(r => r.Credit);
        return Page();
    }
}
