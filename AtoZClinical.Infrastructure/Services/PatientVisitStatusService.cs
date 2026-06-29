using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public static class PatientVisitStatuses
{
    public const string Pending = "Pending";
    public const string Confirmed = "Confirmed";
    public const string UnderProcess = "Under Process";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";

    public static string Normalize(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return Pending;
        return status.Trim() switch
        {
            "Active" => Pending,
            "Confirm" => Confirmed,
            "Check in" or "Check In" or "Checked In" => UnderProcess,
            "Under" or "UnderProcess" => UnderProcess,
            _ => status.Trim()
        };
    }

    private static int Rank(string status) => Normalize(status) switch
    {
        Pending => 1,
        Confirmed => 2,
        UnderProcess => 3,
        Completed => 4,
        Cancelled => 100,
        _ => 0
    };

    public static bool CanAutoUpgrade(string? currentStatus, string targetStatus)
    {
        var current = Normalize(currentStatus);
        var target = Normalize(targetStatus);
        if (current == Cancelled) return false;
        if (current == Completed) return false;
        return Rank(target) > Rank(current);
    }

    public static bool IsCancelled(string? status) =>
        Normalize(status) == Cancelled;
}

public static class PatientVisitInvoiceMarkers
{
    internal const string ProvisionalToken = "__provisional_visit__";

    public static bool IsProvisional(string? notes) =>
        !string.IsNullOrWhiteSpace(notes) &&
        notes.Contains(ProvisionalToken, StringComparison.Ordinal);

    public static string MarkProvisional(string? notes) =>
        IsProvisional(notes)
            ? notes!
            : string.IsNullOrWhiteSpace(notes)
                ? ProvisionalToken
                : $"{ProvisionalToken}|{notes.Trim()}";

    public static string? ClearProvisional(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return notes;

        var cleared = notes
            .Replace($"{ProvisionalToken}|", "", StringComparison.Ordinal)
            .Replace(ProvisionalToken, "", StringComparison.Ordinal)
            .Trim();
        return string.IsNullOrWhiteSpace(cleared) ? null : cleared;
    }
}

public sealed class PatientVisitStatusService
{
    private readonly ClinicalDbContext _db;
    private readonly AuditService _audit;

