using System.Text.RegularExpressions;

namespace AtoZClinical.Infrastructure.Services;

public static class PhoneNumberNormalizer
{
    private static readonly Regex DigitsOnly = new(@"\D", RegexOptions.Compiled);

    public static bool TryNormalize(string? input, out string normalized, string defaultCountryCode = "964")
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var digits = DigitsOnly.Replace(input.Trim(), "");
        if (digits.Length < 9 || digits.Length > 15)
            return false;

        if (digits.StartsWith("00", StringComparison.Ordinal))
            digits = digits[2..];

        if (digits.StartsWith(defaultCountryCode, StringComparison.Ordinal))
        {
            normalized = "+" + digits;
            return true;
        }

        if (digits.StartsWith('0'))
        {
            normalized = "+" + defaultCountryCode + digits[1..];
            return true;
        }

        if (digits.Length is >= 9 and <= 11)
        {
            normalized = "+" + defaultCountryCode + digits;
            return true;
        }

        normalized = "+" + digits;
        return normalized.Length >= 11 && normalized.Length <= 16;
    }

    public static string NormalizeOrThrow(string input)
    {
        if (!TryNormalize(input, out var normalized))
            throw new ArgumentException("Enter a valid mobile number (e.g. 07xx xxx xxxx or +964...).", nameof(input));
        return normalized;
    }
}
