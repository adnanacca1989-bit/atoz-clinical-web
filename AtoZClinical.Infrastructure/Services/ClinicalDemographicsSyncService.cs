using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

/// <summary>
/// Resolves live patient/doctor master records and applies demographics to transactional rows.
/// </summary>
public sealed class ClinicalDemographicsSyncService
{
    private readonly ClinicalDbContext _db;

    public ClinicalDemographicsSyncService(ClinicalDbContext db) => _db = db;

    public async Task<Patient?> ResolvePatientAsync(
        Guid clinicId,
        Guid? recordId,
        string? patientNo,
        string? patientName)
    {
        if (recordId is Guid id && id != Guid.Empty)
        {
            var byId = await _db.Patients.ForClinic(clinicId).AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id);
            if (byId is not null) return byId;
        }

        if (!string.IsNullOrWhiteSpace(patientNo))
        {
            var no = patientNo.Trim();
            var byNo = await _db.Patients.ForClinic(clinicId).AsNoTracking()
                .FirstOrDefaultAsync(p => p.PatientNo == no);
            if (byNo is not null) return byNo;
        }

        if (string.IsNullOrWhiteSpace(patientName)) return null;
        var name = patientName.Trim();
        var candidates = await _db.Patients.ForClinic(clinicId).AsNoTracking().ToListAsync();
        return candidates.FirstOrDefault(p =>
                   string.Equals(p.FullName, name, StringComparison.OrdinalIgnoreCase))
               ?? candidates.FirstOrDefault(p =>
                   p.FullName.Contains(name, StringComparison.OrdinalIgnoreCase) ||
                   name.Contains(p.FullName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<Doctor?> ResolveDoctorAsync(Guid clinicId, Guid? recordId, string? doctorName)
    {
        if (recordId is Guid id && id != Guid.Empty)
        {
            var byId = await _db.Doctors.ForClinic(clinicId).AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == id);
            if (byId is not null) return byId;
        }

        if (string.IsNullOrWhiteSpace(doctorName)) return null;
        var name = doctorName.Trim();
        var doctors = await _db.Doctors.ForClinic(clinicId).AsNoTracking().ToListAsync();
        return doctors.FirstOrDefault(d =>
                   string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase))
               ?? doctors.FirstOrDefault(d =>
                   d.Name.Contains(name, StringComparison.OrdinalIgnoreCase) ||
                   name.Contains(d.Name, StringComparison.OrdinalIgnoreCase));
    }

    public void ApplyPatientToInvoice(Patient patient, Invoice invoice)
    {
        invoice.PatientRecordId = patient.Id;
        invoice.PatientId = patient.PatientNo;
        invoice.PatientName = patient.FullName;
        invoice.Phone = patient.Phone;
        invoice.Age = patient.AgeYears;
        invoice.Gender = patient.Gender;
        invoice.City = patient.City;
        invoice.DoctorName = patient.DoctorName;
        invoice.DoctorRecordId = patient.DoctorRecordId;
        invoice.Specialty = patient.Specialty;
    }

    public async Task ApplyDoctorToInvoiceAsync(Guid clinicId, Doctor doctor, Invoice invoice)
    {
        invoice.DoctorRecordId = doctor.Id;
        invoice.DoctorName = doctor.Name;
        invoice.Specialty = doctor.Specialty;
        if (string.IsNullOrWhiteSpace(invoice.DoctorName))
            return;

        var patient = await ResolvePatientAsync(clinicId, invoice.PatientRecordId, invoice.PatientId, invoice.PatientName);
        if (patient is not null && string.Equals(patient.DoctorName, doctor.Name, StringComparison.OrdinalIgnoreCase))
            invoice.Specialty = patient.Specialty ?? doctor.Specialty;
    }

