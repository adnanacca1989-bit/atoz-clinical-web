using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AtoZClinical.Infrastructure.Services;

public static class ClinicalJournalSources
{
    public const string Invoice = "Invoice";
    public const string CashReceipt = "CashReceipt";
    public const string CashPayment = "CashPayment";
    public const string PharmacyBill = "PharmacyBill";
    public const string PharmacyOpeningBalance = "PharmacyOpeningBalance";
    public const string PharmacyPurchaseBill = "PharmacyPurchaseBill";
}

public static class InventoryAccountingHelper
{
    public const string InventoryAccount = "Inventory";
    public const string CogsAccount = "Cost of Goods Sold";
    public const string OpeningEquityAccount = "Retained Earnings";

    public static string ResolveInventoryAccount(IReadOnlyList<ChartAccount> accounts) =>
        RevenueAccountingHelper.ResolveAssetAccount(accounts, InventoryAccount, InventoryAccount);

    public static string ResolveCogsAccount(IReadOnlyList<ChartAccount> accounts) =>
        accounts.FirstOrDefault(a =>
            string.Equals(a.Name, CogsAccount, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a.DetailType, CogsAccount, StringComparison.OrdinalIgnoreCase))?.Name ?? CogsAccount;

    public static string ResolvePayableAccount(IReadOnlyList<ChartAccount> accounts) =>
        accounts.FirstOrDefault(a =>
            string.Equals(a.Name, "Account Payable", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a.DetailType, "Account Payable", StringComparison.OrdinalIgnoreCase))?.Name ?? "Account Payable";

    public static string ResolveEquityAccount(IReadOnlyList<ChartAccount> accounts) =>
        accounts.FirstOrDefault(a =>
            string.Equals(a.Name, OpeningEquityAccount, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a.DetailType, OpeningEquityAccount, StringComparison.OrdinalIgnoreCase))?.Name ?? OpeningEquityAccount;
}

public static class RevenueAccountingHelper
{
    public enum RevenueBucket { Consultation, Lab, Radiology, Pharmacy }

