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

    public DoctorScopeService(
        ClinicalDbContext db,
        ClinicSettingsService settings,
        ILogger<DoctorScopeService> logger)
    {
        _db = db;
        _settings = settings;
        _logger = logger;
    }

    public async Task<DoctorScopeFilter> ResolveAsync(Guid clinicId, ApplicationUser user)
    {
        if (user.IsVendorAdmin || user.ClinicRole is not ClinicUserRole.Doctor)
            return DoctorScopeFilter.Unrestricted;

        var config = await _settings.GetOrCreateAsync(clinicId);
        if (config.AllowDoctorViewAllPatients)
        {
            _logger.LogDebug("Doctor scope unrestricted for {User}: all clinic patients visible", user.UserName);
            return DoctorScopeFilter.Unrestricted;
        }

        // Opt-in only: admin turned off "Doctors can view all patients" in Settings → Doctor Access.
        Guid? doctorRecordId = user.DoctorRecordId;
        if (!doctorRecordId.HasValue)
            return new DoctorScopeFilter { IsRestricted = true };

        var doctorName = await _db.Doctors.AsNoTracking()
            .Where(d => d.ClinicId == clinicId && d.Id == doctorRecordId.Value)
            .Select(d => d.Name)
            .FirstOrDefaultAsync();

        if (string.IsNullOrWhiteSpace(doctorName))
            return new DoctorScopeFilter { IsRestricted = true, DoctorRecordId = doctorRecordId };

        return new DoctorScopeFilter
        {
            IsRestricted = true,
            DoctorRecordId = doctorRecordId,
            DoctorName = doctorName
        };
    }
}
