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
        var from = ClinicClock.ToClinicDate(fromDate);
        var to = ClinicClock.ToClinicDate(toDate);

        var patients = await _db.Patients
            .Where(p => p.ClinicId == clinicId)
            .ToListAsync();

        patients = patients.Where(p => PatientReportDateHelper.IsInDateRange(p, from, to)).ToList();

        if (!string.IsNullOrWhiteSpace(gender) && !gender.Equals("All", StringComparison.OrdinalIgnoreCase))
            patients = patients.Where(p => string.Equals(p.Gender, gender, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(doctorName) && !doctorName.Equals("All", StringComparison.OrdinalIgnoreCase))
            patients = patients.Where(p => p.DoctorName?.Contains(doctorName, StringComparison.OrdinalIgnoreCase) == true).ToList();
        if (!string.IsNullOrWhiteSpace(specialty) && !specialty.Equals("All", StringComparison.OrdinalIgnoreCase))
            patients = patients.Where(p => p.Specialty?.Contains(specialty, StringComparison.OrdinalIgnoreCase) == true).ToList();
        if (!string.IsNullOrWhiteSpace(city) && !city.Equals("All", StringComparison.OrdinalIgnoreCase))
            patients = patients.Where(p => p.City?.Contains(city, StringComparison.OrdinalIgnoreCase) == true).ToList();
        if (!string.IsNullOrWhiteSpace(patientSearch))
        {
            var term = patientSearch.Trim();
            patients = patients.Where(p =>
                p.FullName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                p.PatientNo.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (p.Phone?.Contains(term, StringComparison.OrdinalIgnoreCase) == true)).ToList();
        }

        var now = ClinicClock.Now;
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
        var notifications = await GetActiveNotificationsAsync(clinicId);
        return notifications.Count;
    }

    public async Task<List<ClinicalNotificationItem>> GetActiveNotificationsAsync(Guid clinicId)
    {
        var today = ClinicClock.Today;
        var patients = await _db.Patients
            .Where(p => p.ClinicId == clinicId)
            .ToListAsync();
        patients = patients.Where(p => PatientReportDateHelper.IsInDateRange(p, today, today)).ToList();

        var now = ClinicClock.Now;
        return patients
            .Select(p => BuildRow(p, now))
            .Where(r => r is not null && r.ShouldNotify)
            .Select(r => new ClinicalNotificationItem(
                $"appt-{r!.PatientId:N}",
                "appointment",
                "Appointment Reminder",
                $"{r.PatientName} — {FormatAppointmentTime(r.AppointmentTime)} ({r.RemainingLabel})",
                "/Reports/AppointmentReminders",
                r.AppointmentDateTime.ToUniversalTime()))
            .ToList();
    }

    private static string FormatAppointmentTime(TimeSpan? time) =>
        time.HasValue ? DateTime.Today.Add(time.Value).ToString("h:mm tt") : "—";

    public static string ResolveVisitStatus(Patient patient, DateTime now)
    {
        if (patient.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase) ||
            patient.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
            return patient.Status;

        var appointmentAt = ClinicClock.CombineAppointment(patient.AppointmentDate, patient.AppointmentTime);
        if (appointmentAt is null) return string.IsNullOrWhiteSpace(patient.Status) ? "Scheduled" : patient.Status;

        var minutes = (appointmentAt.Value - now).TotalMinutes;
        if (minutes < 0) return "Overdue";
        if (minutes <= 120) return "Under Process";
        return "Scheduled";
    }

    private static AppointmentReminderRow? BuildRow(Patient patient, DateTime now)
    {
        var effectiveDate = patient.AppointmentDate ?? patient.CreatedAt;
        var appointmentAt = ClinicClock.CombineAppointment(effectiveDate, patient.AppointmentTime);
        if (appointmentAt is null) return null;

        var minutes = (int)Math.Round((appointmentAt.Value - now).TotalMinutes);
        var status = ResolveVisitStatus(patient, now);
        var shouldNotify = minutes <= 15 && minutes >= 0 &&
                           !status.Equals("Completed", StringComparison.OrdinalIgnoreCase) &&
                           !status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase);

        string remainingLabel;
        if (status.Equals("Completed", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
            remainingLabel = "—";
        else if (minutes < 0)
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
            patient.Specialty ?? "",
            ClinicClock.ToClinicDate(effectiveDate),
            patient.AppointmentTime,
            appointmentAt.Value,
            remainingLabel,
            status,
            shouldNotify);
    }
}

public sealed record AppointmentReminderRow(
    Guid PatientId,
    string PatientNo,
    string PatientName,
    string Mobile,
    string DoctorName,
    string Specialty,
    DateTime AppointmentDate,
    TimeSpan? AppointmentTime,
    DateTime AppointmentDateTime,
    string RemainingLabel,
    string Status,
    bool ShouldNotify);

public sealed record ClinicalNotificationItem(
    string Id,
    string Kind,
    string Title,
    string Detail,
    string Link,
    DateTime AtUtc);
