using AtoZClinical.Infrastructure.Services;

namespace AtoZClinical.Tests;

public class ClinicBrandingHelperTests
{
    [Theory]
    [InlineData(null, "#0b4f8a")]
    [InlineData("", "#0b4f8a")]
    [InlineData("#ff5500", "#ff5500")]
    [InlineData("ff5500", "#ff5500")]
    [InlineData("not-a-color", "#0b4f8a")]
    public void NormalizePrimaryColor_returns_safe_hex(string? input, string expected) =>
        Assert.Equal(expected, ClinicBrandingHelper.NormalizePrimaryColor(input));
}
