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
    public DateTime FromDate { get; set; } = ClinicClock.Today.AddMonths(-6);

    [BindProperty(SupportsGet = true)]
    public DateTime ToDate { get; set; } = ClinicClock.Today;

    [BindProperty(SupportsGet = true)]
    public string? UserName { get; set; }

    public List<AuditLogEntry> Records { get; private set; } = [];
    public List<string> UserNames { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync() => await SearchAsync();

    public Task<IActionResult> OnPostSearchAsync() => SearchAsync();

    private async Task<IActionResult> SearchAsync()
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();

        UserNames = await _audit.ListUserNamesAsync(clinicId.Value);

        var all = await _audit.ListAsync(clinicId.Value, 5000);
        var from = ClinicClock.ToClinicDate(FromDate);
        var to = ClinicClock.ToClinicDate(ToDate);
        Records = all
            .Where(a =>
            {
                var d = ClinicClock.ToClinicDateTime(a.DateTime).Date;
                if (d < from || d > to) return false;
                if (!string.IsNullOrWhiteSpace(UserName) &&
                    !string.Equals(a.UserName, UserName.Trim(), StringComparison.OrdinalIgnoreCase))
                    return false;
                return true;
            })
            .ToList();
        return Page();
    }
}
