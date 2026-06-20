using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtoZClinical.Web.Pages.Settings;

public abstract class SettingsFormPageModel : ClinicFormPageModel
{
    protected SettingsFormPageModel(ClinicContextService clinicContext) : base(clinicContext) { }

    protected async Task<Guid?> RequireSettingsClinicIdAsync()
    {
        if (await ClinicContext.IsVendorAsync()) return null;
        return await RequireClinicIdAsync();
    }

    protected IActionResult ClinicRequired() => RedirectToPage("/Dashboard/Index");
}
