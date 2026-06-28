using AtoZClinical.Core.Enums;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public sealed class DoctorScopeService
{
    private readonly ClinicalDbContext _db;
    private readonly ClinicSettingsService _settings;

    public DoctorScopeService(ClinicalDbContext db, ClinicSettingsService settings)
    {
        _db = db;
        _settings = settings;
    }

    public async Task<DoctorScopeFilter> ResolveAsync(Guid clinicId, ApplicationUser user)
    {
        if (user.IsVendorAdmin || user.ClinicRole is not ClinicUserRole.Doctor)
            return DoctorScopeFilter.Unrestricted;

        var config = await _settings.GetOrCreateAsync(clinicId);
        if (config.AllowDoctorViewAllPatients)
            return DoctorScopeFilter.Unrestricted;

        Guid? doctorRecordId = user.DoctorRecordId;
        string? doctorName = null;

        if (doctorRecordId.HasValue)
        {
            doctorName = await _db.Doctors.AsNoTracking()
                .Where(d => d.ClinicId == clinicId && d.Id == doctorRecordId.Value)
                .Select(d => d.Name)
                .FirstOrDefaultAsync();
        }

        if (string.IsNullOrWhiteSpace(doctorName) && !string.IsNullOrWhiteSpace(user.FullName))
        {
            var name = user.FullName.Trim();
            var doctor = await _db.Doctors.AsNoTracking()
                .ForClinic(clinicId)
                .Where(d => d.Name != null && EF.Functions.ILike(d.Name, name))
                .FirstOrDefaultAsync();
            if (doctor is not null)
            {
                doctorRecordId = doctor.Id;
                doctorName = doctor.Name;
            }
            else
            {
                doctorName = name;
            }
        }

        return new DoctorScopeFilter
        {
            IsRestricted = true,
            DoctorRecordId = doctorRecordId,
            DoctorName = doctorName
        };
    }
}