    public async Task<bool> RefreshInvoiceFromMastersAsync(
        Guid clinicId,
        Invoice invoice,
        IList<InvoiceLine> lines,
        bool persist)
    {
        var changed = false;
        var patient = await ResolvePatientAsync(clinicId, invoice.PatientRecordId, invoice.PatientId, invoice.PatientName);
        if (patient is not null)
        {
            if (invoice.PatientRecordId != patient.Id ||
                !string.Equals(invoice.PatientId, patient.PatientNo, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(invoice.PatientName, patient.FullName, StringComparison.OrdinalIgnoreCase) ||
                invoice.Phone != patient.Phone ||
                invoice.Age != patient.AgeYears ||
                invoice.Gender != patient.Gender ||
                invoice.City != patient.City ||
                !string.Equals(invoice.DoctorName, patient.DoctorName, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(invoice.Specialty, patient.Specialty, StringComparison.OrdinalIgnoreCase))
            {
                ApplyPatientToInvoice(patient, invoice);
                changed = true;
            }
        }

        var doctor = await ResolveDoctorAsync(
            clinicId,
            invoice.DoctorRecordId,
            invoice.DoctorName ?? patient?.DoctorName);
        if (doctor is not null)
        {
            if (invoice.DoctorRecordId != doctor.Id ||
                !string.Equals(invoice.DoctorName, doctor.Name, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(invoice.Specialty, doctor.Specialty, StringComparison.OrdinalIgnoreCase))
            {
                invoice.DoctorRecordId = doctor.Id;
                invoice.DoctorName = doctor.Name;
                invoice.Specialty = doctor.Specialty ?? invoice.Specialty;
                changed = true;
            }

            foreach (var line in lines.Where(l => IsConsultationLine(l.ServiceName, doctor.Name)))
            {
                var expectedName = $"Consultation Fee - {doctor.Name}";
                if (!string.Equals(line.ServiceName, expectedName, StringComparison.OrdinalIgnoreCase))
                {
                    line.ServiceName = expectedName;
                    changed = true;
                }

                if (line.UnitFee != doctor.ConsultationFee)
                {
                    line.UnitFee = doctor.ConsultationFee;
                    line.LineTotal = line.Qty * line.UnitFee;
                    changed = true;
                }
            }
        }

        if (!changed) return false;

        invoice.SubTotal = lines.Sum(l => l.LineTotal);
        invoice.TotalAmount = invoice.SubTotal - invoice.Discount + invoice.TaxAmount;
        invoice.BalanceDue = invoice.TotalAmount - invoice.AmountPaid;
        invoice.UpdatedAt = DateTime.UtcNow;

        if (persist)
            await _db.SaveChangesAsync();

        return true;
    }

    public async Task LinkInvoiceMastersAsync(Guid clinicId, Invoice invoice)
    {
        var patient = await ResolvePatientAsync(clinicId, invoice.PatientRecordId, invoice.PatientId, invoice.PatientName);
        if (patient is not null)
            ApplyPatientToInvoice(patient, invoice);

        var doctor = await ResolveDoctorAsync(clinicId, invoice.DoctorRecordId, invoice.DoctorName ?? patient?.DoctorName);
        if (doctor is not null)
            await ApplyDoctorToInvoiceAsync(clinicId, doctor, invoice);
    }

    public Doctor? ResolveDoctorFromList(IReadOnlyList<Doctor> doctors, Guid? recordId, string? doctorName) =>
        DoctorNameMatcher.ResolveSingleDoctor(doctors, recordId, doctorName);

    public async Task LinkPatientDoctorAsync(Guid clinicId, Patient patient)
    {
        var doctor = await ResolveDoctorAsync(clinicId, patient.DoctorRecordId, patient.DoctorName);
        if (doctor is null)
            return;

        patient.DoctorRecordId = doctor.Id;
        patient.DoctorName = doctor.Name;
        patient.Specialty = doctor.Specialty ?? patient.Specialty;
    }

    public async Task LinkDoctorFieldsAsync(Guid clinicId, Guid? doctorRecordId, string? doctorName, Action<Doctor> apply)
    {
        var doctor = await ResolveDoctorAsync(clinicId, doctorRecordId, doctorName);
        if (doctor is null)
            return;

        apply(doctor);
    }

    public static bool IsConsultationLine(string? serviceName, string doctorName) =>
        !string.IsNullOrWhiteSpace(serviceName) &&
        (serviceName.Contains("Consultation", StringComparison.OrdinalIgnoreCase) ||
         serviceName.Contains(doctorName, StringComparison.OrdinalIgnoreCase));
}
