using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AtoZClinical.Infrastructure.Services;

public static class ClinicalJournalSources
{
    public const string Invoice = "Invoice";
    public const string CashReceipt = "CashReceipt";
    public const string PharmacyBill = "PharmacyBill";
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

        var journalLines = new List<JournalEntryLine>
        {
            CreateLine(entry.Id, 1, arAccount, "Asset", bill.TotalAmount, 0, $"Bill #{bill.BillNo}"),
            CreateLine(entry.Id, 2, incomeAccount, incomeCategory, 0, bill.TotalAmount, incomeAccount)
        };

        AddLines(journalLines);
        if (saveChanges)
            await _db.SaveChangesAsync();
    }

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
