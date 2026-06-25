using System.ComponentModel.DataAnnotations;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AtoZClinical.Web.Pages.Admin;

public class EnterpriseModel : PageModel
{
    private readonly ClinicContextService _context;
    private readonly ClinicalDbContext _db;
    private readonly ClinicSettingsService _settings;
    private readonly IConfiguration _config;
    private readonly ILogger<EnterpriseModel> _logger;

    public EnterpriseModel(
        ClinicContextService context,
        ClinicalDbContext db,
        ClinicSettingsService settings,
        IConfiguration config,
        ILogger<EnterpriseModel> logger)
    {
        _context = context;
        _db = db;
        _settings = settings;
        _config = config;
        _logger = logger;
    }

    [BindProperty]
    public EnterpriseInput Input { get; set; } = new();

    public string? BaseDomain { get; private set; }
    public string? Message { get; private set; }
    public string? LoadError { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        try
        {
            var clinic = await _context.GetCurrentClinicAsync();
            if (clinic is null) return Forbid();

            BaseDomain = _config["Security:BaseDomain"];
            var config = await _settings.GetOrCreateAsync(clinic.Id);
            Input = EnterpriseInput.From(clinic, config);
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load Enterprise settings.");
            LoadError = "Enterprise settings could not be loaded. The database may need an update — please contact your administrator or try again in a few minutes.";
            BaseDomain = _config["Security:BaseDomain"];
            return Page();
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var clinic = await _context.GetCurrentClinicAsync();
        if (clinic is null) return Forbid();

        BaseDomain = _config["Security:BaseDomain"];

        if (!ModelState.IsValid) return Page();

        try
        {
            var slug = string.IsNullOrWhiteSpace(Input.Subdomain)
                ? null
                : Input.Subdomain.Trim().ToLowerInvariant();

            if (slug is not null)
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(slug, @"^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$"))
                {
                    ModelState.AddModelError(nameof(Input.Subdomain), "Use lowercase letters, numbers, and hyphens (3–63 chars).");
                    return Page();
                }

                var taken = await _db.Clinics.AnyAsync(c => c.Subdomain == slug && c.Id != clinic.Id);
                if (taken)
                {
                    ModelState.AddModelError(nameof(Input.Subdomain), "This subdomain is already in use.");
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
            _settings.InvalidateCache(clinic.Id);
            Message = "Enterprise settings saved.";
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save Enterprise settings for clinic {ClinicId}", clinic.Id);
            ModelState.AddModelError(string.Empty, "Could not save enterprise settings. Please try again or contact support.");
            return Page();
        }
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
