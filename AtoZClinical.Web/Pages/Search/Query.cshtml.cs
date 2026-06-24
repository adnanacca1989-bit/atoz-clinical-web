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

    public async Task<IActionResult> OnGetAsync(string? q)
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();

        var results = await _search.SearchAsync(clinicId.Value, q);
        return new JsonResult(new
        {
            items = results.Select(r => new
            {
                type = r.TransactionType,
                reference = r.Reference,
                date = r.TransactionDate.ToString("d"),
                patient = r.PatientOrParty,
                doctor = r.DoctorName,
                amount = r.Amount,
                link = r.Link
            })
        });
    }
}
