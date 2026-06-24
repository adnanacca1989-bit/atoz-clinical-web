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

        var receipts = await _db.CashReceipts
            .Where(c => c.ClinicId == clinicId && c.ReceiptDate >= FromDate.Date && c.ReceiptDate <= ToDate.Date)
            .ToListAsync();

        var patientPayments = await _db.CashPayments
            .Where(p => p.ClinicId == clinicId && p.PaymentDate >= FromDate.Date && p.PaymentDate <= ToDate.Date)
            .Where(p => p.PayeeName != null || p.PatientId != null)
            .ToListAsync();

        Results = invoices.Select(i =>
        {
            bool MatchesPatient(CashReceipt r) =>
                (!string.IsNullOrWhiteSpace(i.PatientId) && r.PatientId == i.PatientId) ||
                r.PatientName == i.PatientName;
            bool MatchesDoctor(string? doc) =>
                string.IsNullOrWhiteSpace(i.DoctorName) ||
                string.Equals(doc?.Trim(), i.DoctorName.Trim(), StringComparison.OrdinalIgnoreCase);

            var cashPaid = receipts.Where(r => MatchesPatient(r) && MatchesDoctor(r.DoctorName)).Sum(r => r.Amount);
            cashPaid += patientPayments.Where(p =>
                    ((!string.IsNullOrWhiteSpace(i.PatientId) && p.PatientId == i.PatientId) ||
                     p.PayeeName == i.PatientName) &&
                    MatchesDoctor(p.DoctorName))
                .Sum(p => p.Amount);
            var aging = (DateTime.Today - i.InvoiceDate.Date).Days;
            var balance = i.BalanceDue;
            var status = balance <= 0 ? "Paid" : (i.AmountPaid > 0 || cashPaid > 0 ? "Partial" : (i.PaymentStatus ?? "Unpaid"));
            return new ArRow(
                i.InvoiceNo,
                i.InvoiceDate,
                i.PatientName ?? "",
                i.DoctorName ?? "",
                i.TotalAmount,
                i.AmountPaid,
                cashPaid,
                balance,
                aging,
                status);
        }).ToList();

        return Page();
    }

    public async Task<IActionResult> OnPostExportAsync()
    {
        await RunAsync();
        var bytes = ReportExcelService.Export("Accounts Receivable",
            ["Invoice ID", "Invoice Date", "Patient", "Doctor", "Debit", "Credit", "Cash Paid", "Balance", "Aging Days", "Status"],
            Results.Select(r => new object?[]
            {
                r.InvoiceId, r.InvoiceDate, r.Patient, r.Doctor, r.Debit, r.Credit, r.CashPaid, r.Balance, r.AgingDays, r.Status
            }));
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"AccountsReceivable_{DateTime.Now:yyyyMMdd}.xlsx");
    }

    public sealed record ArRow(
        int InvoiceId,
        DateTime InvoiceDate,
        string Patient,
        string Doctor,
        decimal Debit,
        decimal Credit,
        decimal CashPaid,
        decimal Balance,
        int AgingDays,
        string Status);
}
