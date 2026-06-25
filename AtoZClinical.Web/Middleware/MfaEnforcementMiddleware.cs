using AtoZClinical.Infrastructure.Identity;
using AtoZClinical.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;

namespace AtoZClinical.Web.Middleware;

/// <summary>Redirects admin accounts without MFA to enrollment when Security:RequireMfaForAdmins is enabled.</summary>
public sealed class MfaEnforcementMiddleware
{
    private readonly RequestDelegate _next;

    private static readonly string[] AllowedPrefixes =
    [
        "/Account",
        "/Settings/TwoFactor",
        "/lib",
        "/css",
        "/js",
        "/favicon",
        "/health"
    ];

    public MfaEnforcementMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext context,
        UserManager<ApplicationUser> users,
        MfaPolicyService mfaPolicy)
    {
        if (context.User.Identity?.IsAuthenticated == true && mfaPolicy.IsEnforcementEnabled)
        {
            var path = context.Request.Path.Value ?? string.Empty;
            if (!AllowedPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            {
                var user = await users.GetUserAsync(context.User);
                if (user is not null && await mfaPolicy.RequiresMfaAsync(user) && !user.TwoFactorEnabled)
                {
                    context.Response.Redirect("/Settings/TwoFactor?required=1");
                    return;
                }
            }
        }

        await _next(context);
    }
}
