using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Web.Services;

public sealed class AppointmentReminderService
{
    private readonly ClinicalDbContext _db;

    public AppointmentReminderService(ClinicalDbContext db) => _db = db;

    public async Task<List<AppointmentReminderRow>> GetRemindersAsync(
        Guid clinicId,
        DateTime fromDate,
        DateTime toDate,
        string? gender,
        string? doctorName,
        string? specialty,
        string? city,
        string? statusFilter,
        string? patientSearch)
    {
        var patients = await _db.Patients
            .Where(p => p.ClinicId == clinicId &&
                        p.AppointmentDate >= fromDate.Date &&
                        p.AppointmentDate <= toDate.Date)
            .ToListAsync();

        if (!string.IsNullOrWhiteSpace(gender) && !gender.Equals("All", StringComparison.OrdinalIgnoreCase))
            patients = patients.Where(p => string.Equals(p.Gender, gender, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(doctorName) && !doctorName.Equals("All", StringComparison.OrdinalIgnoreCase))
            patients = patients.Where(p => p.DoctorName?.Contains(doctorName, StringComparison.OrdinalIgnoreCase) == true).ToList();
        if (!string.IsNullOrWhiteSpace(specialty) && !specialty.Equals("All", StringComparison.OrdinalIgnoreCase))
            patients = patients.Where(p => p.Specialty?.Contains(specialty, StringComparison.OrdinalIgnoreCase) == true).ToList();
        if (!string.IsNullOrWhiteSpace(city) && !city.Equals("All", StringComparison.OrdinalIgnoreCase))
            patients = patients.Where(p => p.City?.Contains(city, StringComparison.OrdinalIgnoreCase) == true).ToList();
        if (!string.IsNullOrWhiteSpace(patientSearch))
            patients = patients.Where(p =>
                p.FullName.Contains(patientSearch, StringComparison.OrdinalIgnoreCase) ||
                p.PatientNo.Contains(patientSearch, StringComparison.OrdinalIgnoreCase)).ToList();

        var now = DateTime.Now;
        var rows = patients
            .Select(p => BuildRow(p, now))
            .Where(r => r is not null)
            .Cast<AppointmentReminderRow>()
            .ToList();

        if (!string.IsNullOrWhiteSpace(statusFilter) && !statusFilter.Equals("All", StringComparison.OrdinalIgnoreCase))
            rows = rows.Where(r =>
                PatientVisitStatuses.Normalize(r.Status).Equals(statusFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        return rows.OrderBy(r => r.AppointmentDateTime).ToList();
    }

    public async Task<int> GetUpcomingReminderCountAsync(Guid clinicId)
    {
        var today = DateTime.Today;
        var patients = await _db.Patients
            .Where(p => p.ClinicId == clinicId && p.AppointmentDate == today)
            .ToListAsync();

        var now = DateTime.Now;
        return patients.Count(p =>
        {
            var row = BuildRow(p, now);
            return row is not null && row.ShouldNotify;
        });
    }

    public static string ResolveVisitStatus(Patient patient, DateTime now)
    {
        if (patient.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase) ||
            patient.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
            return patient.Status;

        var appointmentAt = CombineDateTime(patient.AppointmentDate, patient.AppointmentTime);
        if (appointmentAt is null) return string.IsNullOrWhiteSpace(patient.Status) ? "Scheduled" : patient.Status;

        var minutes = (appointmentAt.Value - now).TotalMinutes;
        if (minutes < 0) return "Overdue";
        if (minutes <= 120) return "Under Process";
        return "Scheduled";
    }

    private static AppointmentReminderRow? BuildRow(Patient patient, DateTime now)
    {
        var appointmentAt = CombineDateTime(patient.AppointmentDate, patient.AppointmentTime);
        if (appointmentAt is null) return null;

        var minutes = (int)Math.Round((appointmentAt.Value - now).TotalMinutes);
        var status = PatientVisitStatuses.Normalize(patient.Status);
        var shouldNotify = minutes <= 15 && minutes >= 0 &&
                           !status.Equals("Completed", StringComparison.OrdinalIgnoreCase) &&
                           !status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase);

        string remainingLabel;
        if (minutes < 0)
            remainingLabel = $"Overdue {Math.Abs(minutes)} min";
        else if (minutes == 0)
            remainingLabel = "Now";
        else
            remainingLabel = $"{minutes} min";

        return new AppointmentReminderRow(
            patient.Id,
            patient.PatientNo,
            patient.FullName,
            patient.Phone ?? "",
            patient.DoctorName ?? "",
            patient.AppointmentDate!.Value,
            patient.AppointmentTime,
            appointmentAt.Value,
            remainingLabel,
            status,
            shouldNotify);
    }

    private static DateTime? CombineDateTime(DateTime? date, TimeSpan? time)
    {
        if (!date.HasValue) return null;
        return date.Value.Date + (time ?? TimeSpan.Zero);
    }
}

public sealed record AppointmentReminderRow(
    Guid PatientId,
    string PatientNo,
    string PatientName,
    string Mobile,
    string DoctorName,
    DateTime AppointmentDate,
    TimeSpan? AppointmentTime,
    DateTime AppointmentDateTime,
    string RemainingLabel,
    string Status,
    bool ShouldNotify);
