using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Tests;

public class RequestReportServiceTests
{
    [Fact]
    public async Task Lab_request_shows_not_yet_until_result_exists()
    {
        var clinicId = Guid.NewGuid();
        await using var db = await SqliteTestDatabase.CreateAsync(TestClinicProvider.ForClinic(clinicId));
        db.Db.Clinics.Add(new Clinic { Id = clinicId, ClinicCode = "TST", Name = "Test Clinic" });
        await DatabaseInitializer.EnsureStandardChartAccountsAsync(db.Db, clinicId);

        db.Db.LabRequests.Add(new LabRequest
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            RequestNo = 1,
            RequestDate = new DateTime(2026, 6, 29),
            PatientName = "Noor",
            PatientBarcode = "PAT-00001",
            DoctorName = "CIMA",
            TotalAmount = 10_000m,
            UpdatedAt = DateTime.UtcNow
        });
        await db.Db.SaveChangesAsync();

        var service = new RequestReportService(db.Db, new DoctorScopeContext());
        var pending = await service.BuildAsync(clinicId, new DateTime(2026, 6, 1), new DateTime(2026, 6, 30));
        var row = Assert.Single(pending.Rows);
        Assert.Equal(RequestReportService.StatusNotYet, row.ResultInvoiceStatus);

        db.Db.LabResults.Add(new LabResult
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            ResultNo = 1,
            RequestNo = 1,
            ResultDate = new DateTime(2026, 6, 29),
            PatientName = "Noor",
            DoctorName = "CIMA",
            UpdatedAt = DateTime.UtcNow
        });
        await db.Db.SaveChangesAsync();

        var completed = await service.BuildAsync(clinicId, new DateTime(2026, 6, 1), new DateTime(2026, 6, 30));
        row = Assert.Single(completed.Rows);
        Assert.Equal(RequestReportService.StatusCreated, row.ResultInvoiceStatus);
        Assert.Equal("1", row.ResultId);
    }

    [Fact]
    public async Task Pending_only_filter_excludes_created_requests()
    {
        var clinicId = Guid.NewGuid();
        await using var db = await SqliteTestDatabase.CreateAsync(TestClinicProvider.ForClinic(clinicId));
        db.Db.Clinics.Add(new Clinic { Id = clinicId, ClinicCode = "TST", Name = "Test Clinic" });
        await DatabaseInitializer.EnsureStandardChartAccountsAsync(db.Db, clinicId);

        db.Db.LabRequests.Add(new LabRequest
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            RequestNo = 1,
            RequestDate = new DateTime(2026, 6, 29),
            PatientName = "A",
            TotalAmount = 100m,
            UpdatedAt = DateTime.UtcNow
        });
        db.Db.LabRequests.Add(new LabRequest
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            RequestNo = 2,
            RequestDate = new DateTime(2026, 6, 29),
            PatientName = "B",
            TotalAmount = 200m,
            UpdatedAt = DateTime.UtcNow
        });
        db.Db.LabResults.Add(new LabResult
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            ResultNo = 1,
            RequestNo = 1,
            ResultDate = new DateTime(2026, 6, 29),
            PatientName = "A",
            UpdatedAt = DateTime.UtcNow
        });
        await db.Db.SaveChangesAsync();

        var service = new RequestReportService(db.Db, new DoctorScopeContext());
        var report = await service.BuildAsync(
            clinicId, new DateTime(2026, 6, 1), new DateTime(2026, 6, 30), pendingOnly: true);

        var row = Assert.Single(report.Rows);
        Assert.Equal(2, row.RequestNo);
        Assert.Equal(RequestReportService.StatusNotYet, row.ResultInvoiceStatus);
    }
}
