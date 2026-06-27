using AtoZClinical.Web.DataProtection;

namespace AtoZClinical.Web.Middleware;

/// <summary>
/// Strips stale encrypted auth cookies from login requests before UseAuthentication runs.
/// Without this, decrypt failures can abort POST /Account/Login before credentials are checked.
/// </summary>
public sealed class LoginCookieSanitizerMiddleware
{
    private static readonly string[] SanitizedPaths = ["/Account/Login", "/Portal/Login"];

    private readonly RequestDelegate _next;
    private readonly ILogger<LoginCookieSanitizerMiddleware> _logger;

    public LoginCookieSanitizerMiddleware(RequestDelegate next, ILogger<LoginCookieSanitizerMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (SanitizedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            var stale = context.Request.Cookies.Keys
                .Where(DataProtectionExceptionHelper.IsProtectedCookie)
                .ToList();

            if (stale.Count > 0)
            {
                _logger.LogInformation(
                    "Stripping {Count} protected cookie(s) from login request for {ClientIp}: {Cookies}",
                    stale.Count,
                    ClientIpHelper.GetClientIp(context),
                    string.Join(", ", stale));

                var remaining = context.Request.Cookies
                    .Where(c => !DataProtectionExceptionHelper.IsProtectedCookie(c.Key))
                    .Select(c => $"{c.Key}={c.Value}");

                context.Request.Headers.Cookie = string.Join("; ", remaining);
            }
        }

        await _next(context);
    }
}
