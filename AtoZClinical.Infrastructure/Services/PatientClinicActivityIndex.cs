using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

/// <summary>
/// Preloads clinic-wide patient activity flags once for bulk status reconciliation.
/// </summary>
internal sealed class PatientClinicActivityIndex
{
    private readonly HashSet<Guid> _invoiceRecordIds;
    private readonly HashSet<string> _invoicePatientNos;
    private readonly List<string> _invoicePatientNames;
    private readonly HashSet<Guid> _receiptRecordIds;
    private readonly HashSet<string> _receiptPatientNos;
    private readonly List<string> _receiptPatientNames;
    private readonly HashSet<Guid> _clinicalRecordIds;
    private readonly HashSet<string> _clinicalPatientNos;
    private readonly List<string> _clinicalPatientNames;

    private PatientClinicActivityIndex(
        HashSet<Guid> invoiceRecordIds,
        HashSet<string> invoicePatientNos,
        List<string> invoicePatientNames,
        HashSet<Guid> receiptRecordIds,
        HashSet<string> receiptPatientNos,
        List<string> receiptPatientNames,
        HashSet<Guid> clinicalRecordIds,
        HashSet<string> clinicalPatientNos,
        List<string> clinicalPatientNames)
    {
        _invoiceRecordIds = invoiceRecordIds;
        _invoicePatientNos = invoicePatientNos;
        _invoicePatientNames = invoicePatientNames;
        _receiptRecordIds = receiptRecordIds;
        _receiptPatientNos = receiptPatientNos;
        _receiptPatientNames = receiptPatientNames;
        _clinicalRecordIds = clinicalRecordIds;
        _clinicalPatientNos = clinicalPatientNos;
        _clinicalPatientNames = clinicalPatientNames;
    }

