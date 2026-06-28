using AtoZClinical.Infrastructure;
using AtoZClinical.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Filters;

public sealed class FormPermissionPageFilter : IAsyncPageFilter
{
    private static readonly string[] SkipPrefixes =
    [
        "/Account", "/Vendor", "/Register", "/Error", "/Index", "/Privacy", "/Search"
    ];

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
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

        var formKey = FormPermissionService.ResolveFormKeyFromPath(path);
        if (formKey is null)
        {
            await next();
            return;
        }

        var isClinicUser = context.HttpContext.User.Identity?.IsAuthenticated == true
            && !context.HttpContext.User.IsInRole("Vendor");

        if (context.HttpContext.Items[FormPermissionService.VisibleFormsItemKey] is not HashSet<string> visible)
        {
            if (isClinicUser)
            {
                context.Result = DenyAccess(path);
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

            context.Result = visible.Contains(ClinicalFormKeys.Dashboard)
                && !path.StartsWith("/dashboard", StringComparison.OrdinalIgnoreCase)
                ? new RedirectToPageResult("/Dashboard/Index")
                : DenyAccess(path);
            return;
        }

        await next();
    }

    private static IActionResult DenyAccess(string path)
    {
        if (path.StartsWith("/dashboard", StringComparison.OrdinalIgnoreCase))
            return new ContentResult { StatusCode = 403, Content = "You do not have permission to access any clinic forms." };

        return new RedirectToPageResult("/Dashboard/Index");
    }

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;
}
