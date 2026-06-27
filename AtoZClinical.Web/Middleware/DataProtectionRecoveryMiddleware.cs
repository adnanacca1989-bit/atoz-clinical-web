using AtoZClinical.Web.DataProtection;

namespace AtoZClinical.Web.Middleware;

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
                "Data protection / antiforgery failure on {Path} trace={TraceId}",
                context.Request.Path,
                context.TraceIdentifier);

            if (context.Response.HasStarted)
                throw;

            DataProtectionExceptionHelper.ClearProtectedCookies(context);

            context.Response.Clear();
            var redirect = HttpMethods.IsPost(context.Request.Method)
                ? (context.Request.Path.Value ?? "/Account/Login")
                : "/Account/Login?session=refresh";
            context.Response.Redirect(redirect);
        }
    }
}
