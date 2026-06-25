using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public static class SecurityAuditEvents
{
    public const string Login = "Login";
    public const string Logout = "Logout";
    public const string LoginFailed = "LoginFailed";
    public const string UserAdmin = "UserAdmin";
}

public sealed class SecurityAuditService
{
    private readonly ClinicalDbContext _db;

    public SecurityAuditService(ClinicalDbContext db) => _db = db;

    public async Task LogAsync(
        string eventType,
        string? userName,
        Guid? clinicId = null,
        string? details = null,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        _db.SecurityAuditEntries.Add(new SecurityAuditEntry
        {
            EventType = eventType,
            UserName = userName,
            ClinicId = clinicId,
            Details = details,
            IpAddress = ipAddress,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);
    }

    public Task<List<SecurityAuditEntry>> ListForClinicAsync(Guid clinicId, int take = 200, CancellationToken ct = default) =>
        _db.SecurityAuditEntries.AsNoTracking()
            .Where(e => e.ClinicId == clinicId)
            .OrderByDescending(e => e.CreatedAt)
            .Take(take)
            .ToListAsync(ct);

    public Task<List<SecurityAuditEntry>> ListPlatformAsync(int take = 500, CancellationToken ct = default) =>
        _db.SecurityAuditEntries.AsNoTracking()
            .OrderByDescending(e => e.CreatedAt)
            .Take(take)
            .ToListAsync(ct);
}
