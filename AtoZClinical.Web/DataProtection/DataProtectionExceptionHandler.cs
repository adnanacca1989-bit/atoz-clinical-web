using Microsoft.AspNetCore.Diagnostics;

namespace AtoZClinical.Web.DataProtection;

public sealed class DataProtectionExceptionHandler : IExceptionHandler
{
    private readonly ILogger<DataProtectionExceptionHandler> _logger;

    public DataProtectionExceptionHandler(ILogger<DataProtectionExceptionHandler> logger) =>
        _logger = logger;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (!DataProtectionExceptionHelper.IsRecoverable(exception))
            return false;

        _logger.LogWarning(
            exception,
            "Recoverable data protection failure on {Method} {Path} trace={TraceId}",
            context.Request.Method,
            context.Request.Path,
            context.TraceIdentifier);

        if (context.Response.HasStarted)
            return false;

        context.Response.Clear();
        await LoginRecoveryHelper.RecoverToLoginAsync(context);
        return true;
    }
}
