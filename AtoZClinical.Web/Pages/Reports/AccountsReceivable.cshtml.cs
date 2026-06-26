using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.Reports;

public class AccountsReceivableModel : PageModel
{
    private readonly ClinicContextService _clinicContext;
    private readonly ArReportService _ar;

    public AccountsReceivableModel(ClinicContextService clinicContext, ArReportService ar)
    {
        _clinicContext = clinicContext;
        _ar = ar;
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

    public List<ArReportRow> Results { get; private set; } = [];
    public decimal TotalCashReceipt { get; private set; }
    public decimal TotalCashPayment { get; private set; }
    public decimal TotalInvoiceAmount { get; private set; }
    public decimal TotalDiscount { get; private set; }
    public decimal TotalEndingBalance { get; private set; }
    public decimal TotalPatientCredit { get; private set; }

    public async Task<IActionResult> OnGetAsync() => await RunAsync();

    public Task<IActionResult> OnPostRunAsync() => RunAsync();

    public IActionResult OnPostClearAsync() => RedirectToPage();

    private async Task<IActionResult> RunAsync()
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();

        var report = await _ar.BuildAsync(
            clinicId.Value, FromDate, ToDate, PatientName, PatientBarcode, DoctorName);

        Results = report.Rows;
        TotalCashReceipt = report.TotalCashReceipt;
        TotalCashPayment = report.TotalCashPayment;
        TotalInvoiceAmount = report.TotalInvoiceAmount;
        TotalDiscount = report.TotalDiscount;
        TotalEndingBalance = report.TotalEndingBalance;
        TotalPatientCredit = report.TotalPatientCredit;

        return Page();
    }

    public async Task<IActionResult> OnPostExportAsync()
    {
        await RunAsync();
        var bytes = ReportExcelService.Export("Accounts Receivable",
            [
                "Invoice ID", "Invoice Date", "Patient", "Doctor", "Specialty", "Gender", "City",
                "Mother Name", "Married Status", "Health Insurance", "Health Insurance No",
                "Appointment Date", "Appointment Time", "Cash Receipt (Applied)", "Cash Payment",
                "Total Received", "Patient Credit", "Total Invoice", "Discount", "Ending Balance (Dr/Cr)", "Aging Days", "Status"
            ],
            Results.Select(r => new object?[]
            {
                r.InvoiceId, r.InvoiceDate, r.Patient, r.Doctor, r.Specialty, r.Gender, r.City,
                r.MotherName, r.MarriedStatus, r.HealthInsurance, r.HealthInsuranceNo,
                r.AppointmentDate?.ToString("d"), FormatTime(r.AppointmentTime),
                r.CashReceipt, r.CashPayment, r.TotalReceived, r.PatientCredit,
                r.TotalInvoice, r.Discount, FormatEndingBalance(r.EndingBalance),
                r.AgingDays, r.Status
            }));
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"AccountsReceivable_{DateTime.Now:yyyyMMdd}.xlsx");
    }

    private static string FormatTime(TimeSpan? time) =>
        time is null ? "" : DateTime.Today.Add(time.Value).ToString("t");

    public static string FormatEndingBalance(decimal balance) => ArBalanceFormatter.Format(balance);
}
