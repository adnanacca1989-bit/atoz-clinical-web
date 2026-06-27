namespace AtoZClinical.Web.Middleware;

/// <summary>Logs login POST outcomes: auth state, redirect target, and Set-Cookie issuance.</summary>
public sealed class LoginAuthDiagnosticsMiddleware
{
    private static readonly string[] LoginPaths = ["/Account/Login", "/Portal/Login"];

    private readonly RequestDelegate _next;
    private readonly ILogger<LoginAuthDiagnosticsMiddleware> _logger;

    public LoginAuthDiagnosticsMiddleware(RequestDelegate next, ILogger<LoginAuthDiagnosticsMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var isLoginPost = HttpMethods.IsPost(context.Request.Method)
            && LoginPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));

        await _next(context);

        if (!isLoginPost)
            return;

        var status = context.Response.StatusCode;
        var location = context.Response.Headers.Location.ToString();
        var setCookie = context.Response.Headers.SetCookie.ToString();
        var authCookieIssued = setCookie.Contains(".AspNetCore.", StringComparison.OrdinalIgnoreCase);
        var isAuthenticated = context.User.Identity?.IsAuthenticated == true;
        var isRedirect = status is StatusCodes.Status302Found or StatusCodes.Status303SeeOther;
        var redirectLoop = isRedirect && location.Contains("/Account/Login", StringComparison.OrdinalIgnoreCase);

        _logger.LogInformation(
            "Login POST diagnostics status={StatusCode} authenticated={Authenticated} authCookieIssued={AuthCookieIssued} redirect={Redirect} redirectLoop={RedirectLoop} location={Location} client={ClientIp} trace={TraceId}",
            status,
            isAuthenticated,
            authCookieIssued,
            isRedirect,
            redirectLoop,
            string.IsNullOrEmpty(location) ? "(none)" : location,
            ClientIpHelper.GetClientIp(context),
            context.TraceIdentifier);

        if (redirectLoop)
        {
            _logger.LogWarning(
                "Login POST redirect loop detected for {ClientIp} trace={TraceId} location={Location}",
                ClientIpHelper.GetClientIp(context),
                context.TraceIdentifier,
                location);
        }

        if (isAuthenticated && !authCookieIssued && isRedirect)
        {
            _logger.LogWarning(
                "Login POST authenticated but no auth Set-Cookie on redirect for {ClientIp} trace={TraceId}",
                ClientIpHelper.GetClientIp(context),
                context.TraceIdentifier);
        }
    }
}
