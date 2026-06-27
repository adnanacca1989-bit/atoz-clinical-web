using System.ComponentModel.DataAnnotations;
using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace AtoZClinical.Web.Pages.Portal;

[DisableRateLimiting]
[IgnoreAntiforgeryToken]
public class BookModel : PageModel
{
    private readonly PatientPortalSession _session;
    private readonly PatientPortalService _portal;
    private readonly ILogger<BookModel> _logger;

    public BookModel(PatientPortalSession session, PatientPortalService portal, ILogger<BookModel> logger)
    {
        _session = session;
        _portal = portal;
        _logger = logger;
    }

    [BindProperty]
    public BookInput Input { get; set; } = new();

    public List<Doctor> Doctors { get; private set; } = [];
    public string? Message { get; private set; }
    public bool Success { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var session = _session.Get(HttpContext);
        if (session is null) return RedirectToPage("/Portal/Login");

        HttpContext.Items[HttpContextClinicProvider.TenantClinicIdKey] = session.ClinicId;

        try
        {
            Doctors = await _portal.GetBookableDoctorsAsync(session.ClinicId);
            Input.AppointmentDate = ClinicClock.Today.AddDays(1);
            Input.StartTime = new TimeSpan(9, 0, 0);
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Portal Book GET failed clinicId={ClinicId}", session.ClinicId);
            throw;
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var session = _session.Get(HttpContext);
        if (session is null) return RedirectToPage("/Portal/Login");

        HttpContext.Items[HttpContextClinicProvider.TenantClinicIdKey] = session.ClinicId;

        Doctors = await _portal.GetBookableDoctorsAsync(session.ClinicId);
        if (!ModelState.IsValid) return Page();

        var (ok, error) = await _portal.RequestAppointmentAsync(
            session.ClinicId,
            session.PatientId,
            Input.AppointmentDate,
            Input.StartTime,
            Input.DoctorName,
            Input.Reason);

        if (!ok)
        {
            ModelState.AddModelError(string.Empty, error ?? "Could not book appointment.");
            return Page();
        }

        _logger.LogInformation(
            "Portal appointment booked patientId={PatientId} clinicId={ClinicId} date={Date}",
            session.PatientId,
            session.ClinicId,
            Input.AppointmentDate.ToString("yyyy-MM-dd"));

        Success = true;
        Message = "Your appointment request has been submitted. The clinic will confirm shortly.";
        return Page();
    }

    public sealed class BookInput
    {
        [Required, DataType(DataType.Date), Display(Name = "Preferred date")]
        public DateTime AppointmentDate { get; set; } = DateTime.Today.AddDays(1);

        [Required, Display(Name = "Preferred time")]
        public TimeSpan StartTime { get; set; } = new(9, 0, 0);

        [Display(Name = "Doctor (optional)")]
        public string? DoctorName { get; set; }

        [Display(Name = "Reason for visit")]
        [StringLength(500)]
        public string? Reason { get; set; }
    }
}
