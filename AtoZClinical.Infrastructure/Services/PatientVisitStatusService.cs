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

    public PatientVisitStatusService(ClinicalDbContext db) => _db = db;

    public Task OnPatientRegisteredAsync(Guid clinicId, Patient patient)
    {
        patient.Status = PatientVisitStatuses.Pending;
        return Task.CompletedTask;
    }

    public Task OnCashReceiptAsync(Guid clinicId, CashReceipt receipt) =>
        TryUpgradeAsync(clinicId, receipt.PatientId, receipt.PatientName, PatientVisitStatuses.Confirmed);

    public Task OnClinicalCheckInAsync(Guid clinicId, string? patientId, string? patientName) =>
        TryUpgradeAsync(clinicId, patientId, patientName, PatientVisitStatuses.UnderProcess);

    public Task OnInvoiceBillingAsync(Guid clinicId, string? patientId, string? patientName) =>
        ForceStatusAsync(clinicId, patientId, patientName, PatientVisitStatuses.Completed);

    public async Task TryUpgradeAsync(Guid clinicId, string? patientId, string? patientName, string targetStatus)
    {
        var patient = await FindPatientAsync(clinicId, patientId, patientName);
        if (patient is null) return;
        if (!PatientVisitStatuses.CanAutoUpgrade(patient.Status, targetStatus)) return;

        patient.Status = PatientVisitStatuses.Normalize(targetStatus);
        patient.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task ForceStatusAsync(Guid clinicId, string? patientId, string? patientName, string status)
    {
        var patient = await FindPatientAsync(clinicId, patientId, patientName);
        if (patient is null) return;

        patient.Status = PatientVisitStatuses.Normalize(status);
        patient.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
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
            return await query
                .Where(p => p.FirstName == name || (p.FirstName + " " + p.LastName).Trim() == name)
                .OrderByDescending(p => p.UpdatedAt)
                .FirstOrDefaultAsync();
        }

        return null;
    }
}
