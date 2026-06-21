using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace AtoZClinical.Infrastructure.Data;

public static class DatabaseInitializer
{
    private const int SchemaVersion = 11;

    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseInitializer");
        var db = scope.ServiceProvider.GetRequiredService<ClinicalDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();

        await EnsureSchemaAsync(db, env, logger);

        foreach (var role in new[] { ClinicalRoles.Vendor, ClinicalRoles.ClinicAdmin, ClinicalRoles.ClinicStaff })
        {
            if (!await roles.RoleExistsAsync(role))
                await roles.CreateAsync(new IdentityRole(role));
        }

        var vendorUser = config["Seed:VendorUsername"] ?? "vendor";
        var vendorPass = config["Seed:VendorPassword"] ?? "Vendor@2026";

        if (await users.FindByNameAsync(vendorUser) is null)
        {
            var vendor = new ApplicationUser
            {
                UserName = vendorUser,
                FullName = "A to Z Vendor Admin",
                IsVendorAdmin = true,
                EmailConfirmed = true
            };
            var result = await users.CreateAsync(vendor, vendorPass);
            if (result.Succeeded)
            {
                await users.AddToRoleAsync(vendor, ClinicalRoles.Vendor);
                logger.LogInformation("Vendor account created: {User}", vendorUser);
            }
        }

