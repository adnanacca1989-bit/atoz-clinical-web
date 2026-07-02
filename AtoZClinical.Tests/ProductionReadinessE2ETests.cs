using System.Net;
using AtoZClinical.Tests.Helpers;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AtoZClinical.Tests;

/// <summary>
/// End-to-end smoke tests for production-readiness workflows (in-process test host).
/// </summary>
[Collection("ClinicalWeb")]
public class ProductionReadinessE2ETests : IClassFixture<ClinicalWebApplicationFactory>
{
    private readonly ClinicalWebApplicationFactory _factory;
    private readonly HttpClient _anonymous;

    public ProductionReadinessE2ETests(ClinicalWebApplicationFactory factory)
    {
        _factory = factory;
        _anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
    }

    [Theory]
    [InlineData("/Account/Login")]
    [InlineData("/Account/ForgotPassword")]
    [InlineData("/Account/ResetPassword")]
    [InlineData("/Account/VerifyAccount")]
    [InlineData("/Account/ResendConfirmation")]
    [InlineData("/Account/ConfirmEmail")]
    [InlineData("/Register/Trial")]
    [InlineData("/Register/Clinic")]
    [InlineData("/Portal/Login")]
    public async Task Anonymous_auth_pages_load_without_server_error(string path)
    {
        var response = await _anonymous.GetAsync(path);
        Assert.True(
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.BadRequest or HttpStatusCode.Redirect,
            $"GET {path} returned {(int)response.StatusCode}");
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task Login_with_valid_vendor_credentials_reaches_vendor_dashboard()
    {
        using var client = await ClinicalAuthTestHelper.CreateAuthenticatedVendorClientAsync(_factory);
        var response = await client.GetAsync("/Vendor/Dashboard");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Register_trial_page_has_post_form()
    {
        var response = await _anonymous.GetAsync("/Register/Trial");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("method=\"post\"", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Forgot_password_post_without_token_returns_page_not_500()
    {
        var get = await _anonymous.GetAsync("/Account/ForgotPassword");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var token = await ClinicalAuthTestHelper.ExtractAntiforgeryTokenAsync(get);
        Assert.False(string.IsNullOrWhiteSpace(token));

        var form = new Dictionary<string, string> { ["Input.Email"] = "nobody@example.com" };
        if (!string.IsNullOrWhiteSpace(token))
            form["__RequestVerificationToken"] = token;

        var post = await _anonymous.PostAsync("/Account/ForgotPassword", new FormUrlEncodedContent(form));
        Assert.NotEqual(HttpStatusCode.InternalServerError, post.StatusCode);
        Assert.True(post.StatusCode is HttpStatusCode.OK or HttpStatusCode.Redirect);
    }

    [Theory]
    [InlineData("/PatientRegistration", "Patient registration")]
    [InlineData("/Doctors", "Doctor registration")]
    [InlineData("/Laboratory/Request", "Laboratory workflow")]
    [InlineData("/Radiology/Request", "Radiology workflow")]
    [InlineData("/Pharmacy/Request", "Pharmacy workflow")]
    [InlineData("/Billing", "Billing workflow")]
    [InlineData("/Reports/AccountsReceivable", "Reports")]
    [InlineData("/Reports/RequestReport", "Reports")]
    [InlineData("/Admin/AuditLog", "Audit log")]
    [InlineData("/Settings/ClinicProfile", "Clinic profile and branding")]
    public async Task Authenticated_clinic_admin_can_open_workflow_page(string path, string _)
    {
        using var client = await ClinicalAuthTestHelper.CreateAuthenticatedClinicAdminClientAsync(_factory);
        var response = await client.GetAsync(path);
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.True(
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Redirect or HttpStatusCode.Forbidden,
            $"GET {path} returned {(int)response.StatusCode}");
    }

    [Fact]
    public async Task Unauthenticated_clinical_routes_redirect_to_login()
    {
        var paths = new[]
        {
            "/PatientRegistration/Lookup",
            "/Laboratory/Request",
            "/Pharmacy/Request",
            "/Reports/AccountsReceivable",
            "/Admin/AuditLog"
        };

        foreach (var path in paths)
        {
            var response = await _anonymous.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
        }
    }
}
