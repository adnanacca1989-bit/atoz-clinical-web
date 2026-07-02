using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace AtoZClinical.Tests.Helpers;

public sealed class ClinicalWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string HealthToken = "test-health-token";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ClinicalDatabase"] = $"Data Source={Path.Combine(Path.GetTempPath(), $"atoz_test_{Guid.NewGuid():N}.db")}",
                ["Database:Provider"] = "Sqlite",
                ["Database:RecreateSqliteOnStartup"] = "true",
                ["Seed:VendorUsername"] = "vendor",
                ["Seed:VendorPassword"] = "TestVendor@123456!",
                ["Billing:Enabled"] = "false",
                ["Email:Enabled"] = "false",
                ["Captcha:Enabled"] = "false",
                ["AccountVerification:Required"] = "false",
                ["Operations:HealthToken"] = HealthToken
            });
        });
    }

    public HttpClient CreateClientWithHealthToken()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Health-Token", HealthToken);
        return client;
    }
}
