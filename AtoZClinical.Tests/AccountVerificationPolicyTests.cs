using AtoZClinical.Infrastructure.Services;
using Microsoft.Extensions.Configuration;

namespace AtoZClinical.Tests;

public class AccountVerificationPolicyTests
{
    [Fact]
    public void IsRequired_returns_explicit_config_when_set()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AccountVerification:Required"] = "true" })
            .Build();

        Assert.True(AccountVerificationPolicy.IsRequired(config));
    }

    [Fact]
    public void IsRequired_defaults_false_in_development_without_explicit_config()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ASPNETCORE_ENVIRONMENT"] = "Development" })
            .Build();

        Assert.False(AccountVerificationPolicy.IsRequired(config));
    }
}
