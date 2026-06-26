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
            "Under" => UnderProcess,
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
        if (current == Completed && target != Completed) return false;
        return Rank(target) > Rank(current);
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
        TryUpgradeAsync(clinicId, receipt.PatientId, receipt.PatientName, PatientVisitStatuses.Confirmed);

    public Task OnCashPaymentAsync(Guid clinicId, CashPayment payment) =>
        TryUpgradeAsync(clinicId, payment.PatientId, payment.PayeeName, PatientVisitStatuses.Confirmed);

    public Task OnClinicalCheckInAsync(Guid clinicId, string? patientId, string? patientName) =>
        TryUpgradeAsync(clinicId, patientId, patientName, PatientVisitStatuses.UnderProcess);

    public Task OnInvoiceBillingAsync(Guid clinicId, string? patientId, string? patientName) =>
        ForceStatusAsync(clinicId, patientId, patientName, PatientVisitStatuses.Completed);

    public async Task TryUpgradeAsync(Guid clinicId, string? patientId, string? patientName, string targetStatus)
    {
        var patient = await FindPatientAsync(clinicId, patientId, patientName);
        if (patient is null) return;
        if (!PatientVisitStatuses.CanAutoUpgrade(patient.Status, targetStatus)) return;

        var previous = patient.Status;
        patient.Status = PatientVisitStatuses.Normalize(targetStatus);
        patient.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await LogStatusChangeAsync(clinicId, patient, previous, patient.Status);
    }

    public async Task ForceStatusAsync(Guid clinicId, string? patientId, string? patientName, string status)
    {
        var patient = await FindPatientAsync(clinicId, patientId, patientName);
        if (patient is null) return;

        var previous = patient.Status;
        var normalized = PatientVisitStatuses.Normalize(status);
        if (string.Equals(previous, normalized, StringComparison.OrdinalIgnoreCase))
            return;

        patient.Status = normalized;
        patient.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await LogStatusChangeAsync(clinicId, patient, previous, normalized);
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

    public async Task<Patient?> FindPatientAsync(Guid clinicId, string? patientId, string? patientName)
    {
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
                .Where(p => string.Equals(p.FirstName, name, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(p.FullName, name, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(p => p.UpdatedAt)
                .FirstOrDefault();
        }

        return null;
    }
}
