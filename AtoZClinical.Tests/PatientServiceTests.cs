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

        var service = new PatientService(
            db.Db,
            new MasterDataPropagationService(db.Db),
            new InvoiceDeleteGuardService(db.Db),
            new PatientVisitStatusService(db.Db),
            new NoOpWebhookDispatchService(),
            new AuditService(db.Db));

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
}
