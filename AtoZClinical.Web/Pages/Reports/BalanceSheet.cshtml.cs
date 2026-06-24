using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Web.Pages.Reports;

public class BalanceSheetModel : PageModel
{
    private readonly ClinicalDbContext _db;
    private readonly ClinicContextService _clinicContext;
    private readonly PharmacyCogsService _cogs;

    public BalanceSheetModel(ClinicalDbContext db, ClinicContextService clinicContext, PharmacyCogsService cogs)
    {
        _db = db;
        _clinicContext = clinicContext;
        _cogs = cogs;
    }

    [BindProperty(SupportsGet = true)]
    public DateTime FromDate { get; set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    [BindProperty(SupportsGet = true)]
    public DateTime ToDate { get; set; } = DateTime.Today;

    public List<BsRow> Assets { get; private set; } = [];
    public List<BsRow> Liabilities { get; private set; } = [];
    public List<BsRow> Equity { get; private set; } = [];
    public decimal TotalAssets { get; private set; }
    public decimal TotalLiabilities { get; private set; }
    public decimal TotalEquity { get; private set; }

    public async Task<IActionResult> OnGetAsync() => await RunAsync();

    public Task<IActionResult> OnPostRunAsync() => RunAsync();

    public IActionResult OnPostClearAsync() => RedirectToPage();

    private async Task<IActionResult> RunAsync()
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();

        var asOf = ToDate.Date;
        var periodStart = FromDate.Date;

        var cashReceipts = await _db.CashReceipts
            .Where(c => c.ClinicId == clinicId && c.ReceiptDate <= asOf)
            .SumAsync(c => c.Amount);
        var cashPayments = await _db.CashPayments
            .Where(c => c.ClinicId == clinicId && c.PaymentDate <= asOf)
            .SumAsync(c => c.Amount);
        var cashOnHand = cashReceipts - cashPayments;

        var accountsReceivable = await _db.Invoices
            .Where(i => i.ClinicId == clinicId && i.InvoiceDate <= asOf)
            .SumAsync(i => i.BalanceDue);

        var inventoryValue = await _db.PharmacyItems
            .Where(p => p.ClinicId == clinicId && p.IsActive)
            .SumAsync(p => p.QuantityOnHand * p.MovingAverageCost);

        var accountsPayable = await _db.PharmacyPurchaseBills
            .Where(b => b.ClinicId == clinicId && b.PurchaseDate <= asOf)
            .SumAsync(b => b.BalanceDue);

        var invoiceRevenue = await _db.Invoices
            .Where(i => i.ClinicId == clinicId && i.InvoiceDate <= asOf)
            .SumAsync(i => i.TotalAmount);
        var pharmacyRevenue = await _db.PharmacyBills
            .Where(b => b.ClinicId == clinicId && b.BillDate <= asOf)
            .SumAsync(b => b.TotalAmount);
        var totalRevenue = invoiceRevenue + pharmacyRevenue;

        var totalCogs = await _cogs.GetTotalCogsAsync(clinicId.Value, DateTime.MinValue, asOf, null, null);
        var operatingExpenses = await _db.CashPayments
            .Where(p => p.ClinicId == clinicId && p.PaymentDate <= asOf)
            .SumAsync(p => p.Amount);

        var retainedEarnings = totalRevenue - totalCogs - operatingExpenses;

        Assets =
        [
            new BsRow("Cash on Hand", cashOnHand),
            new BsRow("Accounts Receivable", accountsReceivable),
            new BsRow("Pharmacy Inventory", inventoryValue)
        ];

        Liabilities = [new BsRow("Accounts Payable", accountsPayable)];

        Equity =
        [
            new BsRow("Retained Earnings", retainedEarnings)
        ];

        TotalAssets = Assets.Sum(r => r.Amount);
        TotalLiabilities = Liabilities.Sum(r => r.Amount);
        TotalEquity = Equity.Sum(r => r.Amount);

        return Page();
    }

    public async Task<IActionResult> OnPostExportAsync()
    {
        await RunAsync();
        var rows = Assets.Select(r => new object?[] { "Asset", r.Account, r.Amount })
            .Concat(Liabilities.Select(r => new object?[] { "Liability", r.Account, r.Amount }))
            .Concat(Equity.Select(r => new object?[] { "Equity", r.Account, r.Amount }));
        var bytes = ReportExcelService.Export("Balance Sheet",
            ["Section", "Account", "Amount"],
            rows);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"BalanceSheet_{DateTime.Now:yyyyMMdd}.xlsx");
    }

    public sealed record BsRow(string Account, decimal Amount);
}
