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

namespace AtoZClinical.Infrastructure.Data;

public static class DatabaseInitializer
{
    /// <summary>Must match the initial EF Core migration id in Data/Migrations.</summary>
    private const string InitialMigrationId = "20260625014530_InitialCreate";

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
        await EnsureEnterpriseSchemaAsync(db, logger);
        await EnsureSaasPlatformSchemaAsync(db, logger);
        await EnsureServiceIncomeRequestSchemaAsync(db, logger);
        await BackfillCashReceiptPatientCreditsAsync(db, logger);

        foreach (var role in new[] { ClinicalRoles.Vendor, ClinicalRoles.ClinicAdmin, ClinicalRoles.ClinicStaff })
        {
            if (!await roles.RoleExistsAsync(role))
                await roles.CreateAsync(new IdentityRole(role));
        }

        var vendorUser = config["Seed:VendorUsername"] ?? "vendor";
        var vendorPass = config["Seed:VendorPassword"];
        if (string.IsNullOrWhiteSpace(vendorPass))
        {
            if (env.IsProduction())
            {
                throw new InvalidOperationException(
                    "Seed:VendorPassword is required in production. Set it in Render environment variables.");
            }

            vendorPass = "ChangeMe@Local2026!";
            logger.LogWarning("Using development-only vendor password. Set Seed:VendorPassword for production.");
        }

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
        var cache = scope.ServiceProvider.GetService<ClinicRuntimeCache>();
        foreach (var clinicId in clinicIds)
        {
            await SeedClinicDefaultsAsync(db, clinicId, logger);
            cache?.InvalidateVisibleForms(clinicId);
        }
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

        await BackfillRolePermissionsAsync(db, clinicId, logger);

        if (!await db.ClinicConfigurations.AnyAsync(c => c.ClinicId == clinicId))
        {
            db.ClinicConfigurations.Add(new ClinicConfiguration { ClinicId = clinicId, PatientPortalEnabled = true });
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
        if (db.Database.IsSqlite())
        {
            var recreate = !await db.Database.CanConnectAsync();
            if (!recreate)
            {
                try
                {
                    await db.Database.ExecuteSqlRawAsync("SELECT \"UserNo\" FROM \"AspNetUsers\" LIMIT 1;");
                }
                catch
                {
                    recreate = true;
                }
            }

            if (recreate)
            {
                await db.Database.EnsureDeletedAsync();
                await db.Database.EnsureCreatedAsync();
                logger.LogInformation("SQLite development schema ensured.");
            }

            return;
        }

        try
        {
            await BaselineLegacySchemaIfNeededAsync(db, logger);
            await db.Database.MigrateAsync();
            logger.LogInformation("PostgreSQL migrations applied.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database migration failed.");
            if (env.IsProduction())
                throw;

            logger.LogWarning("Development fallback: recreating local database.");
            await db.Database.EnsureDeletedAsync();
            await db.Database.MigrateAsync();
        }
    }

