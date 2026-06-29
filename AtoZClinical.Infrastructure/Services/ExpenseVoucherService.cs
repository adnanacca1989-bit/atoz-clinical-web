using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public static class ExpenseAccountingHelper
{
    public const string ExpenseVoucherSource = "ExpenseVoucher";

    public static string ResolveCreditAccountName(string paymentMethod, IReadOnlyList<ChartAccount> accounts, string? overrideAccountName = null) =>
        PaymentJournalHelper.ResolvePaymentCreditAccount(paymentMethod, accounts, overrideAccountName);

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

        var creditAccountName = ExpenseAccountingHelper.ResolveCreditAccountName(
            voucher.PaymentMethod, chartAccounts, voucher.CreditAccountName);
        var creditCategory = chartAccounts.FirstOrDefault(a =>
            string.Equals(a.Name, creditAccountName, StringComparison.OrdinalIgnoreCase))?.CategoryType ?? "Asset";

        var memo = string.IsNullOrWhiteSpace(voucher.PayeeName)
            ? voucher.Description
            : $"{voucher.PayeeName} — {voucher.Description}".Trim(' ', '—');

        JournalEntry entry;
        if (voucher.JournalEntryId is Guid existingId)
        {
            entry = await _db.JournalEntries.Include(j => j.Lines).ForClinic(clinicId)
                .FirstOrDefaultAsync(j => j.Id == existingId)
                ?? throw new InvalidOperationException("Linked journal entry was not found.");
            var oldLines = await _db.JournalEntryLines.Where(l => l.JournalEntryId == entry.Id).ToListAsync();
            _db.JournalEntryLines.RemoveRange(oldLines);
            entry.EntryDate = voucher.ExpenseDate;
            entry.Description = memo;
            entry.PatientName = voucher.PayeeName;
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
                Description = memo,
                PatientName = voucher.PayeeName,
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
    private readonly ClinicalJournalSyncService _journalSync;

    public JournalReportService(ClinicalDbContext db, ClinicalJournalSyncService journalSync)
    {
        _db = db;
        _journalSync = journalSync;
    }

    /// <summary>Posted, non-deleted journal entries for a clinic — single source for all GL reports.</summary>
    public IQueryable<JournalEntry> PostedEntries(Guid clinicId) =>
        _db.JournalEntries
            .Include(j => j.Lines)
            .ForClinic(clinicId)
            .Where(j => j.IsPosted && !j.IsDeleted);

    public async Task<JournalIntegrityReport> ValidateIntegrityAsync(Guid clinicId, CancellationToken ct = default)
    {
        await _journalSync.EnsureClinicalJournalsAsync(clinicId);

        var entries = await _db.JournalEntries
            .Include(j => j.Lines)
            .ForClinic(clinicId)
            .AsNoTracking()
            .ToListAsync(ct);

        var unbalanced = new List<JournalIntegrityReport.UnbalancedEntry>();
        foreach (var entry in entries.Where(e => e.IsPosted && !e.IsDeleted))
        {
            var debit = entry.Lines.Sum(l => l.Debit);
            var credit = entry.Lines.Sum(l => l.Credit);
            if (Math.Abs(debit - credit) > 0.005m)
            {
                unbalanced.Add(new JournalIntegrityReport.UnbalancedEntry(
                    entry.EntryNo,
                    entry.EntryDate,
                    entry.SourceType,
                    debit,
                    credit));
            }
        }

        var postedLines = entries
            .Where(e => e.IsPosted && !e.IsDeleted)
            .SelectMany(e => e.Lines)
            .ToList();

        var draftCount = entries.Count(e => !e.IsPosted && !e.IsDeleted);
        var deletedCount = entries.Count(e => e.IsDeleted);

        return new JournalIntegrityReport(
            entries.Count,
            postedLines.Count,
            postedLines.Sum(l => l.Debit),
            postedLines.Sum(l => l.Credit),
            unbalanced,
            draftCount,
            deletedCount);
    }

    public async Task<List<GeneralLedgerRow>> GetGeneralLedgerAsync(
        Guid clinicId,
        DateTime from,
        DateTime to,
        string? accountName = null,
        string? patientName = null,
        string? doctorName = null,
        string sortBy = "Date")
    {
        await _journalSync.EnsureClinicalJournalsAsync(clinicId);

        var fromDate = from.Date;
        var toDate = to.Date;
        var accountFilter = string.IsNullOrWhiteSpace(accountName) ? null : accountName.Trim();

        var accountLookup = await _db.ChartAccounts.ForClinic(clinicId).AsNoTracking()
            .ToDictionaryAsync(a => a.Name, a => a.AccountNo, StringComparer.OrdinalIgnoreCase);

        var entries = await PostedEntries(clinicId)
            .AsNoTracking()
            .Where(j => j.EntryDate <= toDate)
            .OrderBy(j => j.EntryDate).ThenBy(j => j.EntryNo)
            .ToListAsync();

        var rows = new List<GeneralLedgerRow>();

        if (!string.IsNullOrWhiteSpace(accountFilter))
        {
            var opening = entries
                .Where(j => j.EntryDate < fromDate)
                .SelectMany(e => e.Lines)
                .Where(l => AccountNamesEqual(l.AccountName, accountFilter))
                .Sum(l => l.Debit - l.Credit);

            accountLookup.TryGetValue(accountFilter, out var openingAccountNo);
            rows.Add(new GeneralLedgerRow(
                fromDate,
                0,
                "Opening",
                openingAccountNo,
                accountFilter,
                "Opening balance",
                opening > 0 ? opening : 0,
                opening < 0 ? -opening : 0,
                opening,
                null,
                null,
                IsOpeningBalance: true));
        }

        foreach (var entry in entries.Where(j => j.EntryDate >= fromDate && j.EntryDate <= toDate))
        {
            foreach (var line in entry.Lines.OrderBy(l => l.LineNo))
            {
                if (accountFilter is not null && !AccountNamesEqual(line.AccountName, accountFilter))
                    continue;

                accountLookup.TryGetValue(line.AccountName, out var accountNo);
                rows.Add(new GeneralLedgerRow(
                    entry.EntryDate,
                    entry.EntryNo,
                    entry.SourceType,
                    accountNo,
                    line.AccountName,
                    line.Description ?? entry.Description,
                    line.Debit,
                    line.Credit,
                    0,
                    entry.PatientName,
                    entry.DoctorName));
            }
        }

        if (!string.IsNullOrWhiteSpace(patientName))
        {
            var filter = patientName.Trim();
            rows = rows.Where(r => r.IsOpeningBalance ||
                                   r.PatientName?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true).ToList();
        }

        if (!string.IsNullOrWhiteSpace(doctorName))
        {
            var filter = doctorName.Trim();
            rows = rows.Where(r => r.IsOpeningBalance ||
                                   r.DoctorName?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true).ToList();
        }

        rows = sortBy.Trim().ToLowerInvariant() switch
        {
            "doctor" => rows.OrderBy(r => r.IsOpeningBalance ? 0 : 1).ThenBy(r => r.DoctorName).ThenBy(r => r.EntryDate).ThenBy(r => r.EntryNo).ToList(),
            "patient" => rows.OrderBy(r => r.IsOpeningBalance ? 0 : 1).ThenBy(r => r.PatientName).ThenBy(r => r.EntryDate).ThenBy(r => r.EntryNo).ToList(),
            _ => rows.OrderBy(r => r.IsOpeningBalance ? 0 : 1).ThenBy(r => r.EntryDate).ThenBy(r => r.EntryNo).ToList()
        };

        if (!string.IsNullOrWhiteSpace(accountFilter))
        {
            var running = rows.FirstOrDefault(r => r.IsOpeningBalance)?.RunningBalance ?? 0m;
            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i].IsOpeningBalance)
                    continue;

                running += rows[i].Debit - rows[i].Credit;
                rows[i] = rows[i] with { RunningBalance = running };
            }
        }

        return rows;
    }

    public async Task<List<TrialBalanceRow>> GetTrialBalanceAsync(Guid clinicId, DateTime asOf)
    {
        await _journalSync.EnsureClinicalJournalsAsync(clinicId);

        var asOfDate = asOf.Date;
        var chartAccounts = await _db.ChartAccounts.ForClinic(clinicId).AsNoTracking()
            .OrderBy(a => a.AccountNo)
            .ToListAsync();

        var chartByName = chartAccounts.ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);

        var entries = await PostedEntries(clinicId)
            .AsNoTracking()
            .Where(j => j.EntryDate <= asOfDate)
            .ToListAsync();

        var journalTotals = entries
            .SelectMany(e => e.Lines)
            .GroupBy(l => l.AccountName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    chartByName.TryGetValue(g.Key, out var chart);
                    return new AccountTotals(
                        chart?.CategoryType ?? g.First().AccountCategory ?? "",
                        g.Sum(x => x.Debit),
                        g.Sum(x => x.Credit));
                },
                StringComparer.OrdinalIgnoreCase);

        var rows = new List<TrialBalanceRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var acct in chartAccounts)
        {
            seen.Add(acct.Name);
            journalTotals.TryGetValue(acct.Name, out var totals);
            rows.Add(new TrialBalanceRow(
                acct.AccountNo,
                acct.CategoryType,
                acct.Name,
                totals?.Debit ?? 0,
                totals?.Credit ?? 0));
        }

        foreach (var extra in journalTotals.Keys.Where(k => !seen.Contains(k)).OrderBy(k => k))
        {
            var totals = journalTotals[extra];
            rows.Add(new TrialBalanceRow(0, totals.Category, extra, totals.Debit, totals.Credit));
        }

        return rows;
    }

    private static bool AccountNamesEqual(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private sealed record AccountTotals(string Category, decimal Debit, decimal Credit);

    public sealed record JournalIntegrityReport(
        int TotalEntries,
        int PostedLineCount,
        decimal TotalDebits,
        decimal TotalCredits,
        IReadOnlyList<JournalIntegrityReport.UnbalancedEntry> UnbalancedEntries,
        int DraftEntryCount,
        int DeletedEntryCount)
    {
        public bool IsTrialBalanceBalanced => Math.Abs(TotalDebits - TotalCredits) < 0.01m;

        public sealed record UnbalancedEntry(
            int EntryNo,
            DateTime EntryDate,
            string SourceType,
            decimal TotalDebit,
            decimal TotalCredit);
    }

    public sealed record GeneralLedgerRow(
        DateTime EntryDate,
        int EntryNo,
        string SourceType,
        int AccountNo,
        string AccountName,
        string? Description,
        decimal Debit,
        decimal Credit,
        decimal RunningBalance,
        string? PatientName,
        string? DoctorName,
        bool IsOpeningBalance = false);

    public sealed record TrialBalanceRow(
        int AccountNo,
        string AccountCategory,
        string AccountName,
        decimal TotalDebit,
        decimal TotalCredit)
    {
        public decimal Balance => TotalDebit - TotalCredit;

        /// <summary>Net debit column for standard trial balance presentation.</summary>
        public decimal DisplayDebit => Balance > 0 ? Balance : 0;

        /// <summary>Net credit column for standard trial balance presentation.</summary>
        public decimal DisplayCredit => Balance < 0 ? -Balance : 0;
    }
}