    public static async Task<PatientClinicActivityIndex> LoadAsync(
        ClinicalDbContext db,
        Guid clinicId,
        CancellationToken ct = default)
    {
        var provisional = PatientVisitInvoiceMarkers.ProvisionalToken;

        var invoiceRows = await db.Invoices.ForClinic(clinicId).AsNoTracking()
            .Where(i => i.Notes == null || !i.Notes.Contains(provisional))
            .Select(i => new { i.PatientRecordId, i.PatientId, i.PatientName })
            .ToListAsync(ct);

        var receiptRows = await db.CashReceipts.ForClinic(clinicId).AsNoTracking()
            .Select(r => new { r.PatientRecordId, r.PatientId, r.PatientName })
            .ToListAsync(ct);

        var clinicalRecordIds = new HashSet<Guid>();
        var clinicalPatientNos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var clinicalPatientNames = new List<string>();

        await LoadClinicalActivityAsync(db, clinicId, clinicalRecordIds, clinicalPatientNos, clinicalPatientNames, ct);

        return new PatientClinicActivityIndex(
            invoiceRows.Where(r => r.PatientRecordId.HasValue).Select(r => r.PatientRecordId!.Value).ToHashSet(),
            invoiceRows.Select(r => r.PatientId).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase),
            invoiceRows.Select(r => r.PatientName).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            receiptRows.Where(r => r.PatientRecordId.HasValue).Select(r => r.PatientRecordId!.Value).ToHashSet(),
            receiptRows.Select(r => r.PatientId).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase),
            receiptRows.Select(r => r.PatientName).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            clinicalRecordIds,
            clinicalPatientNos,
            clinicalPatientNames);
    }

    private static async Task LoadClinicalActivityAsync(
        ClinicalDbContext db,
        Guid clinicId,
        HashSet<Guid> recordIds,
        HashSet<string> patientNos,
        List<string> patientNames,
        CancellationToken ct)
    {
        static void AddName(List<string> names, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                names.Add(value.Trim());
        }

        foreach (var id in await db.LabRequests.ForClinic(clinicId).AsNoTracking()
                     .Where(r => r.PatientRecordId != null).Select(r => r.PatientRecordId!.Value).ToListAsync(ct))
            recordIds.Add(id);
        foreach (var id in await db.LabResults.ForClinic(clinicId).AsNoTracking()
                     .Where(r => r.PatientRecordId != null).Select(r => r.PatientRecordId!.Value).ToListAsync(ct))
            recordIds.Add(id);
        foreach (var id in await db.RadiologyRequests.ForClinic(clinicId).AsNoTracking()
                     .Where(r => r.PatientRecordId != null).Select(r => r.PatientRecordId!.Value).ToListAsync(ct))
            recordIds.Add(id);
        foreach (var id in await db.RadiologyResults.ForClinic(clinicId).AsNoTracking()
                     .Where(r => r.PatientRecordId != null).Select(r => r.PatientRecordId!.Value).ToListAsync(ct))
            recordIds.Add(id);
        foreach (var id in await db.PharmacyRequests.ForClinic(clinicId).AsNoTracking()
                     .Where(r => r.PatientRecordId != null).Select(r => r.PatientRecordId!.Value).ToListAsync(ct))
            recordIds.Add(id);
        foreach (var id in await db.PharmacyBills.ForClinic(clinicId).AsNoTracking()
                     .Where(r => r.PatientRecordId != null).Select(r => r.PatientRecordId!.Value).ToListAsync(ct))
            recordIds.Add(id);
        foreach (var id in await db.Prescriptions.ForClinic(clinicId).AsNoTracking()
                     .Where(r => r.PatientRecordId != null).Select(r => r.PatientRecordId!.Value).ToListAsync(ct))
            recordIds.Add(id);
        foreach (var id in await db.ServiceIncomeRequests.ForClinic(clinicId).AsNoTracking()
                     .Where(r => r.PatientRecordId != null).Select(r => r.PatientRecordId!.Value).ToListAsync(ct))
            recordIds.Add(id);
        foreach (var id in await db.CashPayments.ForClinic(clinicId).AsNoTracking()
                     .Where(p => p.VendorId == null && p.PatientRecordId != null)
                     .Select(p => p.PatientRecordId!.Value).ToListAsync(ct))
            recordIds.Add(id);

        foreach (var no in await db.LabRequests.ForClinic(clinicId).AsNoTracking()
                     .Select(r => r.PatientBarcode).Where(v => v != null && v != "").ToListAsync(ct))
            patientNos.Add(no!);
        foreach (var no in await db.RadiologyRequests.ForClinic(clinicId).AsNoTracking()
                     .Select(r => r.PatientBarcode).Where(v => v != null && v != "").ToListAsync(ct))
            patientNos.Add(no!);
        foreach (var no in await db.PharmacyRequests.ForClinic(clinicId).AsNoTracking()
                     .Select(r => r.PatientId).Where(v => v != null && v != "").ToListAsync(ct))
            patientNos.Add(no!);
        foreach (var no in await db.PharmacyBills.ForClinic(clinicId).AsNoTracking()
                     .Select(r => r.PatientId).Where(v => v != null && v != "").ToListAsync(ct))
            patientNos.Add(no!);
        foreach (var no in await db.CashPayments.ForClinic(clinicId).AsNoTracking()
                     .Where(p => p.VendorId == null).Select(p => p.PatientId)
                     .Where(v => v != null && v != "").ToListAsync(ct))
            patientNos.Add(no!);

        foreach (var name in await db.LabRequests.ForClinic(clinicId).AsNoTracking().Select(r => r.PatientName).ToListAsync(ct))
            AddName(patientNames, name);
        foreach (var name in await db.LabResults.ForClinic(clinicId).AsNoTracking().Select(r => r.PatientName).ToListAsync(ct))
            AddName(patientNames, name);
        foreach (var name in await db.RadiologyRequests.ForClinic(clinicId).AsNoTracking().Select(r => r.PatientName).ToListAsync(ct))
            AddName(patientNames, name);
        foreach (var name in await db.RadiologyResults.ForClinic(clinicId).AsNoTracking().Select(r => r.PatientName).ToListAsync(ct))
            AddName(patientNames, name);
        foreach (var name in await db.PharmacyRequests.ForClinic(clinicId).AsNoTracking().Select(r => r.PatientName).ToListAsync(ct))
            AddName(patientNames, name);
        foreach (var name in await db.PharmacyBills.ForClinic(clinicId).AsNoTracking().Select(r => r.PatientName).ToListAsync(ct))
            AddName(patientNames, name);
        foreach (var name in await db.Prescriptions.ForClinic(clinicId).AsNoTracking().Select(r => r.PatientName).ToListAsync(ct))
            AddName(patientNames, name);
        foreach (var name in await db.CashPayments.ForClinic(clinicId).AsNoTracking()
                     .Where(p => p.VendorId == null).Select(p => p.PayeeName).ToListAsync(ct))
            AddName(patientNames, name);
    }

    public string DeriveStatus(Patient patient)
    {
        if (HasInvoiceActivity(patient))
            return PatientVisitStatuses.Completed;
        if (HasClinicalActivity(patient))
            return PatientVisitStatuses.UnderProcess;
        if (HasReceiptActivity(patient))
            return PatientVisitStatuses.Confirmed;
        return PatientVisitStatuses.Pending;
    }

    private bool HasInvoiceActivity(Patient patient) =>
        _invoiceRecordIds.Contains(patient.Id)
        || (!string.IsNullOrWhiteSpace(patient.PatientNo) && _invoicePatientNos.Contains(patient.PatientNo))
        || MatchesName(_invoicePatientNames, patient);

    private bool HasReceiptActivity(Patient patient) =>
        _receiptRecordIds.Contains(patient.Id)
        || (!string.IsNullOrWhiteSpace(patient.PatientNo) && _receiptPatientNos.Contains(patient.PatientNo))
        || MatchesName(_receiptPatientNames, patient);

    private bool HasClinicalActivity(Patient patient) =>
        _clinicalRecordIds.Contains(patient.Id)
        || (!string.IsNullOrWhiteSpace(patient.PatientNo) && _clinicalPatientNos.Contains(patient.PatientNo))
        || MatchesName(_clinicalPatientNames, patient);

    private static bool MatchesName(IReadOnlyList<string> names, Patient patient)
    {
        foreach (var name in names)
        {
            if (PatientChargeMatcher.MatchesPatient(
                    patient.PatientNo, patient.FullName, null, patient.PatientNo, name))
                return true;
        }

        return false;
    }
}
