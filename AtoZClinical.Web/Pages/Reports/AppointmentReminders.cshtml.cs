using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Web.Pages.Reports;

public class AppointmentRemindersModel : PageModel
{
    private readonly ClinicalDbContext _db;
    private readonly ClinicContextService _clinicContext;
    private readonly AppointmentReminderService _reminders;
    private readonly ILogger<AppointmentRemindersModel> _logger;

    public AppointmentRemindersModel(
        ClinicalDbContext db,
        ClinicContextService clinicContext,
        AppointmentReminderService reminders,
        ILogger<AppointmentRemindersModel> logger)
    {
        _db = db;
        _clinicContext = clinicContext;
        _reminders = reminders;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)]
    public DateTime FromDate { get; set; } = ClinicClock.Today.AddMonths(-6);

    [BindProperty(SupportsGet = true)]
    public DateTime ToDate { get; set; } = ClinicClock.Today.AddMonths(3);

    [BindProperty(SupportsGet = true)]
    public string Gender { get; set; } = "All";

    [BindProperty(SupportsGet = true)]
    public string DoctorName { get; set; } = "All";

    [BindProperty(SupportsGet = true)]
    public string Specialty { get; set; } = "All";

    [BindProperty(SupportsGet = true)]
    public string City { get; set; } = "All";

    [BindProperty(SupportsGet = true)]
    public string Status { get; set; } = "All";

    [BindProperty(SupportsGet = true)]
    public string? PatientSearch { get; set; }

    public List<AppointmentReminderRow> Results { get; private set; } = [];
    public List<string> DoctorOptions { get; private set; } = [];
    public List<string> SpecialtyOptions { get; private set; } = [];
    public List<string> CityOptions { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync() => await RunAsync();

    public Task<IActionResult> OnPostSearchAsync() => RunAsync();

    public IActionResult OnPostClearAsync() => RedirectToPage();

    public async Task<IActionResult> OnGetCountAsync()
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return new JsonResult(new { count = 0 });
        var count = await _reminders.GetUpcomingReminderCountAsync(clinicId.Value);
        return new JsonResult(new { count });
    }

    public async Task<IActionResult> OnPostAdjustTimeAsync(
        [FromForm] Guid patientId,
        [FromForm] DateTime appointmentDate,
        [FromForm] string? appointmentTime)
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null)
        {
            _logger.LogWarning("AdjustTime denied: no clinic context for user trace={TraceId}", HttpContext.TraceIdentifier);
            return Forbid();
        }

        var patient = await _db.Patients
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.ClinicId == clinicId && p.Id == patientId);
        if (patient is null)
        {
            _logger.LogWarning(
                "AdjustTime patient not found patientId={PatientId} clinicId={ClinicId} trace={TraceId}",
                patientId,
                clinicId,
                HttpContext.TraceIdentifier);
            return NotFound();
        }

        patient.AppointmentDate = appointmentDate.Date;
        if (!string.IsNullOrWhiteSpace(appointmentTime) && TimeSpan.TryParse(appointmentTime, out var time))
            patient.AppointmentTime = time;
        patient.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "AdjustTime saved patientId={PatientId} date={Date} time={Time} clinicId={ClinicId} trace={TraceId}",
            patientId,
            appointmentDate.ToString("yyyy-MM-dd"),
            appointmentTime ?? "(unchanged)",
            clinicId,
            HttpContext.TraceIdentifier);

        return RedirectToPage(new { FromDate, ToDate, Gender, DoctorName, Specialty, City, Status, PatientSearch });
    }

    private async Task<IActionResult> RunAsync()
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();

        await LoadFilterOptionsAsync(clinicId.Value);

        Results = await _reminders.GetRemindersAsync(
            clinicId.Value,
            FromDate,
            ToDate,
            Gender,
            DoctorName,
            Specialty,
            City,
            Status,
            PatientSearch);

        return Page();
    }

    private async Task LoadFilterOptionsAsync(Guid clinicId)
    {
        var patients = await _db.Patients.IgnoreQueryFilters().Where(p => p.ClinicId == clinicId).ToListAsync();
        DoctorOptions = patients.Select(p => p.DoctorName).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().OrderBy(n => n).Cast<string>().ToList();
        SpecialtyOptions = patients.Select(p => p.Specialty).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().OrderBy(n => n).Cast<string>().ToList();
        CityOptions = patients.Select(p => p.City).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().OrderBy(n => n).Cast<string>().ToList();
    }
}
