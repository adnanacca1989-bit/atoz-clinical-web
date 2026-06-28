using AtoZClinical.Core.Entities;

namespace AtoZClinical.Infrastructure.Services;

public static class PatientNameMatcher
{
    public static bool NamesReferToSamePatient(string? storedName, string? canonicalName)
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

    public static bool BarcodesReferToSamePatient(string? storedBarcode, string patientNo, string oldPatientNo)
    {
        if (string.IsNullOrWhiteSpace(storedBarcode))
            return false;

        var barcode = storedBarcode.Trim();
        return string.Equals(barcode, patientNo, StringComparison.OrdinalIgnoreCase)
            || string.Equals(barcode, oldPatientNo, StringComparison.OrdinalIgnoreCase);
    }

    public static bool ShouldUpdatePatient(
        Guid? recordId,
        string? storedName,
        string? storedBarcode,
        Guid patientId,
        string patientNo,
        string oldPatientNo,
        ISet<string> nameVariants)
    {
        if (recordId == patientId)
            return true;

        if (BarcodesReferToSamePatient(storedBarcode, patientNo, oldPatientNo))
            return true;

        if (!string.IsNullOrWhiteSpace(storedName) && nameVariants.Contains(storedName.Trim()))
            return true;

        return false;
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

    public static Patient? ResolveSinglePatient(
        IReadOnlyList<Patient> patients,
        Guid? recordId,
        string? patientNo,
        string? patientName)
    {
        if (recordId is Guid id && id != Guid.Empty)
        {
            var byId = patients.FirstOrDefault(p => p.Id == id);
            if (byId is not null)
                return byId;
        }

        if (!string.IsNullOrWhiteSpace(patientNo))
        {
            var no = patientNo.Trim();
            var byNo = patients.FirstOrDefault(p =>
                string.Equals(p.PatientNo, no, StringComparison.OrdinalIgnoreCase));
            if (byNo is not null)
                return byNo;
        }

        if (string.IsNullOrWhiteSpace(patientName))
            return null;

        var name = patientName.Trim();
        var exact = patients.FirstOrDefault(p =>
            string.Equals(p.FullName, name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact;

        var prefixMatches = patients
            .Where(p => NamesReferToSamePatient(p.FullName, name))
            .ToList();

        return prefixMatches.Count == 1 ? prefixMatches[0] : null;
    }

    public static void CollectUnambiguousVariants(
        IReadOnlyList<Patient> patients,
        Guid patientId,
        string canonicalName,
        ISet<string> variants)
    {
        foreach (var patient in patients.Where(p => p.Id == patientId))
        {
            variants.Add(patient.FullName.Trim());
            variants.Add(canonicalName.Trim());
        }

        var candidateNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var patient in patients)
        {
            if (NamesReferToSamePatient(patient.FullName, canonicalName))
                candidateNames.Add(patient.FullName.Trim());
        }

        foreach (var candidate in candidateNames)
        {
            var matches = patients.Where(p => NamesReferToSamePatient(p.FullName, candidate)).ToList();
            if (matches.Count == 1 && matches[0].Id == patientId)
                variants.Add(candidate);
        }
    }
}
