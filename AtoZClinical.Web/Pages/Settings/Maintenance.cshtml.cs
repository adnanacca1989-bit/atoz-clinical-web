using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtoZClinical.Web.Pages.Settings;

public class MaintenanceModel : SettingsPageModel
{
    public MaintenanceModel(ClinicContextService clinicContext, ClinicSettingsService settingsService)
        : base(clinicContext, settingsService) { }

    [BindProperty] public bool MaintenanceMode { get; set; }
    [BindProperty] public string? MaintenanceNotes { get; set; }
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadClinicContextAsync();
        var ctx = await LoadConfigAsync();
        if (ctx is null) return ClinicRequired();
        MaintenanceMode = ctx.Value.Config.MaintenanceMode;
        MaintenanceNotes = ctx.Value.Config.MaintenanceNotes;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var ctx = await LoadConfigAsync();
        if (ctx is null) return ClinicRequired();
        ctx.Value.Config.MaintenanceMode = MaintenanceMode;
        ctx.Value.Config.MaintenanceNotes = MaintenanceNotes?.Trim();
        await SettingsService.SaveAsync(ctx.Value.Config);
        StatusMessage = "Maintenance settings saved.";
        return Page();
    }
}
