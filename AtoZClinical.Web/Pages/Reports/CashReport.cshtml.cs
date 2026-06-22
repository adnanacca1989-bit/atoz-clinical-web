using AtoZClinical.Infrastructure.Data;

using AtoZClinical.Web.Services;

using Microsoft.AspNetCore.Mvc;

using Microsoft.AspNetCore.Mvc.RazorPages;

using Microsoft.EntityFrameworkCore;



namespace AtoZClinical.Web.Pages.Reports;



public class CashReportModel : PageModel

{

    private readonly ClinicalDbContext _db;

    private readonly ClinicContextService _clinicContext;



    public CashReportModel(ClinicalDbContext db, ClinicContextService clinicContext)

    {

        _db = db;

        _clinicContext = clinicContext;

    }



    [BindProperty(SupportsGet = true)]

    public DateTime FromDate { get; set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);



    [BindProperty(SupportsGet = true)]

    public DateTime ToDate { get; set; } = DateTime.Today;



    [BindProperty(SupportsGet = true)]

    public string? PatientName { get; set; }



    [BindProperty(SupportsGet = true)]

    public string? DoctorName { get; set; }



    [BindProperty(SupportsGet = true)]

    public string TransactionType { get; set; } = "All";



    public List<CashReportRow> Results { get; private set; } = [];

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



        var from = FromDate.Date;

        var to = ToDate.Date;



        var rows = new List<CashReportRow>();



        if (TransactionType is "All" or "Receipt")

        {

            var receipts = await _db.CashReceipts

                .Where(c => c.ClinicId == clinicId && c.ReceiptDate >= from && c.ReceiptDate <= to)

                .OrderBy(c => c.ReceiptDate).ThenBy(c => c.ReceiptNo)

                .ToListAsync();



            if (!string.IsNullOrWhiteSpace(PatientName))

                receipts = receipts.Where(r => r.PatientName?.Contains(PatientName, StringComparison.OrdinalIgnoreCase) == true).ToList();

            if (!string.IsNullOrWhiteSpace(DoctorName))

                receipts = receipts.Where(r => r.DoctorName?.Contains(DoctorName, StringComparison.OrdinalIgnoreCase) == true).ToList();



            rows.AddRange(receipts.Select(r => new CashReportRow(
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

                .Where(p => p.ClinicId == clinicId && p.PaymentDate >= from && p.PaymentDate <= to)

                .OrderBy(p => p.PaymentDate).ThenBy(p => p.PaymentNo)

                .ToListAsync();



            if (!string.IsNullOrWhiteSpace(PatientName))

                payments = payments.Where(p => p.PayeeName?.Contains(PatientName, StringComparison.OrdinalIgnoreCase) == true).ToList();



            rows.AddRange(payments.Select(p => new CashReportRow(
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



        rows = rows

            .OrderBy(r => r.TransactionDate)

            .ThenBy(r => r.TransactionType)

            .ThenBy(r => r.DocumentNo)

            .ToList();



        decimal balance = 0m;

        foreach (var row in rows)

        {

            balance += row.Debit - row.Credit;

            row.Balance = balance;

        }



        Results = rows;

        TotalDebit = rows.Sum(r => r.Debit);

        TotalCredit = rows.Sum(r => r.Credit);

        ClosingBalance = balance;

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


