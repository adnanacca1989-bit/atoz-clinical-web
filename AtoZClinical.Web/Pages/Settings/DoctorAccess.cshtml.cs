using AtoZClinical.Core.Enums;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtoZClinical.Web.Pages.Settings;

public class DoctorAccessModel : SettingsPageModel
{
    public DoctorAccessModel(ClinicContextService clinicContext, ClinicSettingsService settingsService)
        : base(clinicContext, settingsService)
    {
    }

    [BindProperty]
    public bool AllowDoctorViewAllPatients { get; set; }

    public string? StatusMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadClinicContextAsync()) return Page();
        if (!await RequireAdminAsync()) return Forbid();

        var loaded = await LoadConfigAsync();
        if (loaded is null) return ClinicRequired();

        AllowDoctorViewAllPatients = loaded.Value.Config.AllowDoctorViewAllPatients;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadClinicContextAsync()) return Page();
        if (!await RequireAdminAsync()) return Forbid();

        var loaded = await LoadConfigAsync();
        if (loaded is null) return ClinicRequired();

        var config = await SettingsService.GetAsync(loaded.Value.ClinicId);
        if (config is null) return ClinicRequired();

        config.AllowDoctorViewAllPatients = AllowDoctorViewAllPatients;
        await SettingsService.SaveAsync(config);
        StatusMessage = "Doctor access settings saved.";
        return Page();
    }

    private async Task<bool> RequireAdminAsync()
    {
        var user = await ClinicContext.GetCurrentUserAsync();
        return user is { IsVendorAdmin: true } or { ClinicRole: ClinicUserRole.ClinicAdmin };
    }
}
