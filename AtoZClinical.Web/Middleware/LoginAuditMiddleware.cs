namespace AtoZClinical.Web.Middleware;

/// <summary>Structured logging for login GET/POST outcomes.</summary>
public sealed class LoginAuditMiddleware
{
    private static readonly string[] AuditedPaths = ["/Account/Login", "/Account/LoginWith2fa", "/Portal/Login"];

    private readonly RequestDelegate _next;
    private readonly ILogger<LoginAuditMiddleware> _logger;

    public LoginAuditMiddleware(RequestDelegate next, ILogger<LoginAuditMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var isAudited = AuditedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));

        await _next(context);

        if (!isAudited)
            return;

        var status = context.Response.StatusCode;
        var level = status >= 500 ? LogLevel.Error : status >= 400 ? LogLevel.Warning : LogLevel.Information;

        _logger.Log(
            level,
            "Login {Method} {Path} status={StatusCode} client={ClientIp} trace={TraceId}",
            context.Request.Method,
            path,
            status,
            ClientIpHelper.GetClientIp(context),
            context.TraceIdentifier);
    }
}
