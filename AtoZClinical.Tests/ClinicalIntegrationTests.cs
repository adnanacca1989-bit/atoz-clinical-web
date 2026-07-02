using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Tests.Helpers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AtoZClinical.Tests;

public class ClinicalIntegrationTests : IClassFixture<ClinicalWebApplicationFactory>
{
    private readonly ClinicalWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ClinicalIntegrationTests(ClinicalWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
    }

    [Fact]
    public async Task Forgot_password_page_loads()
    {
        var response = await _client.GetAsync("/Account/ForgotPassword");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Reset_password_invalid_token_shows_page()
    {
        var response = await _client.GetAsync("/Account/ResetPassword?token=invalid-token");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Protected_dashboard_redirects_to_login()
    {
        var response = await _client.GetAsync("/Dashboard");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Protected_ward_redirects_to_login()
    {
        var response = await _client.GetAsync("/Ward/PatientRoom");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task Protected_search_redirects_to_login()
    {
        var response = await _client.GetAsync("/Search/Query");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task Debug_email_config_requires_health_token()
    {
        var response = await _client.GetAsync("/debug-email-config");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Debug_email_config_works_with_health_token()
    {
        using var client = _factory.CreateClientWithHealthToken();
        var response = await client.GetAsync("/debug-email-config");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Test_email_requires_health_token()
    {
        var response = await _client.GetAsync("/test-email?to=test@example.com");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Otp_send_api_returns_generic_message_for_unknown_user()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/send-otp", new { username = "nobody@example.com" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Otp_verify_api_rejects_empty_code()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/verify-otp", new { username = "vendor", code = "" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patient_service_crud_roundtrip()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClinicalDbContext>();
        var clinicId = db.Clinics.Select(c => c.Id).First();
        var patients = scope.ServiceProvider.GetRequiredService<PatientService>();

        var patient = new Patient
        {
            FirstName = "Audit",
            LastName = "Patient",
            Gender = "Female",
            Phone = "5551234567",
            DateOfBirth = new DateTime(1990, 1, 1)
        };

        var saved = await patients.SaveAsync(clinicId, patient, "integration-test");
        Assert.NotEqual(Guid.Empty, saved.Id);

        var loaded = await patients.GetAsync(clinicId, saved.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Audit Patient", loaded!.FullName);
    }

    [Fact]
    public async Task Doctor_service_crud_roundtrip()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClinicalDbContext>();
        var clinicId = db.Clinics.Select(c => c.Id).First();
        var doctors = scope.ServiceProvider.GetRequiredService<DoctorService>();

        var doctor = new Doctor
        {
            Name = "Audit Test Doctor",
            Specialty = "General",
            Phone = "5559876543"
        };

        var saved = await doctors.SaveAsync(clinicId, doctor, "integration-test");
        Assert.NotEqual(Guid.Empty, saved.Id);

        var list = await doctors.ListAsync(clinicId);
        Assert.Contains(list, d => d.Id == saved.Id);
    }

    [Fact]
    public async Task Billing_reports_page_requires_auth()
    {
        var response = await _client.GetAsync("/Reports/PlStatement");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }
}
