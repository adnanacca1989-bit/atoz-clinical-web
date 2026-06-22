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

        var from = FromDate.Date;
        var to = ToDate.Date;

        var patients = await _db.Patients
            .AsNoTracking()
            .Where(p => p.ClinicId == clinicId)
            .Select(p => new
            {
                p.FirstName,
                p.LastName,
                p.PatientNo,
                p.AppointmentDate,
                p.AppointmentTime,
                p.DoctorName,
                p.Specialty,
                p.Status
            })
            .ToListAsync();

        var patientRows = patients
            .Where(p => p.AppointmentDate.HasValue &&
                        p.AppointmentDate.Value.Date >= from &&
                        p.AppointmentDate.Value.Date <= to)
            .ToList();

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

        var rows = patientRows.Select(p =>
        {
            var fullName = string.IsNullOrWhiteSpace(p.LastName)
                ? p.FirstName.Trim()
                : $"{p.FirstName} {p.LastName}".Trim();

            decimal balance = 0;
            if (!string.IsNullOrWhiteSpace(p.PatientNo) && balanceByPatientId.TryGetValue(p.PatientNo, out var byId))
                balance = byId;
            else if (!string.IsNullOrWhiteSpace(fullName) && balanceByName.TryGetValue(fullName, out var byName))
                balance = byName;

            return new StatusRow(
                fullName,
                p.AppointmentDate,
                p.AppointmentTime,
                p.DoctorName ?? "",
                p.Specialty ?? "",
                balance,
                PatientVisitStatuses.Normalize(p.Status));
        }).OrderBy(r => r.AppointmentDate).ToList();

        if (!Status.Equals("All", StringComparison.OrdinalIgnoreCase))
            rows = rows.Where(r =>
                PatientVisitStatuses.Normalize(r.Status).Equals(Status, StringComparison.OrdinalIgnoreCase)).ToList();

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
