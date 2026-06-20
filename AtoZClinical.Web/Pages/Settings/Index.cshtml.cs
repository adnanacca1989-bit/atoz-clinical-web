using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtoZClinical.Web.Pages.Settings;

public class IndexModel : SettingsPageModel
{
    public IndexModel(ClinicContextService clinicContext, ClinicSettingsService settingsService)
        : base(clinicContext, settingsService) { }

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadClinicContextAsync();
        return Page();
    }
}
