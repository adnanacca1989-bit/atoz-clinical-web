using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;

namespace AtoZClinical.Web.Middleware;

public sealed class FormPermissionMiddleware
{
    private readonly RequestDelegate _next;

    public FormPermissionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext context,
        ClinicContextService clinicContext,
        FormPermissionService permissions,
        ClinicModuleService modules)
    {
        if (context.User.Identity?.IsAuthenticated == true && !await clinicContext.IsVendorAsync())
        {
            var user = await clinicContext.GetCurrentUserAsync();
            if (user?.ClinicId is Guid clinicId)
            {
                var role = permissions.ResolveResponsibilityRole(user);
                var visible = await permissions.GetVisibleFormsAsync(clinicId, role);
                var enabled = await modules.GetEnabledFormsAsync(clinicId);
                visible.IntersectWith(enabled);
                context.Items[FormPermissionService.VisibleFormsItemKey] = visible;
                context.Items[ClinicModuleService.EnabledFormsItemKey] = enabled;
            }
        }

        await _next(context);
    }
}
