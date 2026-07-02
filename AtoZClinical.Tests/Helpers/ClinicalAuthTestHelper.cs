using System.Net;
using AtoZClinical.Core.Entities;
using AtoZClinical.Core.Enums;
using AtoZClinical.Infrastructure;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AtoZClinical.Tests.Helpers;

public static class ClinicalAuthTestHelper
{
    public const string VendorUsername = "vendor";
    public const string VendorPassword = "TestVendor@123456!";
    public const string ClinicAdminUsername = "clinicadmin@test.local";
    public const string ClinicAdminPassword = "TestClinicAdmin@123!";

    public static async Task<HttpClient> CreateAuthenticatedVendorClientAsync(
        ClinicalWebApplicationFactory factory,
        CancellationToken cancellationToken = default)
    {
        var client = CreateClient(factory);
        await LoginAsync(client, VendorUsername, VendorPassword, cancellationToken);

        var probe = await client.GetAsync("/Vendor/Dashboard", cancellationToken);
        if (probe.StatusCode == HttpStatusCode.Redirect &&
            probe.Headers.Location?.ToString().Contains("/Account/Login", StringComparison.Ordinal) == true)
        {
            throw new InvalidOperationException("Vendor login did not establish an authenticated session.");
        }

        if (probe.StatusCode == HttpStatusCode.InternalServerError)
        {
            var body = await probe.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Vendor dashboard returned 500: {body[..Math.Min(500, body.Length)]}");
        }

        return client;
    }

    public static async Task<HttpClient> CreateAuthenticatedClinicAdminClientAsync(
        ClinicalWebApplicationFactory factory,
        CancellationToken cancellationToken = default)
    {
        await EnsureClinicAdminUserAsync(factory, cancellationToken);

        var client = CreateClient(factory);
        await LoginAsync(client, ClinicAdminUsername, ClinicAdminPassword, cancellationToken);

        var probe = await client.GetAsync("/Dashboard", cancellationToken);
        if (probe.StatusCode != HttpStatusCode.OK)
        {
            var body = await probe.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Clinic admin dashboard returned {(int)probe.StatusCode}: {body[..Math.Min(500, body.Length)]}");
        }

        return client;
    }

    private static HttpClient CreateClient(ClinicalWebApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = true,
            HandleCookies = true
        });

    private static async Task LoginAsync(
        HttpClient client,
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        var loginResponse = await client.PostAsync(
            "/Account/Login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Input.Username"] = username,
                ["Input.Password"] = password,
                ["Input.RememberMe"] = "false"
            }),
            cancellationToken);

        if (loginResponse.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Redirect))
        {
            var body = await loginResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Login failed for {username} with {(int)loginResponse.StatusCode}: {body[..Math.Min(500, body.Length)]}");
        }
    }

    private static async Task EnsureClinicAdminUserAsync(
        ClinicalWebApplicationFactory factory,
        CancellationToken cancellationToken)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClinicalDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        if (!await roles.RoleExistsAsync(ClinicalRoles.ClinicAdmin))
            await roles.CreateAsync(new IdentityRole(ClinicalRoles.ClinicAdmin));

        var clinic = await db.Clinics.OrderBy(c => c.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (clinic is null)
        {
            clinic = new Clinic
            {
                ClinicCode = "TST-0001",
                Name = "Test Clinic",
                PlanName = "Standard",
                Status = ClinicStatus.Active,
                MaxUsers = 25
            };
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync(cancellationToken);
            await DatabaseInitializer.SeedClinicDefaultsAsync(db, clinic.Id);
        }

        if (await users.FindByNameAsync(ClinicAdminUsername) is null)
        {
            var admin = new ApplicationUser
            {
                UserName = ClinicAdminUsername,
                FullName = "Test Clinic Admin",
                ClinicId = clinic.Id,
                ClinicRole = ClinicUserRole.ClinicAdmin,
                EmailConfirmed = true
            };
            var result = await users.CreateAsync(admin, ClinicAdminPassword);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    "Failed to create clinic admin: " + string.Join(", ", result.Errors.Select(e => e.Description)));
            }

            await users.AddToRoleAsync(admin, ClinicalRoles.ClinicAdmin);
        }
    }

    public static async Task<string?> ExtractAntiforgeryTokenAsync(
        HttpResponseMessage pageResponse,
        CancellationToken cancellationToken = default)
    {
        var html = await pageResponse.Content.ReadAsStringAsync(cancellationToken);
        var match = System.Text.RegularExpressions.Regex.Match(
            html, @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""");
        return match.Success ? match.Groups[1].Value : null;
    }
}
