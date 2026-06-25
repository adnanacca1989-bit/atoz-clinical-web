using System.ComponentModel.DataAnnotations;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Web.Pages.Portal;

[EnableRateLimiting("auth")]
public class LoginModel : PageModel
{
    private readonly PatientPortalService _portal;
    private readonly PatientPortalSession _session;
    private readonly ClinicalDbContext _db;

    public LoginModel(PatientPortalService portal, PatientPortalSession session, ClinicalDbContext db)
    {
        _portal = portal;
        _session = session;
        _db = db;
    }

    [BindProperty]
    public PortalLoginInput Input { get; set; } = new();

    public string? ClinicName { get; private set; }
    public bool PortalDisabled { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (_session.Get(HttpContext) is not null)
            return RedirectToPage("/Portal/Index");

        await LoadClinicContextAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadClinicContextAsync();
        if (PortalDisabled) return Page();

        if (!ModelState.IsValid) return Page();

        var clinicId = await ResolveClinicIdAsync();
        if (clinicId is null)
        {
            ModelState.AddModelError(string.Empty, "Clinic not found.");
            return Page();
        }

        var patient = await _portal.AuthenticateAsync(
            clinicId.Value,
            Input.PatientNo,
            Input.DateOfBirth,
            Input.PhoneLast4);

        if (patient is null)
        {
            ModelState.AddModelError(string.Empty, "We could not verify your details. Check your information and try again.");
            return Page();
        }

        _session.SignIn(HttpContext, clinicId.Value, patient.Id);
        return RedirectToPage("/Portal/Index");
    }

    private async Task LoadClinicContextAsync()
    {
        var clinicId = await ResolveClinicIdAsync();
        if (clinicId is null) return;

        var config = await _db.ClinicConfigurations.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ClinicId == clinicId);
        var clinic = await _db.Clinics.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clinicId);
        ClinicName = clinic?.Name;
        PortalDisabled = config is null || !config.PatientPortalEnabled;
    }

    private async Task<Guid?> ResolveClinicIdAsync()
    {
        if (HttpContext.Items[HttpContextClinicProvider.SubdomainClinicIdKey] is Guid subdomainId)
            return subdomainId;

        if (string.IsNullOrWhiteSpace(Input.ClinicCode))
            return null;

        var clinic = await _db.Clinics.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ClinicCode == Input.ClinicCode.Trim());
        return clinic?.Id;
    }

    public sealed class PortalLoginInput
    {
        [Display(Name = "Clinic code")]
        public string? ClinicCode { get; set; }

        [Required, Display(Name = "Patient / barcode number")]
        public string PatientNo { get; set; } = string.Empty;

        [Required, DataType(DataType.Date), Display(Name = "Date of birth")]
        public DateTime DateOfBirth { get; set; } = DateTime.Today.AddYears(-30);

        [Required, Display(Name = "Last 4 digits of phone")]
        [StringLength(4, MinimumLength = 4)]
        public string PhoneLast4 { get; set; } = string.Empty;
    }
}
