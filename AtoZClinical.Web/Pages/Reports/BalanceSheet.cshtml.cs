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
    private readonly FinancialReportCalculator _financial;
    private readonly PharmacyInventoryService _inventory;

    public BalanceSheetModel(
        ClinicalDbContext db,
        ClinicContextService clinicContext,
        FinancialReportCalculator financial,
        PharmacyInventoryService inventory)
    {
        _db = db;
        _clinicContext = clinicContext;
        _financial = financial;
        _inventory = inventory;
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

            var cashReceipts = await SumCashReceiptsAsync(id, asOf);
            var cashPayments = await SumCashPaymentsAsync(id, asOf);
            var cashOnHand = cashReceipts - cashPayments;

            var invoiceReceivable = await SumInvoiceReceivableAsync(id, asOf);
            var pharmacyReceivable = await SumPharmacyBillReceivableAsync(id, asOf);
            var accountsReceivable = invoiceReceivable + pharmacyReceivable;

            var inventoryValue = await SumInventoryValueAsync(id);
            var accountsPayable = await SumAccountsPayableAsync(id, asOf);
            var patientCredit = await _financial.ComputePatientCreditLiabilityAsync(id, asOf);

            var assetRows = new List<BsRow>
            {
                new("Cash", cashOnHand),
                new("Accounts Receivable — Invoices", invoiceReceivable),
                new("Accounts Receivable — Pharmacy Bills", pharmacyReceivable),
                new("Pharmacy Inventory", inventoryValue)
            };
            Assets = assetRows.Where(r => r.Amount != 0).ToList();

            var liabilityRows = new List<BsRow>
            {
                new("Accounts Payable", accountsPayable),
                new("Patient Credit (overpayments)", patientCredit)
            };
            Liabilities = liabilityRows.Where(r => r.Amount != 0).ToList();

            TotalAssets = cashOnHand + accountsReceivable + inventoryValue;
            TotalLiabilities = Liabilities.Sum(r => r.Amount);

            var retainedEarnings = await _financial.ComputeNetIncomeAsync(id, FromDate, ToDate);
            Equity = [new BsRow("Retained Earnings", retainedEarnings)];
            TotalEquity = retainedEarnings;
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not load balance sheet data: {ex.Message}";
            Assets = [];
            Liabilities = [];
            Equity = [];
            TotalAssets = TotalLiabilities = TotalEquity = 0;
        }

        return Page();
    }

    private async Task<decimal> SumCashReceiptsAsync(Guid clinicId, DateTime asOf) =>
        await _db.CashReceipts.ForClinic(clinicId)
            .Where(c => c.ReceiptDate.Date <= asOf)
            .SumAsync(c => (decimal?)c.Amount) ?? 0m;

    private async Task<decimal> SumCashPaymentsAsync(Guid clinicId, DateTime asOf) =>
        await _db.CashPayments.ForClinic(clinicId)
            .Where(c => c.PaymentDate.Date <= asOf)
            .SumAsync(c => (decimal?)c.Amount) ?? 0m;

    private async Task<decimal> SumInvoiceReceivableAsync(Guid clinicId, DateTime asOf) =>
        await _db.Invoices.ForClinic(clinicId)
            .Where(i => i.InvoiceDate.Date <= asOf && i.BalanceDue > 0)
            .SumAsync(i => (decimal?)i.BalanceDue) ?? 0m;

    private async Task<decimal> SumPharmacyBillReceivableAsync(Guid clinicId, DateTime asOf)
    {
        try
        {
            return await _db.PharmacyBills.ForClinic(clinicId)
                .Where(b => b.BillDate.Date <= asOf && b.BalanceDue > 0)
                .SumAsync(b => (decimal?)b.BalanceDue) ?? 0m;
        }
        catch
        {
            return 0m;
        }
    }

    private async Task<decimal> SumInventoryValueAsync(Guid clinicId)
    {
        await _inventory.RecalculateClinicInventoryAsync(clinicId);
        var items = await _db.PharmacyItems.ForClinic(clinicId)
            .Select(p => new { p.QuantityOnHand, p.MovingAverageCost })
            .ToListAsync();
        return items.Sum(p => p.QuantityOnHand * p.MovingAverageCost);
    }

    private async Task<decimal> SumAccountsPayableAsync(Guid clinicId, DateTime asOf)
    {
        try
        {
            return await _db.PharmacyPurchaseBills.ForClinic(clinicId)
                .Where(b => b.PurchaseDate.Date <= asOf && b.BalanceDue > 0)
                .SumAsync(b => (decimal?)b.BalanceDue) ?? 0m;
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
