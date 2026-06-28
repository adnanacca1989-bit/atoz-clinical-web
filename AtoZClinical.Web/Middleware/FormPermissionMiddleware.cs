using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;

namespace AtoZClinical.Web.Middleware;

public sealed class FormPermissionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<FormPermissionMiddleware> _logger;

    public FormPermissionMiddleware(RequestDelegate next, ILogger<FormPermissionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ClinicContextService clinicContext,
        FormPermissionService permissions,
        ClinicModuleService modules)
    {
        if (context.User.Identity?.IsAuthenticated == true && !await clinicContext.IsVendorAsync())
        {
            try
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
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Form permission middleware failed for {Path} trace={TraceId}. Continuing with empty permissions.",
                    context.Request.Path,
                    context.TraceIdentifier);
                context.Items[FormPermissionService.VisibleFormsItemKey] =
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        await _next(context);
    }
}
