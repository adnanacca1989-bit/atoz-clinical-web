using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Tests.Helpers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AtoZClinical.Tests;

[Collection("ClinicalWeb")]
public class ClinicProfileTests : IClassFixture<ClinicalWebApplicationFactory>
{
    private readonly ClinicalWebApplicationFactory _factory;

    public ClinicProfileTests(ClinicalWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Get_clinic_profile_returns_ok_with_expected_content()
    {
        using var client = await ClinicalAuthTestHelper.CreateAuthenticatedClinicAdminClientAsync(_factory);
        var response = await client.GetAsync("/Settings/ClinicProfile");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("Something went wrong", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Clinic Profile &amp; Branding", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Save Profile", body);
        Assert.Contains("Upload Logo", body);
    }

    [Fact]
    public async Task Post_save_profile_persists_primary_color()
    {
        using var client = await ClinicalAuthTestHelper.CreateAuthenticatedClinicAdminClientAsync(_factory);
        var get = await client.GetAsync("/Settings/ClinicProfile");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var token = await ClinicalAuthTestHelper.ExtractAntiforgeryTokenAsync(get);
        var html = await get.Content.ReadAsStringAsync();

        var form = new Dictionary<string, string>
        {
            ["Input.Name"] = "Test Clinic Branding",
            ["Input.PrimaryColor"] = "#ff5500",
            ["Input.TimeZoneId"] = "UTC",
            ["Input.LanguageCode"] = "en",
            ["Input.FormStyle"] = "Default"
        };
        if (!string.IsNullOrWhiteSpace(token))
            form["__RequestVerificationToken"] = token;

        var post = await client.PostAsync("/Settings/ClinicProfile", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        var postBody = await post.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Something went wrong", postBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Clinic profile and branding saved", postBody, StringComparison.OrdinalIgnoreCase);

        using var scope = _factory.Services.CreateScope();
        var profile = scope.ServiceProvider.GetRequiredService<ClinicProfileService>();
        var clinicId = await ClinicalAuthTestHelper.GetClinicAdminClinicIdAsync(_factory);
        var saved = await profile.GetAsync(clinicId);
        Assert.Equal("#ff5500", saved.PrimaryColor);
        Assert.Equal("Test Clinic Branding", saved.Name);
    }

    [Fact]
    public async Task Post_upload_logo_saves_and_page_shows_success()
    {
        using var client = await ClinicalAuthTestHelper.CreateAuthenticatedClinicAdminClientAsync(_factory);
        var get = await client.GetAsync("/Settings/ClinicProfile");
        var token = await ClinicalAuthTestHelper.ExtractAntiforgeryTokenAsync(get);

        // 1x1 red PNG
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

        using var content = new MultipartFormDataContent();
        if (!string.IsNullOrWhiteSpace(token))
            content.Add(new StringContent(token), "__RequestVerificationToken");
        var fileContent = new ByteArrayContent(png);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "logoFile", "logo.png");

        var post = await client.PostAsync("/Settings/ClinicProfile?handler=UploadLogo", content);
        var body = await post.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        Assert.DoesNotContain("Something went wrong", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Logo uploaded", body, StringComparison.OrdinalIgnoreCase);

        using var scope = _factory.Services.CreateScope();
        var profile = scope.ServiceProvider.GetRequiredService<ClinicProfileService>();
        var clinicId = await ClinicalAuthTestHelper.GetClinicAdminClinicIdAsync(_factory);
        var saved = await profile.GetAsync(clinicId);
        Assert.False(string.IsNullOrWhiteSpace(saved.LogoBase64));
        Assert.StartsWith("data:image/png;base64,", saved.LogoBase64, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Branding_filter_applies_primary_color_on_next_request()
    {
        using var client = await ClinicalAuthTestHelper.CreateAuthenticatedClinicAdminClientAsync(_factory);
        var get = await client.GetAsync("/Settings/ClinicProfile");
        var token = await ClinicalAuthTestHelper.ExtractAntiforgeryTokenAsync(get);

        var form = new Dictionary<string, string>
        {
            ["Input.Name"] = "Branded Clinic",
            ["Input.PrimaryColor"] = "#00aa66",
            ["Input.TimeZoneId"] = "UTC",
            ["Input.LanguageCode"] = "en",
            ["Input.FormStyle"] = "Default"
        };
        if (!string.IsNullOrWhiteSpace(token))
            form["__RequestVerificationToken"] = token;
        await client.PostAsync("/Settings/ClinicProfile", new FormUrlEncodedContent(form));

        var dashboard = await client.GetAsync("/Dashboard/Index");
        var html = await dashboard.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
        Assert.Contains("--clinic-primary: #00aa66", html, StringComparison.OrdinalIgnoreCase);
    }
}