        var clinicIds = await db.Clinics.Select(c => c.Id).ToListAsync();
        foreach (var clinicId in clinicIds)
            await SeedClinicDefaultsAsync(db, clinicId, logger);
    }

    public static async Task SeedClinicDefaultsAsync(ClinicalDbContext db, Guid clinicId, ILogger? logger = null)
    {
        if (!await db.ChartAccounts.AnyAsync(a => a.ClinicId == clinicId))
        {
            var accounts = new[]
            {
                new ChartAccount { ClinicId = clinicId, AccountNo = 1000, Name = "Cash", CategoryType = "Asset", DetailType = "Bank", Description = "Cash on hand" },
                new ChartAccount { ClinicId = clinicId, AccountNo = 1100, Name = "Accounts Receivable", CategoryType = "Asset", DetailType = "Accounts Receivable", Description = "Patient balances due" },
                new ChartAccount { ClinicId = clinicId, AccountNo = 4000, Name = "Clinical Revenue", CategoryType = "Income", DetailType = "Service/Fee Income", Description = "General clinical services" },
                new ChartAccount { ClinicId = clinicId, AccountNo = 4100, Name = "Laboratory Revenue", CategoryType = "Income", DetailType = "Service/Fee Income", Description = "Laboratory test income" },
                new ChartAccount { ClinicId = clinicId, AccountNo = 4200, Name = "Radiology Revenue", CategoryType = "Income", DetailType = "Service/Fee Income", Description = "Radiology imaging income" },
                new ChartAccount { ClinicId = clinicId, AccountNo = 4300, Name = "Consultation Revenue", CategoryType = "Income", DetailType = "Service/Fee Income", Description = "Doctor consultation fees" },
                new ChartAccount { ClinicId = clinicId, AccountNo = 5000, Name = "Operating Expenses", CategoryType = "Expense", DetailType = "Office/General Administrative", Description = "General clinic expenses" }
            };
            db.ChartAccounts.AddRange(accounts);
            logger?.LogInformation("Seeded {Count} default chart accounts for clinic {ClinicId}.", accounts.Length, clinicId);
        }

        if (!await db.RolePermissions.AnyAsync(r => r.ClinicId == clinicId && r.RoleName == ClinicalRoles.ClinicAdmin))
        {
            var permissions = ClinicalFormKeys.All.Select(formKey => new RolePermission
            {
                ClinicId = clinicId,
                RoleName = ClinicalRoles.ClinicAdmin,
                FormKey = formKey,
                IsVisible = true
            });
            db.RolePermissions.AddRange(permissions);
            logger?.LogInformation("Seeded Admin role permissions for clinic {ClinicId}.", clinicId);
        }

        if (!await db.ClinicConfigurations.AnyAsync(c => c.ClinicId == clinicId))
        {
            db.ClinicConfigurations.Add(new ClinicConfiguration { ClinicId = clinicId });
            logger?.LogInformation("Seeded default clinic configuration for {ClinicId}.", clinicId);
        }

        if (!await db.ClinicUoms.AnyAsync(x => x.ClinicId == clinicId))
        {
            var defaults = new[] { ("Pcs", "Pieces"), ("Box", "Box"), ("Strip", "Strip"), ("Bottle", "Bottle") };
            var n = 0;
            foreach (var (code, name) in defaults)
                db.ClinicUoms.Add(new ClinicUom { ClinicId = clinicId, UomNo = ++n, Code = code, Name = name });
        }

        if (!await db.ClinicCurrencies.AnyAsync(x => x.ClinicId == clinicId))
        {
            db.ClinicCurrencies.Add(new ClinicCurrency
            {
                ClinicId = clinicId, CurrencyNo = 1, Code = "USD", Symbol = "$", Name = "US Dollar", IsDefault = true
            });
        }

        if (!await db.ClinicLanguages.AnyAsync(x => x.ClinicId == clinicId))
        {
            db.ClinicLanguages.Add(new ClinicLanguage
            {
                ClinicId = clinicId, LanguageNo = 1, Code = "en", Name = "English", IsDefault = true
            });
        }

        if (!await db.ClinicVendors.AnyAsync(x => x.ClinicId == clinicId))
        {
            var config = await db.ClinicConfigurations.FirstOrDefaultAsync(c => c.ClinicId == clinicId);
            if (!string.IsNullOrWhiteSpace(config?.VendorName))
            {
                db.ClinicVendors.Add(new ClinicVendor
                {
                    ClinicId = clinicId,
                    VendorNo = 1,
                    Name = config.VendorName.Trim(),
                    Phone = config.VendorPhone,
                    Email = config.VendorEmail,
                    Address = config.VendorAddress
                });
            }
        }

        await db.SaveChangesAsync();
    }

    private static async Task EnsureSchemaAsync(ClinicalDbContext db, IHostEnvironment env, ILogger logger)
    {
        var creator = db.Database.GetService<IRelationalDatabaseCreator>();

        if (!await db.Database.CanConnectAsync())
        {
            await creator.CreateAsync();
            logger.LogInformation("Database created (schema v{Version}).", SchemaVersion);
            return;
        }

        if (!await creator.HasTablesAsync())
        {
            await db.Database.EnsureCreatedAsync();
            logger.LogInformation("Database schema created on empty PostgreSQL database (schema v{Version}).", SchemaVersion);
            return;
        }

        try
        {
            _ = await db.Clinics.Select(c => c.PlanName).FirstOrDefaultAsync();
            await ApplySchemaPatchesAsync(db, logger);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            await db.Database.EnsureCreatedAsync();
            logger.LogInformation("Created missing tables on PostgreSQL (schema v{Version}).", SchemaVersion);
        }
        catch (Exception ex)
        {
            if (env.IsProduction())
            {
                logger.LogError(ex, "Database schema is out of date. Back up data and apply a schema update before restarting.");
                throw;
            }

            logger.LogWarning("Schema upgrade detected — recreating database (development data will reset).");
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();
            logger.LogInformation("Database recreated (schema v{Version}).", SchemaVersion);
        }
    }

    private static async Task ApplySchemaPatchesAsync(ClinicalDbContext db, ILogger logger)
    {
        if (!db.Database.IsNpgsql()) return;

        var patches = new[]
        {
            """ALTER TABLE "PharmacyRequests" ADD COLUMN IF NOT EXISTS "Phone" text;""",
            """ALTER TABLE "PharmacyRequests" ADD COLUMN IF NOT EXISTS "City" text;""",
            """ALTER TABLE "PharmacyItems" ADD COLUMN IF NOT EXISTS "ReorderPoint" integer NOT NULL DEFAULT 0;""",
            """ALTER TABLE "PharmacyItems" ADD COLUMN IF NOT EXISTS "IncomeAccountName" text;""",
            """ALTER TABLE "PharmacyItems" ADD COLUMN IF NOT EXISTS "CostAccountName" text;"""
        };

        foreach (var sql in patches)
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(sql);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Schema patch: {Sql}", sql);
            }
        }

        logger.LogInformation("Applied PostgreSQL schema patches (v{Version}).", SchemaVersion);
    }
}
