using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Web.Pages.Reports;

public class OperatingReportModel : PageModel
{
    private readonly ClinicalDbContext _db;
    private readonly ClinicContextService _clinicContext;

    public OperatingReportModel(ClinicalDbContext db, ClinicContextService clinicContext)
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

    [BindProperty(SupportsGet = true)]
    public string InvoiceStatus { get; set; } = "All";

    public List<OpRow> Results { get; private set; } = [];
    public int InvoiceCount { get; private set; }
    public decimal NetTotal { get; private set; }
    public decimal CashReceived { get; private set; }
    public decimal CashPaid { get; private set; }
    public decimal BalanceTotal { get; private set; }

    public async Task<IActionResult> OnGetAsync() => await RunAsync();

    public Task<IActionResult> OnPostRunAsync() => RunAsync();

    public IActionResult OnPostClearAsync() => RedirectToPage();

    private async Task<IActionResult> RunAsync()
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();

        var invoices = await _db.Invoices
            .Where(i => i.ClinicId == clinicId && i.InvoiceDate >= FromDate.Date && i.InvoiceDate <= ToDate.Date)
            .ToListAsync();

        if (!string.IsNullOrWhiteSpace(PatientName))
            invoices = invoices.Where(i => i.PatientName?.Contains(PatientName, StringComparison.OrdinalIgnoreCase) == true).ToList();
        if (!string.IsNullOrWhiteSpace(PatientBarcode))
            invoices = invoices.Where(i => i.PatientId?.Equals(PatientBarcode.Trim(), StringComparison.OrdinalIgnoreCase) == true).ToList();
        if (!string.IsNullOrWhiteSpace(DoctorName))
            invoices = invoices.Where(i => i.DoctorName?.Contains(DoctorName, StringComparison.OrdinalIgnoreCase) == true).ToList();
        if (InvoiceStatus != "All")
            invoices = invoices.Where(i => string.Equals(i.PaymentStatus, InvoiceStatus, StringComparison.OrdinalIgnoreCase)).ToList();

        var receipts = await _db.CashReceipts
            .Where(c => c.ClinicId == clinicId && c.ReceiptDate >= FromDate.Date && c.ReceiptDate <= ToDate.Date)
            .ToListAsync();

        var payments = await _db.CashPayments
            .Where(p => p.ClinicId == clinicId && p.PaymentDate >= FromDate.Date && p.PaymentDate <= ToDate.Date)
            .ToListAsync();

        Results = invoices.Select(i =>
        {
            var received = receipts.Where(r => r.PatientName == i.PatientName).Sum(r => r.Amount);
            var paid = payments.Where(p => p.PayeeName == i.PatientName).Sum(p => p.Amount);
            return new OpRow(
                i.PatientName ?? "",
                i.DoctorName ?? "",
                i.Specialty ?? "",
                received,
                paid,
                i.TotalAmount - i.Discount,
                i.BalanceDue,
                i.PaymentStatus ?? "Unpaid");
        }).ToList();

        InvoiceCount = Results.Count;
        NetTotal = Results.Sum(r => r.InvoiceNetAmount);
        CashReceived = Results.Sum(r => r.CashReceived);
        CashPaid = Results.Sum(r => r.CashPaid);
        BalanceTotal = Results.Sum(r => r.EndingBalance);
        return Page();
    }

    public async Task<IActionResult> OnPostExportAsync()
    {
        await RunAsync();
        var bytes = ReportExcelService.Export("Operating Report",
            ["Patient", "Doctor", "Specialty", "Cash Received", "Cash Paid", "Invoice Net", "Balance", "Status"],
            Results.Select(r => new object?[]
            {
                r.Patient, r.DoctorName, r.DoctorSpecialty, r.CashReceived, r.CashPaid, r.InvoiceNetAmount, r.EndingBalance, r.InvoiceStatus
            }));
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"OperatingReport_{DateTime.Now:yyyyMMdd}.xlsx");
    }

    public sealed record OpRow(
        string Patient,
        string DoctorName,
        string DoctorSpecialty,
        decimal CashReceived,
        decimal CashPaid,
        decimal InvoiceNetAmount,
        decimal EndingBalance,
        string InvoiceStatus);
}
