using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public static class DoctorSpecialtyResolver
{
    public static string Resolve(string? doctorName, string? storedSpecialty, IReadOnlyList<Doctor> doctors)
    {
        if (!string.IsNullOrWhiteSpace(doctorName))
        {
            var name = doctorName.Trim();
            var match = doctors.FirstOrDefault(d =>
                string.Equals(d.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match?.Specialty))
                return match.Specialty.Trim();
        }

        return storedSpecialty?.Trim() ?? "";
    }

    public static async Task<Dictionary<string, string>> BuildMapAsync(ClinicalDbContext db, Guid clinicId)
    {
        var doctors = await db.Doctors
            .AsNoTracking()
            .Where(d => d.ClinicId == clinicId)
            .ToListAsync();

        return doctors
            .Where(d => !string.IsNullOrWhiteSpace(d.Name) && !string.IsNullOrWhiteSpace(d.Specialty))
            .GroupBy(d => d.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Specialty!.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    public static string ResolveFromMap(string? doctorName, string? storedSpecialty, IReadOnlyDictionary<string, string> map)
    {
        if (!string.IsNullOrWhiteSpace(doctorName) &&
            map.TryGetValue(doctorName.Trim(), out var specialty) &&
            !string.IsNullOrWhiteSpace(specialty))
            return specialty;

        return storedSpecialty?.Trim() ?? "";
    }
}
