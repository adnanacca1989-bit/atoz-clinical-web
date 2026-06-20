using AtoZClinical.Core.Entities;
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

        var patients = await _db.Patients
            .Where(p => p.ClinicId == clinicId &&
                        p.AppointmentDate >= FromDate.Date &&
                        p.AppointmentDate <= ToDate.Date)
            .ToListAsync();

        if (Status != "All")
            patients = patients.Where(p => string.Equals(p.Status, Status, StringComparison.OrdinalIgnoreCase)).ToList();

        var invoices = await _db.Invoices.Where(i => i.ClinicId == clinicId).ToListAsync();

        Results = patients.Select(p =>
        {
            var inv = invoices.FirstOrDefault(i =>
                string.Equals(i.PatientName, p.FullName, StringComparison.OrdinalIgnoreCase));
            return new StatusRow(
                p.FullName,
                p.AppointmentDate,
                p.AppointmentTime,
                p.DoctorName ?? "",
                p.Specialty ?? "",
                inv?.BalanceDue ?? 0,
                p.Status);
        }).OrderBy(r => r.AppointmentDate).ToList();

        return Page();
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
