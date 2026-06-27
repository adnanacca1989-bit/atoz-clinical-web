using AtoZClinical.Web.DataProtection;

namespace AtoZClinical.Web.Middleware;

/// <summary>
/// Runs before the rate limiter: strips legacy login query strings, clears stale cookies,
/// and prevents cached 429 responses on the login page.
/// </summary>
public sealed class LoginStabilizationMiddleware
{
    private static readonly string[] LoginPaths = ["/Account/Login", "/Portal/Login"];

    private readonly RequestDelegate _next;

    public LoginStabilizationMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (!LoginPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        context.Response.Headers.Pragma = "no-cache";

        // Legacy r67 redirect target — strip query and start fresh (before rate limiter runs).
        if (HttpMethods.IsGet(context.Request.Method) && context.Request.Query.Count > 0)
        {
            DataProtectionExceptionHelper.ClearProtectedCookies(context);
            context.Response.Redirect(path);
            return;
        }

        if (HttpMethods.IsGet(context.Request.Method)
            && context.Request.Cookies.Keys.Any(DataProtectionExceptionHelper.IsProtectedCookie))
        {
            DataProtectionExceptionHelper.ClearProtectedCookiesForNextRequest(context);
        }

        await _next(context);
    }
}
