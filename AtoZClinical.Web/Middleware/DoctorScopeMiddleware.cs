using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;

namespace AtoZClinical.Web.Middleware;

public sealed class DoctorScopeMiddleware
{
    private readonly RequestDelegate _next;

    public DoctorScopeMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext context,
        DoctorScopeContext scopeContext,
        DoctorScopeService doctorScope,
        ClinicContextService clinicContext)
    {
        var user = await clinicContext.GetCurrentUserAsync();
        if (user?.ClinicId is Guid clinicId)
            scopeContext.Filter = await doctorScope.ResolveAsync(clinicId, user);

        await _next(context);
    }
}
