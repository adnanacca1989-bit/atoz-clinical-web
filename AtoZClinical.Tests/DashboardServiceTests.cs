using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Tests.Helpers;
using Microsoft.Extensions.Configuration;

namespace AtoZClinical.Tests;

public class DashboardServiceTests
{
    private static DashboardService CreateService(SqliteTestDatabase db, Guid tenantClinicId)
    {
        var config = new ConfigurationBuilder().Build();
        var reporting = new ReportingDataService(db.Db, config, TestClinicProvider.ForClinic(tenantClinicId));
        var visitStatus = new PatientVisitStatusService(db.Db, new AuditService(db.Db));
        return new DashboardService(reporting, visitStatus, new DoctorScopeContext());
    }

    [Fact]
    public async Task GetSummaryAsync_returns_clinic_data_even_when_tenant_filter_mismatches()
    {
        var clinicId = Guid.NewGuid();
        var otherClinicId = Guid.NewGuid();
        await using var db = await SqliteTestDatabase.CreateAsync(TestClinicProvider.ForClinic(otherClinicId));

        var today = DateTime.Today;
        db.Db.Clinics.Add(new Clinic { Id = clinicId, ClinicCode = "ABC", Name = "ABC" });
        db.Db.Doctors.Add(new Doctor { ClinicId = clinicId, DoctorNo = 1, Name = "Dr A", Status = "Active" });
        db.Db.Patients.Add(new Patient
        {
            ClinicId = clinicId,
            PatientNo = "PAT-00001",
            FirstName = "Adnan",
            Status = "Pending",
            CreatedAt = today,
            AppointmentDate = null
        });
        db.Db.Invoices.Add(new Invoice
        {
            ClinicId = clinicId,
            InvoiceNo = 1,
            InvoiceDate = today,
            TotalAmount = 25_000m,
            BalanceDue = 0m,
            PatientName = "Adnan"
        });
        db.Db.CashReceipts.Add(new CashReceipt
        {
            ClinicId = clinicId,
            ReceiptNo = 1,
            ReceiptDate = today,
            Amount = 100_000m,
            PatientName = "Adnan"
        });
        await db.Db.SaveChangesAsync();

        var service = CreateService(db, otherClinicId);

        var summary = await service.GetSummaryAsync(clinicId, today.AddDays(-7), today, isTodayScope: false);

        Assert.Equal(1, summary.ActiveDoctorCount);
        Assert.Equal(1, summary.NewRegistrations);
        Assert.Equal(0, summary.PeriodPending);
        Assert.Equal(1, summary.PeriodCompleted);
        Assert.Equal(25_000m, summary.InvoiceTotal);
        Assert.Equal(100_000m, summary.CashReceived);
    }

    [Fact]
    public async Task GetSummaryAsync_counts_patients_without_appointment_date_by_created_at()
    {
        var clinicId = Guid.NewGuid();
        await using var db = await SqliteTestDatabase.CreateAsync(TestClinicProvider.ForClinic(clinicId));

        var today = DateTime.Today;
        db.Db.Clinics.Add(new Clinic { Id = clinicId, ClinicCode = "ABC", Name = "ABC" });
        db.Db.Patients.Add(new Patient
        {
            ClinicId = clinicId,
            PatientNo = "PAT-00001",
            FirstName = "Today",
            Status = "Confirmed",
            CreatedAt = today,
            AppointmentDate = null
        });
        await db.Db.SaveChangesAsync();

        var service = CreateService(db, clinicId);

        var summary = await service.GetSummaryAsync(clinicId, today, today, isTodayScope: true);

        Assert.Equal(1, summary.TodayConfirmed);
        Assert.Equal(1, summary.PeriodConfirmed);
    }
}
