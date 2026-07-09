using AtoZClinical.Infrastructure.Services;

namespace AtoZClinical.Tests;

public class VerificationCodeLengthTests
{
    [Theory]
    [InlineData("511979", "511979")]
    [InlineData("511 979", "511979")]
    [InlineData("511-979", "511979")]
    public void NormalizeVerificationDigits_accepts_six_digits(string input, string expected)
    {
        Assert.Equal(expected, TrialRegistrationVerificationService.NormalizeVerificationDigits(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1234")]
    [InlineData("1234567")]
    [InlineData("abcd12")]
    public void NormalizeVerificationDigits_rejects_invalid_lengths(string? input)
    {
        Assert.Null(TrialRegistrationVerificationService.NormalizeVerificationDigits(input));
    }

    [Fact]
    public void VerificationCodeLength_matches_generation_format() =>
        Assert.Equal(6, TrialRegistrationVerificationService.VerificationCodeLength);
}
