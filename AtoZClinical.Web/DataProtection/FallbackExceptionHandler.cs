using Microsoft.AspNetCore.Diagnostics;

namespace AtoZClinical.Web.DataProtection;

/// <summary>
/// Last-resort handler: writes a minimal HTML response without re-executing Razor pages
/// (avoids 500 loops when /Error itself fails).
/// </summary>
public sealed class FallbackExceptionHandler : IExceptionHandler
{
    private readonly ILogger<FallbackExceptionHandler> _logger;
    private readonly IHostEnvironment _env;

    public FallbackExceptionHandler(ILogger<FallbackExceptionHandler> logger, IHostEnvironment env)
    {
        _logger = logger;
        _env = env;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        _logger.LogError(
            exception,
            "Unhandled exception on {Method} {Path} trace={TraceId}",
            context.Request.Method,
            context.Request.Path,
            context.TraceIdentifier);

        if (context.Response.HasStarted)
            return false;

        var path = context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/Account", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/Error", StringComparison.OrdinalIgnoreCase))
        {
            await LoginRecoveryHelper.RecoverToLoginAsync(context);
            return true;
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "text/html; charset=utf-8";

        var detail = _env.IsDevelopment()
            ? $"<pre style=\"white-space:pre-wrap;font-size:12px;\">{System.Net.WebUtility.HtmlEncode(exception.Message)}</pre>"
            : string.Empty;

        await context.Response.WriteAsync(
            $"""
            <!DOCTYPE html>
            <html lang="en"><head><meta charset="utf-8"/><title>Error</title></head>
            <body style="font-family:Segoe UI,sans-serif;max-width:640px;margin:2rem auto;padding:1rem;">
            <h1 style="color:#b00020;">Something went wrong</h1>
            <p>An unexpected error occurred. Please try again or sign in again.</p>
            <p><strong>Reference:</strong> <code>{context.TraceIdentifier}</code></p>
            {detail}
            <p><a href="/Account/Login">Sign in</a></p>
            </body></html>
            """,
            cancellationToken);

        return true;
    }
}
