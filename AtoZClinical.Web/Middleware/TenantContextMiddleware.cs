using AtoZClinical.Infrastructure.Identity;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Identity;

namespace AtoZClinical.Web.Middleware;

/// <summary>Sets tenant context for EF global query filters after authentication.</summary>
public sealed class TenantContextMiddleware
{
    private readonly RequestDelegate _next;

    private static readonly string[] VendorPrefixes =
    [
        "/Vendor",
        "/Account",
        "/Register",
        "/Index",
        "/Error",
        "/health",
        "/test-email",
        "/Portal",
        "/lib",
        "/css",
        "/js",
        "/favicon"
    ];

    public TenantContextMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, UserManager<ApplicationUser> users)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var user = await users.GetUserAsync(context.User);
            if (user?.IsVendorAdmin == true || context.User.IsInRole("Vendor"))
            {
                context.Items[HttpContextClinicProvider.BypassTenantFilterKey] = true;
            }
            else if (user?.ClinicId is Guid clinicId)
            {
                context.Items[HttpContextClinicProvider.TenantClinicIdKey] = clinicId;
            }
        }
        else
        {
            var path = context.Request.Path.Value ?? string.Empty;
            var isApi = path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase);

            if (context.Items["ApiKeyAuthenticated"] is true
                && context.Items[HttpContextClinicProvider.TenantClinicIdKey] is Guid)
            {
                // API key sets clinic tenant — do not bypass filter.
            }
            else if (!isApi && (path == "/" || VendorPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase))))
            {
                context.Items[HttpContextClinicProvider.BypassTenantFilterKey] = true;
            }

            if (context.Items[HttpContextClinicProvider.SubdomainClinicIdKey] is Guid subdomainClinicId
                && (path.StartsWith("/Portal", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("/Account/Login", StringComparison.OrdinalIgnoreCase)))
            {
                context.Items[HttpContextClinicProvider.TenantClinicIdKey] = subdomainClinicId;
            }
        }

        await _next(context);
    }
}
