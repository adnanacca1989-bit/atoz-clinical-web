using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public sealed class ArReportService
{
    private readonly ClinicalDbContext _db;

    public ArReportService(ClinicalDbContext db) => _db = db;

    public async Task<ArReportResult> BuildAsync(
        Guid clinicId,
        DateTime? fromDate,
        DateTime? toDate,
        string? patientName,
        string? patientBarcode,
        string? doctorName)
    {
        var invoices = await _db.Invoices
            .ForClinic(clinicId)
            .AsNoTracking()
            .OrderByDescending(i => i.InvoiceDate)
            .ThenByDescending(i => i.InvoiceNo)
            .ToListAsync();

        if (fromDate.HasValue)
            invoices = invoices.Where(i => i.InvoiceDate.Date >= fromDate.Value.Date).ToList();
        if (toDate.HasValue)
            invoices = invoices.Where(i => i.InvoiceDate.Date <= toDate.Value.Date).ToList();
        if (!string.IsNullOrWhiteSpace(patientName))
            invoices = invoices.Where(i => i.PatientName?.Contains(patientName.Trim(), StringComparison.OrdinalIgnoreCase) == true).ToList();
        if (!string.IsNullOrWhiteSpace(patientBarcode))
            invoices = invoices.Where(i => i.PatientId?.Equals(patientBarcode.Trim(), StringComparison.OrdinalIgnoreCase) == true).ToList();
        if (!string.IsNullOrWhiteSpace(doctorName))
            invoices = invoices.Where(i => i.DoctorName?.Contains(doctorName.Trim(), StringComparison.OrdinalIgnoreCase) == true).ToList();

        var allInvoices = await _db.Invoices.ForClinic(clinicId).AsNoTracking().ToListAsync();
        var patients = await _db.Patients.ForClinic(clinicId).AsNoTracking().ToListAsync();
        var receipts = await _db.CashReceipts.ForClinic(clinicId).AsNoTracking().ToListAsync();
        var payments = await _db.CashPayments
            .ForClinic(clinicId)
            .AsNoTracking()
            .Where(p => p.PayeeName != null || p.PatientId != null)
            .ToListAsync();

        var rows = invoices.Select(i =>
        {
            var totals = InvoiceArCalculator.ForInvoice(i, receipts, payments, allInvoices);
            var aging = (DateTime.Today - i.InvoiceDate.Date).Days;
            var endingBalance = totals.EndingBalance;
            var status = endingBalance < 0
                ? "Credit"
                : endingBalance > 0
                    ? (totals.AmountApplied > 0 ? "Partial" : (i.PaymentStatus ?? "Unpaid"))
                    : "Paid";
            var patient = ResolvePatient(patients, i);

            return new ArReportRow(
                i.InvoiceNo,
                i.InvoiceDate,
                i.PatientName ?? "",
                i.DoctorName ?? "",
                i.Specialty ?? patient?.Specialty ?? "",
                i.Gender ?? patient?.Gender ?? "",
                i.City ?? patient?.City ?? "",
                patient?.MotherName ?? "",
                patient?.MarriedStatus ?? "",
                patient?.HealthInsuranceName ?? "",
                patient?.HealthInsuranceNumber ?? "",
                patient?.AppointmentDate,
                patient?.AppointmentTime,
                totals.CashReceipt,
                totals.CashPayment,
                totals.TotalReceived,
                totals.PatientCredit,
                totals.TotalInvoice,
                totals.Discount,
                endingBalance,
                aging,
                status);
        }).ToList();

        return new ArReportResult(
            rows,
            rows.Sum(r => r.CashReceipt),
            rows.Sum(r => r.CashPayment),
            rows.Sum(r => r.TotalInvoice),
            rows.Sum(r => r.Discount),
            rows.GroupBy(r => (r.Patient, r.Doctor))
                .Sum(g =>
                {
                    var debits = g.Where(x => x.EndingBalance > 0).ToList();
                    if (debits.Count > 0) return debits.Sum(x => x.EndingBalance);
                    var credit = g.FirstOrDefault(x => x.EndingBalance < 0);
                    return credit?.EndingBalance ?? 0m;
                }),
            rows.GroupBy(r => (r.Patient, r.Doctor)).Sum(g => g.First().PatientCredit));
    }

    private static Patient? ResolvePatient(List<Patient> patients, Invoice invoice)
    {
        if (!string.IsNullOrWhiteSpace(invoice.PatientId))
        {
            var byId = patients.FirstOrDefault(p =>
                string.Equals(p.PatientNo, invoice.PatientId, StringComparison.OrdinalIgnoreCase));
            if (byId is not null) return byId;
        }

        if (!string.IsNullOrWhiteSpace(invoice.PatientName))
        {
            return patients.FirstOrDefault(p =>
                string.Equals(p.FullName, invoice.PatientName, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }
}

public sealed record ArReportRow(
    int InvoiceId,
    DateTime InvoiceDate,
    string Patient,
    string Doctor,
    string Specialty,
    string Gender,
    string City,
    string MotherName,
    string MarriedStatus,
    string HealthInsurance,
    string HealthInsuranceNo,
    DateTime? AppointmentDate,
    TimeSpan? AppointmentTime,
    decimal CashReceipt,
    decimal CashPayment,
    decimal TotalReceived,
    decimal PatientCredit,
    decimal TotalInvoice,
    decimal Discount,
    decimal EndingBalance,
    int AgingDays,
    string Status);

public sealed record ArReportResult(
    List<ArReportRow> Rows,
    decimal TotalCashReceipt,
    decimal TotalCashPayment,
    decimal TotalInvoiceAmount,
    decimal TotalDiscount,
    decimal TotalEndingBalance,
    decimal TotalPatientCredit);
