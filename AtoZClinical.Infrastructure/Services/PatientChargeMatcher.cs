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

    private static bool Equals(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
