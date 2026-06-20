using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.Settings;

public abstract class SettingsPageModel : PageModel
{
    protected ClinicContextService ClinicContext { get; }
    protected ClinicSettingsService SettingsService { get; }

    protected SettingsPageModel(ClinicContextService clinicContext, ClinicSettingsService settingsService)
    {
        ClinicContext = clinicContext;
        SettingsService = settingsService;
    }

    public bool IsVendor { get; private set; }
    public string? UserName => User.Identity?.Name;

    protected async Task<bool> LoadClinicContextAsync()
    {
        IsVendor = await ClinicContext.IsVendorAsync();
        return !IsVendor;
    }

    protected async Task<Guid?> RequireClinicIdAsync()
    {
        if (await ClinicContext.IsVendorAsync()) return null;
        return await ClinicContext.RequireOperationalClinicIdAsync();
    }

    protected async Task<(Guid ClinicId, ClinicConfiguration Config)?> LoadConfigAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return null;
        var config = await SettingsService.GetOrCreateAsync(clinicId.Value);
        return (clinicId.Value, config);
    }

    protected IActionResult ClinicRequired() => RedirectToPage("/Dashboard/Index");
}
