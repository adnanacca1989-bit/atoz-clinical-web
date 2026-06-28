using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Tests;

public class DoctorServiceTests
{
    [Fact]
    public async Task SaveAsync_creates_new_doctor()
    {
        var clinicId = Guid.NewGuid();
        await using var db = await SqliteTestDatabase.CreateAsync(TestClinicProvider.ForClinic(clinicId));
        db.Db.Clinics.Add(new Clinic { Id = clinicId, ClinicCode = "TST", Name = "Test Clinic" });
        await db.Db.SaveChangesAsync();

        var service = new DoctorService(
            db.Db,
            ServiceTestFactory.CreatePropagation(db.Db),
            new InvoiceDeleteGuardService(db.Db),
            new AuditService(db.Db));

        var saved = await service.SaveAsync(clinicId, new Doctor
        {
            Id = Guid.Empty,
            Name = "Muslem Essa",
            Specialty = "Dental",
            ConsultationFee = 150_000m,
            Status = "Active"
        }, "tester");

        Assert.NotEqual(Guid.Empty, saved.Id);
        Assert.Equal(1, saved.DoctorNo);
        Assert.Equal("Muslem Essa", saved.Name);
    }

    [Fact]
    public async Task SaveAsync_adds_second_doctor_with_incremented_number()
    {
        var clinicId = Guid.NewGuid();
        await using var db = await SqliteTestDatabase.CreateAsync(TestClinicProvider.ForClinic(clinicId));
        db.Db.Clinics.Add(new Clinic { Id = clinicId, ClinicCode = "TST", Name = "Test Clinic" });
        await db.Db.SaveChangesAsync();

        var service = new DoctorService(
            db.Db,
            ServiceTestFactory.CreatePropagation(db.Db),
            new InvoiceDeleteGuardService(db.Db),
            new AuditService(db.Db));

        await service.SaveAsync(clinicId, new Doctor
        {
            Id = Guid.Empty,
            Name = "Doctor One",
            Specialty = "ENT",
            ConsultationFee = 50_000m
        }, "tester");

        var second = await service.SaveAsync(clinicId, new Doctor
        {
            Id = Guid.Empty,
            Name = "Doctor Two",
            Specialty = "Dental",
            ConsultationFee = 75_000m
        }, "tester");

        Assert.Equal(2, second.DoctorNo);
    }

    [Fact]
    public async Task SaveAsync_treats_unknown_id_as_new_doctor()
    {
        var clinicId = Guid.NewGuid();
        await using var db = await SqliteTestDatabase.CreateAsync(TestClinicProvider.ForClinic(clinicId));
        db.Db.Clinics.Add(new Clinic { Id = clinicId, ClinicCode = "TST", Name = "Test Clinic" });
        await db.Db.SaveChangesAsync();

        var service = new DoctorService(
            db.Db,
            ServiceTestFactory.CreatePropagation(db.Db),
            new InvoiceDeleteGuardService(db.Db),
            new AuditService(db.Db));

        var saved = await service.SaveAsync(clinicId, new Doctor
        {
            Id = Guid.NewGuid(),
            Name = "Stale Id Doctor",
            Specialty = "ENT",
            ConsultationFee = 25_000m
        }, "tester");

        Assert.Equal(1, saved.DoctorNo);
        Assert.Single(await db.Db.Doctors.Where(d => d.ClinicId == clinicId).ToListAsync());
    }
}
