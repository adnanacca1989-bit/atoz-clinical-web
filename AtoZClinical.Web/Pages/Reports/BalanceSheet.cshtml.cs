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
    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync() => await RunAsync();
    public Task<IActionResult> OnPostRunAsync() => RunAsync();
    public IActionResult OnPostClearAsync() => RedirectToPage();

    private async Task<IActionResult> RunAsync()
    {
        try
        {
            var clinicId = await _clinicContext.GetClinicIdAsync();
            if (clinicId is null) return Forbid();

            var id = clinicId.Value;
            var asOf = ToDate.Date;

            var cashReceipts = await SafeSumAsync(() => SumCashReceiptsAsync(id, asOf));
            var cashPayments = await SafeSumAsync(() => SumCashPaymentsAsync(id, asOf));
            var cashOnHand = cashReceipts - cashPayments;

            var accountsReceivable = await SafeSumAsync(() => SumAccountsReceivableAsync(id, asOf));
            var inventoryValue = await SafeSumAsync(() => SumInventoryValueAsync(id));
            var accountsPayable = await SafeSumAsync(() => SumAccountsPayableAsync(id, asOf));

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
            ErrorMessage = null;
        }
        catch
        {
            ErrorMessage = "Could not load balance sheet data. Please try again or contact support.";
            Assets = [];
            Liabilities = [];
            Equity = [];
            TotalAssets = TotalLiabilities = TotalEquity = 0;
        }

        return Page();
    }

    private static async Task<decimal> SafeSumAsync(Func<Task<decimal>> sum)
    {
        try
        {
            return await sum();
        }
        catch
        {
            return 0m;
        }
    }

    private async Task<decimal> SumCashReceiptsAsync(Guid clinicId, DateTime asOf)
    {
        var rows = await _db.CashReceipts.AsNoTracking()
            .Where(c => c.ClinicId == clinicId)
            .Select(c => new { c.ReceiptDate, c.Amount })
            .ToListAsync();
        return rows.Where(c => c.ReceiptDate.Date <= asOf).Sum(c => c.Amount);
    }

    private async Task<decimal> SumCashPaymentsAsync(Guid clinicId, DateTime asOf)
    {
        var rows = await _db.CashPayments.AsNoTracking()
            .Where(c => c.ClinicId == clinicId)
            .Select(c => new { c.PaymentDate, c.Amount })
            .ToListAsync();
        return rows.Where(c => c.PaymentDate.Date <= asOf).Sum(c => c.Amount);
    }

    private async Task<decimal> SumAccountsReceivableAsync(Guid clinicId, DateTime asOf)
    {
        var rows = await _db.Invoices.AsNoTracking()
            .Where(i => i.ClinicId == clinicId)
            .Select(i => new { i.InvoiceDate, i.BalanceDue })
            .ToListAsync();
        return rows.Where(i => i.InvoiceDate.Date <= asOf).Sum(i => i.BalanceDue);
    }

    private async Task<decimal> SumInventoryValueAsync(Guid clinicId)
    {
        var items = await _db.PharmacyItems.AsNoTracking()
            .Where(p => p.ClinicId == clinicId)
            .Select(p => new { p.QuantityOnHand, p.MovingAverageCost })
            .ToListAsync();
        return items.Sum(p => p.QuantityOnHand * p.MovingAverageCost);
    }

    private async Task<decimal> SumAccountsPayableAsync(Guid clinicId, DateTime asOf)
    {
        try
        {
            var rows = await _db.PharmacyPurchaseBills.AsNoTracking()
                .Where(b => b.ClinicId == clinicId)
                .Select(b => new { b.PurchaseDate, b.BalanceDue })
                .ToListAsync();
            return rows.Where(b => b.PurchaseDate.Date <= asOf).Sum(b => b.BalanceDue);
        }
        catch
        {
            return 0m;
        }
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
