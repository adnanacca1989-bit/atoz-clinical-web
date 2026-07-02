using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AtoZClinical.Infrastructure.Services;

public sealed class ClinicProfileService
{
    private readonly ClinicalDbContext _db;
    private readonly ClinicSettingsService _settings;
    private readonly ClinicRuntimeCache _cache;
    private readonly ILogger<ClinicProfileService> _logger;

    public ClinicProfileService(
        ClinicalDbContext db,
        ClinicSettingsService settings,
        ClinicRuntimeCache cache,
        ILogger<ClinicProfileService> logger)
    {
        _db = db;
        _settings = settings;
        _cache = cache;
        _logger = logger;
    }

    public async Task<ClinicProfileDto> GetAsync(Guid clinicId, CancellationToken ct = default)
    {
        _logger.LogDebug("Loading clinic profile for clinic {ClinicId}", clinicId);

        var clinic = await _db.Clinics.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clinicId, ct);
        if (clinic is null)
        {
            _logger.LogWarning("Clinic profile load failed — clinic {ClinicId} not found", clinicId);
            throw new InvalidOperationException("Clinic not found.");
        }

        ClinicConfiguration config;
        try
        {
            config = await _settings.GetOrCreateAsync(clinicId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load clinic configuration for clinic {ClinicId}", clinicId);
            throw;
        }

        var dto = new ClinicProfileDto(
            clinic.Id,
            clinic.Name ?? "Clinic",
            clinic.ContactPerson,
            clinic.Email,
            clinic.Phone,
            clinic.Address,
            clinic.City,
            clinic.Country,
            ClinicBrandingHelper.NormalizeTimeZoneId(
                string.IsNullOrWhiteSpace(clinic.TimeZoneId) ? config.TimeZoneId : clinic.TimeZoneId),
            ClinicBrandingHelper.NormalizeLanguageCode(config.LanguageCode),
            string.IsNullOrWhiteSpace(config.LanguageName) ? "English" : config.LanguageName,
            config.LogoBase64,
            config.Tagline,
            config.Website,
            ClinicBrandingHelper.NormalizePrimaryColor(config.PrimaryColor),
            ClinicBrandingHelper.NormalizeFormStyle(config.FormStyle));

        _logger.LogInformation(
            "Loaded clinic profile for clinic {ClinicId} (logo={HasLogo}, color={PrimaryColor})",
            clinicId,
            !string.IsNullOrWhiteSpace(dto.LogoBase64),
            dto.PrimaryColor);

        return dto;
    }

    public async Task SaveAsync(Guid clinicId, ClinicProfileInput input, CancellationToken ct = default)
    {
        if (input is null)
            throw new ArgumentNullException(nameof(input));

        _logger.LogDebug("Saving clinic profile for clinic {ClinicId}", clinicId);

        var clinic = await _db.Clinics.FirstOrDefaultAsync(c => c.Id == clinicId, ct);
        if (clinic is null)
        {
            _logger.LogWarning("Clinic profile save failed — clinic {ClinicId} not found", clinicId);
            throw new InvalidOperationException("Clinic not found.");
        }

        var config = await _db.ClinicConfigurations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.ClinicId == clinicId, ct);
        if (config is null)
        {
            config = new ClinicConfiguration { ClinicId = clinicId };
            _db.ClinicConfigurations.Add(config);
        }

        clinic.Name = (input.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(clinic.Name))
            throw new InvalidOperationException("Clinic name is required.");

        clinic.ContactPerson = input.ContactPerson?.Trim();
        clinic.Email = input.Email?.Trim();
        clinic.Phone = input.Phone?.Trim();
        clinic.Address = input.Address?.Trim();
        clinic.City = input.City?.Trim();
        clinic.Country = input.Country?.Trim();
        clinic.TimeZoneId = ClinicBrandingHelper.NormalizeTimeZoneId(input.TimeZoneId);

        config.LanguageCode = ClinicBrandingHelper.NormalizeLanguageCode(input.LanguageCode);
        config.LanguageName = string.IsNullOrWhiteSpace(input.LanguageName) ? "English" : input.LanguageName.Trim();
        config.TimeZoneId = clinic.TimeZoneId;
        config.Tagline = input.Tagline?.Trim();
        config.Website = input.Website?.Trim();
        config.PrimaryColor = ClinicBrandingHelper.NormalizePrimaryColor(input.PrimaryColor);
        config.FormStyle = ClinicBrandingHelper.NormalizeFormStyle(input.FormStyle);

        if (!string.IsNullOrWhiteSpace(input.LogoBase64))
            config.LogoBase64 = input.LogoBase64;

        if (input.ClearLogo)
            config.LogoBase64 = null;

        config.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _settings.InvalidateCache(clinicId);
        _cache.InvalidateClinic(clinicId);

        _logger.LogInformation(
            "Saved clinic profile for clinic {ClinicId} (logo={HasLogo}, color={PrimaryColor})",
            clinicId,
            !string.IsNullOrWhiteSpace(config.LogoBase64),
            config.PrimaryColor);
    }
}

public sealed record ClinicProfileDto(
    Guid ClinicId,
    string Name,
    string? ContactPerson,
    string? Email,
    string? Phone,
    string? Address,
    string? City,
    string? Country,
    string TimeZoneId,
    string LanguageCode,
    string LanguageName,
    string? LogoBase64,
    string? Tagline,
    string? Website,
    string PrimaryColor,
    string FormStyle);

public sealed class ClinicProfileInput
{
    public string Name { get; set; } = string.Empty;
    public string? ContactPerson { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string TimeZoneId { get; set; } = "UTC";
    public string LanguageCode { get; set; } = "en";
    public string LanguageName { get; set; } = "English";
    public string? LogoBase64 { get; set; }
    public bool ClearLogo { get; set; }
    public string? Tagline { get; set; }
    public string? Website { get; set; }
    public string PrimaryColor { get; set; } = ClinicBrandingHelper.DefaultPrimaryColor;
    public string FormStyle { get; set; } = "Default";
}
