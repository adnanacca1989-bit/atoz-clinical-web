using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AtoZClinical.Tests;

public class FinancialStatementTests
{
    [Fact]
    public async Task Invoice_journal_uses_net_amount_for_accounts_receivable()
    {
        var clinicId = Guid.NewGuid();
        await using var db = await SqliteTestDatabase.CreateAsync(TestClinicProvider.ForClinic(clinicId));
        db.Db.Clinics.Add(new Clinic { Id = clinicId, ClinicCode = "TST", Name = "Test Clinic" });
        await DatabaseInitializer.EnsureStandardChartAccountsAsync(db.Db, clinicId);

        var invoiceId = Guid.NewGuid();
        var invoice = new Invoice
        {
            Id = invoiceId,
            ClinicId = clinicId,
            InvoiceNo = 2,
            PatientName = "Zaid",
            DoctorName = "Zainab",
            InvoiceDate = new DateTime(2026, 6, 10),
            SubTotal = 41_000m,
            Discount = 6_000m,
            TotalAmount = 35_000m,
            UpdatedAt = DateTime.UtcNow
        };
        invoice.Lines.Add(new InvoiceLine
        {
            Id = Guid.NewGuid(),
            InvoiceId = invoiceId,
            ServiceName = "Consultation",
            LineTotal = 41_000m
        });
        db.Db.Invoices.Add(invoice);
        await db.Db.SaveChangesAsync();

        var sync = new ClinicalJournalSyncService(db.Db, NullLogger<ClinicalJournalSyncService>.Instance);
        await sync.EnsureClinicalJournalsAsync(clinicId);

        var journal = await db.Db.JournalReportService_GetLines(clinicId);
        var arLine = journal.Single(l => l.AccountName == "Accounts Receivable");
        Assert.Equal(35_000m, arLine.Debit);
        Assert.Equal(0m, arLine.Credit);
        Assert.Equal(35_000m, journal.Sum(l => l.Credit));
    }

    [Fact]
    public async Task Balance_sheet_includes_accounts_receivable_and_balances()
    {
        var clinicId = Guid.NewGuid();
        await using var db = await SqliteTestDatabase.CreateAsync(TestClinicProvider.ForClinic(clinicId));
        db.Db.Clinics.Add(new Clinic { Id = clinicId, ClinicCode = "TST", Name = "Test Clinic" });
        await DatabaseInitializer.EnsureStandardChartAccountsAsync(db.Db, clinicId);

        var invoiceId = Guid.NewGuid();
        db.Db.Invoices.Add(new Invoice
        {
            Id = invoiceId,
            ClinicId = clinicId,
            InvoiceNo = 1,
            PatientName = "Zaid",
            DoctorName = "Mohammed",
            InvoiceDate = new DateTime(2026, 6, 1),
            TotalAmount = 59_000m,
            UpdatedAt = DateTime.UtcNow,
            Lines =
            [
                new InvoiceLine
                {
                    Id = Guid.NewGuid(),
                    InvoiceId = invoiceId,
                    ServiceName = "Consultation",
                    LineTotal = 59_000m
                }
            ]
        });
        db.Db.CashReceipts.Add(new CashReceipt
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            ReceiptNo = 1,
            PatientName = "Zaid",
            DoctorName = "Mohammed",
            ReceiptDate = new DateTime(2026, 6, 2),
            Amount = 59_000m,
            PaymentMethod = "Cash",
            UpdatedAt = DateTime.UtcNow
        });
        await db.Db.SaveChangesAsync();

        var sync = new ClinicalJournalSyncService(db.Db, NullLogger<ClinicalJournalSyncService>.Instance);
        var journal = new JournalReportService(db.Db, sync);
        await sync.EnsureClinicalJournalsAsync(clinicId);

        var tb = await journal.GetTrialBalanceAsync(clinicId, new DateTime(2026, 6, 28));
        var snapshot = FinancialStatementBuilder.BuildBalanceSheet(tb);

        var ar = snapshot.Assets.SingleOrDefault(a => a.Account == "Accounts Receivable");
        Assert.NotNull(ar);
        Assert.Equal(0m, ar!.Amount);

        var cash = snapshot.Assets.Single(a => a.Account == "Cash");
        Assert.Equal(59_000m, cash.Amount);
        Assert.Equal(snapshot.TotalLiabilities + snapshot.TotalEquity, snapshot.TotalAssets, 2);
    }

    [Fact]
    public async Task Cash_report_closing_matches_trial_balance_liquid_accounts()
    {
        var clinicId = Guid.NewGuid();
        await using var db = await SqliteTestDatabase.CreateAsync(TestClinicProvider.ForClinic(clinicId));
        db.Db.Clinics.Add(new Clinic { Id = clinicId, ClinicCode = "TST", Name = "Test Clinic" });
        await DatabaseInitializer.EnsureStandardChartAccountsAsync(db.Db, clinicId);

        db.Db.CashReceipts.Add(new CashReceipt
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            ReceiptNo = 1,
            PatientName = "AAA",
            DoctorName = "Zainab",
            ReceiptDate = new DateTime(2026, 6, 5),
            Amount = 25_000m,
            PaymentMethod = "Cash",
            UpdatedAt = DateTime.UtcNow
        });
        db.Db.CashReceipts.Add(new CashReceipt
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            ReceiptNo = 2,
            PatientName = "Zaid",
            DoctorName = "Zainab",
            ReceiptDate = new DateTime(2026, 6, 6),
            Amount = 25_000m,
            PaymentMethod = "Bank Transfer",
            UpdatedAt = DateTime.UtcNow
        });
        await db.Db.SaveChangesAsync();

        var sync = new ClinicalJournalSyncService(db.Db, NullLogger<ClinicalJournalSyncService>.Instance);
        var journal = new JournalReportService(db.Db, sync);
        await sync.EnsureClinicalJournalsAsync(clinicId);

        var tb = await journal.GetTrialBalanceAsync(clinicId, new DateTime(2026, 6, 28));
        var liquid = FinancialStatementBuilder.ResolveLiquidAccounts("All", await db.Db.ChartAccounts.ForClinic(clinicId).ToListAsync());
        var closing = FinancialStatementBuilder.SumLiquidBalance(tb, liquid);

        Assert.Equal(25_000m, tb.Single(r => r.AccountName == "Cash").Balance);
        Assert.Equal(25_000m, tb.Single(r => r.AccountName == "Bank").Balance);
        Assert.Equal(50_000m, closing);
    }
}

internal static class JournalTestExtensions
{
    public static async Task<List<JournalEntryLine>> JournalReportService_GetLines(this ClinicalDbContext db, Guid clinicId) =>
        await db.JournalEntryLines
            .Where(l => l.JournalEntry!.ClinicId == clinicId)
            .ToListAsync();
}
