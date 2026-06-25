using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Web.Filters;

/// <summary>Loads clinic branding into ViewData for professional report headers.</summary>
public sealed class ReportBrandingPageFilter : IAsyncPageFilter
{
    private readonly ClinicContextService _clinicContext;
    private readonly ClinicalDbContext _db;
    private readonly ClinicSettingsService _settings;

    public ReportBrandingPageFilter(
        ClinicContextService clinicContext,
        ClinicalDbContext db,
        ClinicSettingsService settings)
    {
        _clinicContext = clinicContext;
        _db = db;
        _settings = settings;
    }

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        if (context.HandlerInstance is PageModel pageModel)
        {
            var path = context.HttpContext.Request.Path.Value ?? string.Empty;
            if (path.StartsWith("/Reports", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains("PatientPrintBundle", StringComparison.OrdinalIgnoreCase))
            {
                await PopulateBrandingAsync(context.HttpContext, pageModel.ViewData);
            }
        }

        await next();
    }

    private async Task PopulateBrandingAsync(HttpContext http, Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary viewData)
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return;

        var clinic = await _db.Clinics.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clinicId.Value);
        if (clinic is null) return;

        var config = await _settings.GetAsync(clinicId.Value);
        var currency = config?.CurrencySymbol ?? config?.CurrencyCode ?? "";

        viewData["ReportClinicName"] = clinic.Name;
        viewData["ReportClinicAddress"] = clinic.Address;
        viewData["ReportClinicPhone"] = clinic.Phone;
        viewData["ReportClinicEmail"] = clinic.Email;
        viewData["ReportClinicCity"] = clinic.City;
        viewData["ReportClinicCountry"] = clinic.Country;
        viewData["ReportCurrency"] = currency;
        viewData["ReportGeneratedAt"] = DateTime.Now.ToString("MMMM d, yyyy h:mm tt");
        viewData["ReportGeneratedBy"] = http.User.Identity?.Name ?? "User";
    }
}
