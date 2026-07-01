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
        await EnsurePasswordResetTokenSchemaAsync(db, logger);
        await EnsureRegistrationVerificationCodeSchemaAsync(db, logger);
        await EnsureServiceIncomeRequestSchemaAsync(db, logger);
        await EnsureInpatientSchemaAsync(db, logger);
        await EnsurePrescriptionLinesSchemaAsync(db, logger);
        await EnsureExpenseJournalSchemaAsync(db, logger);
        await EnsureVendorPaymentSchemaAsync(db, logger);
        await EnsureJournalEntryNamesSchemaAsync(db, logger);
        await EnsureJournalPostingSchemaAsync(db, logger);
        await EnsureInvoiceRecordLinkSchemaAsync(db, logger);
        await EnsureDoctorRecordLinkSchemaAsync(db, logger);
        await BackfillDoctorRecordLinksAsync(db, scope.ServiceProvider, logger);
        await EnsurePatientRecordLinkSchemaAsync(db, logger);
        await BackfillPatientRecordLinksAsync(db, scope.ServiceProvider, logger);
        await BackfillPatientVisitStatusesAsync(db, scope.ServiceProvider, logger);
        await EnsureRoleAccessSchemaAsync(db, logger);
        await EnsureMessagingSchemaAsync(db, logger);
        await EnsureDataProtectionKeysSchemaAsync(db, logger);
        await BackfillClinicEnabledModulesAsync(db, logger);
        await BackfillCashReceiptPatientCreditsAsync(db, logger);
        await BackfillUnconfirmedAccountsAsync(db, config, logger);

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
        var audit = scope.ServiceProvider.GetService<AuditService>();
        foreach (var clinicId in clinicIds)
        {
            await SeedClinicDefaultsAsync(db, clinicId, logger, audit);
            cache?.InvalidateVisibleForms(clinicId);
        }

        await EnsureDoctorViewAllPatientsEnabledAsync(db, cache, logger);
    }

    /// <summary>
    /// Doctors see all clinic patients by default. Updates legacy clinics that still have isolation enabled.
    /// </summary>
    private static async Task EnsureDoctorViewAllPatientsEnabledAsync(
        ClinicalDbContext db,
        ClinicRuntimeCache? cache,
        ILogger logger)
    {
        try
        {
            var clinicIds = await db.ClinicConfigurations
                .IgnoreQueryFilters()
                .Where(c => !c.AllowDoctorViewAllPatients)
                .Select(c => c.ClinicId)
                .ToListAsync();

            if (clinicIds.Count == 0)
                return;

            await db.ClinicConfigurations
                .IgnoreQueryFilters()
                .Where(c => !c.AllowDoctorViewAllPatients)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.AllowDoctorViewAllPatients, true));

            foreach (var clinicId in clinicIds)
                cache?.InvalidateConfiguration(clinicId);

            logger.LogInformation(
                "Doctor patient isolation disabled for {Count} clinic(s) — all doctors can view all patients.",
                clinicIds.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Doctor view-all patients enable step skipped.");
        }
    }

    public static async Task SeedClinicDefaultsAsync(
        ClinicalDbContext db,
        Guid clinicId,
        ILogger? logger = null,
        AuditService? audit = null)
    {
        try
        {
            await EnsureStandardChartAccountsAsync(db, clinicId, logger);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Could not seed standard chart accounts for clinic {ClinicId}.", clinicId);
        }

        if (!await db.RolePermissions.AnyAsync(r => r.ClinicId == clinicId && (r.RoleName == "Admin" || r.RoleName == ClinicalRoles.ClinicAdmin)))
        {
            var permissions = ClinicalFormKeys.All.Select(formKey => new RolePermission
            {
                ClinicId = clinicId,
                RoleName = "Admin",
                FormKey = formKey,
                IsVisible = true
            });
            db.RolePermissions.AddRange(permissions);
            logger?.LogInformation("Seeded Admin role permissions for clinic {ClinicId}.", clinicId);
        }

        await BackfillRolePermissionsAsync(db, clinicId, logger);
        await RolePermissionBootstrap.EnsureClinicRolesAsync(db, clinicId, logger, audit);

        if (!await db.ClinicConfigurations.AnyAsync(c => c.ClinicId == clinicId))
        {
            db.ClinicConfigurations.Add(new ClinicConfiguration
            {
                ClinicId = clinicId,
                PatientPortalEnabled = true,
                AllowDoctorViewAllPatients = true
            });
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

    private static async Task EnsureSqliteCompatibilityPatchesAsync(ClinicalDbContext db, ILogger logger)
    {
        if (!db.Database.IsSqlite())
            return;

        foreach (var (table, column, sqlType) in new (string, string, string)[]
        {
            ("AspNetUsers", "DoctorRecordId", "TEXT"),
            ("Patients", "DoctorRecordId", "TEXT"),
            ("Patients", "PatientRecordId", "TEXT"),
        })
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(
                    $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {sqlType} NULL;");
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "SQLite patch skipped for {Table}.{Column}", table, column);
            }
        }
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
                    await db.Database.ExecuteSqlRawAsync(
                        "SELECT \"UserNo\", \"DoctorRecordId\" FROM \"AspNetUsers\" LIMIT 1;");
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

            await EnsureSqliteCompatibilityPatchesAsync(db, logger);
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
                ALTER TABLE "ClinicConfigurations" ADD COLUMN IF NOT EXISTS "PrimaryColor" text NOT NULL DEFAULT '#0b4f8a';
                ALTER TABLE "ClinicConfigurations" ADD COLUMN IF NOT EXISTS "FormStyle" text NOT NULL DEFAULT 'Default';
                ALTER TABLE "ClinicConfigurations" ADD COLUMN IF NOT EXISTS "LanguageCode" text NOT NULL DEFAULT 'en';
                ALTER TABLE "ClinicConfigurations" ADD COLUMN IF NOT EXISTS "LanguageName" text NOT NULL DEFAULT 'English';
                ALTER TABLE "ClinicConfigurations" ADD COLUMN IF NOT EXISTS "AllowDoctorViewAllPatients" boolean NOT NULL DEFAULT false;
                ALTER TABLE "AspNetUsers" ADD COLUMN IF NOT EXISTS "DoctorRecordId" uuid NULL;
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

    private static async Task EnsurePasswordResetTokenSchemaAsync(ClinicalDbContext db, ILogger logger)
    {
        if (db.Database.IsSqlite())
            return;

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "PasswordResetTokens" (
                    "Id" uuid NOT NULL,
                    "UserId" character varying(450) NOT NULL,
                    "TokenHash" character varying(128) NOT NULL,
                    "ExpiryDate" timestamp without time zone NOT NULL,
                    "Used" boolean NOT NULL DEFAULT false,
                    "CreatedAt" timestamp without time zone NOT NULL,
                    CONSTRAINT "PK_PasswordResetTokens" PRIMARY KEY ("Id")
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_PasswordResetTokens_TokenHash" ON "PasswordResetTokens" ("TokenHash");
                CREATE INDEX IF NOT EXISTS "IX_PasswordResetTokens_UserId" ON "PasswordResetTokens" ("UserId");
                """);

            logger.LogInformation("Password reset token schema verified.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Password reset token schema verification skipped.");
        }
    }

    private static async Task EnsureRegistrationVerificationCodeSchemaAsync(ClinicalDbContext db, ILogger logger)
    {
        if (db.Database.IsSqlite())
            return;

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "RegistrationVerificationCodes" (
                    "Id" uuid NOT NULL,
                    "UserId" character varying(450) NOT NULL,
                    "Channel" integer NOT NULL,
                    "Destination" character varying(256) NOT NULL,
                    "CodeHash" character varying(128) NOT NULL,
                    "ExpiryDate" timestamp without time zone NOT NULL,
                    "Used" boolean NOT NULL DEFAULT false,
                    "FailedAttempts" integer NOT NULL DEFAULT 0,
                    "CreatedAt" timestamp without time zone NOT NULL,
                    CONSTRAINT "PK_RegistrationVerificationCodes" PRIMARY KEY ("Id")
                );
                CREATE INDEX IF NOT EXISTS "IX_RegistrationVerificationCodes_UserId" ON "RegistrationVerificationCodes" ("UserId");
                CREATE INDEX IF NOT EXISTS "IX_RegistrationVerificationCodes_CodeHash" ON "RegistrationVerificationCodes" ("CodeHash");
                """);

            logger.LogInformation("Registration verification code schema verified.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Registration verification code schema verification skipped.");
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

    private static async Task EnsureInpatientSchemaAsync(ClinicalDbContext db, ILogger logger)
    {
        if (db.Database.IsSqlite())
            return;

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "DoctorSurgeries" (
                    "Id" uuid NOT NULL,
                    "ClinicId" uuid NOT NULL,
                    "SurgeryNo" integer NOT NULL,
                    "RecordDate" timestamp without time zone NOT NULL,
                    "SurgeryDate" timestamp without time zone NULL,
                    "SurgeryTime" interval NULL,
                    "PatientRecordId" uuid NULL,
                    "PatientName" text NULL,
                    "PatientBarcode" text NULL,
                    "Age" integer NULL,
                    "City" text NULL,
                    "NationalId" text NULL,
                    "Phone" text NULL,
                    "MotherName" text NULL,
                    "DoctorRecordId" uuid NULL,
                    "DoctorName" text NULL,
                    "Specialty" text NULL,
                    "TypeOfSurgery" text NULL,
                    "Classify" text NULL,
                    "SurgeryName" text NULL,
                    "InitialAmount" numeric NOT NULL DEFAULT 0,
                    "CreatedAt" timestamp without time zone NOT NULL,
                    "UpdatedAt" timestamp without time zone NOT NULL,
                    CONSTRAINT "PK_DoctorSurgeries" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_DoctorSurgeries_Clinics_ClinicId" FOREIGN KEY ("ClinicId") REFERENCES "Clinics" ("Id") ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_DoctorSurgeries_ClinicId_SurgeryNo"
                    ON "DoctorSurgeries" ("ClinicId", "SurgeryNo");

                CREATE TABLE IF NOT EXISTS "RoomBookings" (
                    "Id" uuid NOT NULL,
                    "ClinicId" uuid NOT NULL,
                    "BookingNo" integer NOT NULL,
                    "DateBook" timestamp without time zone NOT NULL,
                    "PatientRecordId" uuid NULL,
                    "DoctorSurgeryId" uuid NULL,
                    "PatientName" text NULL,
                    "PatientBarcode" text NULL,
                    "Age" integer NULL,
                    "City" text NULL,
                    "NationalId" text NULL,
                    "Phone" text NULL,
                    "MotherName" text NULL,
                    "DoctorName" text NULL,
                    "Specialty" text NULL,
                    "TypeOfSurgery" text NULL,
                    "Classify" text NULL,
                    "SurgeryName" text NULL,
                    "RoomNumber" integer NULL,
                    "EnterDate" timestamp without time zone NULL,
                    "ExitDate" timestamp without time zone NULL,
                    "EnterTime" interval NULL,
                    "ExitTime" interval NULL,
                    "Days" integer NULL,
                    "Note" text NULL,
                    "CreatedAt" timestamp without time zone NOT NULL,
                    "UpdatedAt" timestamp without time zone NOT NULL,
                    CONSTRAINT "PK_RoomBookings" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_RoomBookings_Clinics_ClinicId" FOREIGN KEY ("ClinicId") REFERENCES "Clinics" ("Id") ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_RoomBookings_ClinicId_BookingNo"
                    ON "RoomBookings" ("ClinicId", "BookingNo");

                CREATE TABLE IF NOT EXISTS "WardRooms" (
                    "Id" uuid NOT NULL,
                    "ClinicId" uuid NOT NULL,
                    "RoomNo" integer NOT NULL,
                    "Status" text NOT NULL,
                    "UpdatedAt" timestamp without time zone NOT NULL,
                    CONSTRAINT "PK_WardRooms" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_WardRooms_Clinics_ClinicId" FOREIGN KEY ("ClinicId") REFERENCES "Clinics" ("Id") ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_WardRooms_ClinicId_RoomNo"
                    ON "WardRooms" ("ClinicId", "RoomNo");
                """);

            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                VALUES ('20260630150000_AddInpatientForms', '8.0.11')
                ON CONFLICT ("MigrationId") DO NOTHING;
                """);

            logger.LogInformation("Inpatient (surgery/ward) schema verified.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Inpatient schema verification skipped.");
        }
    }

    private static async Task EnsurePrescriptionLinesSchemaAsync(ClinicalDbContext db, ILogger logger)
    {
        if (db.Database.IsSqlite())
            return;

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "PrescriptionLines" (
                    "Id" uuid NOT NULL,
                    "PrescriptionId" uuid NOT NULL,
                    "LineNo" integer NOT NULL,
                    "PharmacyItemId" uuid NULL,
                    "MedicineName" text NULL,
                    "MedicationForm" text NULL,
                    "Dose" text NULL,
                    "Unit" text NULL,
                    "Frequency" text NULL,
                    "Duration" text NULL,
                    "Instruction" text NULL,
                    CONSTRAINT "PK_PrescriptionLines" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_PrescriptionLines_Prescriptions_PrescriptionId"
                        FOREIGN KEY ("PrescriptionId") REFERENCES "Prescriptions" ("Id") ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS "IX_PrescriptionLines_PrescriptionId"
                    ON "PrescriptionLines" ("PrescriptionId");
                """);

            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                VALUES ('20260627150000_AddPrescriptionLines', '8.0.11')
                ON CONFLICT ("MigrationId") DO NOTHING;
                """);

            logger.LogInformation("Prescription lines schema verified.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Prescription lines schema verification skipped.");
        }
    }

    private static async Task EnsureExpenseJournalSchemaAsync(ClinicalDbContext db, ILogger logger)
    {
        if (db.Database.IsSqlite())
            return;

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "JournalEntries" (
                    "Id" uuid NOT NULL,
                    "ClinicId" uuid NOT NULL,
                    "EntryNo" integer NOT NULL,
                    "EntryDate" timestamp without time zone NOT NULL,
                    "SourceType" text NOT NULL,
                    "SourceId" uuid NULL,
                    "Description" text NULL,
                    "CreatedAt" timestamp without time zone NOT NULL,
                    "UpdatedAt" timestamp without time zone NOT NULL,
                    CONSTRAINT "PK_JournalEntries" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_JournalEntries_Clinics_ClinicId" FOREIGN KEY ("ClinicId") REFERENCES "Clinics" ("Id") ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_JournalEntries_ClinicId_EntryNo"
                    ON "JournalEntries" ("ClinicId", "EntryNo");

                CREATE TABLE IF NOT EXISTS "JournalEntryLines" (
                    "Id" uuid NOT NULL,
                    "JournalEntryId" uuid NOT NULL,
                    "LineNo" integer NOT NULL,
                    "AccountName" text NOT NULL,
                    "AccountCategory" text NULL,
                    "Debit" numeric NOT NULL,
                    "Credit" numeric NOT NULL,
                    "Description" text NULL,
                    CONSTRAINT "PK_JournalEntryLines" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_JournalEntryLines_JournalEntries_JournalEntryId"
                        FOREIGN KEY ("JournalEntryId") REFERENCES "JournalEntries" ("Id") ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS "IX_JournalEntryLines_JournalEntryId"
                    ON "JournalEntryLines" ("JournalEntryId");

                CREATE TABLE IF NOT EXISTS "ExpenseVouchers" (
                    "Id" uuid NOT NULL,
                    "ClinicId" uuid NOT NULL,
                    "ExpenseNo" integer NOT NULL,
                    "ExpenseDate" timestamp without time zone NOT NULL,
                    "PaymentMethod" text NOT NULL,
                    "Description" text NULL,
                    "TotalAmount" numeric NOT NULL,
                    "JournalEntryId" uuid NULL,
                    "CreatedAt" timestamp without time zone NOT NULL,
                    "UpdatedAt" timestamp without time zone NOT NULL,
                    CONSTRAINT "PK_ExpenseVouchers" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_ExpenseVouchers_Clinics_ClinicId" FOREIGN KEY ("ClinicId") REFERENCES "Clinics" ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_ExpenseVouchers_JournalEntries_JournalEntryId" FOREIGN KEY ("JournalEntryId") REFERENCES "JournalEntries" ("Id") ON DELETE SET NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_ExpenseVouchers_ClinicId_ExpenseNo"
                    ON "ExpenseVouchers" ("ClinicId", "ExpenseNo");
                CREATE INDEX IF NOT EXISTS "IX_ExpenseVouchers_JournalEntryId"
                    ON "ExpenseVouchers" ("JournalEntryId");

                CREATE TABLE IF NOT EXISTS "ExpenseVoucherLines" (
                    "Id" uuid NOT NULL,
                    "ExpenseVoucherId" uuid NOT NULL,
                    "LineNo" integer NOT NULL,
                    "ChartAccountName" text NOT NULL,
                    "Amount" numeric NOT NULL,
                    "Description" text NULL,
                    CONSTRAINT "PK_ExpenseVoucherLines" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_ExpenseVoucherLines_ExpenseVouchers_ExpenseVoucherId"
                        FOREIGN KEY ("ExpenseVoucherId") REFERENCES "ExpenseVouchers" ("Id") ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS "IX_ExpenseVoucherLines_ExpenseVoucherId"
                    ON "ExpenseVoucherLines" ("ExpenseVoucherId");
                """);

            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                VALUES ('20260627160000_AddExpenseVoucherAndJournal', '8.0.11')
                ON CONFLICT ("MigrationId") DO NOTHING;
                """);

            logger.LogInformation("Expense voucher and journal schema verified.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Expense voucher and journal schema verification skipped.");
        }
    }

    private static async Task EnsureVendorPaymentSchemaAsync(ClinicalDbContext db, ILogger logger)
    {
        if (db.Database.IsSqlite())
            return;

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE "CashPayments" ADD COLUMN IF NOT EXISTS "VendorId" uuid NULL;
                ALTER TABLE "CashPayments" ADD COLUMN IF NOT EXISTS "JournalEntryId" uuid NULL;
                ALTER TABLE "ExpenseVouchers" ADD COLUMN IF NOT EXISTS "PayeeName" text NULL;
                ALTER TABLE "ExpenseVouchers" ADD COLUMN IF NOT EXISTS "CreditAccountName" text NULL;
                """);

            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                VALUES ('20260627170000_AddVendorPaymentAndExpenseFields', '8.0.11')
                ON CONFLICT ("MigrationId") DO NOTHING;
                """);

            logger.LogInformation("Vendor payment and expense voucher columns verified.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Vendor payment schema verification skipped.");
        }
    }

    private static async Task EnsureJournalPostingSchemaAsync(ClinicalDbContext db, ILogger logger)
    {
        if (db.Database.IsSqlite())
            return;

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE "JournalEntries" ADD COLUMN IF NOT EXISTS "IsPosted" boolean NOT NULL DEFAULT true;
                ALTER TABLE "JournalEntries" ADD COLUMN IF NOT EXISTS "IsDeleted" boolean NOT NULL DEFAULT false;
                """);

            logger.LogInformation("Journal posting flags verified.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Journal posting schema verification skipped.");
        }
    }

    private static async Task EnsureJournalEntryNamesSchemaAsync(ClinicalDbContext db, ILogger logger)
    {
        if (db.Database.IsSqlite())
            return;

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE "JournalEntries" ADD COLUMN IF NOT EXISTS "PatientName" text NULL;
                ALTER TABLE "JournalEntries" ADD COLUMN IF NOT EXISTS "DoctorName" text NULL;
                """);

            logger.LogInformation("Journal entry patient/doctor columns verified.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Journal entry name columns verification skipped.");
        }
    }

    private static async Task EnsureDoctorRecordLinkSchemaAsync(ClinicalDbContext db, ILogger logger)
    {
        if (db.Database.IsSqlite())
            return;

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE "Patients" ADD COLUMN IF NOT EXISTS "DoctorRecordId" uuid NULL;
                ALTER TABLE "LabRequests" ADD COLUMN IF NOT EXISTS "DoctorRecordId" uuid NULL;
                ALTER TABLE "LabResults" ADD COLUMN IF NOT EXISTS "DoctorRecordId" uuid NULL;
                ALTER TABLE "RadiologyRequests" ADD COLUMN IF NOT EXISTS "DoctorRecordId" uuid NULL;
                ALTER TABLE "RadiologyResults" ADD COLUMN IF NOT EXISTS "DoctorRecordId" uuid NULL;
                ALTER TABLE "PharmacyRequests" ADD COLUMN IF NOT EXISTS "DoctorRecordId" uuid NULL;
                ALTER TABLE "PharmacyBills" ADD COLUMN IF NOT EXISTS "DoctorRecordId" uuid NULL;
                ALTER TABLE "CashReceipts" ADD COLUMN IF NOT EXISTS "DoctorRecordId" uuid NULL;
                ALTER TABLE "CashPayments" ADD COLUMN IF NOT EXISTS "DoctorRecordId" uuid NULL;
                ALTER TABLE "Prescriptions" ADD COLUMN IF NOT EXISTS "DoctorRecordId" uuid NULL;
                ALTER TABLE "ServiceIncomeRequests" ADD COLUMN IF NOT EXISTS "DoctorRecordId" uuid NULL;
                ALTER TABLE "Appointments" ADD COLUMN IF NOT EXISTS "DoctorRecordId" uuid NULL;
                """);

            logger.LogInformation("Doctor record link columns verified.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Doctor record link columns verification skipped.");
        }
    }

    private static async Task BackfillDoctorRecordLinksAsync(
        ClinicalDbContext db,
        IServiceProvider services,
        ILogger logger)
    {
        if (db.Database.IsSqlite())
            return;

        try
        {
            await DoctorRecordLinkBackfill.BackfillAsync(db);
            await DoctorUserLinkBackfill.BackfillAsync(db, logger);

            var propagation = services.GetRequiredService<MasterDataPropagationService>();
            var clinicIds = await db.Clinics.AsNoTracking().Select(c => c.Id).ToListAsync();
            foreach (var clinicId in clinicIds)
                await propagation.SyncAllDoctorLinkedRowsAsync(clinicId);

            logger.LogInformation("Doctor record links backfilled and synced.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Doctor record link backfill skipped.");
        }
    }

    private static async Task EnsurePatientRecordLinkSchemaAsync(ClinicalDbContext db, ILogger logger)
    {
        if (db.Database.IsSqlite())
            return;

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE "LabRequests" ADD COLUMN IF NOT EXISTS "PatientRecordId" uuid NULL;
                ALTER TABLE "LabResults" ADD COLUMN IF NOT EXISTS "PatientRecordId" uuid NULL;
                ALTER TABLE "RadiologyRequests" ADD COLUMN IF NOT EXISTS "PatientRecordId" uuid NULL;
                ALTER TABLE "RadiologyResults" ADD COLUMN IF NOT EXISTS "PatientRecordId" uuid NULL;
                ALTER TABLE "PharmacyRequests" ADD COLUMN IF NOT EXISTS "PatientRecordId" uuid NULL;
                ALTER TABLE "PharmacyBills" ADD COLUMN IF NOT EXISTS "PatientRecordId" uuid NULL;
                ALTER TABLE "CashReceipts" ADD COLUMN IF NOT EXISTS "PatientRecordId" uuid NULL;
                ALTER TABLE "CashPayments" ADD COLUMN IF NOT EXISTS "PatientRecordId" uuid NULL;
                ALTER TABLE "Prescriptions" ADD COLUMN IF NOT EXISTS "PatientRecordId" uuid NULL;
                ALTER TABLE "ServiceIncomeRequests" ADD COLUMN IF NOT EXISTS "PatientRecordId" uuid NULL;
                """);

            logger.LogInformation("Patient record link columns verified.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Patient record link columns verification skipped.");
        }
    }

    private static async Task BackfillPatientRecordLinksAsync(
        ClinicalDbContext db,
        IServiceProvider services,
        ILogger logger)
    {
        try
        {
            await PatientRecordLinkBackfill.BackfillAsync(db);

            var propagation = services.GetRequiredService<MasterDataPropagationService>();
            var clinicIds = await db.Clinics.AsNoTracking().Select(c => c.Id).ToListAsync();
            foreach (var clinicId in clinicIds)
                await propagation.SyncAllPatientLinkedRowsAsync(clinicId);

            logger.LogInformation("Patient record links backfilled and synced.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Patient record link backfill skipped.");
        }
    }

    private static async Task BackfillPatientVisitStatusesAsync(
        ClinicalDbContext db,
        IServiceProvider services,
        ILogger logger)
    {
        try
        {
            var visitStatus = services.GetRequiredService<PatientVisitStatusService>();
            var clinicIds = await db.Clinics.AsNoTracking().Select(c => c.Id).ToListAsync();
            var total = 0;
            foreach (var clinicId in clinicIds)
                total += await visitStatus.SyncAllPatientStatusesForClinicAsync(clinicId);

            logger.LogInformation("Patient visit status backfill updated {Count} patient rows.", total);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Patient visit status backfill skipped.");
        }
    }

    private static async Task EnsureRoleAccessSchemaAsync(ClinicalDbContext db, ILogger logger)
    {
        if (db.Database.IsSqlite())
            return;

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "ClinicalNotifications" (
                    "Id" uuid NOT NULL,
                    "ClinicId" uuid NOT NULL,
                    "TargetRole" character varying(64) NOT NULL,
                    "Title" character varying(200) NOT NULL,
                    "Detail" character varying(500) NOT NULL,
                    "Link" character varying(256) NOT NULL,
                    "CreatedAt" timestamp without time zone NOT NULL,
                    CONSTRAINT "PK_ClinicalNotifications" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_ClinicalNotifications_Clinics_ClinicId" FOREIGN KEY ("ClinicId")
                        REFERENCES "Clinics" ("Id") ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS "IX_ClinicalNotifications_ClinicId_CreatedAt"
                    ON "ClinicalNotifications" ("ClinicId", "CreatedAt");
                CREATE INDEX IF NOT EXISTS "IX_ClinicalNotifications_ClinicId_TargetRole"
                    ON "ClinicalNotifications" ("ClinicId", "TargetRole");
                """);
            logger.LogInformation("Role access and notification schema verified.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Role access schema verification skipped.");
        }
    }

    private static async Task EnsureInvoiceRecordLinkSchemaAsync(ClinicalDbContext db, ILogger logger)
    {
        if (db.Database.IsSqlite())
            return;

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE "Invoices" ADD COLUMN IF NOT EXISTS "PatientRecordId" uuid NULL;
                ALTER TABLE "Invoices" ADD COLUMN IF NOT EXISTS "DoctorRecordId" uuid NULL;
                """);

            await db.Database.ExecuteSqlRawAsync(
                """
                UPDATE "Invoices" i
                SET "PatientRecordId" = p."Id"
                FROM "Patients" p
                WHERE i."ClinicId" = p."ClinicId"
                  AND i."PatientRecordId" IS NULL
                  AND i."PatientId" IS NOT NULL
                  AND i."PatientId" = p."PatientNo";
                """);

            await db.Database.ExecuteSqlRawAsync(
                """
                UPDATE "Invoices" i
                SET "DoctorRecordId" = d."Id"
                FROM "Doctors" d
                WHERE i."ClinicId" = d."ClinicId"
                  AND i."DoctorRecordId" IS NULL
                  AND i."DoctorName" IS NOT NULL
                  AND LOWER(TRIM(i."DoctorName")) = LOWER(TRIM(d."Name"));
                """);

            logger.LogInformation("Invoice patient/doctor record link columns verified.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Invoice record link columns verification skipped.");
        }
    }

    private static async Task EnsureDataProtectionKeysSchemaAsync(ClinicalDbContext db, ILogger logger)
    {
        if (db.Database.IsSqlite())
            return;

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "DataProtectionKeys" (
                    "Id" integer GENERATED BY DEFAULT AS IDENTITY,
                    "FriendlyName" text NULL,
                    "Xml" text NULL,
                    CONSTRAINT "PK_DataProtectionKeys" PRIMARY KEY ("Id")
                );
                """);

            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                VALUES ('20260627140000_AddDataProtectionKeys', '8.0.11')
                ON CONFLICT ("MigrationId") DO NOTHING;
                """);

            logger.LogInformation("Data protection keys schema verified.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Data protection keys schema verification skipped.");
        }
    }

    private static async Task BackfillClinicEnabledModulesAsync(ClinicalDbContext db, ILogger logger)
    {
        if (db.Database.IsSqlite())
            return;

        try
        {
            var allKeys = ClinicalModuleCatalog.AllFormKeys();
            var clinics = await db.Clinics.ToListAsync();
            var updated = 0;

            foreach (var clinic in clinics)
            {
                var enabled = ClinicModuleService.ParseEnabledForms(clinic);
                var before = enabled.Count;
                foreach (var key in allKeys)
                    enabled.Add(key);

                if (enabled.Count <= before)
                    continue;

                clinic.EnabledFormKeys = ClinicModuleService.SerializeEnabledForms(enabled);
                updated++;
            }

            if (updated > 0)
            {
                await db.SaveChangesAsync();
                logger.LogInformation("Backfilled enabled modules for {Count} clinic(s).", updated);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Enabled module backfill skipped.");
        }
    }

    private static async Task EnsureMessagingSchemaAsync(ClinicalDbContext db, ILogger logger)
    {
        if (db.Database.IsSqlite())
            return;

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "ClinicMessageAttachments" (
                    "Id" uuid NOT NULL,
                    "ClinicId" uuid NOT NULL,
                    "UploadedByUserId" character varying(450) NOT NULL,
                    "FileName" character varying(260) NOT NULL,
                    "ContentType" character varying(128) NOT NULL,
                    "FileSize" integer NOT NULL,
                    "Data" bytea NOT NULL,
                    "CreatedAt" timestamp without time zone NOT NULL,
                    CONSTRAINT "PK_ClinicMessageAttachments" PRIMARY KEY ("Id")
                );
                CREATE INDEX IF NOT EXISTS "IX_ClinicMessageAttachments_ClinicId"
                    ON "ClinicMessageAttachments" ("ClinicId");

                CREATE TABLE IF NOT EXISTS "ClinicMessages" (
                    "Id" uuid NOT NULL,
                    "ClinicId" uuid NOT NULL,
                    "SenderUserId" character varying(450) NOT NULL,
                    "RecipientUserId" character varying(450) NOT NULL,
                    "Body" character varying(4000) NULL,
                    "SentAt" timestamp without time zone NOT NULL,
                    "ReadAt" timestamp without time zone NULL,
                    "AttachmentId" uuid NULL,
                    CONSTRAINT "PK_ClinicMessages" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_ClinicMessages_ClinicMessageAttachments_AttachmentId"
                        FOREIGN KEY ("AttachmentId") REFERENCES "ClinicMessageAttachments" ("Id") ON DELETE SET NULL
                );
                CREATE INDEX IF NOT EXISTS "IX_ClinicMessages_ClinicId_SenderUserId_RecipientUserId_SentAt"
                    ON "ClinicMessages" ("ClinicId", "SenderUserId", "RecipientUserId", "SentAt");
                CREATE INDEX IF NOT EXISTS "IX_ClinicMessages_ClinicId_RecipientUserId_ReadAt"
                    ON "ClinicMessages" ("ClinicId", "RecipientUserId", "ReadAt");
                CREATE INDEX IF NOT EXISTS "IX_ClinicMessages_AttachmentId"
                    ON "ClinicMessages" ("AttachmentId");
                """);

            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                VALUES ('20260627120000_AddClinicMessaging', '8.0.11')
                ON CONFLICT ("MigrationId") DO NOTHING;
                """);

            await db.Database.ExecuteSqlRawAsync(
                """
                UPDATE "RolePermissions" SET "RoleName" = 'Admin' WHERE "RoleName" = 'ClinicAdmin';
                """);

            logger.LogInformation("Clinic messaging schema verified.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Clinic messaging schema verification skipped.");
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

    private static async Task BackfillUnconfirmedAccountsAsync(
        ClinicalDbContext db,
        IConfiguration config,
        ILogger logger)
    {
        if (AccountVerificationPolicy.IsRequired(config))
            return;

        try
        {
            var updated = await db.Database.ExecuteSqlRawAsync(
                """
                UPDATE "AspNetUsers"
                SET "EmailConfirmed" = true, "PhoneNumberConfirmed" = true
                WHERE "EmailConfirmed" = false OR "PhoneNumberConfirmed" = false
                """);

            if (updated > 0)
                logger.LogInformation("Auto-confirmed {Count} user account(s) (verification disabled).", updated);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Account auto-confirm backfill skipped.");
        }
    }
}
