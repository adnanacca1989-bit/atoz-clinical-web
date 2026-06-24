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

    public BalanceSheetModel(ClinicalDbContext db, ClinicContextService clinicContext)
    {
        _db = db;
        _clinicContext = clinicContext;
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

        var id = clinicId.Value;
        var asOf = ToDate.Date;

        var cashReceipts = await _db.CashReceipts
            .Where(c => c.ClinicId == id && c.ReceiptDate <= asOf)
            .SumAsync(c => (decimal?)c.Amount) ?? 0m;

        var cashPayments = await _db.CashPayments
            .Where(c => c.ClinicId == id && c.PaymentDate <= asOf)
            .SumAsync(c => (decimal?)c.Amount) ?? 0m;

        var cashOnHand = cashReceipts - cashPayments;

        var accountsReceivable = await _db.Invoices
            .Where(i => i.ClinicId == id && i.InvoiceDate <= asOf)
            .SumAsync(i => (decimal?)i.BalanceDue) ?? 0m;

        var inventoryValue = await _db.PharmacyItems
            .Where(p => p.ClinicId == id && p.IsActive)
            .SumAsync(p => (decimal?)(p.QuantityOnHand * p.MovingAverageCost)) ?? 0m;

        var accountsPayable = await _db.PharmacyPurchaseBills
            .Where(b => b.ClinicId == id && b.PurchaseDate <= asOf)
            .SumAsync(b => (decimal?)b.BalanceDue) ?? 0m;

        Assets =
        [
            new BsRow("Cash on Hand", cashOnHand),
            new BsRow("Accounts Receivable", accountsReceivable),
            new BsRow("Pharmacy Inventory", inventoryValue)
        ];

        Liabilities = [new BsRow("Accounts Payable", accountsPayable)];

        TotalAssets = Assets.Sum(r => r.Amount);
        TotalLiabilities = Liabilities.Sum(r => r.Amount);
        var retainedEarnings = TotalAssets - TotalLiabilities;

        Equity = [new BsRow("Retained Earnings", retainedEarnings)];
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
