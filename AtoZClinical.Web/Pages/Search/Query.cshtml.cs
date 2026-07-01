using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.Search;

public class QueryModel : PageModel
{
    private readonly ClinicContextService _clinicContext;
    private readonly GlobalTransactionSearchService _search;

    public QueryModel(ClinicContextService clinicContext, GlobalTransactionSearchService search)
    {
        _clinicContext = clinicContext;
        _search = search;
    }

    public async Task<IActionResult> OnGetAsync(
        DateTime? fromDate,
        DateTime? toDate,
        string? transactionType,
        string? patientName,
        string? doctorName,
        decimal? amount,
        bool useDateOfBirth = false,
        DateTime? dateOfBirth = null,
        string? q = null)
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();

        var criteria = new GlobalSearchCriteria
        {
            FromDate = fromDate ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1),
            ToDate = toDate ?? DateTime.Today,
            TransactionType = string.IsNullOrWhiteSpace(transactionType) ? GlobalSearchTypes.All : transactionType.Trim(),
            PatientName = patientName,
            DoctorName = doctorName,
            Amount = amount,
            UseDateOfBirth = useDateOfBirth,
            DateOfBirth = dateOfBirth,
            QuickTerm = q
        };

        var results = await _search.SearchAdvancedAsync(clinicId.Value, criteria);

        return new JsonResult(new
        {
            items = results.Select((r, index) => new
            {
                no = index + 1,
                type = r.TransactionType,
                reference = r.Reference,
                date = r.TransactionDate.ToString("d"),
                patient = r.PatientOrParty,
                doctor = r.DoctorName,
                amount = r.Amount,
                details = r.Details,
                link = r.Link
            })
        });
    }
}
