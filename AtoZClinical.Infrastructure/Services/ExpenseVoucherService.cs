using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public static class ExpenseAccountingHelper
{
    public const string ExpenseVoucherSource = "ExpenseVoucher";

    public static string ResolveCreditAccountName(string paymentMethod, IReadOnlyList<ChartAccount> accounts)
    {
        return paymentMethod.Trim().ToUpperInvariant() switch
        {
            "BANK" => FindAccount(accounts, "Asset", "Bank") ?? "Bank",
            "CREDIT" => FindAccount(accounts, "Liability", "Account Payable") ?? "Account Payable",
            _ => FindAccount(accounts, "Asset", "Cash") ?? "Cash"
        };
    }

    public static void ValidateExpenseLines(
        IReadOnlyList<ExpenseVoucherLine> lines,
        IReadOnlyDictionary<string, ChartAccount> expenseAccountsByName)
    {
        var valid = lines.Where(l => l.Amount > 0 && !string.IsNullOrWhiteSpace(l.ChartAccountName)).ToList();
        if (valid.Count == 0)
            throw new InvalidOperationException("Add at least one expense line with an account and amount.");

        foreach (var line in valid)
        {
            if (!expenseAccountsByName.ContainsKey(line.ChartAccountName.Trim()))
                throw new InvalidOperationException(
                    $"\"{line.ChartAccountName}\" is not a valid expense account. Select accounts from the Expense category only.");
        }

        var totalDebits = valid.Sum(l => l.Amount);
        if (totalDebits <= 0)
            throw new InvalidOperationException("Expense total must be greater than zero.");
    }

    public static void EnsureBalancedJournal(IReadOnlyList<JournalEntryLine> lines)
    {
        var debits = lines.Sum(l => l.Debit);
        var credits = lines.Sum(l => l.Credit);
        if (debits != credits)
            throw new InvalidOperationException($"Journal entry is not balanced (debit {debits:N2} ≠ credit {credits:N2}).");
    }

    private static string? FindAccount(IReadOnlyList<ChartAccount> accounts, string category, string detailOrName)
    {
        var match = accounts.FirstOrDefault(a =>
            string.Equals(a.CategoryType, category, StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(a.DetailType, detailOrName, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(a.Name, detailOrName, StringComparison.OrdinalIgnoreCase)));
        return match?.Name;
    }
}

public sealed class ExpenseVoucherService
{
    private readonly ClinicalDbContext _db;
    private readonly AuditService _audit;

    public ExpenseVoucherService(ClinicalDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public Task<List<ExpenseVoucher>> ListAsync(Guid clinicId) =>
        _db.ExpenseVouchers.Include(v => v.Lines).ForClinic(clinicId).OrderByDescending(v => v.ExpenseNo).ToListAsync();

    public Task<ExpenseVoucher?> GetAsync(Guid clinicId, Guid id) =>
        _db.ExpenseVouchers.Include(v => v.Lines).ForClinic(clinicId).FirstOrDefaultAsync(v => v.Id == id);

    public async Task<int> NextExpenseNoAsync(Guid clinicId) =>
        (await _db.ExpenseVouchers.ForClinic(clinicId).MaxAsync(v => (int?)v.ExpenseNo) ?? 0) + 1;

    public async Task<ExpenseVoucher> SaveAsync(
        Guid clinicId,
        ExpenseVoucher item,
        List<ExpenseVoucherLine> lines,
        string? userName = null)
    {
        var chartAccounts = await _db.ChartAccounts.ForClinic(clinicId).AsNoTracking().ToListAsync();
        var expenseMap = chartAccounts
            .Where(a => string.Equals(a.CategoryType, "Expense", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(a => a.Name, a => a, StringComparer.OrdinalIgnoreCase);

        var validLines = lines
            .Where(l => l.Amount > 0 && !string.IsNullOrWhiteSpace(l.ChartAccountName))
            .Select((l, i) =>
            {
                l.LineNo = i + 1;
                l.ChartAccountName = l.ChartAccountName.Trim();
                return l;
            })
            .ToList();

        ExpenseAccountingHelper.ValidateExpenseLines(validLines, expenseMap);

        var isNew = item.Id == Guid.Empty;
        item.ClinicId = clinicId;
        item.UpdatedAt = DateTime.UtcNow;
        item.TotalAmount = validLines.Sum(l => l.Amount);

        if (isNew)
        {
            item.Id = Guid.NewGuid();
            item.ExpenseNo = (await _db.ExpenseVouchers.ForClinic(clinicId).MaxAsync(v => (int?)v.ExpenseNo) ?? 0) + 1;
            item.CreatedAt = DateTime.UtcNow;
            _db.ExpenseVouchers.Add(item);
        }
        else
        {
            var existingLines = await _db.ExpenseVoucherLines.Where(l => l.ExpenseVoucherId == item.Id).ToListAsync();
            _db.ExpenseVoucherLines.RemoveRange(existingLines);
            _db.ExpenseVouchers.Update(item);
        }

        foreach (var line in validLines)
        {
            line.Id = Guid.NewGuid();
            line.ExpenseVoucherId = item.Id;
            _db.ExpenseVoucherLines.Add(line);
        }

        await SyncJournalEntryAsync(clinicId, item, validLines, chartAccounts);

        await _db.SaveChangesAsync();
        await _audit.LogAsync(clinicId, userName, "Expense Voucher", isNew ? "Create" : "Update",
            $"Expense #{item.ExpenseNo} — {item.TotalAmount:N2} ({item.PaymentMethod})");
        return item;
    }

    public async Task DeleteAsync(Guid clinicId, Guid id, string? userName = null)
    {
        var item = await GetAsync(clinicId, id);
        if (item is null) return;

        if (item.JournalEntryId is Guid journalId)
        {
            var journalLines = await _db.JournalEntryLines.Where(l => l.JournalEntryId == journalId).ToListAsync();
            _db.JournalEntryLines.RemoveRange(journalLines);
            var journal = await _db.JournalEntries.ForClinic(clinicId).FirstOrDefaultAsync(j => j.Id == journalId);
            if (journal is not null)
                _db.JournalEntries.Remove(journal);
        }

        _db.ExpenseVouchers.Remove(item);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(clinicId, userName, "Expense Voucher", "Delete", $"Expense #{item.ExpenseNo}");
    }

    private async Task SyncJournalEntryAsync(
        Guid clinicId,
        ExpenseVoucher voucher,
        List<ExpenseVoucherLine> lines,
        List<ChartAccount> chartAccounts)
    {
        var expenseMap = chartAccounts
            .Where(a => string.Equals(a.CategoryType, "Expense", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(a => a.Name, a => a, StringComparer.OrdinalIgnoreCase);

        var creditAccountName = ExpenseAccountingHelper.ResolveCreditAccountName(voucher.PaymentMethod, chartAccounts);
        var creditCategory = chartAccounts.FirstOrDefault(a =>
            string.Equals(a.Name, creditAccountName, StringComparison.OrdinalIgnoreCase))?.CategoryType ?? "Asset";

        JournalEntry entry;
        if (voucher.JournalEntryId is Guid existingId)
        {
            entry = await _db.JournalEntries.Include(j => j.Lines).ForClinic(clinicId)
                .FirstOrDefaultAsync(j => j.Id == existingId)
                ?? throw new InvalidOperationException("Linked journal entry was not found.");
            var oldLines = await _db.JournalEntryLines.Where(l => l.JournalEntryId == entry.Id).ToListAsync();
            _db.JournalEntryLines.RemoveRange(oldLines);
            entry.EntryDate = voucher.ExpenseDate;
            entry.Description = voucher.Description;
            entry.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            entry = new JournalEntry
            {
                Id = Guid.NewGuid(),
                ClinicId = clinicId,
                EntryNo = (await _db.JournalEntries.ForClinic(clinicId).MaxAsync(j => (int?)j.EntryNo) ?? 0) + 1,
                EntryDate = voucher.ExpenseDate,
                SourceType = ExpenseAccountingHelper.ExpenseVoucherSource,
                SourceId = voucher.Id,
                Description = voucher.Description,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.JournalEntries.Add(entry);
            voucher.JournalEntryId = entry.Id;
        }

        var journalLines = new List<JournalEntryLine>();
        var lineNo = 1;
        foreach (var line in lines)
        {
            var account = expenseMap[line.ChartAccountName];
            journalLines.Add(new JournalEntryLine
            {
                Id = Guid.NewGuid(),
                JournalEntryId = entry.Id,
                LineNo = lineNo++,
                AccountName = account.Name,
                AccountCategory = account.CategoryType,
                Debit = line.Amount,
                Credit = 0,
                Description = line.Description
            });
        }

        journalLines.Add(new JournalEntryLine
        {
            Id = Guid.NewGuid(),
            JournalEntryId = entry.Id,
            LineNo = lineNo,
            AccountName = creditAccountName,
            AccountCategory = creditCategory,
            Debit = 0,
            Credit = voucher.TotalAmount,
            Description = $"Payment — {voucher.PaymentMethod}"
        });

        ExpenseAccountingHelper.EnsureBalancedJournal(journalLines);
        foreach (var jl in journalLines)
            _db.JournalEntryLines.Add(jl);
    }
}

public sealed class JournalReportService
{
    private readonly ClinicalDbContext _db;

    public JournalReportService(ClinicalDbContext db) => _db = db;

    public async Task<List<GeneralLedgerRow>> GetGeneralLedgerAsync(Guid clinicId, DateTime from, DateTime to, string? accountName = null)
    {
        var fromDate = from.Date;
        var toDate = to.Date;

        var entries = await _db.JournalEntries
            .Include(j => j.Lines)
            .ForClinic(clinicId)
            .Where(j => j.EntryDate >= fromDate && j.EntryDate <= toDate)
            .OrderBy(j => j.EntryDate).ThenBy(j => j.EntryNo)
            .ToListAsync();

        var rows = new List<GeneralLedgerRow>();
        foreach (var entry in entries)
        {
            foreach (var line in entry.Lines.OrderBy(l => l.LineNo))
            {
                if (!string.IsNullOrWhiteSpace(accountName) &&
                    !line.AccountName.Contains(accountName, StringComparison.OrdinalIgnoreCase))
                    continue;

                rows.Add(new GeneralLedgerRow(
                    entry.EntryDate,
                    entry.EntryNo,
                    entry.SourceType,
                    line.AccountName,
                    line.Description ?? entry.Description,
                    line.Debit,
                    line.Credit));
            }
        }

        return rows;
    }

    public async Task<List<TrialBalanceRow>> GetTrialBalanceAsync(Guid clinicId, DateTime asOf)
    {
        var asOfDate = asOf.Date;
        var entries = await _db.JournalEntries
            .Include(j => j.Lines)
            .ForClinic(clinicId)
            .Where(j => j.EntryDate <= asOfDate)
            .AsNoTracking()
            .ToListAsync();

        return entries
            .SelectMany(e => e.Lines)
            .GroupBy(l => new { l.AccountName, l.AccountCategory })
            .Select(g => new TrialBalanceRow(
                g.Key.AccountCategory ?? "",
                g.Key.AccountName,
                g.Sum(x => x.Debit),
                g.Sum(x => x.Credit)))
            .OrderBy(r => r.AccountCategory).ThenBy(r => r.AccountName)
            .ToList();
    }

    public sealed record GeneralLedgerRow(
        DateTime EntryDate,
        int EntryNo,
        string SourceType,
        string AccountName,
        string? Description,
        decimal Debit,
        decimal Credit);

    public sealed record TrialBalanceRow(
        string AccountCategory,
        string AccountName,
        decimal TotalDebit,
        decimal TotalCredit)
    {
        public decimal Balance => TotalDebit - TotalCredit;
    }
}
