using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Identity;
using AtoZClinical.Infrastructure.Services;
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
    private const int SchemaVersion = 20;

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
        try
        {
            await EnsureStandardChartAccountsAsync(db, clinicId, logger);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Could not seed standard chart accounts for clinic {ClinicId}.", clinicId);
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

    public static async Task EnsureStandardChartAccountsAsync(ClinicalDbContext db, Guid clinicId, ILogger? logger = null)
    {
        var existing = await db.ChartAccounts.Where(a => a.ClinicId == clinicId).ToListAsync();
        var added = 0;

        foreach (var template in ClinicLookup.StandardChartAccountTemplates)
        {
            var alreadyExists = existing.Any(a =>
                a.Name.Equals(template.Name, StringComparison.OrdinalIgnoreCase) ||
                (a.CategoryType.Equals(template.CategoryType, StringComparison.OrdinalIgnoreCase) &&
                 a.DetailType.Equals(template.DetailType, StringComparison.OrdinalIgnoreCase)));
            if (alreadyExists) continue;

            var accountNo = AllocateAccountNo(existing, template.CategoryType);
            var account = new ChartAccount
            {
                ClinicId = clinicId,
                AccountNo = accountNo,
                Name = template.Name,
                CategoryType = template.CategoryType,
                DetailType = template.DetailType,
                Description = $"{template.CategoryType} account — {template.DetailType}"
            };
            db.ChartAccounts.Add(account);
            existing.Add(account);
            added++;
            logger?.LogInformation("Added standard chart account {AccountNo} {Name} ({Category}) for clinic {ClinicId}.",
                accountNo, template.Name, template.CategoryType, clinicId);
        }

        if (added > 0)
            await db.SaveChangesAsync();
    }

    private static int AllocateAccountNo(List<ChartAccount> existing, string categoryType)
    {
        var used = existing.Select(a => a.AccountNo).ToHashSet();
        var baseNo = ClinicLookup.GetCategoryBaseAccountNo(categoryType);
        var ceiling = baseNo + 999;

        for (var no = baseNo; no <= ceiling; no++)
        {
            if (!used.Contains(no))
                return no;
        }

        var candidate = existing.Count > 0 ? existing.Max(a => a.AccountNo) + 1 : baseNo;
        while (used.Contains(candidate))
            candidate++;

        return candidate;
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
            """ALTER TABLE "PharmacyItems" ADD COLUMN IF NOT EXISTS "CostAccountName" text;""",
            """ALTER TABLE "PharmacyItems" ADD COLUMN IF NOT EXISTS "InventoryAccountName" text;""",
            """ALTER TABLE "CashReceipts" ADD COLUMN IF NOT EXISTS "Age" integer;""",
            """ALTER TABLE "CashReceipts" ADD COLUMN IF NOT EXISTS "Gender" text;""",
            """ALTER TABLE "CashReceipts" ADD COLUMN IF NOT EXISTS "Phone" text;""",
            """ALTER TABLE "CashReceipts" ADD COLUMN IF NOT EXISTS "City" text;""",
            """ALTER TABLE "CashReceipts" ADD COLUMN IF NOT EXISTS "Specialty" text;""",
            """ALTER TABLE "Invoices" ADD COLUMN IF NOT EXISTS "Age" integer;""",
            """ALTER TABLE "Invoices" ADD COLUMN IF NOT EXISTS "Gender" text;""",
            """ALTER TABLE "Invoices" ADD COLUMN IF NOT EXISTS "City" text;""",
            """CREATE INDEX IF NOT EXISTS "IX_Patients_ClinicId_AppointmentDate" ON "Patients" ("ClinicId", "AppointmentDate");""",
            """CREATE INDEX IF NOT EXISTS "IX_Patients_ClinicId_Status" ON "Patients" ("ClinicId", "Status");""",
            """CREATE INDEX IF NOT EXISTS "IX_Invoices_ClinicId_PatientId" ON "Invoices" ("ClinicId", "PatientId");""",
            """CREATE INDEX IF NOT EXISTS "IX_Invoices_ClinicId_PatientName" ON "Invoices" ("ClinicId", "PatientName");""",
            """ALTER TABLE "CashReceipts" ADD COLUMN IF NOT EXISTS "ChartAccountName" text;""",
            """ALTER TABLE "Patients" ADD COLUMN IF NOT EXISTS "HealthInsuranceName" text;""",
            """ALTER TABLE "Patients" ADD COLUMN IF NOT EXISTS "HealthInsuranceNumber" text;""",
            """ALTER TABLE "PharmacyItems" ADD COLUMN IF NOT EXISTS "ExpiryDate" timestamp with time zone;""",
            """ALTER TABLE "AspNetUsers" ADD COLUMN IF NOT EXISTS "UserNo" integer NOT NULL DEFAULT 0;""",
            """ALTER TABLE "Patients" ADD COLUMN IF NOT EXISTS "MarriedStatus" text;""",
            """ALTER TABLE "Patients" ADD COLUMN IF NOT EXISTS "MotherName" text;""",
            """ALTER TABLE "Clinics" ADD COLUMN IF NOT EXISTS "EnabledFormKeys" text;"""
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
