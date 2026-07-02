using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AtoZClinical.Infrastructure.Services;

public sealed class ArReportService
{
    private const int MaxRows = 2000;

    private readonly ClinicalDbContext _db;
    private readonly DoctorScopeContext _doctorScope;
    private readonly ILogger<ArReportService> _logger;

    public ArReportService(ClinicalDbContext db, DoctorScopeContext doctorScope, ILogger<ArReportService> logger)
    {
        _db = db;
        _doctorScope = doctorScope;
        _logger = logger;
    }

    public async Task<ArReportResult> BuildAsync(
        Guid clinicId,
        DateTime? fromDate,
        DateTime? toDate,
        string? patientName,
        string? patientBarcode,
        string? doctorName)
    {
        var invoiceQuery = ApplyInvoiceFilters(
            _db.Invoices.ForClinic(clinicId).Apply(_doctorScope.Filter).AsNoTracking(),
            fromDate,
            toDate,
            patientName,
            patientBarcode,
            doctorName);

        var invoices = await invoiceQuery
            .OrderByDescending(i => i.InvoiceDate)
            .ThenByDescending(i => i.InvoiceNo)
            .Take(MaxRows)
            .ToListAsync();

        var allInvoicesQuery = _db.Invoices.ForClinic(clinicId).Apply(_doctorScope.Filter).AsNoTracking();
        if (!string.IsNullOrWhiteSpace(patientBarcode))
            allInvoicesQuery = allInvoicesQuery.Where(i => i.PatientId == patientBarcode.Trim());
        else if (!string.IsNullOrWhiteSpace(patientName))
        {
            var term = patientName.Trim();
            allInvoicesQuery = allInvoicesQuery.Where(i => i.PatientName != null && i.PatientName.Contains(term));
        }

        var allInvoices = await allInvoicesQuery.ToListAsync();

        var patientQuery = _db.Patients.ForClinic(clinicId).Apply(_doctorScope.Filter).AsNoTracking();
        if (!string.IsNullOrWhiteSpace(patientBarcode))
            patientQuery = patientQuery.Where(p => p.PatientNo == patientBarcode.Trim());
        else if (!string.IsNullOrWhiteSpace(patientName))
        {
            var term = patientName.Trim();
            patientQuery = patientQuery.Where(p =>
                p.FirstName.Contains(term) || p.LastName.Contains(term));
        }

        var patients = await patientQuery.ToListAsync();
        var specialtyLookup = await DoctorSpecialtyResolver.BuildMapAsync(_db, clinicId);

        var receiptQuery = _db.CashReceipts.ForClinic(clinicId).Apply(_doctorScope.Filter).AsNoTracking();
        var paymentQuery = _db.CashPayments
            .ForClinic(clinicId)
            .Apply(_doctorScope.Filter)
            .AsNoTracking()
            .Where(p => p.PayeeName != null || p.PatientId != null);

        if (!string.IsNullOrWhiteSpace(patientBarcode))
        {
            receiptQuery = receiptQuery.Where(r => r.PatientId == patientBarcode.Trim());
            paymentQuery = paymentQuery.Where(p => p.PatientId == patientBarcode.Trim());
        }
        else if (!string.IsNullOrWhiteSpace(patientName))
        {
            var term = patientName.Trim();
            receiptQuery = receiptQuery.Where(r => r.PatientName != null && r.PatientName.Contains(term));
            paymentQuery = paymentQuery.Where(p => p.PayeeName != null && p.PayeeName.Contains(term));
        }

        if (toDate.HasValue)
        {
            var asOf = toDate.Value.Date.AddDays(1);
            receiptQuery = receiptQuery.Where(r => r.ReceiptDate < asOf);
            paymentQuery = paymentQuery.Where(p => p.PaymentDate < asOf);
        }

        var receipts = await receiptQuery.ToListAsync();
        var payments = await paymentQuery.ToListAsync();

        _logger.LogDebug(
            "AR report clinic {ClinicId}: {InvoiceCount} invoices, {ReceiptCount} receipts, {PaymentCount} payments as of {AsOf}",
            clinicId, invoices.Count, receipts.Count, payments.Count, toDate?.ToString("d") ?? "all");

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
                DoctorSpecialtyResolver.ResolveFromMap(i.DoctorName, i.Specialty ?? patient?.Specialty, specialtyLookup),
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

    private static IQueryable<Invoice> ApplyInvoiceFilters(
        IQueryable<Invoice> query,
        DateTime? fromDate,
        DateTime? toDate,
        string? patientName,
        string? patientBarcode,
        string? doctorName)
    {
        if (fromDate.HasValue)
            query = query.Where(i => i.InvoiceDate >= fromDate.Value.Date);
        if (toDate.HasValue)
            query = query.Where(i => i.InvoiceDate <= toDate.Value.Date);
        if (!string.IsNullOrWhiteSpace(patientBarcode))
            query = query.Where(i => i.PatientId == patientBarcode.Trim());
        if (!string.IsNullOrWhiteSpace(patientName))
        {
            var term = patientName.Trim();
            query = query.Where(i => i.PatientName != null && i.PatientName.Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(doctorName))
        {
            var term = doctorName.Trim();
            query = query.Where(i => i.DoctorName != null && i.DoctorName.Contains(term));
        }

        return query;
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

    public static decimal ResolvePatientDoctorEndingBalance(
        IReadOnlyList<ArReportRow> rows, string patientName, string doctorName)
    {
        var group = rows.Where(r =>
            string.Equals(r.Patient, patientName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.Doctor, doctorName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (group.Count == 0) return 0m;

        var debits = group.Where(x => x.EndingBalance > 0).ToList();
        if (debits.Count > 0) return debits.Sum(x => x.EndingBalance);

        return group.FirstOrDefault(x => x.EndingBalance < 0)?.EndingBalance ?? 0m;
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
