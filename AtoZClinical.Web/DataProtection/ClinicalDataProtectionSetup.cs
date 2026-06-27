using AtoZClinical.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Web.DataProtection;

public static class ClinicalDataProtectionSetup
{
    public const string ApplicationName = "AtoZClinical";

    public static IDataProtectionBuilder AddClinicalDataProtection(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString,
        bool useSqlite)
    {
        var appName = configuration["DataProtection:ApplicationName"] ?? ApplicationName;

        services.AddDbContext<DataProtectionDbContext>(options =>
        {
            if (useSqlite)
                options.UseSqlite(connectionString);
            else
                options.UseNpgsql(connectionString);
        });

        var builder = services.AddDataProtection()
            .SetApplicationName(appName)
            .SetDefaultKeyLifetime(TimeSpan.FromDays(90));

        if (!useSqlite)
            builder.PersistKeysToDbContext<DataProtectionDbContext>();

        return builder;
    }

    public static async Task WarmUpAsync(IServiceProvider services, bool useSqlite, ILogger logger)
    {
        if (useSqlite) return;

        try
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DataProtectionDbContext>();
            _ = await db.DataProtectionKeys.CountAsync();

            var dp = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();
            dp.CreateProtector("AtoZClinical.Warmup").Protect("ok");

            var count = await db.DataProtectionKeys.CountAsync();
            logger.LogInformation("Data protection key ring ready ({Count} key(s) in PostgreSQL).", count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Data protection warm-up failed; keys will be created on first use.");
        }
    }
}
