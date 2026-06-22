using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Web.Pages.Reports;

public class PatientStatusModel : PageModel
{
    private readonly ClinicalDbContext _db;
    private readonly ClinicContextService _clinicContext;

    public PatientStatusModel(ClinicalDbContext db, ClinicContextService clinicContext)
    {
        _db = db;
        _clinicContext = clinicContext;
    }

    [BindProperty(SupportsGet = true)]
    public DateTime FromDate { get; set; } = DateTime.Today.AddMonths(-1);

    [BindProperty(SupportsGet = true)]
    public DateTime ToDate { get; set; } = DateTime.Today;

    [BindProperty(SupportsGet = true)]
    public string Status { get; set; } = "All";

    public List<StatusRow> Results { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync() => await RunAsync();

    public Task<IActionResult> OnPostRunAsync() => RunAsync();

    public IActionResult OnPostClearAsync() => RedirectToPage();

    private async Task<IActionResult> RunAsync()
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();

        var now = DateTime.Now;
        var patients = await _db.Patients
            .Where(p => p.ClinicId == clinicId &&
                        p.AppointmentDate >= FromDate.Date &&
                        p.AppointmentDate <= ToDate.Date)
            .ToListAsync();

        var invoices = await _db.Invoices.Where(i => i.ClinicId == clinicId).ToListAsync();

        var rows = patients.Select(p =>
        {
            var inv = invoices.FirstOrDefault(i =>
                (!string.IsNullOrWhiteSpace(p.PatientNo) && i.PatientId == p.PatientNo) ||
                string.Equals(i.PatientName, p.FullName, StringComparison.OrdinalIgnoreCase));
            var visitStatus = AppointmentReminderService.ResolveVisitStatus(p, now);
            return new StatusRow(
                p.FullName,
                p.AppointmentDate,
                p.AppointmentTime,
                p.DoctorName ?? "",
                p.Specialty ?? "",
                inv?.BalanceDue ?? 0,
                visitStatus);
        }).OrderBy(r => r.AppointmentDate).ToList();

        if (!Status.Equals("All", StringComparison.OrdinalIgnoreCase))
            rows = rows.Where(r => r.Status.Equals(Status, StringComparison.OrdinalIgnoreCase)).ToList();

        Results = rows;
        return Page();
    }

    public async Task<IActionResult> OnPostExportAsync()
    {
        await RunAsync();
        var bytes = ReportExcelService.Export("Patient Status",
            ["Patient", "Appointment Date", "Time", "Doctor", "Specialty", "Invoice Balance", "Status"],
            Results.Select(r => new object?[]
            {
                r.PatientName, r.AppointmentDate, r.AppointmentTime?.ToString(@"hh\:mm"), r.DoctorName, r.Specialty, r.InvoiceBalance, r.Status
            }));
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"PatientStatus_{DateTime.Now:yyyyMMdd}.xlsx");
    }

    public sealed record StatusRow(
        string PatientName,
        DateTime? AppointmentDate,
        TimeSpan? AppointmentTime,
        string DoctorName,
        string Specialty,
        decimal InvoiceBalance,
        string Status);
}
