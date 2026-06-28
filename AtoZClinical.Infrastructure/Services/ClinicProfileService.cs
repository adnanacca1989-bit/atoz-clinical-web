using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public sealed class ClinicProfileService
{
    private readonly ClinicalDbContext _db;
    private readonly ClinicSettingsService _settings;
    private readonly ClinicRuntimeCache _cache;

    public ClinicProfileService(
        ClinicalDbContext db,
        ClinicSettingsService settings,
        ClinicRuntimeCache cache)
    {
        _db = db;
        _settings = settings;
        _cache = cache;
    }

    public async Task<ClinicProfileDto> GetAsync(Guid clinicId, CancellationToken ct = default)
    {
        var clinic = await _db.Clinics.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clinicId, ct)
            ?? throw new InvalidOperationException("Clinic not found.");
        var config = await _settings.GetOrCreateAsync(clinicId);

        return new ClinicProfileDto(
            clinic.Id,
            clinic.Name,
            clinic.ContactPerson,
            clinic.Email,
            clinic.Phone,
            clinic.Address,
            clinic.City,
            clinic.Country,
            string.IsNullOrWhiteSpace(clinic.TimeZoneId) ? config.TimeZoneId ?? "UTC" : clinic.TimeZoneId,
            string.IsNullOrWhiteSpace(config.LanguageCode) ? "en" : config.LanguageCode,
            string.IsNullOrWhiteSpace(config.LanguageName) ? "English" : config.LanguageName,
            config.LogoBase64,
            config.Tagline,
            config.Website,
            string.IsNullOrWhiteSpace(config.PrimaryColor) ? "#0b4f8a" : config.PrimaryColor,
            string.IsNullOrWhiteSpace(config.FormStyle) ? "Default" : config.FormStyle);
    }

    public async Task SaveAsync(Guid clinicId, ClinicProfileInput input, CancellationToken ct = default)
    {
        var clinic = await _db.Clinics.FirstOrDefaultAsync(c => c.Id == clinicId, ct)
            ?? throw new InvalidOperationException("Clinic not found.");

        var config = await _db.ClinicConfigurations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.ClinicId == clinicId, ct);
        if (config is null)
        {
            config = new ClinicConfiguration { ClinicId = clinicId };
            _db.ClinicConfigurations.Add(config);
        }

        clinic.Name = input.Name.Trim();
        clinic.ContactPerson = input.ContactPerson?.Trim();
        clinic.Email = input.Email?.Trim();
        clinic.Phone = input.Phone?.Trim();
        clinic.Address = input.Address?.Trim();
        clinic.City = input.City?.Trim();
        clinic.Country = input.Country?.Trim();
        clinic.TimeZoneId = string.IsNullOrWhiteSpace(input.TimeZoneId) ? "UTC" : input.TimeZoneId.Trim();

        config.LanguageCode = string.IsNullOrWhiteSpace(input.LanguageCode) ? "en" : input.LanguageCode.Trim();
        config.LanguageName = string.IsNullOrWhiteSpace(input.LanguageName) ? "English" : input.LanguageName.Trim();
        config.TimeZoneId = clinic.TimeZoneId ?? "UTC";
        config.Tagline = input.Tagline?.Trim();
        config.Website = input.Website?.Trim();
        config.PrimaryColor = string.IsNullOrWhiteSpace(input.PrimaryColor) ? "#0b4f8a" : input.PrimaryColor.Trim();
        config.FormStyle = string.IsNullOrWhiteSpace(input.FormStyle) ? "Default" : input.FormStyle.Trim();

        if (!string.IsNullOrWhiteSpace(input.LogoBase64))
            config.LogoBase64 = input.LogoBase64;

        if (input.ClearLogo)
            config.LogoBase64 = null;

        config.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _settings.InvalidateCache(clinicId);
        _cache.InvalidateClinic(clinicId);
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
    public string PrimaryColor { get; set; } = "#0b4f8a";
    public string FormStyle { get; set; } = "Default";
}