    public static RevenueBucket Classify(string? serviceName)
    {
        var name = serviceName ?? "";
        if (name.Contains("Pharmacy", StringComparison.OrdinalIgnoreCase))
            return RevenueBucket.Pharmacy;
        if (name.StartsWith("Lab", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Laboratory", StringComparison.OrdinalIgnoreCase))
            return RevenueBucket.Lab;
        if (name.StartsWith("Radiology", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Radiology", StringComparison.OrdinalIgnoreCase))
            return RevenueBucket.Radiology;
        return RevenueBucket.Consultation;
    }

    public static string DefaultIncomeAccountName(RevenueBucket bucket) => bucket switch
    {
        RevenueBucket.Pharmacy => "Pharmacy Income",
        RevenueBucket.Lab => "Laboratory Income",
        RevenueBucket.Radiology => "Radiology Income",
        _ => "Consultation Income"
    };

    public static string ResolveIncomeAccountName(string? serviceName, IReadOnlyList<ChartAccount> accounts)
    {
        var preferred = DefaultIncomeAccountName(Classify(serviceName));
        return accounts.FirstOrDefault(a => string.Equals(a.Name, preferred, StringComparison.OrdinalIgnoreCase))?.Name
               ?? preferred;
    }

    public static string ResolveAssetAccount(IReadOnlyList<ChartAccount> accounts, string preferredName, string detailType) =>
        accounts.FirstOrDefault(a =>
            string.Equals(a.Name, preferredName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a.DetailType, detailType, StringComparison.OrdinalIgnoreCase))?.Name ?? preferredName;
}

public sealed class ClinicalJournalSyncService
{
    private readonly ClinicalDbContext _db;
    private readonly ILogger<ClinicalJournalSyncService> _logger;
    private int? _batchNextEntryNo;

    public ClinicalJournalSyncService(ClinicalDbContext db, ILogger<ClinicalJournalSyncService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task EnsureClinicalJournalsAsync(Guid clinicId)
    {
        try
        {
            _batchNextEntryNo = (await _db.JournalEntries.ForClinic(clinicId).MaxAsync(j => (int?)j.EntryNo) ?? 0) + 1;

            var chartAccounts = await _db.ChartAccounts.ForClinic(clinicId).AsNoTracking().ToListAsync();

            var invoices = await _db.Invoices.Include(i => i.Lines).ForClinic(clinicId).ToListAsync();
            foreach (var invoice in invoices)
            {
                try
                {
                    await SyncInvoiceAsync(clinicId, invoice, invoice.Lines.ToList(), chartAccounts, saveChanges: false, forceResync: true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Skipped invoice journal sync #{InvoiceNo}", invoice.InvoiceNo);
                }
            }

            var receipts = await _db.CashReceipts.ForClinic(clinicId).ToListAsync();
            foreach (var receipt in receipts)
            {
                try
                {
                    await SyncCashReceiptAsync(clinicId, receipt, chartAccounts, saveChanges: false, forceResync: true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Skipped cash receipt journal sync #{ReceiptNo}", receipt.ReceiptNo);
                }
            }

            var bills = await _db.PharmacyBills.Include(b => b.Lines).ForClinic(clinicId).ToListAsync();
            foreach (var bill in bills)
            {
                try
                {
                    await SyncPharmacyBillAsync(clinicId, bill, bill.Lines.ToList(), chartAccounts, saveChanges: false, forceResync: true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Skipped pharmacy bill journal sync #{BillNo}", bill.BillNo);
                }
            }

            var openingBalances = await _db.PharmacyOpeningBalances.Include(b => b.Lines).ForClinic(clinicId).ToListAsync();
            foreach (var balance in openingBalances)
            {
                try
                {
                    await SyncPharmacyOpeningBalanceAsync(clinicId, balance, chartAccounts, saveChanges: false, forceResync: true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Skipped pharmacy opening balance journal sync #{BalanceNo}", balance.BalanceNo);
                }
            }

            var purchases = await _db.PharmacyPurchaseBills.Include(b => b.Lines).ForClinic(clinicId).ToListAsync();
            foreach (var purchase in purchases)
            {
                try
                {
                    await SyncPharmacyPurchaseBillAsync(clinicId, purchase, chartAccounts, saveChanges: false, forceResync: true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Skipped pharmacy purchase journal sync #{PurchaseNo}", purchase.PurchaseNo);
                }
            }

            var patientPayments = await _db.CashPayments
                .ForClinic(clinicId)
                .Where(p => !p.VendorId.HasValue && (p.PatientId != null || p.PayeeName != null))
                .ToListAsync();
            foreach (var payment in patientPayments)
            {
                try
                {
                    await SyncPatientCashPaymentAsync(clinicId, payment, chartAccounts, saveChanges: false, forceResync: true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Skipped patient cash payment journal sync #{PaymentNo}", payment.PaymentNo);
                }
            }

            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Clinical journal batch sync failed for clinic {ClinicId}", clinicId);
            _db.ChangeTracker.Clear();
        }
        finally
        {
            _batchNextEntryNo = null;
        }
    }

    public Task SyncInvoiceAsync(Guid clinicId, Invoice invoice, List<InvoiceLine> lines) =>
        SyncInvoiceAsync(clinicId, invoice, lines, null, saveChanges: true);

    public Task SyncCashReceiptAsync(Guid clinicId, CashReceipt receipt) =>
        SyncCashReceiptAsync(clinicId, receipt, null, saveChanges: true);

    public Task SyncPharmacyBillAsync(Guid clinicId, PharmacyBill bill, List<PharmacyBillLine> lines) =>
        SyncPharmacyBillAsync(clinicId, bill, lines, null, saveChanges: true);

    public Task SyncPharmacyOpeningBalanceAsync(Guid clinicId, PharmacyOpeningBalance balance) =>
        SyncPharmacyOpeningBalanceAsync(clinicId, balance, null, saveChanges: true);

    public Task SyncPharmacyPurchaseBillAsync(Guid clinicId, PharmacyPurchaseBill bill) =>
        SyncPharmacyPurchaseBillAsync(clinicId, bill, null, saveChanges: true);

    public Task SyncPatientCashPaymentAsync(Guid clinicId, CashPayment payment) =>
        SyncPatientCashPaymentAsync(clinicId, payment, null, saveChanges: true);

    private async Task SyncInvoiceAsync(
        Guid clinicId,
        Invoice invoice,
        List<InvoiceLine> lines,
        List<ChartAccount>? chartAccounts,
        bool saveChanges,
        bool forceResync = false)
    {
        if (invoice.TotalAmount <= 0 && lines.Sum(l => l.LineTotal) <= 0)
            return;

        chartAccounts ??= await _db.ChartAccounts.ForClinic(clinicId).AsNoTracking().ToListAsync();
        if (!forceResync && await IsJournalCurrentAsync(clinicId, ClinicalJournalSources.Invoice, invoice.Id, invoice.UpdatedAt))
            return;

        var arAccount = RevenueAccountingHelper.ResolveAssetAccount(chartAccounts, "Accounts Receivable", "Accounts Receivable");
        var grossRevenue = lines.Where(l => l.LineTotal > 0).Sum(l => l.LineTotal);
        var netAr = invoice.TotalAmount > 0 ? invoice.TotalAmount : grossRevenue;
        if (netAr <= 0 && grossRevenue <= 0)
            return;

        var incomeCredits = lines
            .Where(l => l.LineTotal > 0)
            .GroupBy(l => RevenueAccountingHelper.ResolveIncomeAccountName(l.ServiceName, chartAccounts), StringComparer.OrdinalIgnoreCase)
            .Select(g => (AccountName: g.Key, GrossAmount: g.Sum(x => x.LineTotal)))
            .Where(x => x.GrossAmount > 0)
            .ToList();

        if (incomeCredits.Count == 0 && netAr > 0)
            incomeCredits.Add((RevenueAccountingHelper.DefaultIncomeAccountName(RevenueAccountingHelper.RevenueBucket.Consultation), netAr));

        var entry = await GetOrResetEntryAsync(
            clinicId,
            ClinicalJournalSources.Invoice,
            invoice.Id,
            invoice.InvoiceDate,
            $"Invoice #{invoice.InvoiceNo} — {invoice.PatientName}",
            invoice.PatientName,
            invoice.DoctorName);

        var journalLines = new List<JournalEntryLine>();
        var lineNo = 1;
        journalLines.Add(CreateLine(entry.Id, lineNo++, arAccount, "Asset", netAr, 0,
            $"Invoice #{invoice.InvoiceNo}"));

        var allocatedCredits = AllocateIncomeCredits(incomeCredits, grossRevenue, netAr);
        foreach (var credit in allocatedCredits)
        {
            var category = chartAccounts.FirstOrDefault(a =>
                string.Equals(a.Name, credit.AccountName, StringComparison.OrdinalIgnoreCase))?.CategoryType ?? "Income";
            journalLines.Add(CreateLine(entry.Id, lineNo++, credit.AccountName, category, 0, credit.Amount,
                credit.AccountName));
        }

        AddLines(journalLines);
        if (saveChanges)
            await _db.SaveChangesAsync();
    }

    private async Task SyncPatientCashPaymentAsync(
        Guid clinicId,
        CashPayment payment,
        List<ChartAccount>? chartAccounts,
        bool saveChanges,
        bool forceResync = false)
    {
        if (payment.Amount <= 0 || payment.VendorId.HasValue)
            return;

        chartAccounts ??= await _db.ChartAccounts.ForClinic(clinicId).AsNoTracking().ToListAsync();
        if (!forceResync && await IsJournalCurrentAsync(clinicId, ClinicalJournalSources.CashPayment, payment.Id, payment.UpdatedAt))
            return;

        var entry = await GetOrResetEntryAsync(
            clinicId,
            ClinicalJournalSources.CashPayment,
            payment.Id,
            payment.PaymentDate,
            $"Cash payment #{payment.PaymentNo} — {payment.PayeeName}",
            payment.PayeeName,
            payment.DoctorName);

        var cashAccount = PaymentJournalHelper.ResolvePaymentCreditAccount(
            payment.PaymentMethod, chartAccounts, payment.ChartAccountName);
        var cashCategory = chartAccounts.FirstOrDefault(a =>
            string.Equals(a.Name, cashAccount, StringComparison.OrdinalIgnoreCase))?.CategoryType ?? "Asset";
        var arAccount = RevenueAccountingHelper.ResolveAssetAccount(chartAccounts, "Accounts Receivable", "Accounts Receivable");

        var journalLines = new List<JournalEntryLine>
        {
            CreateLine(entry.Id, 1, arAccount, "Asset", payment.Amount, 0,
                $"Payment #{payment.PaymentNo}"),
            CreateLine(entry.Id, 2, cashAccount, cashCategory, 0, payment.Amount,
                $"Payment #{payment.PaymentNo}")
        };

        AddLines(journalLines);
        payment.JournalEntryId = entry.Id;
        if (saveChanges)
            await _db.SaveChangesAsync();
    }

    private async Task SyncCashReceiptAsync(
        Guid clinicId,
        CashReceipt receipt,
        List<ChartAccount>? chartAccounts,
        bool saveChanges,
        bool forceResync = false)
    {
        if (receipt.Amount <= 0)
            return;

        chartAccounts ??= await _db.ChartAccounts.ForClinic(clinicId).AsNoTracking().ToListAsync();
        if (!forceResync && await IsJournalCurrentAsync(clinicId, ClinicalJournalSources.CashReceipt, receipt.Id, receipt.UpdatedAt))
            return;

        var entry = await GetOrResetEntryAsync(
            clinicId,
            ClinicalJournalSources.CashReceipt,
            receipt.Id,
            receipt.ReceiptDate,
            $"Cash receipt #{receipt.ReceiptNo} — {receipt.PatientName}",
            receipt.PatientName,
            receipt.DoctorName);

        var cashAccount = PaymentJournalHelper.ResolvePaymentCreditAccount(
            receipt.PaymentMethod, chartAccounts, receipt.ChartAccountName);
        var cashCategory = chartAccounts.FirstOrDefault(a =>
            string.Equals(a.Name, cashAccount, StringComparison.OrdinalIgnoreCase))?.CategoryType ?? "Asset";
        var arAccount = RevenueAccountingHelper.ResolveAssetAccount(chartAccounts, "Accounts Receivable", "Accounts Receivable");

        var journalLines = new List<JournalEntryLine>
        {
            CreateLine(entry.Id, 1, cashAccount, cashCategory, receipt.Amount, 0,
                $"Receipt #{receipt.ReceiptNo}"),
            CreateLine(entry.Id, 2, arAccount, "Asset", 0, receipt.Amount,
                $"Receipt #{receipt.ReceiptNo}")
        };

        AddLines(journalLines);
        if (saveChanges)
            await _db.SaveChangesAsync();
    }

    private async Task SyncPharmacyBillAsync(
        Guid clinicId,
        PharmacyBill bill,
        List<PharmacyBillLine> lines,
        List<ChartAccount>? chartAccounts,
        bool saveChanges,
        bool forceResync = false)
    {
        if (bill.TotalAmount <= 0)
            return;

        chartAccounts ??= await _db.ChartAccounts.ForClinic(clinicId).AsNoTracking().ToListAsync();
        if (!forceResync && await IsJournalCurrentAsync(clinicId, ClinicalJournalSources.PharmacyBill, bill.Id, bill.UpdatedAt))
            return;

        var entry = await GetOrResetEntryAsync(
            clinicId,
            ClinicalJournalSources.PharmacyBill,
            bill.Id,
            bill.BillDate,
            $"Pharmacy bill #{bill.BillNo} — {bill.PatientName}",
            bill.PatientName,
            bill.DoctorName);

        var arAccount = RevenueAccountingHelper.ResolveAssetAccount(chartAccounts, "Accounts Receivable", "Accounts Receivable");
        var incomeAccount = RevenueAccountingHelper.ResolveIncomeAccountName("Pharmacy", chartAccounts);
        var incomeCategory = chartAccounts.FirstOrDefault(a =>
            string.Equals(a.Name, incomeAccount, StringComparison.OrdinalIgnoreCase))?.CategoryType ?? "Income";

        var cogsTotal = await SumBillCogsAsync(clinicId, bill.Id);
        var inventoryAccount = InventoryAccountingHelper.ResolveInventoryAccount(chartAccounts);
        var cogsAccount = InventoryAccountingHelper.ResolveCogsAccount(chartAccounts);
        var inventoryCategory = CategoryFor(chartAccounts, inventoryAccount, "Asset");
        var cogsCategory = CategoryFor(chartAccounts, cogsAccount, "Expense");

        var journalLines = new List<JournalEntryLine>
        {
            CreateLine(entry.Id, 1, arAccount, "Asset", bill.TotalAmount, 0, $"Bill #{bill.BillNo} revenue"),
            CreateLine(entry.Id, 2, incomeAccount, incomeCategory, 0, bill.TotalAmount, incomeAccount)
        };

        if (cogsTotal > 0)
        {
            journalLines.Add(CreateLine(entry.Id, 3, cogsAccount, cogsCategory, cogsTotal, 0, $"Bill #{bill.BillNo} COGS"));
            journalLines.Add(CreateLine(entry.Id, 4, inventoryAccount, inventoryCategory, 0, cogsTotal, inventoryAccount));
        }

        AddLines(journalLines);
        if (saveChanges)
            await _db.SaveChangesAsync();
    }

    private async Task SyncPharmacyOpeningBalanceAsync(
        Guid clinicId,
        PharmacyOpeningBalance balance,
        List<ChartAccount>? chartAccounts,
        bool saveChanges,
        bool forceResync = false)
    {
        chartAccounts ??= await _db.ChartAccounts.ForClinic(clinicId).AsNoTracking().ToListAsync();
        if (!forceResync && await IsJournalCurrentAsync(clinicId, ClinicalJournalSources.PharmacyOpeningBalance, balance.Id, balance.UpdatedAt))
            return;

        var total = await SumInventoryMovementValueAsync(
            clinicId, PharmacyInventoryTypes.ReferenceOpeningBalance, balance.Id);
        if (total <= 0)
            return;

        var entry = await GetOrResetEntryAsync(
            clinicId,
            ClinicalJournalSources.PharmacyOpeningBalance,
            balance.Id,
            balance.BalanceDate,
            $"Pharmacy opening balance #{balance.BalanceNo}",
            null,
            null);

        var inventoryAccount = InventoryAccountingHelper.ResolveInventoryAccount(chartAccounts);
        var equityAccount = InventoryAccountingHelper.ResolveEquityAccount(chartAccounts);

        var journalLines = new List<JournalEntryLine>
        {
            CreateLine(entry.Id, 1, inventoryAccount, CategoryFor(chartAccounts, inventoryAccount, "Asset"), total, 0,
                $"Opening balance #{balance.BalanceNo}"),
            CreateLine(entry.Id, 2, equityAccount, CategoryFor(chartAccounts, equityAccount, "Equity"), 0, total,
                $"Opening balance #{balance.BalanceNo}")
        };

        AddLines(journalLines);
        if (saveChanges)
            await _db.SaveChangesAsync();
    }

    private async Task SyncPharmacyPurchaseBillAsync(
        Guid clinicId,
        PharmacyPurchaseBill bill,
        List<ChartAccount>? chartAccounts,
        bool saveChanges,
        bool forceResync = false)
    {
        if (bill.NetAmount <= 0)
            return;

        chartAccounts ??= await _db.ChartAccounts.ForClinic(clinicId).AsNoTracking().ToListAsync();
        if (!forceResync && await IsJournalCurrentAsync(clinicId, ClinicalJournalSources.PharmacyPurchaseBill, bill.Id, bill.UpdatedAt))
            return;

        var entry = await GetOrResetEntryAsync(
            clinicId,
            ClinicalJournalSources.PharmacyPurchaseBill,
            bill.Id,
            bill.PurchaseDate,
            $"Pharmacy purchase #{bill.PurchaseNo} — {bill.SupplierName}",
            null,
            null);

        var inventoryAccount = InventoryAccountingHelper.ResolveInventoryAccount(chartAccounts);
        var payableAccount = InventoryAccountingHelper.ResolvePayableAccount(chartAccounts);
        var inventoryCategory = CategoryFor(chartAccounts, inventoryAccount, "Asset");
        var payableCategory = CategoryFor(chartAccounts, payableAccount, "Liability");

        var journalLines = new List<JournalEntryLine>
        {
            CreateLine(entry.Id, 1, inventoryAccount, inventoryCategory, bill.NetAmount, 0,
                $"Purchase #{bill.PurchaseNo}")
        };

        var lineNo = 2;
        var amountPaid = Math.Max(0m, bill.AmountPaid);
        var balanceDue = Math.Max(0m, bill.BalanceDue);
        if (amountPaid > 0 && balanceDue > 0)
        {
            var cashAccount = PaymentJournalHelper.ResolvePaymentCreditAccount(bill.PaymentMethod, chartAccounts);
            journalLines.Add(CreateLine(entry.Id, lineNo++, cashAccount, CategoryFor(chartAccounts, cashAccount, "Asset"),
                0, amountPaid, $"Purchase #{bill.PurchaseNo} payment"));
            journalLines.Add(CreateLine(entry.Id, lineNo, payableAccount, payableCategory, 0, balanceDue,
                $"Purchase #{bill.PurchaseNo} payable"));
        }
        else if (amountPaid >= bill.NetAmount)
        {
            var cashAccount = PaymentJournalHelper.ResolvePaymentCreditAccount(bill.PaymentMethod, chartAccounts);
            journalLines.Add(CreateLine(entry.Id, lineNo, cashAccount, CategoryFor(chartAccounts, cashAccount, "Asset"),
                0, bill.NetAmount, $"Purchase #{bill.PurchaseNo} payment"));
        }
        else
        {
            journalLines.Add(CreateLine(entry.Id, lineNo, payableAccount, payableCategory, 0, bill.NetAmount,
                $"Purchase #{bill.PurchaseNo} payable"));
        }

        AddLines(journalLines);
        if (saveChanges)
            await _db.SaveChangesAsync();
    }

    private async Task<decimal> SumBillCogsAsync(Guid clinicId, Guid billId) =>
        await _db.PharmacyInventoryMovements
            .ForClinic(clinicId)
            .Where(m => m.ReferenceType == PharmacyInventoryTypes.ReferenceBill &&
                        m.ReferenceId == billId &&
                        m.MovementType == PharmacyInventoryTypes.BillOut)
            .SumAsync(m => m.TotalValue);

    private async Task<decimal> SumInventoryMovementValueAsync(Guid clinicId, string referenceType, Guid referenceId) =>
        await _db.PharmacyInventoryMovements
            .ForClinic(clinicId)
            .Where(m => m.ReferenceType == referenceType && m.ReferenceId == referenceId)
            .SumAsync(m => m.TotalValue);

    private static string CategoryFor(IReadOnlyList<ChartAccount> accounts, string accountName, string fallback) =>
        accounts.FirstOrDefault(a => string.Equals(a.Name, accountName, StringComparison.OrdinalIgnoreCase))?.CategoryType
        ?? fallback;

    private async Task<bool> IsJournalCurrentAsync(Guid clinicId, string sourceType, Guid sourceId, DateTime sourceUpdatedAt)
    {
        var entry = await _db.JournalEntries.AsNoTracking()
            .ForClinic(clinicId)
            .Include(j => j.Lines)
            .FirstOrDefaultAsync(j => j.SourceType == sourceType && j.SourceId == sourceId);
        return entry is not null && entry.Lines.Count > 0 && entry.UpdatedAt >= sourceUpdatedAt;
    }

    private async Task<JournalEntry> GetOrResetEntryAsync(
        Guid clinicId,
        string sourceType,
        Guid sourceId,
        DateTime entryDate,
        string description,
        string? patientName,
        string? doctorName)
    {
        var entry = await _db.JournalEntries
            .Include(j => j.Lines)
            .ForClinic(clinicId)
            .FirstOrDefaultAsync(j => j.SourceType == sourceType && j.SourceId == sourceId);

        if (entry is null)
        {
            entry = new JournalEntry
            {
                Id = Guid.NewGuid(),
                ClinicId = clinicId,
                EntryNo = await AllocateEntryNoAsync(clinicId),
                EntryDate = entryDate,
                SourceType = sourceType,
                SourceId = sourceId,
                Description = description,
                PatientName = patientName,
                DoctorName = doctorName,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.JournalEntries.Add(entry);
        }
        else
        {
            var oldLines = await _db.JournalEntryLines.Where(l => l.JournalEntryId == entry.Id).ToListAsync();
            _db.JournalEntryLines.RemoveRange(oldLines);
            entry.EntryDate = entryDate;
            entry.Description = description;
            entry.PatientName = patientName;
            entry.DoctorName = doctorName;
            entry.UpdatedAt = DateTime.UtcNow;
        }

        return entry;
    }

    private async Task<int> AllocateEntryNoAsync(Guid clinicId)
    {
        if (_batchNextEntryNo is int next)
        {
            _batchNextEntryNo = next + 1;
            return next;
        }

        return (await _db.JournalEntries.ForClinic(clinicId).MaxAsync(j => (int?)j.EntryNo) ?? 0) + 1;
    }

    private void AddLines(IReadOnlyList<JournalEntryLine> lines)
    {
        ExpenseAccountingHelper.EnsureBalancedJournal(lines);
        foreach (var line in lines)
            _db.JournalEntryLines.Add(line);
    }

    private static JournalEntryLine CreateLine(
        Guid journalEntryId,
        int lineNo,
        string accountName,
        string category,
        decimal debit,
        decimal credit,
        string? description) => new()
    {
        Id = Guid.NewGuid(),
        JournalEntryId = journalEntryId,
        LineNo = lineNo,
        AccountName = accountName,
        AccountCategory = category,
        Debit = debit,
        Credit = credit,
        Description = description
    };

    private static List<(string AccountName, decimal Amount)> AllocateIncomeCredits(
        IReadOnlyList<(string AccountName, decimal GrossAmount)> incomeCredits,
        decimal grossRevenue,
        decimal netAr)
    {
        if (incomeCredits.Count == 0)
            return [];

        if (grossRevenue <= 0 || grossRevenue == netAr)
            return incomeCredits.Select(c => (c.AccountName, c.GrossAmount)).ToList();

        var result = new List<(string AccountName, decimal Amount)>();
        decimal allocated = 0;
        for (var i = 0; i < incomeCredits.Count; i++)
        {
            var credit = incomeCredits[i];
            var amount = i == incomeCredits.Count - 1
                ? netAr - allocated
                : Math.Round(credit.GrossAmount * netAr / grossRevenue, 2, MidpointRounding.AwayFromZero);
            allocated += amount;
            result.Add((credit.AccountName, amount));
        }

        return result;
    }
}
