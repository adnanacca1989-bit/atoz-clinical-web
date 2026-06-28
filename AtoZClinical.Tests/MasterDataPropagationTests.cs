using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Tests;

public class MasterDataPropagationTests
{
    [Fact]
    public async Task SaveAsync_patient_rename_propagates_to_invoice_and_journal()
    {
        var clinicId = Guid.NewGuid();
        await using var db = await SqliteTestDatabase.CreateAsync(TestClinicProvider.ForClinic(clinicId));
        db.Db.Clinics.Add(new Clinic { Id = clinicId, ClinicCode = "TST", Name = "Test Clinic" });
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            PatientNo = "PAT-00001",
            FirstName = "Old",
            LastName = "Name",
            Phone = "111"
        };
        db.Db.Patients.Add(patient);
        db.Db.Invoices.Add(new Invoice
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            PatientId = patient.PatientNo,
            PatientName = "Old Name",
            InvoiceDate = DateTime.Today,
            TotalAmount = 100m
        });
        db.Db.JournalEntries.Add(new JournalEntry
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            EntryNo = 1,
            EntryDate = DateTime.Today,
            PatientName = "Old Name",
            SourceType = ClinicalJournalSources.Invoice
        });
        await db.Db.SaveChangesAsync();

        var audit = new AuditService(db.Db);
        var service = new PatientService(
            db.Db,
            ServiceTestFactory.CreatePropagation(db.Db),
            new InvoiceDeleteGuardService(db.Db),
            new PatientVisitStatusService(db.Db, audit),
            new NoOpWebhookDispatchService(),
            audit);

        patient.FirstName = "New";
        patient.Phone = "222";
        await service.SaveAsync(clinicId, patient, "tester");

        db.Db.ChangeTracker.Clear();
        var invoice = await db.Db.Invoices.ForClinic(clinicId).SingleAsync();
        Assert.Equal("New Name", invoice.PatientName);
        Assert.Equal("222", invoice.Phone);

        var journal = await db.Db.JournalEntries.ForClinic(clinicId).SingleAsync();
        Assert.Equal("New Name", journal.PatientName);
    }
}
