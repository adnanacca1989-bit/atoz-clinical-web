using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AtoZClinical.Infrastructure.Services;

public sealed class ClinicalNotificationService
{
    private readonly ClinicalDbContext _db;
    private readonly ILogger<ClinicalNotificationService> _logger;

    public ClinicalNotificationService(ClinicalDbContext db, ILogger<ClinicalNotificationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ClinicalNotification> NotifyDepartmentAsync(
        Guid clinicId,
        string targetRole,
        string title,
        string detail,
        string link,
        CancellationToken ct = default)
    {
        var row = new ClinicalNotification
        {
            ClinicId = clinicId,
            TargetRole = targetRole,
            Title = title,
            Detail = detail,
            Link = link,
            CreatedAt = DateTime.UtcNow
        };
        _db.ClinicalNotifications.Add(row);
        await _db.SaveChangesAsync(ct);
        _logger.LogDebug("Stored department notification {Id} for role {Role} clinic {ClinicId}", row.Id, targetRole, clinicId);
        return row;
    }

    public async Task<List<ClinicalNotification>> ListForUserSinceAsync(
        Guid clinicId,
        Infrastructure.Identity.ApplicationUser user,
        DateTime since,
        int take = 50,
        CancellationToken ct = default)
    {
        var rows = await _db.ClinicalNotifications
            .AsNoTracking()
            .Where(n => n.ClinicId == clinicId && n.CreatedAt >= since)
            .OrderByDescending(n => n.CreatedAt)
            .Take(200)
            .ToListAsync(ct);

        return rows
            .Where(n => ClinicalNotificationRoles.UserReceivesNotification(user, n.TargetRole))
            .Take(take)
            .ToList();
    }

    public static object ToPayload(ClinicalNotification n) => new
    {
        id = $"notify-{n.Id:N}",
        kind = "department",
        title = n.Title,
        detail = n.Detail,
        link = n.Link,
        at = n.CreatedAt.ToLocalTime().ToString("g"),
        atUtc = n.CreatedAt.Ticks
    };
}
