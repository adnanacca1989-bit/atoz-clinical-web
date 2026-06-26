using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtoZClinical.Web.Pages.Settings;

public abstract class SettingsFormPageModel : ClinicFormPageModel
{
    protected SettingsFormPageModel(ClinicContextService clinicContext) : base(clinicContext) { }

    /// <summary>Settings define forms use SaveCoreAsync via the shared Add/Edit toolbar handlers.</summary>
    protected virtual Task<IActionResult> SaveSettingsCoreAsync() =>
        throw new InvalidOperationException($"{GetType().Name} must override SaveSettingsCoreAsync.");

    protected override Task<IActionResult> ExecuteSaveAsync() => SaveSettingsCoreAsync();

    protected async Task<Guid?> RequireSettingsClinicIdAsync()
    {
        if (await ClinicContext.IsVendorAsync()) return null;
        return await RequireClinicIdAsync();
    }

    protected IActionResult ClinicRequired() => RedirectToPage("/Dashboard/Index");
}
