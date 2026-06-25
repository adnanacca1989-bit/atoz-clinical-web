using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;

namespace AtoZClinical.Web.Middleware;

/// <summary>Resolves clinic from request host subdomain when Security:BaseDomain is configured.</summary>
public sealed class ClinicSubdomainMiddleware
{
    private readonly RequestDelegate _next;

    public ClinicSubdomainMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, SubdomainClinicResolver resolver)
    {
        var clinic = await resolver.ResolveFromHostAsync(context.Request.Host.Host);
        if (clinic is not null)
            context.Items[HttpContextClinicProvider.SubdomainClinicIdKey] = clinic.Id;

        await _next(context);
    }
}
