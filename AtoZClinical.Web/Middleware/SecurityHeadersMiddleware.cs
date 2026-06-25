namespace AtoZClinical.Web.Middleware;

public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _config;

    public SecurityHeadersMiddleware(RequestDelegate next, IConfiguration config)
    {
        _next = next;
        _config = config;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        headers["X-XSS-Protection"] = "0";

        if (_config.GetValue("Security:CspEnabled", true))
        {
            var csp = _config["Security:ContentSecurityPolicy"]
                ?? "default-src 'self'; script-src 'self' 'unsafe-inline' https://js.hcaptcha.com https://www.google.com https://www.gstatic.com; style-src 'self' 'unsafe-inline'; img-src 'self' data: https:; font-src 'self' data:; connect-src 'self' https://hcaptcha.com https://*.hcaptcha.com; frame-src https://hcaptcha.com https://*.hcaptcha.com https://www.google.com; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
            headers["Content-Security-Policy"] = csp;
        }

        if (context.Request.IsHttps)
            headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";

        await _next(context);
    }
}
