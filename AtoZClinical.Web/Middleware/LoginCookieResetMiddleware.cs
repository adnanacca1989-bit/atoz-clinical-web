using AtoZClinical.Web.DataProtection;

namespace AtoZClinical.Web.Middleware;

/// <summary>Clears stale encrypted cookies before login pages render or auth runs.</summary>
public sealed class LoginCookieResetMiddleware
{
    private static readonly string[] LoginGetPaths = ["/Account/Login", "/Portal/Login"];

    private readonly RequestDelegate _next;

    public LoginCookieResetMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (HttpMethods.IsGet(context.Request.Method))
        {
            var path = context.Request.Path.Value ?? string.Empty;
            if (LoginGetPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                DataProtectionExceptionHelper.ClearProtectedCookiesForNextRequest(context);
        }

        await _next(context);
    }
}
