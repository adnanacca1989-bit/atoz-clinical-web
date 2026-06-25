using System.ComponentModel.DataAnnotations;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Web.Pages.Admin;

public class EnterpriseModel : PageModel
{
    private readonly ClinicContextService _context;
    private readonly ClinicalDbContext _db;
    private readonly ClinicSettingsService _settings;
    private readonly IConfiguration _config;

    public EnterpriseModel(
        ClinicContextService context,
        ClinicalDbContext db,
        ClinicSettingsService settings,
        IConfiguration config)
    {
        _context = context;
        _db = db;
        _settings = settings;
        _config = config;
    }

    [BindProperty]
    public EnterpriseInput Input { get; set; } = new();

    public string? BaseDomain { get; private set; }
    public string? Message { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var clinic = await _context.GetCurrentClinicAsync();
        if (clinic is null) return Forbid();

        BaseDomain = _config["Security:BaseDomain"];
        var config = await _settings.GetOrCreateAsync(clinic.Id);
        Input = EnterpriseInput.From(clinic, config);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var clinic = await _context.GetCurrentClinicAsync();
        if (clinic is null) return Forbid();

        if (!ModelState.IsValid) return Page();

        var slug = string.IsNullOrWhiteSpace(Input.Subdomain)
            ? null
            : Input.Subdomain.Trim().ToLowerInvariant();

        if (slug is not null)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(slug, @"^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$"))
            {
                ModelState.AddModelError(nameof(Input.Subdomain), "Use lowercase letters, numbers, and hyphens (3–63 chars).");
                BaseDomain = _config["Security:BaseDomain"];
                return Page();
            }

            var taken = await _db.Clinics.AnyAsync(c => c.Subdomain == slug && c.Id != clinic.Id);
            if (taken)
            {
                ModelState.AddModelError(nameof(Input.Subdomain), "This subdomain is already in use.");
                BaseDomain = _config["Security:BaseDomain"];
                return Page();
            }
        }

        clinic.Subdomain = slug;
        clinic.DedicatedConnectionName = string.IsNullOrWhiteSpace(Input.DedicatedConnectionName)
            ? null
            : Input.DedicatedConnectionName.Trim();

        var config = await _settings.GetOrCreateAsync(clinic.Id);
        config.PatientPortalEnabled = Input.PatientPortalEnabled;
        config.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        Message = "Enterprise settings saved.";
        BaseDomain = _config["Security:BaseDomain"];
        return Page();
    }

    public sealed class EnterpriseInput
    {
        [Display(Name = "Custom subdomain")]
        public string? Subdomain { get; set; }

        [Display(Name = "Enable patient portal")]
        public bool PatientPortalEnabled { get; set; }

        [Display(Name = "Dedicated DB connection name (enterprise)")]
        public string? DedicatedConnectionName { get; set; }

        public static EnterpriseInput From(Core.Entities.Clinic clinic, Core.Entities.ClinicConfiguration config) => new()
        {
            Subdomain = clinic.Subdomain,
            PatientPortalEnabled = config.PatientPortalEnabled,
            DedicatedConnectionName = clinic.DedicatedConnectionName
        };
    }
}
