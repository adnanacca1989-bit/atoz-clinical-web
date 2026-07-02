using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace AtoZClinical.Tests;

[Collection("ClinicalWeb")]
public class MigrationValidationTests : IClassFixture<ClinicalWebApplicationFactory>
{
    private readonly ClinicalWebApplicationFactory _factory;

    public MigrationValidationTests(ClinicalWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Migration_assembly_contains_core_migrations()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClinicalDbContext>();
        var migrationsAssembly = db.GetService<IMigrationsAssembly>();

        var allMigrationIds = migrationsAssembly.Migrations.Keys.OrderBy(k => k).ToList();
        Assert.NotEmpty(allMigrationIds);
        Assert.Contains(allMigrationIds, id => id.Contains("InitialCreate", StringComparison.Ordinal));
        Assert.Contains(allMigrationIds, id => id.Contains("AddPhase5Enterprise", StringComparison.Ordinal));
    }

    [Fact]
    public void Migration_files_exist_on_disk()
    {
        var repoRoot = FindRepoRoot();
        var migrationsDir = Path.Combine(repoRoot, "AtoZClinical.Infrastructure", "Data", "Migrations");
        Assert.True(Directory.Exists(migrationsDir), $"Migrations directory not found: {migrationsDir}");

        var migrationFiles = Directory.GetFiles(migrationsDir, "*.cs")
            .Where(f => !f.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase)
                        && !f.EndsWith("ClinicalDbContextModelSnapshot.cs", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(migrationFiles.Count >= 10, $"Expected >= 10 migration files, found {migrationFiles.Count}");
    }

    [Fact]
    public async Task Test_host_schema_supports_enterprise_clinic_columns()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClinicalDbContext>();

        _ = await db.Clinics
            .Select(c => new { c.DedicatedConnectionName, c.SubscriptionExpiryDate, c.Subdomain })
            .FirstOrDefaultAsync();
    }

    [Fact]
    public async Task Test_host_schema_supports_clinic_branding_columns()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClinicalDbContext>();

        _ = await db.ClinicConfigurations
            .Select(c => new { c.LogoBase64, c.Tagline, c.Website, c.PrimaryColor, c.FormStyle, c.TimeZoneId })
            .FirstOrDefaultAsync();
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AtoZClinical.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
