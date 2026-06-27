using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Tests.Helpers;

namespace AtoZClinical.Tests;

public class PatientPortalServiceTests
{
    [Fact]
    public async Task AuthenticateAsync_finds_patient_without_tenant_context()
    {
        var tenant = TestClinicProvider.Bypass();
        await using var db = await SqliteTestDatabase.CreateAsync(tenant);
        var clinicId = Guid.NewGuid();

        db.Db.Clinics.Add(new Clinic
        {
            Id = clinicId,
            ClinicCode = "CLN-TEST",
            Name = "Test Clinic",
            LicenseKey = "TEST",
            LicenseExpires = DateTime.UtcNow.Date.AddYears(1),
            PlanName = "Trial"
        });
        db.Db.ClinicConfigurations.Add(new ClinicConfiguration
        {
            ClinicId = clinicId,
            PatientPortalEnabled = true
        });
        db.Db.Patients.Add(new Patient
        {
            ClinicId = clinicId,
            PatientNo = "PAT-00001",
            FirstName = "Sam",
            DateOfBirth = new DateTime(1996, 6, 25),
            Phone = "+964 770 123 4567"
        });
        await db.Db.SaveChangesAsync();

        var portal = new PatientPortalService(db.Db, null!);

        var patient = await portal.AuthenticateAsync(
            clinicId,
            "pat-00001",
            new DateTime(1996, 6, 25),
            "4567");

        Assert.NotNull(patient);
        Assert.Equal("PAT-00001", patient!.PatientNo);
    }

    [Fact]
    public async Task AuthenticateAsync_matches_dob_across_utc_storage()
    {
        var tenant = TestClinicProvider.Bypass();
        await using var db = await SqliteTestDatabase.CreateAsync(tenant);
        var clinicId = Guid.NewGuid();

        db.Db.Clinics.Add(new Clinic
        {
            Id = clinicId,
            ClinicCode = "CLN-TEST",
            Name = "Test Clinic",
            LicenseKey = "TEST",
            LicenseExpires = DateTime.UtcNow.Date.AddYears(1),
            PlanName = "Trial"
        });
        db.Db.Patients.Add(new Patient
        {
            ClinicId = clinicId,
            PatientNo = "PAT-00005",
            FirstName = "Jones",
            LastName = "Elia",
            DateOfBirth = new DateTime(1999, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Phone = "07701231651"
        });
        await db.Db.SaveChangesAsync();

        var portal = new PatientPortalService(db.Db, null!);

        var patient = await portal.AuthenticateAsync(
            clinicId,
            "PAT-00005",
            new DateTime(1999, 1, 1),
            "1651");

        Assert.NotNull(patient);
        Assert.Equal("Jones", patient!.FirstName);
    }
}
