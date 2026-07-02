namespace AtoZClinical.Infrastructure.Services;

/// <summary>Shared helpers for clinic profile and branding UI.</summary>
public static class ClinicBrandingHelper
{
    public const string DefaultPrimaryColor = "#0b4f8a";

    public static string NormalizePrimaryColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DefaultPrimaryColor;

        var trimmed = value.Trim();
        if (trimmed.StartsWith('#'))
        {
            return trimmed.Length is 4 or 7 ? trimmed : DefaultPrimaryColor;
        }

        return trimmed.Length is 3 or 6 ? $"#{trimmed}" : DefaultPrimaryColor;
    }

    public static string NormalizeTimeZoneId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "UTC" : value.Trim();

    public static string NormalizeLanguageCode(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "en" : value.Trim();

    public static string NormalizeFormStyle(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Default" : value.Trim();
}
