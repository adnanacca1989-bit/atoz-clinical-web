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
        if (!string.IsNullOrWhiteSpace(DoctorName))
            invoices = invoices.Where(i => i.DoctorName?.Contains(DoctorName, StringComparison.OrdinalIgnoreCase) == true).ToList();

        var receipts = await _db.CashReceipts.Where(c => c.ClinicId == clinicId).ToListAsync();

        Results = invoices.Select(i =>
        {
            var cashPaid = receipts.Where(r => r.PatientName == i.PatientName).Sum(r => r.Amount);
            var aging = (DateTime.Today - i.InvoiceDate.Date).Days;
            return new ArRow(
                i.InvoiceNo,
                i.InvoiceDate,
                i.PatientName ?? "",
                i.DoctorName ?? "",
                i.TotalAmount,
                i.AmountPaid,
                cashPaid,
                i.BalanceDue,
                aging,
                i.PaymentStatus ?? (i.BalanceDue > 0 ? "Unpaid" : "Paid"));
        }).ToList();

        return Page();
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
