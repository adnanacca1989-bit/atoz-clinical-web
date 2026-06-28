using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Tests;

public class PatientServiceTests
{
    [Fact]
    public async Task SaveAsync_creates_third_patient_after_two_exist()
    {
        var clinicId = Guid.NewGuid();
        await using var db = await SqliteTestDatabase.CreateAsync(TestClinicProvider.ForClinic(clinicId));
        db.Db.Clinics.Add(new Clinic { Id = clinicId, ClinicCode = "TST", Name = "Test Clinic" });
        db.Db.Patients.AddRange(
            new Patient { Id = Guid.NewGuid(), ClinicId = clinicId, PatientNo = "PAT-00001", FirstName = "A" },
            new Patient { Id = Guid.NewGuid(), ClinicId = clinicId, PatientNo = "PAT-00002", FirstName = "B" });
        await db.Db.SaveChangesAsync();

        var audit = new AuditService(db.Db);
        var service = new PatientService(
            db.Db,
            ServiceTestFactory.CreatePropagation(db.Db),
            new InvoiceDeleteGuardService(db.Db),
            new PatientVisitStatusService(db.Db, audit),
            new NoOpWebhookDispatchService(),
            audit);

        var saved = await service.SaveAsync(clinicId, new Patient
        {
            Id = Guid.Empty,
            FirstName = "Third",
            Phone = "123",
            City = "Baghdad"
        }, "tester");

        Assert.Equal("PAT-00003", saved.PatientNo);
        Assert.Equal(3, await db.Db.Patients.ForClinic(clinicId).CountAsync());
    }

    [Fact]
    public async Task SaveAsync_succeeds_when_change_tracker_has_unrelated_entities()
    {
        var clinicId = Guid.NewGuid();
        await using var db = await SqliteTestDatabase.CreateAsync(TestClinicProvider.ForClinic(clinicId));
        db.Db.Clinics.Add(new Clinic { Id = clinicId, ClinicCode = "TST", Name = "Test Clinic" });
        db.Db.ClinicConfigurations.Add(new ClinicConfiguration { ClinicId = clinicId });
        db.Db.Patients.Add(new Patient { Id = Guid.NewGuid(), ClinicId = clinicId, PatientNo = "PAT-00001", FirstName = "A" });
        db.Db.Patients.Add(new Patient { Id = Guid.NewGuid(), ClinicId = clinicId, PatientNo = "PAT-00002", FirstName = "B" });
        await db.Db.SaveChangesAsync();

        // Simulate stray tracked entity from the same HTTP request (duplicate config add).
        db.Db.ClinicConfigurations.Add(new ClinicConfiguration { ClinicId = clinicId });

        var audit = new AuditService(db.Db);
        var service = new PatientService(
            db.Db,
            ServiceTestFactory.CreatePropagation(db.Db),
            new InvoiceDeleteGuardService(db.Db),
            new PatientVisitStatusService(db.Db, audit),
            new NoOpWebhookDispatchService(),
            audit);

        var saved = await service.SaveAsync(clinicId, new Patient
        {
            Id = Guid.Empty,
            FirstName = "Third",
            Phone = "123",
            City = "Baghdad"
        }, "tester");

        Assert.Equal("PAT-00003", saved.PatientNo);
    }
}
