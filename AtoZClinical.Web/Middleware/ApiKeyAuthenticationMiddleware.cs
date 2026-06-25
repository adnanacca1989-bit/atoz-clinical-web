using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;

namespace AtoZClinical.Web.Middleware;

/// <summary>Sets tenant context for API key authenticated requests.</summary>
public sealed class ApiKeyAuthenticationMiddleware
{
    private readonly RequestDelegate _next;

    public ApiKeyAuthenticationMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ClinicApiKeyService apiKeys)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/api/v1", StringComparison.OrdinalIgnoreCase))
        {
            var key = context.Request.Headers["X-Api-Key"].FirstOrDefault()
                      ?? context.Request.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(key))
            {
                var clinicId = await apiKeys.ValidateAsync(key);
                if (clinicId.HasValue)
                {
                    context.Items[HttpContextClinicProvider.TenantClinicIdKey] = clinicId.Value;
                    context.Items["ApiKeyAuthenticated"] = true;
                }
            }
        }

        await _next(context);
    }
}
