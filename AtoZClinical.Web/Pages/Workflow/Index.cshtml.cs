using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.Workflow;

public class IndexModel : PageModel
{
    private readonly ClinicContextService _clinicContext;

    public IndexModel(ClinicContextService clinicContext) => _clinicContext = clinicContext;

    public async Task<IActionResult> OnGetAsync()
    {
        if (await _clinicContext.GetClinicIdAsync() is null) return Forbid();
        return Page();
    }
}
