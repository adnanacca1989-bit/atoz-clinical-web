using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Web.Pages.Reports;

public class AccountsPayableModel : PageModel
{
    private readonly ClinicalDbContext _db;
    private readonly ClinicContextService _clinicContext;

    public AccountsPayableModel(ClinicalDbContext db, ClinicContextService clinicContext)
    {
        _db = db;
        _clinicContext = clinicContext;
    }

    [BindProperty(SupportsGet = true)]
    public DateTime FromDate { get; set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    [BindProperty(SupportsGet = true)]
    public DateTime ToDate { get; set; } = DateTime.Today;

    [BindProperty(SupportsGet = true)]
    public string? SupplierName { get; set; }

    public List<ApRow> Results { get; private set; } = [];
    public decimal TotalNet { get; private set; }
    public decimal TotalPaid { get; private set; }
    public decimal TotalBalance { get; private set; }

    public async Task<IActionResult> OnGetAsync() => await RunAsync();

    public Task<IActionResult> OnPostRunAsync() => RunAsync();

    public IActionResult OnPostClearAsync() => RedirectToPage();

    private async Task<IActionResult> RunAsync()
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();

        var bills = await _db.PharmacyPurchaseBills
            .Where(b => b.ClinicId == clinicId && b.PurchaseDate >= FromDate.Date && b.PurchaseDate <= ToDate.Date)
            .OrderByDescending(b => b.PurchaseDate)
            .ToListAsync();

        if (!string.IsNullOrWhiteSpace(SupplierName))
            bills = bills.Where(b => b.SupplierName?.Contains(SupplierName, StringComparison.OrdinalIgnoreCase) == true).ToList();

        Results = bills.Select(b =>
        {
            var aging = (DateTime.Today - b.PurchaseDate.Date).Days;
            var status = b.BalanceDue <= 0 ? "Paid" : (b.AmountPaid > 0 ? "Partial" : (b.PaymentStatus ?? "Unpaid"));
            return new ApRow(
                b.PurchaseNo,
                b.PurchaseDate,
                b.SupplierName ?? "",
                b.SupplierInvoiceNo ?? "",
                b.NetAmount,
                b.AmountPaid,
                b.BalanceDue,
                aging,
                status);
        }).ToList();

        TotalNet = Results.Sum(r => r.NetAmount);
        TotalPaid = Results.Sum(r => r.AmountPaid);
        TotalBalance = Results.Sum(r => r.Balance);
        return Page();
    }

    public async Task<IActionResult> OnPostExportAsync()
    {
        await RunAsync();
        var bytes = ReportExcelService.Export("Accounts Payable",
            ["Purchase No", "Date", "Supplier", "Supplier Invoice", "Net Amount", "Paid", "Balance", "Aging Days", "Status"],
            Results.Select(r => new object?[]
            {
                r.PurchaseNo, r.PurchaseDate, r.Supplier, r.SupplierInvoice, r.NetAmount, r.AmountPaid, r.Balance, r.AgingDays, r.Status
            }));
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"AccountsPayable_{DateTime.Now:yyyyMMdd}.xlsx");
    }

    public sealed record ApRow(
        int PurchaseNo,
        DateTime PurchaseDate,
        string Supplier,
        string SupplierInvoice,
        decimal NetAmount,
        decimal AmountPaid,
        decimal Balance,
        int AgingDays,
        string Status);
}
