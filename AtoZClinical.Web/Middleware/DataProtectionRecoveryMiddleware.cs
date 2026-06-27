using System.Security.Cryptography;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;

namespace AtoZClinical.Web.Middleware;

/// <summary>
/// Recovers from stale cookies or antiforgery tokens after deploy/key rotation
/// instead of showing the generic error page.
/// </summary>
public sealed class DataProtectionRecoveryMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<DataProtectionRecoveryMiddleware> _logger;

    public DataProtectionRecoveryMiddleware(RequestDelegate next, ILogger<DataProtectionRecoveryMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            _logger.LogWarning(ex, "Data protection / antiforgery failure on {Path}. Clearing stale cookies.",
                context.Request.Path);

            ClearProtectedCookies(context);

            if (IsMutatingRequest(context.Request.Method))
            {
                context.Response.Clear();
                var path = context.Request.Path.Value ?? "/Account/Login";
                context.Response.Redirect(path);
                return;
            }

            context.Response.Clear();
            context.Response.Redirect("/Account/Login?session=refresh");
        }
    }

    private static bool IsRecoverable(Exception? ex)
    {
        while (ex is not null)
        {
            if (ex is AntiforgeryValidationException or CryptographicException)
                return true;

            if (ex is InvalidOperationException ioe &&
                ioe.Message.Contains("key ring", StringComparison.OrdinalIgnoreCase))
                return true;

            ex = ex.InnerException;
        }

        return false;
    }

    private static void ClearProtectedCookies(HttpContext context)
    {
        foreach (var cookie in context.Request.Cookies.Keys)
        {
            if (cookie.StartsWith(".AspNetCore.", StringComparison.OrdinalIgnoreCase)
                || cookie.StartsWith("__Clinical", StringComparison.OrdinalIgnoreCase)
                || cookie.Equals("atz_patient_portal", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Cookies.Delete(cookie);
            }
        }
    }

    private static bool IsMutatingRequest(string method) =>
        HttpMethods.IsPost(method) ||
        HttpMethods.IsPut(method) ||
        HttpMethods.IsPatch(method) ||
        HttpMethods.IsDelete(method);
}
