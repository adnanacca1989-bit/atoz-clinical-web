namespace AtoZClinical.Infrastructure.Services;

public static class PatientChargeMatcher
{
    public static bool MatchesPatient(
        string? barcode,
        string? name,
        string? recordBarcode,
        string? recordPatientId,
        string? recordName,
        Guid? patientRecordId = null,
        Guid? recordPatientRecordId = null)
    {
        barcode = barcode?.Trim();
        name = name?.Trim();
        recordBarcode = recordBarcode?.Trim();
        recordPatientId = recordPatientId?.Trim();
        recordName = recordName?.Trim();

        if (patientRecordId.HasValue && patientRecordId.Value != Guid.Empty)
        {
            if (recordPatientRecordId.HasValue && recordPatientRecordId.Value != Guid.Empty)
                return recordPatientRecordId == patientRecordId;

            if (!string.IsNullOrEmpty(barcode))
                return Equals(recordBarcode, barcode) || Equals(recordPatientId, barcode);

            return false;
        }

        if (!string.IsNullOrEmpty(barcode))
            return Equals(recordBarcode, barcode) || Equals(recordPatientId, barcode);

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

    /// <summary>Loose barcode/name match for SQLite in-memory filtering (PostgreSQL uses ILike in SQL).</summary>
    public static bool MatchesBarcodeOrNameFields(
        string? barcode, string? name, string? entityBarcode, string? entityName)
    {
        barcode = barcode?.Trim();
        name = name?.Trim();
        entityBarcode = entityBarcode?.Trim();
        entityName = entityName?.Trim();

        if (!string.IsNullOrEmpty(barcode) && !string.IsNullOrEmpty(entityBarcode))
        {
            if (Equals(entityBarcode, barcode)) return true;
            if (entityBarcode.Contains(barcode, StringComparison.OrdinalIgnoreCase)) return true;
        }

        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(entityName))
        {
            if (Equals(entityName, name)) return true;
            if (entityName.Contains(name, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    private static bool Equals(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
