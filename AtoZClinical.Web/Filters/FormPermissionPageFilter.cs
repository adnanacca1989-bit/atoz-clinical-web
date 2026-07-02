using AtoZClinical.Infrastructure;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Filters;

public sealed class FormPermissionPageFilter : IAsyncPageFilter
{
    public const string NoPermissionsMessage =
        "No permissions assigned to this role. Please contact admin.";

    private static readonly string[] SkipPrefixes =
    [
        "/Account", "/Vendor", "/Register", "/Error", "/Index", "/Privacy"
    ];

    private static readonly string[] UnmappedAllowedPrefixes =
    [
        "/hub", "/api", "/css", "/js", "/lib", "/images", "/favicon"
    ];

    private readonly ILogger<FormPermissionPageFilter> _logger;
    private readonly AuditService _audit;
    private readonly ClinicContextService _clinicContext;
    private readonly FormPermissionService _permissions;

    public FormPermissionPageFilter(
        ILogger<FormPermissionPageFilter> logger,
        AuditService audit,
        ClinicContextService clinicContext,
        FormPermissionService permissions)
    {
        _logger = logger;
        _audit = audit;
        _clinicContext = clinicContext;
        _permissions = permissions;
    }

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        try
        {
            await ExecuteAsync(context, next);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Form permission filter failed for {Path} trace={TraceId}.",
                context.HttpContext.Request.Path,
                context.HttpContext.TraceIdentifier);

            if (IsAuthenticatedClinicUser(context.HttpContext))
            {
                context.Result = SafeDenyResult(
                    "Unable to verify your access permissions. Please try again or contact your clinic admin.");
                return;
            }

            await next();
        }
    }

    private async Task ExecuteAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var path = context.HttpContext.Request.Path.Value ?? "";
        if (SkipPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await next();
            return;
        }

        if (context.HttpContext.User.IsInRole("Vendor"))
        {
            await next();
            return;
        }

        var isClinicUser = IsAuthenticatedClinicUser(context.HttpContext);
        var formKey = FormPermissionService.ResolveFormKeyFromPath(path);
        if (formKey is null)
        {
            if (isClinicUser && RequiresFormPermissionGate(path))
            {
                context.Result = await DenyAccessAsync(context.HttpContext, path, hasNoPermissions: false);
                return;
            }

            await next();
            return;
        }

        if (context.HttpContext.Items[FormPermissionService.VisibleFormsItemKey] is not HashSet<string> visible)
        {
            if (isClinicUser)
            {
                context.Result = await DenyAccessAsync(context.HttpContext, path, hasNoPermissions: true);
                return;
            }

            await next();
            return;
        }

        if (!visible.Contains(formKey))
        {
            if (formKey == ClinicalFormKeys.ServiceIncomeRequest &&
                visible.Contains(ClinicalFormKeys.ServiceIncomes))
            {
                await next();
                return;
            }

            if (formKey == ClinicalFormKeys.Messaging &&
                visible.Contains(ClinicalFormKeys.Dashboard))
            {
                await next();
                return;
            }

            var noPermissions = visible.Count == 0;
            context.Result = visible.Contains(ClinicalFormKeys.Dashboard)
                && !path.StartsWith("/dashboard", StringComparison.OrdinalIgnoreCase)
                ? new RedirectToPageResult("/Dashboard/Index")
                : await DenyAccessAsync(context.HttpContext, path, noPermissions);
            return;
        }

        await next();
    }

    private static bool IsAuthenticatedClinicUser(HttpContext httpContext) =>
        httpContext.User.Identity?.IsAuthenticated == true
        && !httpContext.User.IsInRole("Vendor");

    private async Task<IActionResult> DenyAccessAsync(HttpContext httpContext, string path, bool hasNoPermissions)
    {
        if (path.StartsWith("/dashboard", StringComparison.OrdinalIgnoreCase) || hasNoPermissions)
        {
            var user = await _clinicContext.GetCurrentUserAsync();
            var role = user is not null ? _permissions.ResolveResponsibilityRole(user) : null;
            _logger.LogWarning(
                "Login access blocked for {User} role={Role} path={Path}",
                user?.UserName ?? httpContext.User.Identity?.Name,
                role,
                path);

            if (user?.ClinicId is Guid clinicId)
            {
                await _audit.LogAsync(
                    clinicId,
                    user.UserName,
                    "Login Access",
                    "Permission Denied",
                    $"User {user.UserName} blocked at {path} — role {role} has no assigned permissions.");
            }

            return SafeDenyResult(NoPermissionsMessage);
        }

        return new RedirectToPageResult("/Dashboard/Index");
    }

    private static ContentResult SafeDenyResult(string message) =>
        new() { StatusCode = 403, Content = message };

    private static bool RequiresFormPermissionGate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        if (UnmappedAllowedPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            return false;

        return path.StartsWith("/", StringComparison.Ordinal)
               && !path.StartsWith("/portal", StringComparison.OrdinalIgnoreCase);
    }

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;
}
