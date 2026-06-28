using AtoZClinical.Core.Enums;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AtoZClinical.Infrastructure.Services;

public sealed class DoctorUserLinkService
{
    private readonly ClinicalDbContext _db;
    private readonly ILogger<DoctorUserLinkService> _logger;

    public DoctorUserLinkService(ClinicalDbContext db, ILogger<DoctorUserLinkService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public sealed record UnlinkedDoctorUserRow(
        string UserId,
        int UserNo,
        string? Username,
        string? FullName,
        bool IsActive);

    public async Task<int> BackfillAllClinicsAsync()
    {
        var clinicIds = await _db.Clinics.AsNoTracking().Select(c => c.Id).ToListAsync();
        var total = 0;
        foreach (var clinicId in clinicIds)
            total += await BackfillClinicAsync(clinicId);
        return total;
    }

    public async Task<int> BackfillClinicAsync(Guid clinicId)
    {
        var doctors = await _db.Doctors.AsNoTracking()
            .Where(d => d.ClinicId == clinicId)
            .ToListAsync();
        if (doctors.Count == 0) return 0;

        var linkedDoctorIds = await _db.Users.AsNoTracking()
            .Where(u => u.ClinicId == clinicId && u.DoctorRecordId != null)
            .Select(u => u.DoctorRecordId!.Value)
            .ToListAsync();
        var linkedSet = linkedDoctorIds.ToHashSet();

        var users = await _db.Users
            .Where(u => u.ClinicId == clinicId && u.ClinicRole == ClinicUserRole.Doctor && u.DoctorRecordId == null)
            .ToListAsync();

        var linked = 0;
        foreach (var user in users)
        {
            if (string.IsNullOrWhiteSpace(user.FullName)) continue;

            var name = user.FullName.Trim();
            var doctor = doctors.FirstOrDefault(d =>
                string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
            if (doctor is null) continue;
            if (linkedSet.Contains(doctor.Id)) continue;

            user.DoctorRecordId = doctor.Id;
            user.FullName = doctor.Name;
            linkedSet.Add(doctor.Id);
            linked++;
            _logger.LogInformation(
                "Auto-linked doctor user {Username} to doctor {DoctorName} ({DoctorId}) in clinic {ClinicId}",
                user.UserName, doctor.Name, doctor.Id, clinicId);
        }

        if (linked > 0)
            await _db.SaveChangesAsync();

        return linked;
    }

    public async Task<List<UnlinkedDoctorUserRow>> ListUnlinkedAsync(Guid clinicId) =>
        await _db.Users.AsNoTracking()
            .Where(u => u.ClinicId == clinicId &&
                        u.ClinicRole == ClinicUserRole.Doctor &&
                        u.DoctorRecordId == null)
            .OrderBy(u => u.UserNo)
            .Select(u => new UnlinkedDoctorUserRow(
                u.Id,
                u.UserNo,
                u.UserName,
                u.FullName,
                u.IsActive))
            .ToListAsync();
}

public static class DoctorUserLinkBackfill
{
    public static async Task<int> BackfillAsync(ClinicalDbContext db, ILogger? logger = null)
    {
        var doctorsByClinic = await db.Doctors.AsNoTracking()
            .GroupBy(d => d.ClinicId)
            .ToDictionaryAsync(g => g.Key, g => g.ToList());

        var users = await db.Users
            .Where(u => u.ClinicRole == ClinicUserRole.Doctor && u.DoctorRecordId == null)
            .ToListAsync();

        var linkedDoctorIds = await db.Users.AsNoTracking()
            .Where(u => u.DoctorRecordId != null)
            .Select(u => new { u.ClinicId, DoctorId = u.DoctorRecordId!.Value })
            .ToListAsync();
        var linkedSet = linkedDoctorIds
            .Where(x => x.ClinicId.HasValue)
            .GroupBy(x => x.ClinicId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(x => x.DoctorId).ToHashSet());

        var count = 0;
        foreach (var user in users)
        {
            if (user.ClinicId is null || string.IsNullOrWhiteSpace(user.FullName)) continue;
            if (!doctorsByClinic.TryGetValue(user.ClinicId.Value, out var doctors)) continue;

            var name = user.FullName.Trim();
            var doctor = doctors.FirstOrDefault(d =>
                string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
            if (doctor is null) continue;

            linkedSet.TryGetValue(user.ClinicId.Value, out var clinicLinked);
            clinicLinked ??= [];
            if (clinicLinked.Contains(doctor.Id)) continue;

            user.DoctorRecordId = doctor.Id;
            user.FullName = doctor.Name;
            clinicLinked.Add(doctor.Id);
            linkedSet[user.ClinicId.Value] = clinicLinked;
            count++;
            logger?.LogInformation(
                "Startup auto-linked doctor user {Username} to {DoctorName}",
                user.UserName, doctor.Name);
        }

        if (count > 0)
            await db.SaveChangesAsync();

        return count;
    }
}
