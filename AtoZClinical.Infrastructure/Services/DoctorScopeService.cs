using AtoZClinical.Core.Enums;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AtoZClinical.Infrastructure.Services;

public sealed class DoctorScopeService
{
    private readonly ClinicalDbContext _db;
    private readonly ClinicSettingsService _settings;
    private readonly ILogger<DoctorScopeService> _logger;

    public DoctorScopeService(ClinicalDbContext db, ClinicSettingsService settings, ILogger<DoctorScopeService> logger)
    {
        _db = db;
        _settings = settings;
        _logger = logger;
    }

    public async Task<DoctorScopeFilter> ResolveAsync(Guid clinicId, ApplicationUser user)
    {
        if (user.IsVendorAdmin || user.ClinicRole is not ClinicUserRole.Doctor)
        {
            _logger.LogDebug("Doctor scope unrestricted for user {User} role {Role}", user.UserName, user.ClinicRole);
            return DoctorScopeFilter.Unrestricted;
        }

        var config = await _settings.GetOrCreateAsync(clinicId);
        if (config.AllowDoctorViewAllPatients)
        {
            _logger.LogDebug("Doctor scope unrestricted for {User}: AllowDoctorViewAllPatients enabled", user.UserName);
            return DoctorScopeFilter.Unrestricted;
        }

        Guid? doctorRecordId = user.DoctorRecordId;
        string? doctorName = null;

        if (!doctorRecordId.HasValue)
        {
            _logger.LogWarning(
                "Doctor user {User} has no DoctorRecordId — access restricted to empty scope until linked in Define User",
                user.UserName);
            return new DoctorScopeFilter { IsRestricted = true };
        }

        doctorName = await _db.Doctors.AsNoTracking()
            .Where(d => d.ClinicId == clinicId && d.Id == doctorRecordId.Value)
            .Select(d => d.Name)
            .FirstOrDefaultAsync();

        if (string.IsNullOrWhiteSpace(doctorName))
        {
            _logger.LogWarning(
                "Doctor user {User} DoctorRecordId {DoctorRecordId} not found — access restricted to empty scope",
                user.UserName, doctorRecordId);
            return new DoctorScopeFilter { IsRestricted = true, DoctorRecordId = doctorRecordId };
        }

        var filter = new DoctorScopeFilter
        {
            IsRestricted = true,
            DoctorRecordId = doctorRecordId,
            DoctorName = doctorName
        };
        _logger.LogInformation(
            "Doctor scope restricted for {User}: DoctorRecordId={DoctorRecordId}, DoctorName={DoctorName}",
            user.UserName, doctorRecordId, doctorName);
        return filter;
    }
}
