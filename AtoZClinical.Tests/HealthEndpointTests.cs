using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace AtoZClinical.Tests;

public class HealthEndpointTests : IClassFixture<ClinicalWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly ClinicalWebApplicationFactory _factory;

    public HealthEndpointTests(ClinicalWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_returns_ok()
    {
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_page_is_reachable()
    {
        var response = await _client.GetAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_page_form_has_post_submit_and_antiforgery()
    {
        var response = await _client.GetAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("method=\"post\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"loginForm\"", html);
        Assert.Contains("type=\"submit\"", html);
        Assert.Contains("id=\"loginSubmitBtn\"", html);
        Assert.Contains("name=\"Input.Username\"", html);
        Assert.Contains("name=\"Input.Password\"", html);
        Assert.Contains("name=\"__RequestVerificationToken\"", html);
    }

    [Fact]
    public async Task Login_post_with_invalid_credentials_returns_page_not_429()
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Username"] = "nonexistent_user",
            ["Input.Password"] = "WrongPassword123!"
        });

        var response = await _client.PostAsync("/Account/Login", content);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Error_page_is_reachable()
    {
        var response = await _client.GetAsync("/Error");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Portal_login_page_is_reachable()
    {
        var response = await _client.GetAsync("/Portal/Login");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Api_invoices_requires_api_key()
    {
        var response = await _client.GetAsync("/api/v1/invoices");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Portal_book_redirects_when_not_signed_in()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/Portal/Book");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Portal/Login", response.Headers.Location?.ToString());
    }
}

public sealed class ClinicalWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ClinicalDatabase"] = $"Data Source={Path.Combine(Path.GetTempPath(), $"atoz_test_{Guid.NewGuid():N}.db")}",
                ["Database:Provider"] = "Sqlite",
                ["Seed:VendorUsername"] = "vendor",
                ["Seed:VendorPassword"] = "TestVendor@123456!",
                ["Billing:Enabled"] = "false",
                ["Email:Enabled"] = "false",
                ["Captcha:Enabled"] = "false",
                ["Operations:HealthToken"] = "test-health-token"
            });
        });
    }
}
