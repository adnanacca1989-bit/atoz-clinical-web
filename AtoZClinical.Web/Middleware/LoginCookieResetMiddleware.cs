using AtoZClinical.Web.DataProtection;

namespace AtoZClinical.Web.Middleware;

/// <summary>Clears stale encrypted cookies before login and error pages run.</summary>
public sealed class LoginCookieResetMiddleware
{
    private static readonly string[] ResetGetPaths = ["/Account/Login", "/Portal/Login", "/Error"];

    private readonly RequestDelegate _next;

    public LoginCookieResetMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (HttpMethods.IsGet(context.Request.Method))
        {
            var path = context.Request.Path.Value ?? string.Empty;
            if (ResetGetPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                && context.Request.Cookies.Keys.Any(DataProtectionExceptionHelper.IsProtectedCookie))
            {
                DataProtectionExceptionHelper.ClearProtectedCookiesForNextRequest(context);
            }
        }

        await _next(context);
    }
}
