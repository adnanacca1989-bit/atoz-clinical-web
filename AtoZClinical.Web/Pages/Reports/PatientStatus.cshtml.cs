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

    public PatientStatusModel(ClinicalDbContext db, ClinicContextService clinicContext)
    {
        _db = db;
        _clinicContext = clinicContext;
    }

    [BindProperty(SupportsGet = true)]
    public DateTime FromDate { get; set; } = ClinicClock.Today.AddMonths(-1);

    [BindProperty(SupportsGet = true)]
    public DateTime ToDate { get; set; } = ClinicClock.Today;

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
            .Where(p => p.ClinicId == clinicId)
            .ToListAsync();

        patients = patients.Where(p => PatientReportDateHelper.IsInDateRange(p, from, to)).ToList();

        var invoiceBalances = await _db.Invoices
            .AsNoTracking()
            .Where(i => i.ClinicId == clinicId)
            .Select(i => new { i.PatientId, i.PatientName, i.BalanceDue })
            .ToListAsync();

        var balanceByPatientId = invoiceBalances
            .Where(i => !string.IsNullOrWhiteSpace(i.PatientId))
            .GroupBy(i => i.PatientId!)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.BalanceDue), StringComparer.OrdinalIgnoreCase);

        var balanceByName = invoiceBalances
            .Where(i => !string.IsNullOrWhiteSpace(i.PatientName))
            .GroupBy(i => i.PatientName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.BalanceDue), StringComparer.OrdinalIgnoreCase);

        var now = ClinicClock.Now;
        var rows = patients.Select(p =>
        {
            var fullName = p.FullName;
            decimal balance = 0;
            if (!string.IsNullOrWhiteSpace(p.PatientNo) && balanceByPatientId.TryGetValue(p.PatientNo, out var byId))
                balance = byId;
            else if (!string.IsNullOrWhiteSpace(fullName) && balanceByName.TryGetValue(fullName, out var byName))
                balance = byName;

            var effectiveDate = PatientReportDateHelper.GetEffectiveAppointmentDate(p);
            var liveStatus = AppointmentReminderService.ResolveVisitStatus(p, now);

            return new StatusRow(
                fullName,
                p.PatientNo,
                effectiveDate,
                p.AppointmentTime,
                p.DoctorName ?? "",
                p.Specialty ?? "",
                balance,
                liveStatus);
        }).OrderBy(r => r.AppointmentDate).ToList();

        if (!Status.Equals("All", StringComparison.OrdinalIgnoreCase))
            rows = rows.Where(r =>
                PatientVisitStatuses.Normalize(r.Status).Equals(Status, StringComparison.OrdinalIgnoreCase)).ToList();

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
