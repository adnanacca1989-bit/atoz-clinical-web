using System.Threading.RateLimiting;
using AtoZClinical.Web.DataProtection;

namespace AtoZClinical.Web.Middleware;

/// <summary>Rate-limits authentication POST requests only (GET login is never limited).</summary>
public sealed class AuthPostRateLimitMiddleware
{
    private static readonly string[] AuthPostPaths =
    [
        "/Account/Login",
        "/Account/LoginWith2fa",
        "/Account/ForgotPassword",
        "/Account/ResetPassword",
        "/Account/ResendConfirmation",
        "/Account/ExternalLogin",
        "/Portal/Login"
    ];

    private static readonly PartitionedRateLimiter<string> Limiter =
        PartitionedRateLimiter.Create<string, string>(partitionKey =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey,
                _ => new FixedWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromMinutes(1),
                    PermitLimit = 20,
                    QueueLimit = 0
                }));

    private readonly RequestDelegate _next;
    private readonly ILogger<AuthPostRateLimitMiddleware> _logger;

    public AuthPostRateLimitMiddleware(RequestDelegate next, ILogger<AuthPostRateLimitMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method) || !IsAuthPostPath(context))
        {
            await _next(context);
            return;
        }

        var clientIp = ClientIpHelper.GetClientIp(context);
        using var lease = await Limiter.AcquireAsync(clientIp, 1, context.RequestAborted);
        if (!lease.IsAcquired)
        {
            _logger.LogWarning(
                "Auth POST rate limit exceeded for {ClientIp} on {Path} trace={TraceId}",
                clientIp,
                context.Request.Path,
                context.TraceIdentifier);

            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(
                """
                <!DOCTYPE html><html lang="en"><head><meta charset="utf-8"/><title>Too many attempts</title></head>
                <body style="font-family:Segoe UI,sans-serif;max-width:420px;margin:2rem auto;padding:1rem;">
                <h2>Too many sign-in attempts</h2>
                <p>Please wait one minute and try again.</p>
                <p><a href="/Account/Login">Back to sign in</a></p>
                </body></html>
                """);
            return;
        }

        await _next(context);
    }

    private static bool IsAuthPostPath(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        return AuthPostPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }
}
