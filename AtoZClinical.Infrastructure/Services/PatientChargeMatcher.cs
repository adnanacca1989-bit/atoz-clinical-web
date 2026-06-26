namespace AtoZClinical.Infrastructure.Services;

internal static class PatientChargeMatcher
{
    public static bool MatchesPatient(
        string? barcode,
        string? name,
        string? recordBarcode,
        string? recordPatientId,
        string? recordName)
    {
        barcode = barcode?.Trim();
        name = name?.Trim();
        recordBarcode = recordBarcode?.Trim();
        recordPatientId = recordPatientId?.Trim();
        recordName = recordName?.Trim();

        if (!string.IsNullOrEmpty(barcode))
        {
            if (Equals(recordBarcode, barcode) || Equals(recordPatientId, barcode))
                return true;
        }

        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(recordName))
        {
            if (Equals(recordName, name)) return true;
            if (recordName.Contains(name, StringComparison.OrdinalIgnoreCase) ||
                name.Contains(recordName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static bool MatchesDoctor(string? invoiceDoctor, string? recordDoctor)
    {
        invoiceDoctor = invoiceDoctor?.Trim();
        recordDoctor = recordDoctor?.Trim();
        if (string.IsNullOrWhiteSpace(invoiceDoctor) || string.IsNullOrWhiteSpace(recordDoctor))
            return true;
        if (Equals(invoiceDoctor, recordDoctor)) return true;
        return recordDoctor.Contains(invoiceDoctor, StringComparison.OrdinalIgnoreCase) ||
               invoiceDoctor.Contains(recordDoctor, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Equals(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
