using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public sealed class InvoiceDeleteGuardService
{
    private readonly ClinicalDbContext _db;

    public InvoiceDeleteGuardService(ClinicalDbContext db) => _db = db;

    public const string BlockMessage =
        "This record is on invoice billing. Delete the invoice billing first, then try again.";

    public async Task EnsureCanDeletePatientAsync(Guid clinicId, Patient patient)
    {
        if (await HasInvoiceForPatientAsync(clinicId, patient.PatientNo, patient.FullName))
            throw new InvalidOperationException(BlockMessage);
    }

    public async Task EnsureCanDeleteDoctorAsync(Guid clinicId, string doctorName)
    {
        if (await _db.Invoices.ForClinic(clinicId).AnyAsync(i => i.DoctorName == doctorName))
            throw new InvalidOperationException(BlockMessage);
    }

    public Task EnsureCanDeleteLabRequestAsync(Guid clinicId, int requestNo) =>
        EnsureInvoiceLinePatternAsync(clinicId, $"Lab #{requestNo}:");

    public async Task EnsureCanDeleteLabResultAsync(Guid clinicId, LabResult result)
    {
        if (result.RequestNo.HasValue)
            await EnsureInvoiceLinePatternAsync(clinicId, $"Lab #{result.RequestNo}:");
        else if (!string.IsNullOrWhiteSpace(result.PatientName))
            await EnsurePatientHasInvoiceAsync(clinicId, null, result.PatientName);
    }

    public Task EnsureCanDeleteRadiologyRequestAsync(Guid clinicId, int requestNo) =>
        EnsureInvoiceLinePatternAsync(clinicId, $"Radiology #{requestNo}:");

    public async Task EnsureCanDeleteRadiologyResultAsync(Guid clinicId, RadiologyResult result)
    {
        if (result.RequestNo.HasValue)
            await EnsureInvoiceLinePatternAsync(clinicId, $"Radiology #{result.RequestNo}:");
        else if (!string.IsNullOrWhiteSpace(result.PatientName))
            await EnsurePatientHasInvoiceAsync(clinicId, null, result.PatientName);
    }

    public Task EnsureCanDeletePharmacyRequestAsync(Guid clinicId, int requestNo) =>
        EnsureInvoiceLinePatternAsync(clinicId, $"Pharmacy Req #{requestNo}:");

    public Task EnsureCanDeletePharmacyBillAsync(Guid clinicId, int billNo) =>
        EnsureInvoiceLinePatternAsync(clinicId, $"Pharmacy Bill #{billNo}:");

    public async Task EnsureCanDeletePrescriptionAsync(Guid clinicId, Prescription prescription)
    {
        if (!string.IsNullOrWhiteSpace(prescription.PatientName))
            await EnsurePatientHasInvoiceAsync(clinicId, null, prescription.PatientName);
    }

    public async Task EnsureCanDeleteCashReceiptAsync(Guid clinicId, CashReceipt receipt)
    {
        if (!string.IsNullOrWhiteSpace(receipt.PatientId) || !string.IsNullOrWhiteSpace(receipt.PatientName))
            await EnsurePatientHasInvoiceAsync(clinicId, receipt.PatientId, receipt.PatientName);
    }

    public async Task EnsureCanDeleteCashPaymentAsync(Guid clinicId, CashPayment payment)
    {
        if (!string.IsNullOrWhiteSpace(payment.PatientId) || !string.IsNullOrWhiteSpace(payment.PayeeName))
            await EnsurePatientHasInvoiceAsync(clinicId, payment.PatientId, payment.PayeeName);
    }

    private async Task EnsureInvoiceLinePatternAsync(Guid clinicId, string pattern)
    {
        if (await InvoiceLineExistsAsync(clinicId, pattern))
            throw new InvalidOperationException(BlockMessage);
    }

    private async Task EnsurePatientHasInvoiceAsync(Guid clinicId, string? patientId, string? patientName)
    {
        if (await HasInvoiceForPatientAsync(clinicId, patientId, patientName))
            throw new InvalidOperationException(BlockMessage);
    }

    private async Task<bool> HasInvoiceForPatientAsync(Guid clinicId, string? patientId, string? patientName) =>
        await _db.Invoices.ForClinic(clinicId).AnyAsync(i =>
            (!string.IsNullOrWhiteSpace(patientId) && i.PatientId == patientId) ||
            (!string.IsNullOrWhiteSpace(patientName) && i.PatientName == patientName));

    private async Task<bool> InvoiceLineExistsAsync(Guid clinicId, string pattern) =>
        await (
            from line in _db.InvoiceLines
            join invoice in _db.Invoices.ForClinic(clinicId) on line.InvoiceId equals invoice.Id
            where line.ServiceName != null && line.ServiceName.Contains(pattern)
            select line).AnyAsync();
}
