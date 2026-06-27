using AtoZClinical.Core.Entities;
using AtoZClinical.Core.Enums;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public sealed class PatientPortalService
{
    private readonly ClinicalDbContext _db;
    private readonly AppointmentService _appointments;

    public PatientPortalService(ClinicalDbContext db, AppointmentService appointments)
    {
        _db = db;
        _appointments = appointments;
    }

    public async Task<Patient?> AuthenticateAsync(
        Guid clinicId,
        string patientNo,
        DateTime dateOfBirth,
        string phoneLast4)
    {
        var config = await _db.ClinicConfigurations
            .AsNoTracking()
            .ForClinic(clinicId)
            .FirstOrDefaultAsync();
        if (config?.PatientPortalEnabled == false) return null;

        var normalizedNo = patientNo.Trim();
        var last4 = phoneLast4.Trim();
        if (last4.Length != 4 || !last4.All(char.IsDigit)) return null;

        var normalizedNoLower = normalizedNo.ToLowerInvariant();
        var enteredDob = ClinicClock.ToClinicDate(dateOfBirth);

        var candidates = await _db.Patients
            .AsNoTracking()
            .ForClinic(clinicId)
            .Where(p =>
                p.PatientNo.ToLower() == normalizedNoLower
                && p.DateOfBirth != null)
            .ToListAsync();

        var patient = candidates.FirstOrDefault(p =>
            ClinicClock.ToClinicDate(p.DateOfBirth) == enteredDob);

        if (patient is null || string.IsNullOrWhiteSpace(patient.Phone)) return null;

        var digits = new string(patient.Phone.Where(char.IsDigit).ToArray());
        if (digits.Length < 4 || !digits.EndsWith(last4, StringComparison.Ordinal)) return null;

        return patient;
    }

    public Task<List<Appointment>> GetUpcomingAppointmentsAsync(Guid clinicId, Guid patientId)
    {
        var today = ClinicClock.Today;
        return _db.Appointments
            .AsNoTracking()
            .ForClinic(clinicId)
            .Where(a => a.PatientId == patientId && a.AppointmentDate.Date >= today)
            .OrderBy(a => a.AppointmentDate)
            .ThenBy(a => a.StartTime)
            .Take(20)
            .ToListAsync();
    }

    public Task<List<Prescription>> GetRecentPrescriptionsAsync(Guid clinicId, string patientFullName) =>
        _db.Prescriptions
            .AsNoTracking()
            .ForClinic(clinicId)
            .Where(p => p.PatientName == patientFullName)
            .OrderByDescending(p => p.DatePrescription)
            .Take(10)
            .ToListAsync();

    public Task<List<Invoice>> GetRecentInvoicesAsync(Guid clinicId, string patientName) =>
        _db.Invoices
            .AsNoTracking()
            .ForClinic(clinicId)
            .Where(i => i.PatientName == patientName)
            .OrderByDescending(i => i.InvoiceDate)
            .Take(10)
            .ToListAsync();

    public async Task<List<Doctor>> GetBookableDoctorsAsync(Guid clinicId)
    {
        var doctors = await _db.Doctors
            .AsNoTracking()
            .ForClinic(clinicId)
            .OrderBy(d => d.Name)
            .ToListAsync();

        return doctors
            .Where(d => string.IsNullOrWhiteSpace(d.Status)
                        || d.Status.Equals("active", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public async Task<(bool Success, string? Error)> RequestAppointmentAsync(
        Guid clinicId,
        Guid patientId,
        DateTime appointmentDate,
        TimeSpan startTime,
        string? doctorName,
        string? reason)
    {
        var config = await _db.ClinicConfigurations
            .AsNoTracking()
            .ForClinic(clinicId)
            .FirstOrDefaultAsync();
        if (config?.PatientPortalEnabled == false)
            return (false, "Patient portal is not enabled.");

        if (appointmentDate.Date < ClinicClock.Today)
            return (false, "Please choose today or a future date.");

        var patientExists = await _db.Patients.ForClinic(clinicId).AnyAsync(p => p.Id == patientId);
        if (!patientExists)
            return (false, "Patient not found.");

        var hasConflict = await _db.Appointments.ForClinic(clinicId).AnyAsync(a =>
            a.PatientId == patientId
            && a.AppointmentDate.Date == appointmentDate.Date
            && a.Status != AppointmentStatus.Cancelled);
        if (hasConflict)
            return (false, "You already have an appointment on that day.");

        if (!string.IsNullOrWhiteSpace(doctorName))
        {
            var doc = doctorName.Trim();
            var doctorOk = await _db.Doctors.ForClinic(clinicId)
                .Where(d => d.Name == doc)
                .ToListAsync();
            if (!doctorOk.Any(d => string.IsNullOrWhiteSpace(d.Status)
                                   || d.Status.Equals("active", StringComparison.OrdinalIgnoreCase)))
                return (false, "Selected doctor is not available.");
        }

        var appointment = new Appointment
        {
            ClinicId = clinicId,
            PatientId = patientId,
            AppointmentDate = appointmentDate.Date,
            StartTime = startTime,
            DoctorName = string.IsNullOrWhiteSpace(doctorName) ? null : doctorName.Trim(),
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            Status = AppointmentStatus.Scheduled,
            Notes = "Booked via patient portal"
        };

        await _appointments.SaveAsync(clinicId, appointment);
        return (true, null);
    }
}
