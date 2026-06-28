using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Web.Pages.Reports;

public class CashReportModel : PageModel
{
    private readonly ClinicalDbContext _db;
    private readonly ClinicContextService _clinicContext;
    private readonly ClinicalJournalSyncService _journalSync;
    private readonly JournalReportService _journal;

    public CashReportModel(
        ClinicalDbContext db,
        ClinicContextService clinicContext,
        ClinicalJournalSyncService journalSync,
        JournalReportService journal)
    {
        _db = db;
        _clinicContext = clinicContext;
        _journalSync = journalSync;
        _journal = journal;
    }

    [BindProperty(SupportsGet = true)]
    public DateTime FromDate { get; set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    [BindProperty(SupportsGet = true)]
    public DateTime ToDate { get; set; } = DateTime.Today;

    [BindProperty(SupportsGet = true)]
    public string? PatientName { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? PatientBarcode { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? DoctorName { get; set; }

    [BindProperty(SupportsGet = true)]
    public string TransactionType { get; set; } = "All";

    [BindProperty(SupportsGet = true)]
    public string? PaymentMethod { get; set; }

    public List<CashReportRow> Results { get; private set; } = [];
    public decimal OpeningBalance { get; private set; }
    public decimal TotalDebit { get; private set; }
    public decimal TotalCredit { get; private set; }
    public decimal ClosingBalance { get; private set; }

    public async Task<IActionResult> OnGetAsync() => await RunAsync();
    public Task<IActionResult> OnPostRunAsync() => RunAsync();
    public IActionResult OnPostClearAsync() => RedirectToPage();

    private async Task<IActionResult> RunAsync()
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();

        var id = clinicId.Value;
        var from = FromDate.Date;
        var to = ToDate.Date;

        await _journalSync.EnsureClinicalJournalsAsync(id);

        var chartAccounts = await _db.ChartAccounts.ForClinic(id).AsNoTracking().ToListAsync();
        var liquidAccounts = FinancialStatementBuilder
            .ResolveLiquidAccounts(PaymentMethod, chartAccounts)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var openingDate = from.AddDays(-1);
        var openingTb = await _journal.GetTrialBalanceAsync(id, openingDate);
        OpeningBalance = FinancialStatementBuilder.SumLiquidBalance(openingTb, liquidAccounts.ToList());

        var rows = new List<CashReportRow>();

        if (TransactionType is "All" or "Receipt")
        {
            var receipts = await _db.CashReceipts
                .ForClinic(id)
                .Where(c => c.ReceiptDate >= from && c.ReceiptDate <= to)
                .OrderBy(c => c.ReceiptDate).ThenBy(c => c.ReceiptNo)
                .ToListAsync();

            if (!string.IsNullOrWhiteSpace(PatientName))
                receipts = receipts.Where(r => r.PatientName?.Contains(PatientName, StringComparison.OrdinalIgnoreCase) == true).ToList();
            if (!string.IsNullOrWhiteSpace(PatientBarcode))
                receipts = receipts.Where(r => r.PatientId?.Equals(PatientBarcode.Trim(), StringComparison.OrdinalIgnoreCase) == true).ToList();
            if (!string.IsNullOrWhiteSpace(DoctorName))
                receipts = receipts.Where(r => r.DoctorName?.Contains(DoctorName, StringComparison.OrdinalIgnoreCase) == true).ToList();
            if (!string.IsNullOrWhiteSpace(PaymentMethod) && !string.Equals(PaymentMethod, "All", StringComparison.OrdinalIgnoreCase))
                receipts = receipts.Where(r => string.Equals(r.PaymentMethod, PaymentMethod, StringComparison.OrdinalIgnoreCase)).ToList();

            rows.AddRange(receipts
                .Where(r => liquidAccounts.Contains(
                    PaymentJournalHelper.ResolvePaymentCreditAccount(r.PaymentMethod, chartAccounts, r.ChartAccountName),
                    StringComparer.OrdinalIgnoreCase))
                .Select(r => new CashReportRow(
                    "Cash Receipt",
                    r.ReceiptDate,
                    r.PatientName ?? "",
                    r.DoctorName ?? "",
                    r.ReceiptDate,
                    null,
                    r.Amount,
                    0m,
                    0m,
                    r.ReceiptNo,
                    r.PaymentMethod)));
        }

        if (TransactionType is "All" or "Payment")
        {
            var payments = await _db.CashPayments
                .ForClinic(id)
                .Where(p => p.PaymentDate >= from && p.PaymentDate <= to)
                .OrderBy(p => p.PaymentDate).ThenBy(p => p.PaymentNo)
                .ToListAsync();

            if (!string.IsNullOrWhiteSpace(PatientName))
                payments = payments.Where(p => p.PayeeName?.Contains(PatientName, StringComparison.OrdinalIgnoreCase) == true).ToList();
            if (!string.IsNullOrWhiteSpace(PaymentMethod) && !string.Equals(PaymentMethod, "All", StringComparison.OrdinalIgnoreCase))
                payments = payments.Where(p => string.Equals(p.PaymentMethod, PaymentMethod, StringComparison.OrdinalIgnoreCase)).ToList();

            rows.AddRange(payments
                .Where(p => liquidAccounts.Contains(
                    PaymentJournalHelper.ResolvePaymentCreditAccount(p.PaymentMethod, chartAccounts, p.ChartAccountName),
                    StringComparer.OrdinalIgnoreCase))
                .Select(p => new CashReportRow(
                    "Cash Payment",
                    p.PaymentDate,
                    p.PayeeName ?? "",
                    "",
                    null,
                    p.PaymentDate,
                    0m,
                    p.Amount,
                    0m,
                    p.PaymentNo,
                    p.PaymentMethod)));
        }

        if (TransactionType is "All" or "Payment" or "Expense")
        {
            var expenses = await _db.ExpenseVouchers
                .ForClinic(id)
                .Where(v => v.ExpenseDate >= from && v.ExpenseDate <= to)
                .OrderBy(v => v.ExpenseDate).ThenBy(v => v.ExpenseNo)
                .ToListAsync();

            foreach (var expense in expenses)
            {
                var creditAccount = ExpenseAccountingHelper.ResolveCreditAccountName(
                    expense.PaymentMethod, chartAccounts, expense.CreditAccountName);
                if (!liquidAccounts.Contains(creditAccount, StringComparer.OrdinalIgnoreCase))
                    continue;

                if (!string.IsNullOrWhiteSpace(PatientName) &&
                    expense.PayeeName?.Contains(PatientName, StringComparison.OrdinalIgnoreCase) != true)
                    continue;

                if (!string.IsNullOrWhiteSpace(PaymentMethod) &&
                    !string.Equals(PaymentMethod, "All", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(expense.PaymentMethod, PaymentMethod, StringComparison.OrdinalIgnoreCase))
                    continue;

                rows.Add(new CashReportRow(
                    "Expense",
                    expense.ExpenseDate,
                    expense.PayeeName ?? "",
                    "",
                    null,
                    expense.ExpenseDate,
                    0m,
                    expense.TotalAmount,
                    0m,
                    expense.ExpenseNo,
                    expense.PaymentMethod));
            }
        }

        rows = rows
            .OrderBy(r => r.TransactionDate)
            .ThenBy(r => r.TransactionType)
            .ThenBy(r => r.PaymentMethod)
            .ThenBy(r => r.DocumentNo)
            .ToList();

        var balance = OpeningBalance;
        foreach (var row in rows)
        {
            balance += row.Debit - row.Credit;
            row.Balance = balance;
        }

        Results = rows;
        TotalDebit = rows.Sum(r => r.Debit);
        TotalCredit = rows.Sum(r => r.Credit);

        var closingTb = await _journal.GetTrialBalanceAsync(id, to);
        ClosingBalance = FinancialStatementBuilder.SumLiquidBalance(closingTb, liquidAccounts.ToList());

        return Page();
    }

    public async Task<IActionResult> OnPostExportAsync()
    {
        await RunAsync();
        var bytes = ReportExcelService.Export("Cash Report",
            ["Type", "Date", "Patient", "Doctor", "Debit", "Credit", "Balance", "Doc No", "Method"],
            Results.Select(r => new object?[]
            {
                r.TransactionType, r.TransactionDate, r.PatientName, r.DoctorName, r.Debit, r.Credit, r.Balance, r.DocumentNo, r.PaymentMethod
            }));
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"CashReport_{DateTime.Now:yyyyMMdd}.xlsx");
    }

    public sealed class CashReportRow
    {
        public CashReportRow(
            string transactionType,
            DateTime transactionDate,
            string patientName,
            string doctorName,
            DateTime? receiptDate,
            DateTime? paymentDate,
            decimal debit,
            decimal credit,
            decimal balance,
            int documentNo,
            string paymentMethod)
        {
            TransactionType = transactionType;
            TransactionDate = transactionDate;
            PatientName = patientName;
            DoctorName = doctorName;
            ReceiptDate = receiptDate;
            PaymentDate = paymentDate;
            Debit = debit;
            Credit = credit;
            Balance = balance;
            DocumentNo = documentNo;
            PaymentMethod = paymentMethod;
        }

        public string TransactionType { get; }
        public DateTime TransactionDate { get; }
        public string PatientName { get; }
        public string DoctorName { get; }
        public DateTime? ReceiptDate { get; }
        public DateTime? PaymentDate { get; }
        public decimal Debit { get; }
        public decimal Credit { get; }
        public decimal Balance { get; set; }
        public int DocumentNo { get; }
        public string PaymentMethod { get; }
    }
}
