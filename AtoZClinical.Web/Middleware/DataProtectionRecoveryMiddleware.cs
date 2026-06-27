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
            _logger.LogWarning(
                ex,
                "Recoverable data protection failure on {Method} {Path} trace={TraceId}",
                context.Request.Method,
                context.Request.Path,
                context.TraceIdentifier);

            if (context.Response.HasStarted)
                throw;

            DataProtectionExceptionHelper.ClearProtectedCookies(context);
            context.Response.Clear();

            if (LoginRecoveryHelper.IsLoginPath(context))
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.WriteAsync(LoginRecoveryHelper.EmergencyLoginHtml);
                return;
            }

            context.Response.Redirect("/Account/Login");
        }
    }
}
