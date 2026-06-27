using System.Threading.RateLimiting;

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
                    PermitLimit = 30,
                    QueueLimit = 0
                }));

    private readonly RequestDelegate _next;

    public AuthPostRateLimitMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method) || !IsAuthPostPath(context))
        {
            await _next(context);
            return;
        }

        var partition = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        using var lease = await Limiter.AcquireAsync(partition, 1, context.RequestAborted);
        if (!lease.IsAcquired)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync(
                "Too many sign-in attempts. Please wait one minute and try again.");
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
