using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;

namespace AtoZClinical.Web.Middleware;

public sealed class FormPermissionMiddleware
{
    private readonly RequestDelegate _next;

    public FormPermissionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ClinicContextService clinicContext, FormPermissionService permissions)
    {
        if (context.User.Identity?.IsAuthenticated == true && !await clinicContext.IsVendorAsync())
        {
            var user = await clinicContext.GetCurrentUserAsync();
            if (user?.ClinicId is Guid clinicId)
            {
                var role = permissions.ResolveResponsibilityRole(user);
                var visible = await permissions.GetVisibleFormsAsync(clinicId, role);
                context.Items[FormPermissionService.VisibleFormsItemKey] = visible;
            }
        }

        await _next(context);
    }
}
