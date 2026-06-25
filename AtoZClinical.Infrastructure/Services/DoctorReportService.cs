using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public sealed class DoctorReportService
{
    private readonly ReportingDataService _reporting;

    public DoctorReportService(ReportingDataService reporting) => _reporting = reporting;

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

        var patients = await _db.Patients
            .AsNoTracking()
            .Where(p => p.ClinicId == clinicId &&
                        p.AppointmentDate.HasValue &&
                        p.AppointmentDate.Value.Date >= from &&
                        p.AppointmentDate.Value.Date <= to)
            .ToListAsync();

        if (!string.IsNullOrWhiteSpace(doctorName))
        {
            var doc = doctorName.Trim();
            patients = patients.Where(p => p.DoctorName?.Contains(doc, StringComparison.OrdinalIgnoreCase) == true).ToList();
        }

        if (!string.IsNullOrWhiteSpace(patientName))
        {
            var name = patientName.Trim();
            patients = patients.Where(p => p.FullName.Contains(name, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        patients = patients
            .OrderBy(p => p.DoctorName)
            .ThenBy(p => p.FullName)
            .ThenBy(p => p.AppointmentDate)
            .ToList();

        var invoices = await _db.Invoices
            .AsNoTracking()
            .Include(i => i.Lines)
            .Where(i => i.ClinicId == clinicId && i.InvoiceDate >= from && i.InvoiceDate <= to)
            .ToListAsync();

        var receipts = await _db.CashReceipts
            .AsNoTracking()
            .Where(r => r.ClinicId == clinicId && r.ReceiptDate >= from && r.ReceiptDate <= to)
            .ToListAsync();

        var payments = await _db.CashPayments
            .AsNoTracking()
            .Where(p => p.ClinicId == clinicId && p.PaymentDate >= from && p.PaymentDate <= to)
            .ToListAsync();

        var rows = new List<DoctorReportRow>();
        foreach (var p in patients)
        {
            var matchedInvoices = invoices.Where(i =>
                MatchesPatient(i.PatientId, i.PatientName, p) &&
                string.Equals(i.DoctorName, p.DoctorName, StringComparison.OrdinalIgnoreCase)).ToList();

            var invoiceAmount = matchedInvoices.Sum(i => i.TotalAmount);
            var consultationFee = matchedInvoices
                .SelectMany(i => i.Lines)
                .Where(l => IsConsultationLine(l.ServiceName, l.AccountName))
                .Sum(l => l.LineTotal);

            if (consultationFee == 0 && matchedInvoices.Count > 0)
                consultationFee = matchedInvoices.SelectMany(i => i.Lines).Sum(l => l.LineTotal);

            var cashReceipt = receipts.Where(r =>
                MatchesPatient(r.PatientId, r.PatientName, p) &&
                string.Equals(r.DoctorName, p.DoctorName, StringComparison.OrdinalIgnoreCase)).Sum(r => r.Amount);

            var cashPayment = payments.Where(pay =>
                (!string.IsNullOrWhiteSpace(pay.PayeeName) &&
                 pay.PayeeName.Contains(p.FullName, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(pay.Description) &&
                 pay.Description.Contains(p.FullName, StringComparison.OrdinalIgnoreCase)))
                .Sum(pay => pay.Amount);

            rows.Add(new DoctorReportRow(
                p.DoctorName ?? "",
                p.Specialty ?? "",
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
                p.AppointmentDate,
                p.AppointmentTime));
        }

        return rows;
    }

    private static bool MatchesPatient(string? barcode, string? name, Patient patient)
    {
        if (!string.IsNullOrWhiteSpace(barcode) &&
            string.Equals(barcode.Trim(), patient.PatientNo, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrWhiteSpace(name) &&
            string.Equals(name.Trim(), patient.FullName, StringComparison.OrdinalIgnoreCase))
            return true;

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
