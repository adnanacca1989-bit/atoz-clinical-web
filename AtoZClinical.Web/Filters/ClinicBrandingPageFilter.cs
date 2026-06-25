using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Filters;

/// <summary>Applies clinic primary color and logo to layout via ViewData.</summary>
public sealed class ClinicBrandingPageFilter : IAsyncPageFilter
{
    private readonly ClinicContextService _clinicContext;
    private readonly ClinicProfileService _profile;

    public ClinicBrandingPageFilter(ClinicContextService clinicContext, ClinicProfileService profile)
    {
        _clinicContext = clinicContext;
        _profile = profile;
    }

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        if (context.HandlerInstance is PageModel && !await _clinicContext.IsVendorAsync())
        {
            var clinicId = await _clinicContext.GetClinicIdAsync();
            if (clinicId.HasValue)
            {
                try
                {
                    var profile = await _profile.GetAsync(clinicId.Value);
                    context.HttpContext.Items["ClinicPrimaryColor"] = profile.PrimaryColor;
                    context.HttpContext.Items["ClinicLogoBase64"] = profile.LogoBase64;
                    context.HttpContext.Items["ClinicDisplayName"] = profile.Name;
                }
                catch
                {
                    // Non-fatal — layout falls back to defaults.
                }
            }
        }

        await next();
    }
}
