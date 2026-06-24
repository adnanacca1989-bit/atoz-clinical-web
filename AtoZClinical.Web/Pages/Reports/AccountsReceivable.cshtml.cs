using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Web.Pages.Reports;

public class AccountsReceivableModel : PageModel
{
    private readonly ClinicalDbContext _db;
    private readonly ClinicContextService _clinicContext;

    public AccountsReceivableModel(ClinicalDbContext db, ClinicContextService clinicContext)
    {
        _db = db;
        _clinicContext = clinicContext;
    }

    [BindProperty(SupportsGet = true)]
    public DateTime FromDate { get; set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    [BindProperty(SupportsGet = true)]
    public DateTime ToDate { get; set; } = DateTime.Today;

    [BindProperty(SupportsGet = true)]
    public string? PatientName { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? PatientBarcode { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? DoctorName { get; set; }

    public List<ArRow> Results { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync() => await RunAsync();

    public Task<IActionResult> OnPostRunAsync() => RunAsync();

    public IActionResult OnPostClearAsync() => RedirectToPage();

    private async Task<IActionResult> RunAsync()
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();

        var invoices = await _db.Invoices
            .Where(i => i.ClinicId == clinicId && i.InvoiceDate >= FromDate.Date && i.InvoiceDate <= ToDate.Date)
            .OrderByDescending(i => i.InvoiceDate)
            .ToListAsync();

        if (!string.IsNullOrWhiteSpace(PatientName))
            invoices = invoices.Where(i => i.PatientName?.Contains(PatientName, StringComparison.OrdinalIgnoreCase) == true).ToList();
        if (!string.IsNullOrWhiteSpace(PatientBarcode))
            invoices = invoices.Where(i => i.PatientId?.Equals(PatientBarcode.Trim(), StringComparison.OrdinalIgnoreCase) == true).ToList();
        if (!string.IsNullOrWhiteSpace(DoctorName))
            invoices = invoices.Where(i => i.DoctorName?.Contains(DoctorName, StringComparison.OrdinalIgnoreCase) == true).ToList();

        var patients = await _db.Patients
            .Where(p => p.ClinicId == clinicId)
            .AsNoTracking()
            .ToListAsync();

        var receipts = await _db.CashReceipts
            .Where(c => c.ClinicId == clinicId && c.ReceiptDate >= FromDate.Date && c.ReceiptDate <= ToDate.Date)
            .ToListAsync();

        var patientPayments = await _db.CashPayments
            .Where(p => p.ClinicId == clinicId && p.PaymentDate >= FromDate.Date && p.PaymentDate <= ToDate.Date)
            .Where(p => p.PayeeName != null || p.PatientId != null)
            .ToListAsync();

        var allocation = AllocateCredits(invoices, receipts, patientPayments);

        Results = invoices.Select(i =>
        {
            var (cashReceipt, cashPayment) = allocation.TryGetValue(i.InvoiceNo, out var a)
                ? a
                : (0m, 0m);
            var aging = (DateTime.Today - i.InvoiceDate.Date).Days;
            var totalInvoice = i.SubTotal > 0 ? i.SubTotal : i.TotalAmount + i.Discount;
            var endingBalance = i.BalanceDue;
            var status = endingBalance <= 0 ? "Paid" : (i.AmountPaid > 0 || cashReceipt > 0 || cashPayment > 0 ? "Partial" : (i.PaymentStatus ?? "Unpaid"));
            var patient = ResolvePatient(patients, i);

            return new ArRow(
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
                cashReceipt,
                cashPayment,
                totalInvoice,
                i.Discount,
                endingBalance,
                aging,
                status);
        }).ToList();

        return Page();
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

    private static Dictionary<int, (decimal CashReceipt, decimal CashPayment)> AllocateCredits(
        List<Invoice> invoices,
        List<CashReceipt> receipts,
        List<CashPayment> payments)
    {
        var result = invoices.ToDictionary(i => i.InvoiceNo, _ => (CashReceipt: 0m, CashPayment: 0m));
        if (invoices.Count == 0) return result;

        foreach (var group in invoices.GroupBy(i => (
            PatientId: i.PatientId?.Trim() ?? "",
            PatientName: i.PatientName?.Trim() ?? "",
            Doctor: i.DoctorName?.Trim() ?? "")))
        {
            var invs = group.OrderBy(i => i.InvoiceDate).ThenBy(i => i.InvoiceNo).ToList();
            var remainingDue = invs.ToDictionary(i => i.InvoiceNo, i => i.TotalAmount);

            bool MatchDoctor(string? doc) =>
                string.IsNullOrWhiteSpace(group.Key.Doctor) ||
                string.Equals(doc?.Trim(), group.Key.Doctor, StringComparison.OrdinalIgnoreCase);

            var credits = receipts
                .Where(r =>
                    ((!string.IsNullOrWhiteSpace(group.Key.PatientId) && r.PatientId == group.Key.PatientId) ||
                     (!string.IsNullOrWhiteSpace(group.Key.PatientName) && r.PatientName == group.Key.PatientName)) &&
                    MatchDoctor(r.DoctorName))
                .Select(r => new { IsReceipt = true, Date = r.ReceiptDate, Amount = r.Amount, Sort = r.ReceiptNo })
                .Concat(payments
                    .Where(p =>
                        ((!string.IsNullOrWhiteSpace(group.Key.PatientId) && p.PatientId == group.Key.PatientId) ||
                         (!string.IsNullOrWhiteSpace(group.Key.PatientName) && p.PayeeName == group.Key.PatientName)) &&
                        MatchDoctor(p.DoctorName))
                    .Select(p => new { IsReceipt = false, Date = p.PaymentDate, Amount = p.Amount, Sort = p.PaymentNo }))
                .OrderBy(c => c.Date).ThenBy(c => c.Sort)
                .ToList();

            foreach (var credit in credits)
            {
                var remaining = credit.Amount;
                foreach (var inv in invs)
                {
                    if (remaining <= 0) break;
                    var due = remainingDue[inv.InvoiceNo];
                    if (due <= 0) continue;

                    var apply = Math.Min(remaining, due);
                    var current = result[inv.InvoiceNo];
                    result[inv.InvoiceNo] = credit.IsReceipt
                        ? (current.CashReceipt + apply, current.CashPayment)
                        : (current.CashReceipt, current.CashPayment + apply);
                    remainingDue[inv.InvoiceNo] -= apply;
                    remaining -= apply;
                }
            }
        }

        return result;
    }

    public async Task<IActionResult> OnPostExportAsync()
    {
        await RunAsync();
        var bytes = ReportExcelService.Export("Accounts Receivable",
            [
                "Invoice ID", "Invoice Date", "Patient", "Doctor", "Specialty", "Gender", "City",
                "Mother Name", "Married Status", "Health Insurance", "Health Insurance No",
                "Appointment Date", "Appointment Time", "Cash Receipt", "Cash Payment",
                "Total Invoice", "Discount", "Ending Balance", "Aging Days", "Status"
            ],
            Results.Select(r => new object?[]
            {
                r.InvoiceId, r.InvoiceDate, r.Patient, r.Doctor, r.Specialty, r.Gender, r.City,
                r.MotherName, r.MarriedStatus, r.HealthInsurance, r.HealthInsuranceNo,
                r.AppointmentDate?.ToString("d"), FormatTime(r.AppointmentTime),
                r.CashReceipt, r.CashPayment, r.TotalInvoice, r.Discount, r.EndingBalance,
                r.AgingDays, r.Status
            }));
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"AccountsReceivable_{DateTime.Now:yyyyMMdd}.xlsx");
    }

    private static string FormatTime(TimeSpan? time) =>
        time is null ? "" : DateTime.Today.Add(time.Value).ToString("t");

    public sealed record ArRow(
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
        decimal TotalInvoice,
        decimal Discount,
        decimal EndingBalance,
        int AgingDays,
        string Status);
}
