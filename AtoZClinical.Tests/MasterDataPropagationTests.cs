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
        var invoiceId = Guid.NewGuid();
        db.Db.Invoices.Add(new Invoice
        {
            Id = invoiceId,
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
            SourceType = ClinicalJournalSources.Invoice,
            SourceId = invoiceId
        });
        await db.Db.SaveChangesAsync();

        var service = ServiceTestFactory.CreatePatientService(db.Db);

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

    [Fact]
    public async Task PropagatePatientAsync_rename_updates_by_barcode_and_name_variants()
    {
        var clinicId = Guid.NewGuid();
        await using var db = await SqliteTestDatabase.CreateAsync(TestClinicProvider.ForClinic(clinicId));
        db.Db.Clinics.Add(new Clinic { Id = clinicId, ClinicCode = "TST", Name = "Test Clinic" });

        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            PatientNo = "PAT-00002",
            FirstName = "asaad",
            LastName = ""
        };
        db.Db.Patients.Add(patient);
        db.Db.Prescriptions.Add(new Prescription
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            PrescriptionNo = 1,
            PatientName = "asaad",
            DoctorName = "Zainab"
        });
        db.Db.PharmacyBills.Add(new PharmacyBill
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            BillNo = 1,
            PatientId = "PAT-00002",
            PatientName = "asaad",
            BillDate = DateTime.Today
        });
        await db.Db.SaveChangesAsync();

        var propagation = ServiceTestFactory.CreatePropagation(db.Db);
        var previous = new Patient
        {
            Id = patient.Id,
            ClinicId = clinicId,
            PatientNo = patient.PatientNo,
            FirstName = "asaad",
            LastName = ""
        };
        patient.FirstName = "asaad2";
        await propagation.PropagatePatientAsync(clinicId, previous, patient);

        db.Db.ChangeTracker.Clear();
        var prescription = await db.Db.Prescriptions.ForClinic(clinicId).SingleAsync();
        Assert.Equal("asaad2", prescription.PatientName);
        Assert.Equal(patient.Id, prescription.PatientRecordId);

        var bill = await db.Db.PharmacyBills.ForClinic(clinicId).SingleAsync();
        Assert.Equal("asaad2", bill.PatientName);
        Assert.Equal(patient.Id, bill.PatientRecordId);
    }
}
