using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AtoZClinical.Infrastructure.Services;

public sealed class SubdomainClinicResolver
{
    private readonly ClinicalDbContext _db;
    private readonly IConfiguration _config;

    public SubdomainClinicResolver(ClinicalDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task<Clinic?> ResolveFromHostAsync(string host, CancellationToken cancellationToken = default)
    {
        try
        {
            var slug = ExtractSubdomain(host);
            if (string.IsNullOrEmpty(slug)) return null;

            return await _db.Clinics
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Subdomain == slug && c.Status == Core.Enums.ClinicStatus.Active, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public string? ExtractSubdomain(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return null;
        host = host.Split(':')[0].ToLowerInvariant();

        var baseDomain = _config["Security:BaseDomain"]?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(baseDomain)) return null;

        if (!host.EndsWith("." + baseDomain, StringComparison.Ordinal) && host != baseDomain)
            return null;

        if (host == baseDomain) return null;

        var slug = host[..^(baseDomain.Length + 1)];
        if (string.IsNullOrEmpty(slug) || slug.Contains('.')) return null;
        if (ReservedSubdomains.Contains(slug)) return null;
        return slug;
    }

    private static readonly HashSet<string> ReservedSubdomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "www", "app", "api", "admin", "vendor", "mail", "ftp"
    };
}
