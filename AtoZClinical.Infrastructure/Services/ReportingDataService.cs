using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AtoZClinical.Infrastructure.Services;

/// <summary>
/// Optional read-replica access for reporting. Falls back to the primary database when not configured.
/// </summary>
public sealed class ReportingDataService : IDisposable
{
    private readonly ClinicalDbContext _primary;
    private readonly IConfiguration _config;
    private readonly ICurrentClinicProvider _tenant;
    private ClinicalDbContext? _replica;

    public ReportingDataService(
        ClinicalDbContext primary,
        IConfiguration config,
        ICurrentClinicProvider tenant)
    {
        _primary = primary;
        _config = config;
        _tenant = tenant;
    }

    public bool IsReplicaConfigured =>
        !string.IsNullOrWhiteSpace(_config.GetConnectionString("ReportingDatabase"));

    /// <summary>When false (default), all reporting reads use the primary database.</summary>
    public bool UseReadReplica =>
        _config.GetValue("Reporting:UseReadReplica", false) && IsReplicaConfigured;

    /// <summary>Read-only reporting queries should use this context.</summary>
    public ClinicalDbContext ReadDb => UseReadReplica ? GetOrCreateReplica() : _primary;

    private ClinicalDbContext GetOrCreateReplica()
    {
        if (_replica is not null) return _replica;

        var cs = _config.GetConnectionString("ReportingDatabase")!;
        var optionsBuilder = new DbContextOptionsBuilder<ClinicalDbContext>();
        if (cs.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
            optionsBuilder.UseSqlite(cs);
        else
            optionsBuilder.UseNpgsql(cs, npgsql =>
                npgsql.MigrationsAssembly(typeof(ClinicalDbContext).Assembly.FullName));

        _replica = new ClinicalDbContext(optionsBuilder.Options, _tenant);
        _replica.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        return _replica;
    }

    public void Dispose() => _replica?.Dispose();
}
