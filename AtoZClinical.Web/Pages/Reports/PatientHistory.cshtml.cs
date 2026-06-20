using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Web.Pages.Reports;

public class PatientHistoryModel : PageModel
{
    private readonly ClinicalDbContext _db;
    private readonly ClinicContextService _clinicContext;

    public PatientHistoryModel(ClinicalDbContext db, ClinicContextService clinicContext)
    {
        _db = db;
        _clinicContext = clinicContext;
    }

    [BindProperty(SupportsGet = true)]
    public string? PatientName { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? DoctorName { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? PatientId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Age { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? DateOfBirth { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? City { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? PhoneNumber { get; set; }

    public List<HistoryRow> Results { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync() => await RunSearchAsync();

    public Task<IActionResult> OnPostRunAsync() => RunSearchAsync();

    public IActionResult OnPostClearAsync() => RedirectToPage();

    private async Task<IActionResult> RunSearchAsync()
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();

        var hasFilter = !string.IsNullOrWhiteSpace(PatientName) || !string.IsNullOrWhiteSpace(DoctorName) ||
                        !string.IsNullOrWhiteSpace(PatientId) || !string.IsNullOrWhiteSpace(PhoneNumber);

        if (!hasFilter)
            return Page();

        var patients = await _db.Patients.Where(p => p.ClinicId == clinicId).ToListAsync();
        if (!string.IsNullOrWhiteSpace(PatientName))
            patients = patients.Where(p => p.FullName.Contains(PatientName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(PatientId))
            patients = patients.Where(p => p.PatientNo.Contains(PatientId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(PhoneNumber))
            patients = patients.Where(p => (p.Phone ?? "").Contains(PhoneNumber, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(City))
            patients = patients.Where(p => (p.City ?? "").Contains(City, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var p in patients)
        {
            Results.Add(new HistoryRow(p.FullName, "Patient Registration", p.UpdatedAt, $"Status: {p.Status}, Doctor: {p.DoctorName}"));
        }

        var prescriptions = await _db.Prescriptions.Where(x => x.ClinicId == clinicId).ToListAsync();
        foreach (var rx in prescriptions.Where(MatchesPatientFilter))
            Results.Add(new HistoryRow(rx.PatientName ?? "", "Prescription", rx.DatePrescription, rx.DiseaseName ?? rx.DiagnosisText));

        var invoices = await _db.Invoices.Where(x => x.ClinicId == clinicId).ToListAsync();
        foreach (var inv in invoices.Where(MatchesInvoiceFilter))
            Results.Add(new HistoryRow(inv.PatientName ?? "", "Invoice", inv.InvoiceDate, $"Total {inv.TotalAmount:N2}, Balance {inv.BalanceDue:N2}"));

        var labReqs = await _db.LabRequests.Where(x => x.ClinicId == clinicId).ToListAsync();
        foreach (var lr in labReqs.Where(MatchesLabRequest))
            Results.Add(new HistoryRow(lr.PatientName ?? "", "Lab Request", lr.RequestDate, $"Doctor: {lr.DoctorName}"));

        var radReqs = await _db.RadiologyRequests.Where(x => x.ClinicId == clinicId).ToListAsync();
        foreach (var rr in radReqs.Where(MatchesRadRequest))
            Results.Add(new HistoryRow(rr.PatientName ?? "", "Radiology Request", rr.RequestDate, $"Doctor: {rr.DoctorName}"));

        Results = Results.OrderByDescending(r => r.Date).ToList();
        return Page();

        bool MatchesPatientFilter(Prescription p) =>
            (string.IsNullOrWhiteSpace(PatientName) || (p.PatientName?.Contains(PatientName, StringComparison.OrdinalIgnoreCase) == true)) &&
            (string.IsNullOrWhiteSpace(DoctorName) || (p.DoctorName?.Contains(DoctorName, StringComparison.OrdinalIgnoreCase) == true));

        bool MatchesInvoiceFilter(Invoice i) =>
            (string.IsNullOrWhiteSpace(PatientName) || (i.PatientName?.Contains(PatientName, StringComparison.OrdinalIgnoreCase) == true)) &&
            (string.IsNullOrWhiteSpace(DoctorName) || (i.DoctorName?.Contains(DoctorName, StringComparison.OrdinalIgnoreCase) == true)) &&
            (string.IsNullOrWhiteSpace(PatientId) || (i.PatientId?.Contains(PatientId, StringComparison.OrdinalIgnoreCase) == true));

        bool MatchesLabRequest(LabRequest r) =>
            (string.IsNullOrWhiteSpace(PatientName) || (r.PatientName?.Contains(PatientName, StringComparison.OrdinalIgnoreCase) == true)) &&
            (string.IsNullOrWhiteSpace(DoctorName) || (r.DoctorName?.Contains(DoctorName, StringComparison.OrdinalIgnoreCase) == true));

        bool MatchesRadRequest(RadiologyRequest r) =>
            (string.IsNullOrWhiteSpace(PatientName) || (r.PatientName?.Contains(PatientName, StringComparison.OrdinalIgnoreCase) == true)) &&
            (string.IsNullOrWhiteSpace(DoctorName) || (r.DoctorName?.Contains(DoctorName, StringComparison.OrdinalIgnoreCase) == true));
    }

    public sealed record HistoryRow(string Patient, string Form, DateTime Date, string? Details);
}
