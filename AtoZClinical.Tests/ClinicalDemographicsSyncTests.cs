using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Tests;

public class ClinicalDemographicsSyncTests
{
    [Fact]
    public async Task RefreshInvoiceFromMastersAsync_updates_stale_patient_name_on_load()
    {
        var clinicId = Guid.NewGuid();
        await using var db = await SqliteTestDatabase.CreateAsync(TestClinicProvider.ForClinic(clinicId));
        db.Db.Clinics.Add(new Clinic { Id = clinicId, ClinicCode = "TST", Name = "Test Clinic" });
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            PatientNo = "PAT-00004",
            FirstName = "asaad2",
            Phone = "07866090003",
            Gender = "Male",
            City = "Baghdad",
            DateOfBirth = new DateTime(1999, 1, 1),
            DoctorName = "Zainab Ali Hasan",
            Specialty = "Neurology"
        };
        db.Db.Patients.Add(patient);
        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            InvoiceNo = 4,
            PatientId = patient.PatientNo,
            PatientRecordId = patient.Id,
            PatientName = "asaad",
            Phone = patient.Phone,
            Age = 27,
            Gender = patient.Gender,
            City = patient.City,
            DoctorName = patient.DoctorName,
            Specialty = patient.Specialty,
            InvoiceDate = DateTime.Today,
            TotalAmount = 25000m,
            SubTotal = 25000m,
            BalanceDue = 25000m
        };
        db.Db.Invoices.Add(invoice);
        await db.Db.SaveChangesAsync();

        var demographics = new ClinicalDemographicsSyncService(db.Db);
        var changed = await demographics.RefreshInvoiceFromMastersAsync(
            clinicId, invoice, invoice.Lines.ToList(), persist: true);

        Assert.True(changed);
        db.Db.ChangeTracker.Clear();
        var refreshed = await db.Db.Invoices.ForClinic(clinicId).SingleAsync();
        Assert.Equal("asaad2", refreshed.PatientName);
        Assert.Equal(patient.Id, refreshed.PatientRecordId);
    }
}
