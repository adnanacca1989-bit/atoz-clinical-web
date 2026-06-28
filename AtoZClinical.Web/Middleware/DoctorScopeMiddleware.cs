using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.Extensions.Logging;

namespace AtoZClinical.Web.Middleware;

public sealed class DoctorScopeMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<DoctorScopeMiddleware> _logger;

    public DoctorScopeMiddleware(RequestDelegate next, ILogger<DoctorScopeMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        DoctorScopeContext scopeContext,
        DoctorScopeService doctorScope,
        ClinicContextService clinicContext)
    {
        var user = await clinicContext.GetCurrentUserAsync();
        if (user?.ClinicId is Guid clinicId)
        {
            scopeContext.Filter = await doctorScope.ResolveAsync(clinicId, user);
            if (scopeContext.Filter.IsRestricted)
            {
                _logger.LogDebug(
                    "Doctor scope active for {User}: DoctorRecordId={DoctorRecordId} DoctorName={DoctorName} path={Path}",
                    user.UserName,
                    scopeContext.Filter.DoctorRecordId,
                    scopeContext.Filter.DoctorName,
                    context.Request.Path);

                if (user.ClinicRole == Core.Enums.ClinicUserRole.Doctor && user.DoctorRecordId is null)
                {
                    _logger.LogWarning(
                        "Unlinked doctor user {Username} in clinic {ClinicId} — access uses name fallback only",
                        user.UserName, clinicId);
                }
            }
        }

        await _next(context);
    }
}
