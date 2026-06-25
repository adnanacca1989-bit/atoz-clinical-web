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
            .FirstOrDefaultAsync(c => c.ClinicId == clinicId);
        if (config is null || !config.PatientPortalEnabled) return null;

        var normalizedNo = patientNo.Trim();
        var last4 = phoneLast4.Trim();
        if (last4.Length != 4 || !last4.All(char.IsDigit)) return null;

        var patient = await _db.Patients
            .AsNoTracking()
            .FirstOrDefaultAsync(p =>
                p.ClinicId == clinicId
                && p.PatientNo == normalizedNo
                && p.DateOfBirth != null
                && p.DateOfBirth.Value.Date == dateOfBirth.Date);

        if (patient is null || string.IsNullOrWhiteSpace(patient.Phone)) return null;

        var digits = new string(patient.Phone.Where(char.IsDigit).ToArray());
        if (digits.Length < 4 || !digits.EndsWith(last4, StringComparison.Ordinal)) return null;

        return patient;
    }

    public Task<List<Appointment>> GetUpcomingAppointmentsAsync(Guid clinicId, Guid patientId) =>
        _db.Appointments
            .AsNoTracking()
            .Where(a => a.ClinicId == clinicId
                        && a.PatientId == patientId
                        && a.AppointmentDate.Date >= DateTime.UtcNow.Date)
            .OrderBy(a => a.AppointmentDate)
            .ThenBy(a => a.StartTime)
            .Take(20)
            .ToListAsync();

    public Task<List<Prescription>> GetRecentPrescriptionsAsync(Guid clinicId, string patientFullName) =>
        _db.Prescriptions
            .AsNoTracking()
            .Where(p => p.ClinicId == clinicId && p.PatientName == patientFullName)
            .OrderByDescending(p => p.DatePrescription)
            .Take(10)
            .ToListAsync();

    public Task<List<Invoice>> GetRecentInvoicesAsync(Guid clinicId, string patientName) =>
        _db.Invoices
            .AsNoTracking()
            .Where(i => i.ClinicId == clinicId && i.PatientName == patientName)
            .OrderByDescending(i => i.InvoiceDate)
            .Take(10)
            .ToListAsync();

    public Task<List<Doctor>> GetBookableDoctorsAsync(Guid clinicId) =>
        _db.Doctors
            .AsNoTracking()
            .Where(d => d.ClinicId == clinicId && d.Status != null && d.Status.ToLower() == "active")
            .OrderBy(d => d.Name)
            .ToListAsync();

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
            .FirstOrDefaultAsync(c => c.ClinicId == clinicId);
        if (config is null || !config.PatientPortalEnabled)
            return (false, "Patient portal is not enabled.");

        if (appointmentDate.Date < DateTime.UtcNow.Date)
            return (false, "Please choose today or a future date.");

        var patientExists = await _db.Patients.AnyAsync(p => p.ClinicId == clinicId && p.Id == patientId);
        if (!patientExists)
            return (false, "Patient not found.");

        var hasConflict = await _db.Appointments.AnyAsync(a =>
            a.ClinicId == clinicId
            && a.PatientId == patientId
            && a.AppointmentDate.Date == appointmentDate.Date
            && a.Status != AppointmentStatus.Cancelled);
        if (hasConflict)
            return (false, "You already have an appointment on that day.");

        if (!string.IsNullOrWhiteSpace(doctorName))
        {
            var doc = doctorName.Trim();
            var doctorOk = await _db.Doctors.AnyAsync(d =>
                d.ClinicId == clinicId
                && d.Name == doc
                && d.Status != null
                && d.Status.ToLower() == "active");
            if (!doctorOk)
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