    public PatientVisitStatusService(ClinicalDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public Task OnPatientRegisteredAsync(Guid clinicId, Patient patient)
    {
        patient.Status = PatientVisitStatuses.Pending;
        return Task.CompletedTask;
    }

    public Task OnCashReceiptAsync(Guid clinicId, CashReceipt receipt) =>
        TryUpgradeAsync(
            clinicId,
            receipt.PatientId,
            receipt.PatientName,
            PatientVisitStatuses.Confirmed,
            receipt.PatientRecordId);

    public Task OnCashPaymentAsync(Guid clinicId, CashPayment payment)
    {
        if (payment.VendorId.HasValue)
            return Task.CompletedTask;

        if (string.IsNullOrWhiteSpace(payment.PatientId) &&
            string.IsNullOrWhiteSpace(payment.PayeeName) &&
            !payment.PatientRecordId.HasValue)
            return Task.CompletedTask;

        return OnClinicalActivityAsync(
            clinicId,
            payment.PatientId,
            payment.PayeeName,
            payment.PatientRecordId);
    }

    public Task OnClinicalCheckInAsync(
        Guid clinicId,
        string? patientId,
        string? patientName,
        Guid? patientRecordId = null) =>
        OnClinicalActivityAsync(clinicId, patientId, patientName, patientRecordId);

    public Task OnClinicalActivityAsync(
        Guid clinicId,
        string? patientId,
        string? patientName,
        Guid? patientRecordId = null) =>
        TryUpgradeAsync(clinicId, patientId, patientName, PatientVisitStatuses.UnderProcess, patientRecordId);

    public Task OnInvoiceBillingAsync(
        Guid clinicId,
        string? patientId,
        string? patientName,
        Guid? patientRecordId = null) =>
        ForceStatusAsync(clinicId, patientId, patientName, PatientVisitStatuses.Completed, patientRecordId);

    /// <summary>Reconcile every patient status from invoices, receipts, and clinical activity.</summary>
    public async Task<int> SyncAllPatientStatusesForClinicAsync(Guid clinicId, CancellationToken ct = default)
    {
        var patients = await _db.Patients.ForClinic(clinicId).ToListAsync(ct);
        if (patients.Count == 0)
            return 0;

        var updated = 0;
        foreach (var patient in patients)
        {
            if (PatientVisitStatuses.IsCancelled(patient.Status))
                continue;

            var derived = await DeriveStatusFromActivityAsync(clinicId, patient, ct);
            if (string.Equals(PatientVisitStatuses.Normalize(patient.Status), derived, StringComparison.OrdinalIgnoreCase))
                continue;

            patient.Status = derived;
            patient.UpdatedAt = DateTime.UtcNow;
            updated++;
        }

        if (updated > 0)
            await _db.SaveChangesAsync(ct);

        return updated;
    }

    public async Task TryUpgradeAsync(
        Guid clinicId,
        string? patientId,
        string? patientName,
        string targetStatus,
        Guid? patientRecordId = null)
    {
        var patient = await FindPatientAsync(clinicId, patientId, patientName, patientRecordId);
        if (patient is null) return;
        if (!PatientVisitStatuses.CanAutoUpgrade(patient.Status, targetStatus)) return;

        await SetPatientStatusAsync(clinicId, patient, PatientVisitStatuses.Normalize(targetStatus));
    }

    public async Task ForceStatusAsync(
        Guid clinicId,
        string? patientId,
        string? patientName,
        string status,
        Guid? patientRecordId = null)
    {
        var patient = await FindPatientAsync(clinicId, patientId, patientName, patientRecordId);
        if (patient is null) return;

        var normalized = PatientVisitStatuses.Normalize(status);
        if (PatientVisitStatuses.IsCancelled(PatientVisitStatuses.Normalize(patient.Status)) &&
            normalized != PatientVisitStatuses.Cancelled)
            return;

        if (string.Equals(patient.Status, normalized, StringComparison.OrdinalIgnoreCase))
            return;

        await SetPatientStatusAsync(clinicId, patient, normalized);
    }

    private async Task<string> DeriveStatusFromActivityAsync(
        Guid clinicId,
        Patient patient,
        CancellationToken ct)
    {
        if (await HasInvoiceActivityAsync(clinicId, patient, ct))
            return PatientVisitStatuses.Completed;

        if (await HasClinicalActivityAsync(clinicId, patient, ct))
            return PatientVisitStatuses.UnderProcess;

        if (await HasReceiptActivityAsync(clinicId, patient, ct))
            return PatientVisitStatuses.Confirmed;

        return PatientVisitStatuses.Pending;
    }

    private IQueryable<Invoice> CompletionInvoices(Guid clinicId) =>
        _db.Invoices.ForClinic(clinicId).AsNoTracking()
            .Where(i => i.Notes == null || !i.Notes.Contains(PatientVisitInvoiceMarkers.ProvisionalToken));

    private async Task<bool> HasInvoiceActivityAsync(Guid clinicId, Patient patient, CancellationToken ct)
    {
        if (await CompletionInvoices(clinicId).AnyAsync(i => i.PatientRecordId == patient.Id, ct))
            return true;

        var patientNo = patient.PatientNo;
        if (!string.IsNullOrWhiteSpace(patientNo) &&
            await CompletionInvoices(clinicId).AnyAsync(i => i.PatientId == patientNo, ct))
            return true;

        return await MatchesPatientNameInMemoryAsync(
            await CompletionInvoices(clinicId).Select(i => new { i.PatientName }).ToListAsync(ct),
            patient,
            row => row.PatientName);
    }

    private async Task<bool> HasReceiptActivityAsync(Guid clinicId, Patient patient, CancellationToken ct)
    {
        if (await _db.CashReceipts.ForClinic(clinicId).AsNoTracking()
                .AnyAsync(r => r.PatientRecordId == patient.Id, ct))
            return true;

        var patientNo = patient.PatientNo;
        if (!string.IsNullOrWhiteSpace(patientNo) &&
            await _db.CashReceipts.ForClinic(clinicId).AsNoTracking()
                .AnyAsync(r => r.PatientId == patientNo, ct))
            return true;

        return await MatchesPatientNameInMemoryAsync(
            await _db.CashReceipts.ForClinic(clinicId).AsNoTracking()
                .Select(r => new { r.PatientName })
                .ToListAsync(ct),
            patient,
            row => row.PatientName);
    }

    private async Task<bool> HasClinicalActivityAsync(Guid clinicId, Patient patient, CancellationToken ct)
    {
        var patientId = patient.Id;
        var patientNo = patient.PatientNo;

        if (await _db.LabRequests.ForClinic(clinicId).AsNoTracking().AnyAsync(r => r.PatientRecordId == patientId, ct))
            return true;
        if (await _db.LabResults.ForClinic(clinicId).AsNoTracking().AnyAsync(r => r.PatientRecordId == patientId, ct))
            return true;
        if (await _db.RadiologyRequests.ForClinic(clinicId).AsNoTracking().AnyAsync(r => r.PatientRecordId == patientId, ct))
            return true;
        if (await _db.RadiologyResults.ForClinic(clinicId).AsNoTracking().AnyAsync(r => r.PatientRecordId == patientId, ct))
            return true;
        if (await _db.PharmacyRequests.ForClinic(clinicId).AsNoTracking().AnyAsync(r => r.PatientRecordId == patientId, ct))
            return true;
        if (await _db.PharmacyBills.ForClinic(clinicId).AsNoTracking().AnyAsync(r => r.PatientRecordId == patientId, ct))
            return true;
        if (await _db.Prescriptions.ForClinic(clinicId).AsNoTracking().AnyAsync(r => r.PatientRecordId == patientId, ct))
            return true;
        if (await _db.ServiceIncomeRequests.ForClinic(clinicId).AsNoTracking().AnyAsync(r => r.PatientRecordId == patientId, ct))
            return true;

        if (!string.IsNullOrWhiteSpace(patientNo))
        {
            if (await _db.LabRequests.ForClinic(clinicId).AsNoTracking().AnyAsync(r => r.PatientBarcode == patientNo, ct))
                return true;
            if (await _db.RadiologyRequests.ForClinic(clinicId).AsNoTracking().AnyAsync(r => r.PatientBarcode == patientNo, ct))
                return true;
            if (await _db.PharmacyRequests.ForClinic(clinicId).AsNoTracking().AnyAsync(r => r.PatientId == patientNo, ct))
                return true;
            if (await _db.PharmacyBills.ForClinic(clinicId).AsNoTracking().AnyAsync(r => r.PatientId == patientNo, ct))
                return true;
            if (await _db.CashPayments.ForClinic(clinicId).AsNoTracking()
                    .AnyAsync(p => p.VendorId == null && p.PatientId == patientNo, ct))
                return true;
        }

        if (await _db.CashPayments.ForClinic(clinicId).AsNoTracking()
                .AnyAsync(p => p.VendorId == null && p.PatientRecordId == patientId, ct))
            return true;

        if (await MatchesPatientNameInMemoryAsync(
                await _db.LabRequests.ForClinic(clinicId).AsNoTracking().Select(r => new { r.PatientName }).ToListAsync(ct),
                patient, r => r.PatientName))
            return true;
        if (await MatchesPatientNameInMemoryAsync(
                await _db.LabResults.ForClinic(clinicId).AsNoTracking().Select(r => new { r.PatientName }).ToListAsync(ct),
                patient, r => r.PatientName))
            return true;
        if (await MatchesPatientNameInMemoryAsync(
                await _db.RadiologyRequests.ForClinic(clinicId).AsNoTracking().Select(r => new { r.PatientName }).ToListAsync(ct),
                patient, r => r.PatientName))
            return true;
        if (await MatchesPatientNameInMemoryAsync(
                await _db.RadiologyResults.ForClinic(clinicId).AsNoTracking().Select(r => new { r.PatientName }).ToListAsync(ct),
                patient, r => r.PatientName))
            return true;
        if (await MatchesPatientNameInMemoryAsync(
                await _db.PharmacyRequests.ForClinic(clinicId).AsNoTracking().Select(r => new { r.PatientName }).ToListAsync(ct),
                patient, r => r.PatientName))
            return true;
        if (await MatchesPatientNameInMemoryAsync(
                await _db.PharmacyBills.ForClinic(clinicId).AsNoTracking().Select(r => new { r.PatientName }).ToListAsync(ct),
                patient, r => r.PatientName))
            return true;
        if (await MatchesPatientNameInMemoryAsync(
                await _db.Prescriptions.ForClinic(clinicId).AsNoTracking().Select(r => new { r.PatientName }).ToListAsync(ct),
                patient, r => r.PatientName))
            return true;
        if (await MatchesPatientNameInMemoryAsync(
                await _db.CashPayments.ForClinic(clinicId).AsNoTracking()
                    .Where(p => p.VendorId == null)
                    .Select(p => new { Name = p.PayeeName }).ToListAsync(ct),
                patient, r => r.Name))
            return true;

        return false;
    }

    private static Task<bool> MatchesPatientNameInMemoryAsync<T>(
        IReadOnlyList<T> rows,
        Patient patient,
        Func<T, string?> getName)
    {
        foreach (var row in rows)
        {
            var name = getName(row);
            if (PatientChargeMatcher.MatchesPatient(patient.PatientNo, patient.FullName, null, patient.PatientNo, name))
                return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    private async Task SetPatientStatusAsync(Guid clinicId, Patient patient, string newStatus)
    {
        var previous = patient.Status;
        patient.Status = newStatus;
        patient.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await LogStatusChangeAsync(clinicId, patient, previous, newStatus);
    }

    private async Task LogStatusChangeAsync(Guid clinicId, Patient patient, string? previous, string current)
    {
        if (string.Equals(previous, current, StringComparison.OrdinalIgnoreCase))
            return;

        var label = string.IsNullOrWhiteSpace(patient.FullName) ? patient.FirstName : patient.FullName;
        await _audit.LogAsync(
            clinicId,
            null,
            "Patient Status",
            "Update",
            $"{label} — {PatientVisitStatuses.Normalize(previous)} → {current}");
    }

    public Task<Patient?> FindPatientAsync(Guid clinicId, string? patientId, string? patientName) =>
        FindPatientAsync(clinicId, patientId, patientName, null);

    public async Task<Patient?> FindPatientAsync(
        Guid clinicId,
        string? patientId,
        string? patientName,
        Guid? patientRecordId)
    {
        if (patientRecordId.HasValue)
        {
            var byRecord = await _db.Patients.ForClinic(clinicId)
                .FirstOrDefaultAsync(p => p.Id == patientRecordId.Value);
            if (byRecord is not null)
                return byRecord;
        }

        if (string.IsNullOrWhiteSpace(patientId) && string.IsNullOrWhiteSpace(patientName))
            return null;

        var query = _db.Patients.Where(p => p.ClinicId == clinicId);

        if (!string.IsNullOrWhiteSpace(patientId))
        {
            var id = patientId.Trim();
            var byId = await query.FirstOrDefaultAsync(p => p.PatientNo == id);
            if (byId is not null) return byId;
        }

        if (!string.IsNullOrWhiteSpace(patientName))
        {
            var name = patientName.Trim();
            var candidates = await query.ToListAsync();
            return candidates
                .Where(p => PatientChargeMatcher.MatchesPatient(null, name, null, p.PatientNo, p.FullName))
                .OrderByDescending(p => p.UpdatedAt)
                .FirstOrDefault();
        }

        return null;
    }
}
