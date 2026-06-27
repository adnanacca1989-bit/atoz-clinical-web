using AtoZClinical.Web.DataProtection;

namespace AtoZClinical.Web.Middleware;

/// <summary>Recovers from stale encrypted cookies without redirect loops.</summary>
public sealed class DataProtectionRecoveryMiddleware
{
    private const string RecoveryFlagKey = "__clinical_dp_recovery";

    private static readonly string SessionResetHtml =
        """
        <!DOCTYPE html><html lang="en"><head><meta charset="utf-8"/><title>Sign in</title></head>
        <body style="font-family:Segoe UI,sans-serif;max-width:420px;margin:2rem auto;padding:1rem;">
        <h2>Session reset required</h2>
        <p>Your browser had outdated session cookies. They were cleared automatically.</p>
        <p><a href="/Account/Login">Continue to sign in</a></p>
        </body></html>
        """;

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
        catch (Exception ex) when (DataProtectionExceptionHelper.IsRecoverable(ex))
        {
            var path = context.Request.Path.Value ?? string.Empty;
            var clientIp = ClientIpHelper.GetClientIp(context);

            _logger.LogWarning(
                ex,
                "Recoverable session/antiforgery failure for {ClientIp} on {Method} {Path} trace={TraceId}",
                clientIp,
                context.Request.Method,
                path,
                context.TraceIdentifier);

            if (context.Response.HasStarted)
                throw;

            var staleCookies = context.Request.Cookies.Keys.Where(DataProtectionExceptionHelper.IsProtectedCookie).ToList();
            if (staleCookies.Count > 0)
            {
                _logger.LogInformation(
                    "Clearing {Count} stale cookie(s) for {ClientIp}: {Cookies}",
                    staleCookies.Count,
                    clientIp,
                    string.Join(", ", staleCookies));
            }

            DataProtectionExceptionHelper.ClearProtectedCookies(context);
            context.Response.Clear();

            if (IsLoginPath(path) && (context.Items.ContainsKey(RecoveryFlagKey) || context.Request.Query.ContainsKey("recovered")))
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.WriteAsync(SessionResetHtml);
                return;
            }

            context.Items[RecoveryFlagKey] = true;

            if (IsLoginPath(path))
            {
                context.Response.Redirect("/Account/Login?recovered=1");
                return;
            }

            context.Response.Redirect("/Account/Login?recovered=1");
        }
    }

    private static bool IsLoginPath(string path) =>
        path.StartsWith("/Account/Login", StringComparison.OrdinalIgnoreCase);
}
