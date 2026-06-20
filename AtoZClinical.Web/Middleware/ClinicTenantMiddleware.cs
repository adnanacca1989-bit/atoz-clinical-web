using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Identity;
using AtoZClinical.Infrastructure.Identity;

namespace AtoZClinical.Web.Middleware;

public sealed class ClinicTenantMiddleware
{
    private readonly RequestDelegate _next;

    private static readonly string[] SkipPrefixes =
    [
        "/Account",
        "/Vendor",
        "/Register",
        "/Index",
        "/Error",
        "/lib",
        "/css",
        "/js",
        "/favicon"
    ];

    public ClinicTenantMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext context,
        ClinicContextService clinicContext,
        ClinicAccessService access,
        SignInManager<ApplicationUser> signIn)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (path == "/" || SkipPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        if (await clinicContext.IsVendorAsync())
        {
            await _next(context);
            return;
        }

        var clinic = await clinicContext.GetCurrentClinicAsync();
        var result = access.Evaluate(clinic);
        if (!result.IsAllowed)
        {
            await signIn.SignOutAsync();
            context.Response.Redirect($"/Account/LicenseBlocked?reason={(int)result.Reason}");
            return;
        }

        await _next(context);
    }
}
