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
    private readonly ILogger<ClinicBrandingPageFilter> _logger;

    public ClinicBrandingPageFilter(
        ClinicContextService clinicContext,
        ClinicProfileService profile,
        ILogger<ClinicBrandingPageFilter> logger)
    {
        _clinicContext = clinicContext;
        _profile = profile;
        _logger = logger;
    }

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var path = context.HttpContext.Request.Path.Value ?? "";
        if (path.StartsWith("/Error", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/Account", StringComparison.OrdinalIgnoreCase))
        {
            await next();
            return;
        }

        if (context.HandlerInstance is PageModel && !await _clinicContext.IsVendorAsync())
        {
            var clinicId = await _clinicContext.GetClinicIdAsync();
            if (clinicId.HasValue)
            {
                try
                {
                    var profile = await _profile.GetAsync(clinicId.Value);
                    context.HttpContext.Items["ClinicPrimaryColor"] =
                        ClinicBrandingHelper.NormalizePrimaryColor(profile.PrimaryColor);
                    context.HttpContext.Items["ClinicLogoBase64"] = profile.LogoBase64;
                    context.HttpContext.Items["ClinicDisplayName"] = profile.Name ?? "Clinic";
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Clinic branding filter fallback for clinic {ClinicId} path {Path}",
                        clinicId,
                        path);
                    context.HttpContext.Items["ClinicPrimaryColor"] = ClinicBrandingHelper.DefaultPrimaryColor;
                }
            }
        }

        await next();
    }
}
