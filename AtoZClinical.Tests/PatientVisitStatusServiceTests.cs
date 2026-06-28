using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Tests;

public class PatientVisitStatusServiceTests
{
    private static async Task<(SqliteTestDatabase db, Guid clinicId, PatientVisitStatusService status)> CreateAsync(string patientStatus)
    {
        var clinicId = Guid.NewGuid();
        var db = await SqliteTestDatabase.CreateAsync(TestClinicProvider.ForClinic(clinicId));
        db.Db.Clinics.Add(new Clinic { Id = clinicId, ClinicCode = "TST", Name = "Test Clinic" });
        db.Db.Patients.Add(new Patient
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            PatientNo = "PAT-00001",
            FirstName = "Ali Patient",
            Status = patientStatus
        });
        await db.Db.SaveChangesAsync();

        var audit = new AuditService(db.Db);
        var status = new PatientVisitStatusService(db.Db, audit);
        return (db, clinicId, status);
    }

    private static async Task<string> GetStatusAsync(SqliteTestDatabase db, Guid clinicId) =>
        (await db.Db.Patients.ForClinic(clinicId).SingleAsync()).Status;

    [Fact]
    public async Task Registration_sets_pending_status()
    {
        var patient = new Patient { FirstName = "New" };
        var (_, clinicId, status) = await CreateAsync(PatientVisitStatuses.Pending);
        await status.OnPatientRegisteredAsync(clinicId, patient);
        Assert.Equal(PatientVisitStatuses.Pending, patient.Status);
    }

    [Fact]
    public async Task Cash_receipt_sets_confirmed()
    {
        var (db, clinicId, status) = await CreateAsync(PatientVisitStatuses.Pending);
        await status.OnCashReceiptAsync(clinicId, new CashReceipt
        {
            PatientId = "PAT-00001",
            PatientName = "Ali Patient",
            Amount = 50
        });
        Assert.Equal(PatientVisitStatuses.Confirmed, await GetStatusAsync(db, clinicId));
    }

    [Fact]
    public async Task Patient_cash_payment_sets_under_process()
    {
        var (db, clinicId, status) = await CreateAsync(PatientVisitStatuses.Confirmed);
        await status.OnCashPaymentAsync(clinicId, new CashPayment
        {
            PatientId = "PAT-00001",
            PayeeName = "Ali Patient",
            Amount = 25
        });
        Assert.Equal(PatientVisitStatuses.UnderProcess, await GetStatusAsync(db, clinicId));
    }

    [Fact]
    public async Task Vendor_cash_payment_does_not_change_patient_status()
    {
        var (db, clinicId, status) = await CreateAsync(PatientVisitStatuses.Confirmed);
        await status.OnCashPaymentAsync(clinicId, new CashPayment
        {
            VendorId = Guid.NewGuid(),
            PayeeName = "Supplier",
            Amount = 100
        });
        Assert.Equal(PatientVisitStatuses.Confirmed, await GetStatusAsync(db, clinicId));
    }

    [Fact]
    public async Task Clinical_activity_after_invoice_keeps_completed_status()
    {
        var (db, clinicId, status) = await CreateAsync(PatientVisitStatuses.Completed);
        await status.OnClinicalActivityAsync(clinicId, "PAT-00001", "Ali Patient");
        Assert.Equal(PatientVisitStatuses.Completed, await GetStatusAsync(db, clinicId));
    }

    [Fact]
    public async Task Lab_request_after_invoice_keeps_completed_status()
    {
        var (db, clinicId, status) = await CreateAsync(PatientVisitStatuses.Completed);
        await status.OnClinicalCheckInAsync(clinicId, "PAT-00001", "Ali Patient");
        Assert.Equal(PatientVisitStatuses.Completed, await GetStatusAsync(db, clinicId));
    }

    [Fact]
    public async Task Invoice_billing_sets_completed()
    {
        var (db, clinicId, status) = await CreateAsync(PatientVisitStatuses.UnderProcess);
        await status.OnInvoiceBillingAsync(clinicId, "PAT-00001", "Ali Patient");
        Assert.Equal(PatientVisitStatuses.Completed, await GetStatusAsync(db, clinicId));
    }
}
