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
    private readonly ClinicalJournalSyncService _journalSync;
    private readonly JournalReportService _journal;

    public BalanceSheetModel(
        ClinicalDbContext db,
        ClinicContextService clinicContext,
        FinancialReportCalculator financial,
        PharmacyInventoryService inventory,
        ClinicalJournalSyncService journalSync,
        JournalReportService journal)
    {
        _db = db;
        _clinicContext = clinicContext;
        _financial = financial;
        _inventory = inventory;
        _journalSync = journalSync;
        _journal = journal;
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

            await _journalSync.EnsureClinicalJournalsAsync(id);

            var chartAccounts = await _db.ChartAccounts.ForClinic(id).AsNoTracking()
                .OrderBy(a => a.AccountNo)
                .ToListAsync();
            var trialBalance = await _journal.GetTrialBalanceAsync(id, asOf);

            var assetRows = new List<BsRow>();
            var liabilityRows = new List<BsRow>();
            var equityRows = new List<BsRow>();

            foreach (var acct in chartAccounts)
            {
                var tbRow = trialBalance.FirstOrDefault(r =>
                    string.Equals(r.AccountName, acct.Name, StringComparison.OrdinalIgnoreCase));
                if (tbRow is null)
                    continue;

                var balance = tbRow.Balance;
                switch (acct.CategoryType.ToLowerInvariant())
                {
                    case "asset":
                        if (balance != 0)
                            assetRows.Add(new BsRow(acct.Name, balance));
                        break;
                    case "liability":
                        var liabilityAmount = -balance;
                        if (liabilityAmount != 0)
                            liabilityRows.Add(new BsRow(acct.Name, liabilityAmount));
                        break;
                    case "equity":
                        var equityAmount = -balance;
                        if (equityAmount != 0)
                            equityRows.Add(new BsRow(acct.Name, equityAmount));
                        break;
                }
            }

            var inventoryValue = await SumInventoryValueAsync(id);
            if (inventoryValue != 0)
            {
                var inventoryName = chartAccounts.FirstOrDefault(a =>
                    a.Name.Contains("Inventory", StringComparison.OrdinalIgnoreCase))?.Name ?? "Pharmacy Inventory";
                var existing = assetRows.FindIndex(r =>
                    r.Account.Contains("Inventory", StringComparison.OrdinalIgnoreCase));
                if (existing >= 0)
                    assetRows[existing] = assetRows[existing] with { Amount = assetRows[existing].Amount + inventoryValue };
                else
                    assetRows.Add(new BsRow(inventoryName, inventoryValue));
            }

            var purchaseAp = await SumAccountsPayableAsync(id, asOf);
            if (purchaseAp > 0)
            {
                var apName = chartAccounts.FirstOrDefault(a =>
                    string.Equals(a.Name, "Account Payable", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a.Name, "Accounts Payable", StringComparison.OrdinalIgnoreCase))?.Name
                    ?? "Accounts Payable";
                var existing = liabilityRows.FindIndex(r =>
                    string.Equals(r.Account, apName, StringComparison.OrdinalIgnoreCase));
                if (existing >= 0)
                    liabilityRows[existing] = liabilityRows[existing] with { Amount = liabilityRows[existing].Amount + purchaseAp };
                else
                    liabilityRows.Add(new BsRow(apName, purchaseAp));
            }

            var patientCredit = await _financial.ComputePatientCreditLiabilityAsync(id, asOf);
            if (patientCredit > 0)
                liabilityRows.Add(new BsRow("Patient Credit (overpayments)", patientCredit));

            var retainedEarnings = await _financial.ComputeNetIncomeAsync(id, FromDate, ToDate);
            equityRows.Add(new BsRow("Retained Earnings (period)", retainedEarnings));

            Assets = assetRows;
            Liabilities = liabilityRows;
            Equity = equityRows;
            TotalAssets = Assets.Sum(r => r.Amount);
            TotalLiabilities = Liabilities.Sum(r => r.Amount);
            TotalEquity = Equity.Sum(r => r.Amount);
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
