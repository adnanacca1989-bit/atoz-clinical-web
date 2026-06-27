using AtoZClinical.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;

namespace AtoZClinical.Web.DataProtection;

public static class ClinicalDataProtectionSetup
{
    public const string ApplicationName = "AtoZClinical";

    /// <summary>
    /// Persists ASP.NET Core Data Protection keys to PostgreSQL so antiforgery, auth cookies,
    /// and Identity tokens survive Render restarts and redeployments (ephemeral local disk).
    /// </summary>
    public static IDataProtectionBuilder AddClinicalDataProtection(
        this IServiceCollection services,
        IConfiguration configuration,
        bool persistKeysToDatabase)
    {
        var appName = configuration["DataProtection:ApplicationName"] ?? ApplicationName;

        var builder = services.AddDataProtection()
            .SetApplicationName(appName)
            .SetDefaultKeyLifetime(TimeSpan.FromDays(90));

        if (persistKeysToDatabase)
            builder.PersistKeysToDbContext<ClinicalDbContext>();

        return builder;
    }
}