    /// <summary>
    /// Databases created with EnsureCreated (pre-migration) get baselined so MigrateAsync does not fail.
    /// </summary>
    private static async Task BaselineLegacySchemaIfNeededAsync(ClinicalDbContext db, ILogger logger)
    {
        var applied = await db.Database.GetAppliedMigrationsAsync();
        if (applied.Any())
            return;

        var creator = db.Database.GetService<IRelationalDatabaseCreator>();
        if (!await creator.HasTablesAsync())
            return;

        logger.LogWarning(
            "Legacy schema detected without migration history. Baselining {MigrationId}.",
            InitialMigrationId);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                "MigrationId" character varying(150) NOT NULL,
                "ProductVersion" character varying(32) NOT NULL,
                CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
            );
            """);

        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            VALUES ({0}, {1})
            ON CONFLICT ("MigrationId") DO NOTHING;
            """,
            InitialMigrationId,
            "8.0.11");
    }

    private static async Task EnsureEnterpriseSchemaAsync(ClinicalDbContext db, ILogger logger)
    {
        if (db.Database.IsSqlite())
            return;

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE "Clinics" ADD COLUMN IF NOT EXISTS "Subdomain" character varying(63) NULL;
                ALTER TABLE "Clinics" ADD COLUMN IF NOT EXISTS "DedicatedConnectionName" character varying(128) NULL;
                ALTER TABLE "ClinicConfigurations" ADD COLUMN IF NOT EXISTS "PatientPortalEnabled" boolean NOT NULL DEFAULT false;
                """);

            await db.Database.ExecuteSqlRawAsync(
                """
                UPDATE "ClinicConfigurations"
                SET "PatientPortalEnabled" = true
                WHERE "PatientPortalEnabled" = false;
                """);

            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "ClinicApiKeys" (
                    "Id" uuid NOT NULL,
                    "ClinicId" uuid NOT NULL,
                    "Name" character varying(100) NOT NULL,
                    "KeyPrefix" character varying(16) NOT NULL,
                    "KeyHash" character varying(128) NOT NULL,
                    "IsActive" boolean NOT NULL,
                    "CreatedAt" timestamp without time zone NOT NULL,
                    "LastUsedAt" timestamp without time zone NULL,
                    CONSTRAINT "PK_ClinicApiKeys" PRIMARY KEY ("Id")
                );
                """);

            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "WebhookSubscriptions" (
                    "Id" uuid NOT NULL,
                    "ClinicId" uuid NOT NULL,
                    "TargetUrl" character varying(500) NOT NULL,
                    "Secret" character varying(128) NOT NULL,
                    "Events" character varying(500) NOT NULL,
                    "IsActive" boolean NOT NULL,
                    "CreatedAt" timestamp without time zone NOT NULL,
                    CONSTRAINT "PK_WebhookSubscriptions" PRIMARY KEY ("Id")
                );
                """);

            logger.LogInformation("Enterprise schema columns verified.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Enterprise schema verification skipped.");
        }
    }

    private static async Task EnsureSaasPlatformSchemaAsync(ClinicalDbContext db, ILogger logger)
    {
        if (db.Database.IsSqlite())
            return;

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE "Clinics" ADD COLUMN IF NOT EXISTS "SubscriptionType" character varying(64) NOT NULL DEFAULT 'Standard';
                ALTER TABLE "Clinics" ADD COLUMN IF NOT EXISTS "SubscriptionStartDate" timestamp without time zone NULL;
                ALTER TABLE "Clinics" ADD COLUMN IF NOT EXISTS "SubscriptionExpiryDate" timestamp without time zone NULL;
                ALTER TABLE "Clinics" ADD COLUMN IF NOT EXISTS "TimeZoneId" character varying(64) NULL;
                ALTER TABLE "ClinicConfigurations" ADD COLUMN IF NOT EXISTS "TimeZoneId" character varying(64) NOT NULL DEFAULT 'UTC';
                ALTER TABLE "ClinicConfigurations" ADD COLUMN IF NOT EXISTS "LogoBase64" text NULL;
                ALTER TABLE "ClinicConfigurations" ADD COLUMN IF NOT EXISTS "Tagline" character varying(200) NULL;
                ALTER TABLE "ClinicConfigurations" ADD COLUMN IF NOT EXISTS "Website" character varying(256) NULL;
                """);

            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "SecurityAuditEntries" (
                    "Id" uuid NOT NULL,
                    "ClinicId" uuid NULL,
                    "EventType" character varying(64) NOT NULL,
                    "UserName" character varying(256) NULL,
                    "Details" text NULL,
                    "IpAddress" character varying(64) NULL,
                    "CreatedAt" timestamp without time zone NOT NULL,
                    CONSTRAINT "PK_SecurityAuditEntries" PRIMARY KEY ("Id")
                );
                CREATE INDEX IF NOT EXISTS "IX_SecurityAuditEntries_ClinicId" ON "SecurityAuditEntries" ("ClinicId");
                CREATE INDEX IF NOT EXISTS "IX_SecurityAuditEntries_EventType" ON "SecurityAuditEntries" ("EventType");
                """);

            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "ClinicBackupHistories" (
                    "Id" uuid NOT NULL,
                    "ClinicId" uuid NOT NULL,
                    "Action" character varying(32) NOT NULL,
                    "FileName" character varying(256) NOT NULL,
                    "FileSizeBytes" bigint NOT NULL,
                    "PerformedBy" character varying(256) NULL,
                    "CreatedAt" timestamp without time zone NOT NULL,
                    "Notes" text NULL,
                    CONSTRAINT "PK_ClinicBackupHistories" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_ClinicBackupHistories_Clinics_ClinicId" FOREIGN KEY ("ClinicId") REFERENCES "Clinics" ("Id") ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS "IX_ClinicBackupHistories_ClinicId_CreatedAt" ON "ClinicBackupHistories" ("ClinicId", "CreatedAt");
                """);

            logger.LogInformation("SaaS platform schema verified.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SaaS platform schema verification skipped.");
        }
    }

    private static async Task BackfillRolePermissionsAsync(ClinicalDbContext db, Guid clinicId, ILogger? logger)
    {
        var roles = await db.RolePermissions.Where(r => r.ClinicId == clinicId).Select(r => r.RoleName).Distinct().ToListAsync();
        if (roles.Count == 0) return;

        var added = 0;
        foreach (var role in roles)
        {
            var existing = await db.RolePermissions
                .Where(r => r.ClinicId == clinicId && r.RoleName == role)
                .Select(r => r.FormKey)
                .ToListAsync();
            var existingSet = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var formKey in ClinicalFormKeys.All)
            {
                if (existingSet.Contains(formKey)) continue;
                db.RolePermissions.Add(new RolePermission
                {
                    ClinicId = clinicId,
                    RoleName = role,
                    FormKey = formKey,
                    IsVisible = true
                });
                added++;
            }
        }

        if (added > 0)
            logger?.LogInformation("Backfilled {Count} role permission(s) for clinic {ClinicId}.", added, clinicId);
    }

    private static async Task EnsureServiceIncomeRequestSchemaAsync(ClinicalDbContext db, ILogger logger)
    {
        if (db.Database.IsSqlite())
            return;

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "ServiceIncomeRequests" (
                    "Id" uuid NOT NULL,
                    "ClinicId" uuid NOT NULL,
                    "RequestNo" integer NOT NULL,
                    "RequestDate" timestamp without time zone NOT NULL,
                    "PatientName" text NULL,
                    "PatientBarcode" text NULL,
                    "Age" integer NULL,
                    "Gender" text NULL,
                    "Phone" text NULL,
                    "City" text NULL,
                    "DoctorName" text NULL,
                    "Specialty" text NULL,
                    "TotalAmount" numeric NOT NULL,
                    "CreatedAt" timestamp without time zone NOT NULL,
                    "UpdatedAt" timestamp without time zone NOT NULL,
                    CONSTRAINT "PK_ServiceIncomeRequests" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_ServiceIncomeRequests_Clinics_ClinicId" FOREIGN KEY ("ClinicId") REFERENCES "Clinics" ("Id") ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_ServiceIncomeRequests_ClinicId_RequestNo"
                    ON "ServiceIncomeRequests" ("ClinicId", "RequestNo");

                CREATE TABLE IF NOT EXISTS "ServiceIncomeRequestLines" (
                    "Id" uuid NOT NULL,
                    "ServiceIncomeRequestId" uuid NOT NULL,
                    "LineNo" integer NOT NULL,
                    "ServiceNo" integer NULL,
                    "ServiceName" text NULL,
                    "AccountName" text NULL,
                    "Qty" integer NOT NULL,
                    "Fee" numeric NOT NULL,
                    "Total" numeric NOT NULL,
                    CONSTRAINT "PK_ServiceIncomeRequestLines" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_ServiceIncomeRequestLines_ServiceIncomeRequests_ServiceIncomeRequestId"
                        FOREIGN KEY ("ServiceIncomeRequestId") REFERENCES "ServiceIncomeRequests" ("Id") ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS "IX_ServiceIncomeRequestLines_ServiceIncomeRequestId"
                    ON "ServiceIncomeRequestLines" ("ServiceIncomeRequestId");
                """);

            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                VALUES ('20260626150000_AddServiceIncomeRequest', '8.0.11')
                ON CONFLICT ("MigrationId") DO NOTHING;
                """);

            logger.LogInformation("Service income request schema verified.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Service income request schema verification skipped.");
        }
    }

    private static async Task BackfillCashReceiptPatientCreditsAsync(ClinicalDbContext db, ILogger logger)
    {
        if (db.Database.IsSqlite())
            return;

        try
        {
            var receipts = await db.CashReceipts
                .Where(r => r.PatientCredit == 0 && r.BalanceDue > 0 && r.Amount > r.BalanceDue)
                .ToListAsync();

            if (receipts.Count == 0)
                return;

            foreach (var receipt in receipts)
                receipt.PatientCredit = InvoiceArCalculator.GetReceiptUnappliedCredit(receipt);

            await db.SaveChangesAsync();
            logger.LogInformation("Backfilled PatientCredit on {Count} cash receipt(s).", receipts.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cash receipt PatientCredit backfill skipped (column may not exist yet).");
        }
    }
}
