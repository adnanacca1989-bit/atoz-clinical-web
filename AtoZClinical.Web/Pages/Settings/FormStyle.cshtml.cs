using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtoZClinical.Web.Pages.Settings;

public class FormStyleModel : SettingsPageModel
{
    public FormStyleModel(ClinicContextService clinicContext, ClinicSettingsService settingsService)
        : base(clinicContext, settingsService) { }

    [BindProperty] public string FormStyle { get; set; } = "Default";
    [BindProperty] public string PrimaryColor { get; set; } = "#0b4f8a";
    public string? StatusMessage { get; set; }

    public static readonly string[] Styles = ["Default", "Compact", "Large"];

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadClinicContextAsync();
        var ctx = await LoadConfigAsync();
        if (ctx is null) return ClinicRequired();
        FormStyle = ctx.Value.Config.FormStyle;
        PrimaryColor = ctx.Value.Config.PrimaryColor;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var ctx = await LoadConfigAsync();
        if (ctx is null) return ClinicRequired();
        ctx.Value.Config.FormStyle = FormStyle;
        ctx.Value.Config.PrimaryColor = PrimaryColor.Trim();
        await SettingsService.SaveAsync(ctx.Value.Config);
        StatusMessage = "Form style saved. Refresh the page to see changes.";
        return Page();
    }
}
