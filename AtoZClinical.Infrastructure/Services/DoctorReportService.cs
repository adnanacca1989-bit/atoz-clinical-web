using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public sealed class DoctorReportService
{
    private readonly ReportingDataService _reporting;
    private readonly DoctorScopeContext _doctorScope;

    public DoctorReportService(ReportingDataService reporting, DoctorScopeContext doctorScope)
    {
        _reporting = reporting;
        _doctorScope = doctorScope;
    }

    public async Task<List<DoctorReportRow>> GetRowsAsync(
        Guid clinicId,
        DateTime fromDate,
        DateTime toDate,
        string? doctorName,
        string? patientName)
    {
        var _db = _reporting.ReadDb;
        var from = fromDate.Date;
        var to = toDate.Date;
        if (from > to)
            (from, to) = (to, from);

        var patients = await _db.Patients.ForClinic(clinicId).Apply(_doctorScope.Filter)
            .Where(p =>
                (p.AppointmentDate.HasValue &&
                 p.AppointmentDate.Value.Date >= from &&
                 p.AppointmentDate.Value.Date <= to) ||
                (!p.AppointmentDate.HasValue &&
                 p.CreatedAt.Date >= from &&
                 p.CreatedAt.Date <= to))
            .ToListAsync();

        if (!string.IsNullOrWhiteSpace(doctorName))
        {
            var doc = doctorName.Trim();
            patients = patients.Where(p => p.DoctorName?.Contains(doc, StringComparison.OrdinalIgnoreCase) == true).ToList();
        }

        if (!string.IsNullOrWhiteSpace(patientName))
        {
            var name = patientName.Trim();
            patients = patients.Where(p =>
                p.FullName.Contains(name, StringComparison.OrdinalIgnoreCase) ||
                p.PatientNo.Contains(name, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        patients = patients
            .OrderBy(p => p.DoctorName)
            .ThenBy(p => p.FullName)
            .ThenBy(p => p.AppointmentDate ?? p.CreatedAt.Date)
            .ToList();

        var invoices = await _db.Invoices.ForClinic(clinicId)
            .Include(i => i.Lines)
            .Apply(_doctorScope.Filter)
            .Where(i => i.InvoiceDate >= from && i.InvoiceDate <= to)
            .ToListAsync();

        var receipts = await _db.CashReceipts.ForClinic(clinicId)
            .Apply(_doctorScope.Filter)
            .Where(r => r.ReceiptDate >= from && r.ReceiptDate <= to)
            .ToListAsync();

        var payments = await _db.CashPayments.ForClinic(clinicId)
            .Apply(_doctorScope.Filter)
            .Where(p => p.PaymentDate >= from && p.PaymentDate <= to)
            .ToListAsync();

        var doctors = await _db.Doctors.ForClinic(clinicId).AsNoTracking().ToListAsync();
        var demographics = new ClinicalDemographicsSyncService(_db);

        var rows = new List<DoctorReportRow>();
        foreach (var p in patients)
        {
            var liveDoctor = demographics.ResolveDoctorFromList(doctors, p.DoctorRecordId, p.DoctorName);
            var displayDoctorName = liveDoctor?.Name ?? p.DoctorName ?? "";
            var displaySpecialty = liveDoctor?.Specialty ?? p.Specialty ?? "";

            var matchedInvoices = invoices.Where(i =>
                MatchesPatient(i.PatientId, i.PatientName, p) &&
                DoctorMatches(i.DoctorName, p.DoctorName, i.DoctorRecordId, p.DoctorRecordId)).ToList();

            var invoiceAmount = matchedInvoices.Sum(i => i.TotalAmount);
            var consultationFee = matchedInvoices
                .SelectMany(i => i.Lines)
                .Where(l => IsConsultationLine(l.ServiceName, l.AccountName))
                .Sum(l => l.LineTotal);

            if (consultationFee == 0 && matchedInvoices.Count > 0)
                consultationFee = matchedInvoices.SelectMany(i => i.Lines).Sum(l => l.LineTotal);

            var cashReceipt = receipts.Where(r =>
                MatchesPatient(r.PatientId, r.PatientName, p) &&
                DoctorMatches(r.DoctorName, p.DoctorName, r.DoctorRecordId, p.DoctorRecordId)).Sum(r => r.Amount);

            var cashPayment = payments.Where(pay =>
                MatchesPatient(pay.PatientId, pay.PayeeName, p) &&
                DoctorMatches(pay.DoctorName, p.DoctorName, pay.DoctorRecordId, p.DoctorRecordId)).Sum(pay => pay.Amount);

            rows.Add(new DoctorReportRow(
                displayDoctorName,
                displaySpecialty,
                p.FullName,
                consultationFee,
                p.Phone ?? "",
                p.AgeYears,
                p.HealthInsuranceName ?? "",
                p.HealthInsuranceNumber ?? "",
                p.Gender ?? "",
                p.City ?? "",
                p.MarriedStatus ?? "",
                p.MotherName ?? "",
                invoiceAmount,
                cashReceipt,
                cashPayment,
                p.VisitNumber ?? "",
                p.AppointmentDate ?? p.CreatedAt.Date,
                p.AppointmentTime));
        }

        return rows;
    }

    private static bool DoctorMatches(string? recordDoctor, string? patientDoctor, Guid? recordDoctorId, Guid? patientDoctorId)
    {
        if (recordDoctorId is Guid left && patientDoctorId is Guid right && left == right)
            return true;

        if (string.IsNullOrWhiteSpace(recordDoctor) || string.IsNullOrWhiteSpace(patientDoctor))
            return string.IsNullOrWhiteSpace(recordDoctor) && string.IsNullOrWhiteSpace(patientDoctor);

        return string.Equals(recordDoctor.Trim(), patientDoctor.Trim(), StringComparison.OrdinalIgnoreCase)
            || DoctorNameMatcher.NamesReferToSameDoctor(recordDoctor, patientDoctor);
    }

    private static bool MatchesPatient(string? barcode, string? name, Patient patient)
    {
        if (!string.IsNullOrWhiteSpace(barcode) &&
            string.Equals(barcode.Trim(), patient.PatientNo, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrWhiteSpace(name))
        {
            var n = name.Trim();
            if (string.Equals(n, patient.FullName, StringComparison.OrdinalIgnoreCase))
                return true;
            if (patient.FullName.Contains(n, StringComparison.OrdinalIgnoreCase) ||
                n.Contains(patient.FullName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsConsultationLine(string? serviceName, string? accountName)
    {
        var text = $"{serviceName} {accountName}".ToLowerInvariant();
        return text.Contains("consult") || text.Contains("visit") || text.Contains("examination");
    }

    public sealed record DoctorReportRow(
        string DoctorName,
        string Specialty,
        string PatientName,
        decimal ConsultationFee,
        string Phone,
        int? Age,
        string HealthInsurance,
        string HealthInsuranceNo,
        string Gender,
        string City,
        string MarriedStatus,
        string MotherName,
        decimal InvoiceBillingAmount,
        decimal CashReceipt,
        decimal CashPayment,
        string VisitNumber,
        DateTime? AppointmentDate,
        TimeSpan? AppointmentTime);
}
