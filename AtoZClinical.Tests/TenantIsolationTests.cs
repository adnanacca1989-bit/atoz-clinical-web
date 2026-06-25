using AtoZClinical.Core.Entities;
using AtoZClinical.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Tests;

public class TenantIsolationTests
{
    [Fact]
    public async Task Patient_query_returns_only_active_tenant_rows()
    {
        var clinicA = Guid.NewGuid();
        var clinicB = Guid.NewGuid();

        await using var tenantA = await SqliteTestDatabase.CreateAsync(TestClinicProvider.ForClinic(clinicA));
        await using var tenantB = await SqliteTestDatabase.CreateAsync(TestClinicProvider.ForClinic(clinicB));

        // Re-seed into each isolated in-memory DB (separate connections).
        tenantA.Db.Clinics.Add(new Clinic { Id = clinicA, ClinicCode = "A", Name = "Clinic A" });
        tenantA.Db.Clinics.Add(new Clinic { Id = clinicB, ClinicCode = "B", Name = "Clinic B" });
        tenantA.Db.Patients.Add(new Patient { ClinicId = clinicA, PatientNo = "PAT-A", FirstName = "Alice" });
        tenantA.Db.Patients.Add(new Patient { ClinicId = clinicB, PatientNo = "PAT-B", FirstName = "Bob" });
        await tenantA.Db.SaveChangesAsync();

        tenantB.Db.Clinics.Add(new Clinic { Id = clinicA, ClinicCode = "A", Name = "Clinic A" });
        tenantB.Db.Clinics.Add(new Clinic { Id = clinicB, ClinicCode = "B", Name = "Clinic B" });
        tenantB.Db.Patients.Add(new Patient { ClinicId = clinicA, PatientNo = "PAT-A", FirstName = "Alice" });
        tenantB.Db.Patients.Add(new Patient { ClinicId = clinicB, PatientNo = "PAT-B", FirstName = "Bob" });
        await tenantB.Db.SaveChangesAsync();

        var patientsA = await tenantA.Db.Patients.AsNoTracking().ToListAsync();
        var patientsB = await tenantB.Db.Patients.AsNoTracking().ToListAsync();

        Assert.Single(patientsA);
        Assert.Equal("Alice", patientsA[0].FirstName);
        Assert.Equal(clinicA, patientsA[0].ClinicId);

        Assert.Single(patientsB);
        Assert.Equal("Bob", patientsB[0].FirstName);
        Assert.Equal(clinicB, patientsB[0].ClinicId);
    }

    [Fact]
    public async Task Bypass_provider_returns_all_tenant_rows()
    {
        var clinicA = Guid.NewGuid();
        var clinicB = Guid.NewGuid();

        await using var db = await SqliteTestDatabase.CreateAsync(TestClinicProvider.Bypass());
        db.Db.Clinics.AddRange(
            new Clinic { Id = clinicA, ClinicCode = "A", Name = "Clinic A" },
            new Clinic { Id = clinicB, ClinicCode = "B", Name = "Clinic B" });
        db.Db.Patients.AddRange(
            new Patient { ClinicId = clinicA, PatientNo = "PAT-A", FirstName = "Alice" },
            new Patient { ClinicId = clinicB, PatientNo = "PAT-B", FirstName = "Bob" });
        await db.Db.SaveChangesAsync();

        var all = await db.Db.Patients.AsNoTracking().ToListAsync();
        Assert.Equal(2, all.Count);
    }
}
