using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.Reports;

public class DoctorReportModel : PageModel
{
    private readonly DoctorReportService _service;
    private readonly ClinicContextService _clinicContext;

    public DoctorReportModel(DoctorReportService service, ClinicContextService clinicContext)
    {
        _service = service;
        _clinicContext = clinicContext;
    }

    [BindProperty(SupportsGet = true)]
    public DateTime FromDate { get; set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    [BindProperty(SupportsGet = true)]
    public DateTime ToDate { get; set; } = DateTime.Today;

    [BindProperty(SupportsGet = true)]
    public string? DoctorName { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? PatientName { get; set; }

    public List<DoctorReportService.DoctorReportRow> Results { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync() => await RunAsync();
    public Task<IActionResult> OnPostRunAsync() => RunAsync();
    public IActionResult OnPostClearAsync() => RedirectToPage();

    private async Task<IActionResult> RunAsync()
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();

        Results = await _service.GetRowsAsync(
            clinicId.Value, FromDate, ToDate, DoctorName, PatientName);
        return Page();
    }

    public async Task<IActionResult> OnPostExportAsync()
    {
        var result = await RunAsync();
        if (result is not PageResult) return result;

        var bytes = ReportExcelService.Export("Doctor Report",
            ["Doctor", "Specialty", "Patient", "Consultation Fee", "Phone", "Age", "Insurance", "Insurance No",
                "Gender", "City", "Married Status", "Mother Name", "Invoice Amount", "Cash Receipt", "Cash Payment",
                "Visit No", "Appt Date", "Appt Time"],
            Results.Select(r => new object?[]
            {
                r.DoctorName, r.Specialty, r.PatientName, r.ConsultationFee, r.Phone, r.Age,
                r.HealthInsurance, r.HealthInsuranceNo, r.Gender, r.City, r.MarriedStatus, r.MotherName,
                r.InvoiceBillingAmount, r.CashReceipt, r.CashPayment, r.VisitNumber,
                r.AppointmentDate?.ToString("d"), r.AppointmentTime.HasValue
                    ? DateTime.Today.Add(r.AppointmentTime.Value).ToString("h:mm tt") : ""
            }));
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"DoctorReport_{DateTime.Now:yyyyMMdd}.xlsx");
    }
}
