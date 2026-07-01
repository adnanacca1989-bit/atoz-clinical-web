using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Tests.Helpers;

namespace AtoZClinical.Tests;

public class WardPatientReportServiceTests
{
    [Fact]
    public async Task Patient_filter_lists_all_room_bookings_for_same_patient()
    {
        var clinicId = Guid.NewGuid();
        var patientId = Guid.NewGuid();
        await using var db = await SqliteTestDatabase.CreateAsync(TestClinicProvider.ForClinic(clinicId));
        db.Db.Clinics.Add(new Clinic { Id = clinicId, ClinicCode = "TST", Name = "Test Clinic" });
        await DatabaseInitializer.EnsureStandardChartAccountsAsync(db.Db, clinicId);

        db.Db.Patients.Add(new Patient
        {
            Id = patientId,
            ClinicId = clinicId,
            PatientNo = "P001",
            FirstName = "Noor",
            LastName = "Alaa",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        db.Db.RoomBookings.Add(new RoomBooking
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            BookingNo = 1,
            DateBook = new DateTime(2026, 7, 1),
            PatientRecordId = patientId,
            PatientName = "Noor Alaa",
            PatientBarcode = "P001",
            RoomNumber = 5,
            EnterDate = new DateTime(2026, 7, 1),
            UpdatedAt = DateTime.UtcNow
        });
        db.Db.RoomBookings.Add(new RoomBooking
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            BookingNo = 2,
            DateBook = new DateTime(2026, 7, 1),
            PatientRecordId = patientId,
            PatientName = "Noor Alaa",
            PatientBarcode = "P001",
            RoomNumber = 6,
            EnterDate = new DateTime(2026, 7, 2),
            UpdatedAt = DateTime.UtcNow
        });
        await db.Db.SaveChangesAsync();

        var service = new WardPatientReportService(db.Db);
        var report = await service.GetRowsAsync(
            clinicId,
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 1),
            patientBarcode: null,
            patientName: "Noor Alaa",
            doctorName: null);

        Assert.Equal(2, report.Rows.Count);
        Assert.Contains(report.Rows, r => r.RoomNumber == 5);
        Assert.Contains(report.Rows, r => r.RoomNumber == 6);
    }

    [Fact]
    public async Task Date_only_filter_uses_stay_overlap()
    {
        var clinicId = Guid.NewGuid();
        await using var db = await SqliteTestDatabase.CreateAsync(TestClinicProvider.ForClinic(clinicId));
        db.Db.Clinics.Add(new Clinic { Id = clinicId, ClinicCode = "TST", Name = "Test Clinic" });
        await DatabaseInitializer.EnsureStandardChartAccountsAsync(db.Db, clinicId);

        db.Db.RoomBookings.Add(new RoomBooking
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            BookingNo = 1,
            DateBook = new DateTime(2026, 7, 1),
            PatientName = "Noor Alaa",
            RoomNumber = 5,
            EnterDate = new DateTime(2026, 7, 1),
            ExitDate = new DateTime(2026, 7, 3),
            UpdatedAt = DateTime.UtcNow
        });
        db.Db.RoomBookings.Add(new RoomBooking
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            BookingNo = 2,
            DateBook = new DateTime(2026, 7, 1),
            PatientName = "Noor Alaa",
            RoomNumber = 6,
            EnterDate = new DateTime(2026, 7, 2),
            UpdatedAt = DateTime.UtcNow
        });
        await db.Db.SaveChangesAsync();

        var service = new WardPatientReportService(db.Db);
        var report = await service.GetRowsAsync(
            clinicId,
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 1),
            patientBarcode: null,
            patientName: null,
            doctorName: null);

        var row = Assert.Single(report.Rows);
        Assert.Equal(5, row.RoomNumber);
    }
}
