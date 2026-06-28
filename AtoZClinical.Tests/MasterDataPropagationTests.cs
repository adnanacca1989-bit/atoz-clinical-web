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
            audit,
            new ClinicalDemographicsSyncService(db.Db));

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

    [Fact]
    public async Task PropagateDoctorAsync_rename_updates_partial_name_variants()
    {
        var clinicId = Guid.NewGuid();
        await using var db = await SqliteTestDatabase.CreateAsync(TestClinicProvider.ForClinic(clinicId));
        db.Db.Clinics.Add(new Clinic { Id = clinicId, ClinicCode = "TST", Name = "Test Clinic" });

        var doctor = new Doctor
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            DoctorNo = 2,
            Name = "Zainab Ali Hasan",
            Specialty = "Neurology"
        };
        db.Db.Doctors.Add(doctor);
        db.Db.Patients.Add(new Patient
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            PatientNo = "PAT-00001",
            FirstName = "Test",
            DoctorName = "Zainab Ali",
            Specialty = "Neurology"
        });
        db.Db.LabRequests.Add(new LabRequest
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            RequestNo = 1,
            PatientName = "Test",
            DoctorName = "Zainab Ali Hasan",
            Specialty = "Neurology"
        });
        await db.Db.SaveChangesAsync();

        var propagation = ServiceTestFactory.CreatePropagation(db.Db);
        var previous = new Doctor
        {
            Id = doctor.Id,
            ClinicId = clinicId,
            DoctorNo = doctor.DoctorNo,
            Name = "Zainab Ali Hasan",
            Specialty = "Neurology"
        };
        doctor.Name = "Zainab";
        await propagation.PropagateDoctorAsync(clinicId, previous, doctor);

        db.Db.ChangeTracker.Clear();
        var patient = await db.Db.Patients.ForClinic(clinicId).SingleAsync();
        Assert.Equal("Zainab", patient.DoctorName);

        var labRequest = await db.Db.LabRequests.ForClinic(clinicId).SingleAsync();
        Assert.Equal("Zainab", labRequest.DoctorName);
        Assert.Equal(doctor.Id, labRequest.DoctorRecordId);
    }
}
