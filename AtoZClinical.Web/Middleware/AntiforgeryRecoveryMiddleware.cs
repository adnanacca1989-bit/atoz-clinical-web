using Microsoft.AspNetCore.Antiforgery;

namespace AtoZClinical.Web.Middleware;

/// <summary>
/// After deploy or key rotation, stale antiforgery cookies may fail validation.
/// Redirect POST back to GET so the user receives a fresh token instead of an error page.
/// </summary>
public sealed class AntiforgeryRecoveryMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AntiforgeryRecoveryMiddleware> _logger;

    public AntiforgeryRecoveryMiddleware(RequestDelegate next, ILogger<AntiforgeryRecoveryMiddleware> logger)
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
        catch (AntiforgeryValidationException ex)
        {
            _logger.LogWarning(ex, "Antiforgery token validation failed for {Path}.", context.Request.Path);

            if (IsMutatingRequest(context.Request.Method))
            {
                context.Response.Clear();
                var redirect = context.Request.Path.Value ?? "/";
                context.Response.Redirect(redirect);
                return;
            }

            throw;
        }
    }

    private static bool IsMutatingRequest(string method) =>
        HttpMethods.IsPost(method) ||
        HttpMethods.IsPut(method) ||
        HttpMethods.IsPatch(method) ||
        HttpMethods.IsDelete(method);
}
