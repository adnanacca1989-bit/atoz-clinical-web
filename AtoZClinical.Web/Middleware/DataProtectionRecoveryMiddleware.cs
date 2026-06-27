using AtoZClinical.Web.DataProtection;

namespace AtoZClinical.Web.Middleware;

/// <summary>Recovers from stale encrypted cookies without redirect loops.</summary>
public sealed class DataProtectionRecoveryMiddleware
{
    private const string RecoveryFlagKey = "__clinical_dp_recovery";

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

            if (context.Items.ContainsKey(RecoveryFlagKey))
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.WriteAsync(
                    """
                    <!DOCTYPE html><html lang="en"><head><meta charset="utf-8"/><title>Sign in</title></head>
                    <body style="font-family:Segoe UI,sans-serif;max-width:420px;margin:2rem auto;padding:1rem;">
                    <h2>Session reset required</h2>
                    <p>Clear cookies for this site in your browser settings, then open
                    <a href="/Account/Login">Sign in</a>.</p></body></html>
                    """);
                return;
            }

            context.Items[RecoveryFlagKey] = true;
            context.Response.Redirect("/Account/Login?recovered=1");
        }
    }
}
