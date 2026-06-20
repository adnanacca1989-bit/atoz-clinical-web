using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.Admin;

public class AuditLogModel : PageModel
{
    private readonly AuditService _audit;
    private readonly ClinicContextService _clinicContext;

    public AuditLogModel(AuditService audit, ClinicContextService clinicContext)
    {
        _audit = audit;
        _clinicContext = clinicContext;
    }

    [BindProperty(SupportsGet = true)]
    public DateTime FromDate { get; set; } = DateTime.Today.AddMonths(-1);

    [BindProperty(SupportsGet = true)]
    public DateTime ToDate { get; set; } = DateTime.Today;

    public List<AuditLogEntry> Records { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync() => await SearchAsync();

    public Task<IActionResult> OnPostSearchAsync() => SearchAsync();

    private async Task<IActionResult> SearchAsync()
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();

        var all = await _audit.ListAsync(clinicId.Value, 2000);
        Records = all
            .Where(a => a.DateTime.Date >= FromDate.Date && a.DateTime.Date <= ToDate.Date.AddDays(1))
            .ToList();
        return Page();
    }
}
