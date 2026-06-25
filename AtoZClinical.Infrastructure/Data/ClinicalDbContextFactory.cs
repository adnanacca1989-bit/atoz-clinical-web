using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AtoZClinical.Infrastructure.Data;

/// <summary>Design-time factory so EF migrations target PostgreSQL (production provider).</summary>
public sealed class ClinicalDbContextFactory : IDesignTimeDbContextFactory<ClinicalDbContext>
{
    public ClinicalDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__ClinicalDatabase")
            ?? Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? "Host=localhost;Port=5432;Database=atoz_clinical;Username=postgres;Password=postgres";

        if (connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
                ? "postgresql://" + connectionString["postgres://".Length..]
                : connectionString);
            var userInfo = uri.UserInfo.Split(':', 2);
            connectionString =
                $"Host={uri.Host};Port={(uri.Port > 0 ? uri.Port : 5432)};Database={uri.AbsolutePath.TrimStart('/')};Username={Uri.UnescapeDataString(userInfo[0])};Password={Uri.UnescapeDataString(userInfo.Length > 1 ? userInfo[1] : "")};SSL Mode=Require";
        }

        var options = new DbContextOptionsBuilder<ClinicalDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new ClinicalDbContext(options);
    }
}
