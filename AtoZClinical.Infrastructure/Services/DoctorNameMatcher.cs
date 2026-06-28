using AtoZClinical.Core.Entities;

namespace AtoZClinical.Infrastructure.Services;

public static class DoctorNameMatcher
{
    public static bool NamesReferToSameDoctor(string? storedName, string? canonicalName)
    {
        if (string.IsNullOrWhiteSpace(storedName) || string.IsNullOrWhiteSpace(canonicalName))
            return false;

        var a = storedName.Trim();
        var b = canonicalName.Trim();
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
            return true;

        return a.StartsWith(b, StringComparison.OrdinalIgnoreCase)
            || b.StartsWith(a, StringComparison.OrdinalIgnoreCase);
    }

    public static bool ShouldUpdateDoctor(Guid? recordId, string? storedName, Guid doctorId, ISet<string> nameVariants)
    {
        if (recordId == doctorId)
            return true;

        if (string.IsNullOrWhiteSpace(storedName))
            return false;

        return nameVariants.Contains(storedName.Trim());
    }

    public static HashSet<string> BuildNameVariants(string oldName, string newName, IEnumerable<string?> additionalNames)
    {
        var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            oldName.Trim(),
            newName.Trim()
        };

        foreach (var name in additionalNames)
        {
            if (!string.IsNullOrWhiteSpace(name))
                variants.Add(name.Trim());
        }

        return variants;
    }

    public static Doctor? ResolveSingleDoctor(IReadOnlyList<Doctor> doctors, Guid? recordId, string? doctorName)
    {
        if (recordId is Guid id && id != Guid.Empty)
        {
            var byId = doctors.FirstOrDefault(d => d.Id == id);
            if (byId is not null)
                return byId;
        }

        if (string.IsNullOrWhiteSpace(doctorName))
            return null;

        var name = doctorName.Trim();
        var exact = doctors.FirstOrDefault(d =>
            string.Equals(d.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact;

        var prefixMatches = doctors
            .Where(d => NamesReferToSameDoctor(d.Name, name))
            .ToList();

        return prefixMatches.Count == 1 ? prefixMatches[0] : null;
    }

    public static void CollectUnambiguousVariants(
        IReadOnlyList<Doctor> doctors,
        Guid doctorId,
        string canonicalName,
        ISet<string> variants)
    {
        foreach (var doctor in doctors)
        {
            if (doctor.Id != doctorId)
                continue;

            variants.Add(doctor.Name.Trim());
            variants.Add(canonicalName.Trim());
        }

        var candidateNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var doctor in doctors)
        {
            if (NamesReferToSameDoctor(doctor.Name, canonicalName))
                candidateNames.Add(doctor.Name.Trim());
        }

        foreach (var candidate in candidateNames)
        {
            var matches = doctors.Where(d => NamesReferToSameDoctor(d.Name, candidate)).ToList();
            if (matches.Count == 1 && matches[0].Id == doctorId)
                variants.Add(candidate);
        }
    }
}
