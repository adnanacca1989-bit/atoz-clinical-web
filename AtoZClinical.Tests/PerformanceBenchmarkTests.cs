using System.Diagnostics;
using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Tests.Helpers;

namespace AtoZClinical.Tests;

public class PerformanceBenchmarkTests
{
    [Fact]
    public async Task SyncAllPatientStatuses_completes_within_reasonable_time_for_50_patients()
    {
        var clinicId = Guid.NewGuid();
        await using var db = await SqliteTestDatabase.CreateAsync(TestClinicProvider.ForClinic(clinicId));

        db.Db.Clinics.Add(new Clinic { Id = clinicId, ClinicCode = "PERF", Name = "Perf Clinic" });
        for (var i = 0; i < 50; i++)
        {
            db.Db.Patients.Add(new Patient
            {
                ClinicId = clinicId,
                PatientNo = $"PAT-{i:00000}",
                FirstName = $"Patient{i}",
                Status = "Pending"
            });
        }
        await db.Db.SaveChangesAsync();

        var service = new PatientVisitStatusService(db.Db, new AuditService(db.Db));
        var sw = Stopwatch.StartNew();
        var updated = await service.SyncAllPatientStatusesForClinicAsync(clinicId);
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"Sync took {sw.Elapsed.TotalSeconds:F2}s");
        Assert.True(updated >= 0);
    }
}
