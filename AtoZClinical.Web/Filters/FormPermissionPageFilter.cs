using AtoZClinical.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Filters;

public sealed class FormPermissionPageFilter : IAsyncPageFilter
{
    private static readonly string[] SkipPrefixes =
    [
        "/Account", "/Vendor", "/Register", "/Error", "/Index", "/Privacy"
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

        if (context.HttpContext.Items[FormPermissionService.VisibleFormsItemKey] is HashSet<string> visible &&
            !visible.Contains(formKey))
        {
            context.Result = new RedirectToPageResult("/Dashboard/Index");
            return;
        }

        await next();
    }

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;
}
