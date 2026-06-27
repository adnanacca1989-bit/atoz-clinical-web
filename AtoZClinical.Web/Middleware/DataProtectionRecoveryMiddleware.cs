using AtoZClinical.Web.DataProtection;

namespace AtoZClinical.Web.Middleware;

/// <summary>Recovers from stale encrypted cookies without redirect loops.</summary>
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

            if (IsLoginPath(path) && HttpMethods.IsGet(context.Request.Method))
            {
                var showReset = context.Request.Query.ContainsKey("recovered");
                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.WriteAsync(LoginFallbackHtml.Render(showReset));
                return;
            }

            if (IsLoginPath(path) && HttpMethods.IsPost(context.Request.Method))
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
