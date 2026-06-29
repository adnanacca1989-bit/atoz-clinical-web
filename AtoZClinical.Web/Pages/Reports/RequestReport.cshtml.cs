using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.Reports;

public class RequestReportModel : PageModel
{
    private readonly ClinicContextService _clinicContext;
    private readonly RequestReportService _report;

    public RequestReportModel(ClinicContextService clinicContext, RequestReportService report)
    {
        _clinicContext = clinicContext;
        _report = report;
    }

    [BindProperty(SupportsGet = true)]
    public DateTime FromDate { get; set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    [BindProperty(SupportsGet = true)]
    public DateTime ToDate { get; set; } = DateTime.Today;

    [BindProperty(SupportsGet = true)]
    public string? DoctorName { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? PatientName { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool PendingOnly { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool NonZero { get; set; }

    public List<RequestReportService.RequestReportRow> Results { get; private set; } = [];
    public decimal TotalAmount { get; private set; }

    public async Task<IActionResult> OnGetAsync() => await RunAsync();
    public Task<IActionResult> OnPostRunAsync() => RunAsync();
    public IActionResult OnPostClearAsync() => RedirectToPage();

    private async Task<IActionResult> RunAsync()
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();

        var report = await _report.BuildAsync(
            clinicId.Value, FromDate, ToDate, PatientName, DoctorName, PendingOnly, NonZero);

        Results = report.Rows.ToList();
        TotalAmount = report.TotalAmount;
        return Page();
    }

    public async Task<IActionResult> OnPostExportAsync()
    {
        await RunAsync();
        var bytes = ReportExcelService.Export("Request Report",
            [
                "No", "Request Date", "Request No", "Type of Transaction", "Patient ID", "Patient Name",
                "Appointment Date", "Appointment Time", "Age", "Sex", "Phone", "City", "Doctor Name", "Specialty",
                "Amount Request", "Result Invoice", "Created By User", "Result Date", "Result ID"
            ],
            Results.Select((r, i) => new object?[]
            {
                i + 1,
                r.RequestDate.ToString("d"),
                r.RequestNo,
                r.TransactionType,
                r.PatientId,
                r.PatientName,
                r.AppointmentDate?.ToString("d"),
                FormatTime(r.AppointmentTime),
                r.Age,
                r.Sex,
                r.Phone,
                r.City,
                r.DoctorName,
                r.Specialty,
                r.AmountRequest,
                r.ResultInvoiceStatus,
                r.CreatedByUser,
                r.ResultDate?.ToString("d"),
                r.ResultId
            }));
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"RequestReport_{DateTime.Now:yyyyMMdd}.xlsx");
    }

    public static string FormatTime(TimeSpan? time) =>
        time is null ? "" : DateTime.Today.Add(time.Value).ToString("t");

    public static string StatusCssClass(string status) =>
        status == RequestReportService.StatusCreated ? "request-status-created" : "request-status-pending";
}
