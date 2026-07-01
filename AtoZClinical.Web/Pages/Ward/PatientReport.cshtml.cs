using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.Ward;

public class PatientReportModel : PageModel
{
    private readonly WardPatientReportService _service;
    private readonly ClinicContextService _clinicContext;

    public PatientReportModel(WardPatientReportService service, ClinicContextService clinicContext)
    {
        _service = service;
        _clinicContext = clinicContext;
    }

    [BindProperty(SupportsGet = true)]
    public DateTime FromDate { get; set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    [BindProperty(SupportsGet = true)]
    public DateTime ToDate { get; set; } = DateTime.Today;

    [BindProperty(SupportsGet = true)]
    public string? PatientBarcode { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? PatientName { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? DoctorName { get; set; }

    public List<WardPatientReportService.WardPatientReportRow> Results { get; private set; } = [];
    public decimal TotalCashReceipt { get; private set; }
    public decimal TotalCashPayment { get; private set; }
    public decimal TotalInvoiceAmount { get; private set; }
    public decimal TotalDiscount { get; private set; }
    public decimal TotalInitialAmount { get; private set; }
    public decimal TotalEndingBalance { get; private set; }

    public async Task<IActionResult> OnGetAsync() => await RunAsync();
    public Task<IActionResult> OnPostRunAsync() => RunAsync();
    public IActionResult OnPostClearAsync() => RedirectToPage();

    private async Task<IActionResult> RunAsync()
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();

        var report = await _service.GetRowsAsync(
            clinicId.Value, FromDate, ToDate, PatientBarcode, PatientName, DoctorName);

        Results = report.Rows.ToList();
        TotalCashReceipt = report.TotalCashReceipt;
        TotalCashPayment = report.TotalCashPayment;
        TotalInvoiceAmount = report.TotalInvoiceAmount;
        TotalDiscount = report.TotalDiscount;
        TotalInitialAmount = report.TotalInitialAmount;
        TotalEndingBalance = report.TotalEndingBalance;

        return Page();
    }

    public async Task<IActionResult> OnPostExportAsync()
    {
        var result = await RunAsync();
        if (result is not PageResult) return result;

        var bytes = ReportExcelService.Export("Ward Patient Report",
            [
                "ID", "Patient Name", "Age", "City", "Mother Name", "National ID", "Doctor Name", "Specialty",
                "Date of Surgery", "Time of Surgery", "Type of Surgery", "Classify", "Initial Amount",
                "Room Number", "Enter Date", "Exit Date", "Enter Time", "Exit Time", "Days", "Note",
                "Cash Receipt", "Cash Payment", "Invoice Amount", "Discount", "Ending Balance"
            ],
            Results.Select(r => new object?[]
            {
                r.Id, r.PatientName, r.Age, r.City, r.MotherName, r.NationalId, r.DoctorName, r.Specialty,
                r.SurgeryDate?.ToString("d"), FormatTime(r.SurgeryTime), r.TypeOfSurgery, r.Classify, r.InitialAmount,
                r.RoomNumber, r.EnterDate?.ToString("d"), r.ExitDate?.ToString("d"),
                FormatTime(r.EnterTime), FormatTime(r.ExitTime), r.Days, r.Note,
                r.CashReceipt, r.CashPayment, r.InvoiceAmount, r.Discount,
                FormatEndingBalance(r.EndingBalance)
            }));
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"WardPatientReport_{DateTime.Now:yyyyMMdd}.xlsx");
    }

    public static string FormatTime(TimeSpan? time) =>
        time is null ? "" : DateTime.Today.Add(time.Value).ToString("h:mm tt");

    public static string FormatEndingBalance(decimal balance) => ArBalanceFormatter.Format(balance);
}
