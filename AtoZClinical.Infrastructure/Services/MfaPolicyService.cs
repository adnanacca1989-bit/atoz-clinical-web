using AtoZClinical.Core.Enums;
using AtoZClinical.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;

namespace AtoZClinical.Infrastructure.Services;

public sealed class MfaPolicyService
{
    private readonly IConfiguration _config;
    private readonly UserManager<ApplicationUser> _users;

    public MfaPolicyService(IConfiguration config, UserManager<ApplicationUser> users)
    {
        _config = config;
        _users = users;
    }

    public bool IsEnforcementEnabled =>
        _config.GetValue("Security:RequireMfaForAdmins", false);

    public async Task<bool> RequiresMfaAsync(ApplicationUser user)
    {
        if (!IsEnforcementEnabled) return false;
        if (!user.IsActive) return false;
        if (user.IsVendorAdmin || await _users.IsInRoleAsync(user, ClinicalRoles.Vendor))
            return true;
        return user.ClinicRole == ClinicUserRole.ClinicAdmin;
    }

    public async Task<bool> IsCompliantAsync(ApplicationUser user) =>
        !await RequiresMfaAsync(user) || user.TwoFactorEnabled;
}
