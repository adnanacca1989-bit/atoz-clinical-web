using AtoZClinical.Infrastructure.Data;
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
    public DateTime AsOfDate { get; set; } = DateTime.Today;

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

        var asOf = AsOfDate.Date;

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
        var expenses = await _db.CashPayments
            .Where(p => p.ClinicId == clinicId && p.PaymentDate <= asOf)
            .SumAsync(p => p.Amount);
        var retainedEarnings = invoiceRevenue - expenses;

        Assets =
        [
            new BsRow("Cash on Hand", cashOnHand),
            new BsRow("Accounts Receivable", accountsReceivable),
            new BsRow("Pharmacy Inventory", inventoryValue)
        ];

        Liabilities = [new BsRow("Accounts Payable", accountsPayable)];

        Equity = [new BsRow("Retained Earnings", retainedEarnings)];

        TotalAssets = Assets.Sum(r => r.Amount);
        TotalLiabilities = Liabilities.Sum(r => r.Amount);
        TotalEquity = Equity.Sum(r => r.Amount);

        var imbalance = TotalAssets - (TotalLiabilities + TotalEquity);
        if (Math.Abs(imbalance) > 0.01m)
            Equity.Add(new BsRow("Balancing Adjustment", imbalance));

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
