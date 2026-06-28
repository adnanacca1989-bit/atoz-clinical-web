using AtoZClinical.Infrastructure.Services;
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
    private readonly ArReportService _ar;

    public PatientStatusModel(ClinicalDbContext db, ClinicContextService clinicContext, ArReportService ar)
    {
        _db = db;
        _clinicContext = clinicContext;
        _ar = ar;
    }

    [BindProperty(SupportsGet = true)]
    public DateTime FromDate { get; set; } = ClinicClock.Today.AddMonths(-6);

    [BindProperty(SupportsGet = true)]
    public DateTime ToDate { get; set; } = ClinicClock.Today.AddMonths(3);

    [BindProperty(SupportsGet = true)]
    public string Status { get; set; } = "All";

    [BindProperty(SupportsGet = true)]
    public string? PatientBarcode { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? PatientName { get; set; }

    public List<StatusRow> Results { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync() => await RunAsync();

    public Task<IActionResult> OnPostRunAsync() => RunAsync();

    public IActionResult OnPostClearAsync() => RedirectToPage();

    private async Task<IActionResult> RunAsync()
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();

        var from = ClinicClock.ToClinicDate(FromDate);
        var to = ClinicClock.ToClinicDate(ToDate);

        var patients = await _db.Patients
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(p => p.ClinicId == clinicId)
            .ToListAsync();

        var doctors = await _db.Doctors
            .AsNoTracking()
            .Where(d => d.ClinicId == clinicId)
            .ToListAsync();

        var demographics = new ClinicalDemographicsSyncService(_db);

        patients = patients.Where(p => PatientReportDateHelper.IsInDateRange(p, from, to)).ToList();

        var arReport = await _ar.BuildAsync(clinicId.Value, null, null, null, null, null);
        var now = ClinicClock.Now;

        var rows = patients.Select(p =>
        {
            var fullName = p.FullName;
            var liveDoctor = demographics.ResolveDoctorFromList(doctors, p.DoctorRecordId, p.DoctorName);
            var doctorName = liveDoctor?.Name ?? p.DoctorName ?? "";
            var specialty = liveDoctor?.Specialty ?? p.Specialty ?? "";
            var balance = ArReportService.ResolvePatientDoctorEndingBalance(
                arReport.Rows, fullName, doctorName);

            var effectiveDate = PatientReportDateHelper.GetEffectiveAppointmentDate(p);
            var liveStatus = AppointmentReminderService.ResolveVisitStatus(p, now);

            return new StatusRow(
                fullName,
                p.PatientNo,
                effectiveDate,
                p.AppointmentTime,
                doctorName,
                specialty,
                balance,
                liveStatus);
        }).OrderBy(r => r.AppointmentDate).ToList();

        if (!Status.Equals("All", StringComparison.OrdinalIgnoreCase))
            rows = rows.Where(r => StatusMatchesFilter(r.Status, Status)).ToList();

        if (!string.IsNullOrWhiteSpace(PatientBarcode))
        {
            var barcode = PatientBarcode.Trim();
            rows = rows.Where(r =>
                r.PatientNo?.Equals(barcode, StringComparison.OrdinalIgnoreCase) == true ||
                r.PatientNo?.Contains(barcode, StringComparison.OrdinalIgnoreCase) == true).ToList();
        }

        if (!string.IsNullOrWhiteSpace(PatientName))
        {
            var name = PatientName.Trim();
            rows = rows.Where(r =>
                r.PatientName.Contains(name, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        Results = rows;
        return Page();
    }

    private static bool StatusMatchesFilter(string status, string filter)
    {
        if (string.Equals(status, filter, StringComparison.OrdinalIgnoreCase)) return true;
        return PatientVisitStatuses.Normalize(status).Equals(filter, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<IActionResult> OnPostExportAsync()
    {
        await RunAsync();
        var bytes = ReportExcelService.Export("Patient Status",
            ["Patient", "Appointment Date", "Time", "Doctor", "Specialty", "Invoice Balance", "Status"],
            Results.Select(r => new object?[]
            {
                r.PatientName, r.AppointmentDate, r.AppointmentTime?.ToString(@"hh\:mm"), r.DoctorName, r.Specialty,
                ArBalanceFormatter.Format(r.InvoiceBalance), r.Status
            }));
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"PatientStatus_{ClinicClock.Today:yyyyMMdd}.xlsx");
    }

    public sealed record StatusRow(
        string PatientName,
        string? PatientNo,
        DateTime AppointmentDate,
        TimeSpan? AppointmentTime,
        string DoctorName,
        string Specialty,
        decimal InvoiceBalance,
        string Status);
}
