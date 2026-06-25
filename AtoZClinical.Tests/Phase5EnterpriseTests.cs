using AtoZClinical.Infrastructure.Services;
using Microsoft.Extensions.Configuration;

namespace AtoZClinical.Tests;

public class SubdomainClinicResolverTests
{
    [Theory]
    [InlineData("acme.example.com", "example.com", "acme")]
    [InlineData("clinic-1.example.com", "example.com", "clinic-1")]
    [InlineData("example.com", "example.com", null)]
    [InlineData("www.example.com", "example.com", null)]
    public void ExtractSubdomain_parses_host(string host, string baseDomain, string? expected)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Security:BaseDomain"] = baseDomain })
            .Build();

        var resolver = new SubdomainClinicResolver(null!, config);
        Assert.Equal(expected, resolver.ExtractSubdomain(host));
    }
}

public class ClinicApiKeyServiceTests
{
    [Fact]
    public void HashKey_is_deterministic()
    {
        var a = ClinicApiKeyService.HashKey("atz_test_key_12345");
        var b = ClinicApiKeyService.HashKey("atz_test_key_12345");
        Assert.Equal(a, b);
        Assert.NotEqual(a, ClinicApiKeyService.HashKey("atz_other"));
    }
}

public class MfaPolicyServiceTests
{
    [Fact]
    public void Enforcement_disabled_by_default()
    {
        var config = new ConfigurationBuilder().Build();
        var service = new MfaPolicyService(config, null!);
        Assert.False(service.IsEnforcementEnabled);
    }
}
