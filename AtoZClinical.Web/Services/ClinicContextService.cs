using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Identity;
using AtoZClinical.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Web.Services;

public sealed class ClinicContextService
{
    private readonly IHttpContextAccessor _http;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ClinicalDbContext _db;
    private readonly ClinicAccessService _access;

    public ClinicContextService(
        IHttpContextAccessor http,
        UserManager<ApplicationUser> users,
        ClinicalDbContext db,
        ClinicAccessService access)
    {
        _http = http;
        _users = users;
        _db = db;
        _access = access;
    }

    public async Task<ApplicationUser?> GetCurrentUserAsync()
    {
        var principal = _http.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true) return null;
        return await _users.GetUserAsync(principal);
    }

    public async Task<Clinic?> GetCurrentClinicAsync()
    {
        var user = await GetCurrentUserAsync();
        if (user?.ClinicId is null) return null;
        return await _db.Clinics.FirstOrDefaultAsync(c => c.Id == user.ClinicId);
    }

    public async Task<Guid?> GetClinicIdAsync()
    {
        var user = await GetCurrentUserAsync();
        return user?.ClinicId;
    }

    public async Task<bool> IsVendorAsync()
    {
        var user = await GetCurrentUserAsync();
        return user?.IsVendorAdmin == true;
    }

    public async Task<ClinicAccessResult> GetClinicAccessAsync()
    {
        if (await IsVendorAsync())
            return ClinicAccessResult.Allowed(new Clinic { Id = Guid.Empty, Name = "Vendor", Status = Core.Enums.ClinicStatus.Active });

        return _access.Evaluate(await GetCurrentClinicAsync());
    }

    public async Task<Guid?> RequireOperationalClinicIdAsync()
    {
        var access = await GetClinicAccessAsync();
        return access.IsAllowed ? access.Clinic?.Id ?? await GetClinicIdAsync() : null;
    }
}
