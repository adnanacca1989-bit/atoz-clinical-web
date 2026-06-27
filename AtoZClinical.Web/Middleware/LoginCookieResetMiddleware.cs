using AtoZClinical.Web.DataProtection;

namespace AtoZClinical.Web.Middleware;

/// <summary>Clears stale encrypted cookies before login so corrupt tokens cannot crash the page.</summary>
public sealed class LoginCookieResetMiddleware
{
    private readonly RequestDelegate _next;

    public LoginCookieResetMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (HttpMethods.IsGet(context.Request.Method)
            && path.StartsWith("/Account/Login", StringComparison.OrdinalIgnoreCase))
        {
            DataProtectionExceptionHelper.ClearProtectedCookiesForNextRequest(context);
        }

        await _next(context);
    }
}
