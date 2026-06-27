using AtoZClinical.Web.DataProtection;

namespace AtoZClinical.Web.Middleware;

/// <summary>
/// Prepares login/error pages: strips legacy query strings, clears stale cookies,
/// prevents cached responses, and avoids redirect loops.
/// </summary>
public sealed class LoginStabilizationMiddleware
{
    private static readonly string[] StabilizedPaths = ["/Account/Login", "/Portal/Login", "/Error"];

    private readonly RequestDelegate _next;
    private readonly ILogger<LoginStabilizationMiddleware> _logger;

    public LoginStabilizationMiddleware(RequestDelegate next, ILogger<LoginStabilizationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (!StabilizedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        context.Response.Headers.Pragma = "no-cache";

        if (path.StartsWith("/Account/Login", StringComparison.OrdinalIgnoreCase))
        {
            if (HttpMethods.IsGet(context.Request.Method) && context.Request.Query.ContainsKey("session"))
            {
                _logger.LogInformation(
                    "Stripping legacy session query for {ClientIp} trace={TraceId}",
                    ClientIpHelper.GetClientIp(context),
                    context.TraceIdentifier);

                DataProtectionExceptionHelper.ClearProtectedCookies(context);
                context.Response.Redirect("/Account/Login");
                return;
            }

            if (HttpMethods.IsGet(context.Request.Method) && context.Request.Query.ContainsKey("recovered"))
                DataProtectionExceptionHelper.ClearProtectedCookies(context);

            if (context.Request.Cookies.Keys.Any(DataProtectionExceptionHelper.IsProtectedCookie))
                DataProtectionExceptionHelper.ClearProtectedCookiesForNextRequest(context);
        }

        if (path.StartsWith("/Error", StringComparison.OrdinalIgnoreCase)
            && HttpMethods.IsGet(context.Request.Method)
            && context.Request.Cookies.Keys.Any(DataProtectionExceptionHelper.IsProtectedCookie))
        {
            DataProtectionExceptionHelper.ClearProtectedCookiesForNextRequest(context);
        }

        await _next(context);
    }
}
