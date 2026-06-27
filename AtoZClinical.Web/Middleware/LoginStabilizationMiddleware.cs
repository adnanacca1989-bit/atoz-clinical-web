using AtoZClinical.Web.DataProtection;

namespace AtoZClinical.Web.Middleware;

/// <summary>
/// Runs before rate limiting: strips legacy login query strings, clears stale cookies,
/// and prevents cached 429 responses on the login page.
/// </summary>
public sealed class LoginStabilizationMiddleware
{
    private static readonly string[] LoginPaths = ["/Account/Login", "/Portal/Login", "/Error"];

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

        if (path.StartsWith("/Account/Login", StringComparison.OrdinalIgnoreCase))
        {
            if (HttpMethods.IsGet(context.Request.Method) && context.Request.Query.ContainsKey("session"))
            {
                DataProtectionExceptionHelper.ClearProtectedCookies(context);
                context.Response.Redirect("/Account/Login");
                return;
            }

            if (HttpMethods.IsGet(context.Request.Method))
                DataProtectionExceptionHelper.ClearProtectedCookiesForNextRequest(context);
        }

        if (path.StartsWith("/Error", StringComparison.OrdinalIgnoreCase)
            && HttpMethods.IsGet(context.Request.Method))
        {
            DataProtectionExceptionHelper.ClearProtectedCookiesForNextRequest(context);
        }

        await _next(context);
    }
}
