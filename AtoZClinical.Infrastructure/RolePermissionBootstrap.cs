using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AtoZClinical.Infrastructure;

public static class RolePermissionBootstrap
{
    public static async Task EnsureClinicRolesAsync(
        ClinicalDbContext db,
        Guid clinicId,
        ILogger? logger = null,
        AuditService? audit = null)
    {
        foreach (var roleName in RolePermissionDefaults.ByRole.Keys)
            await EnsureRoleAsync(db, clinicId, roleName, cache: null, logger, audit);

        await EnsureRoleAsync(db, clinicId, "Admin", cache: null, logger, audit, adminAllVisible: true);
    }

    public static Task<bool> TryRepairAsync(
        ClinicalDbContext db,
        ClinicRuntimeCache? cache,
        Guid clinicId,
        string roleName,
        AuditService? audit = null,
        ILogger? logger = null)
    {
        if (roleName.Equals("Admin", StringComparison.OrdinalIgnoreCase))
            return EnsureRoleAsync(db, clinicId, roleName, cache, logger, audit, adminAllVisible: true);

        if (!RolePermissionDefaults.ByRole.ContainsKey(roleName))
            return Task.FromResult(false);

        return EnsureRoleAsync(db, clinicId, roleName, cache, logger, audit);
    }

    private static async Task<bool> EnsureRoleAsync(
        ClinicalDbContext db,
        Guid clinicId,
        string roleName,
        ClinicRuntimeCache? cache,
        ILogger? logger,
        AuditService? audit,
        bool adminAllVisible = false)
    {
        var roleNames = roleName.Equals("Admin", StringComparison.OrdinalIgnoreCase)
            ? new[] { "Admin", ClinicalRoles.ClinicAdmin }
            : new[] { roleName };

        var rows = await db.RolePermissions
            .Where(r => r.ClinicId == clinicId && roleNames.Contains(r.RoleName))
            .ToListAsync();

        var visibleCount = rows.Count(r => r.IsVisible);
        var needsSeed = rows.Count == 0;
        var needsRepair = rows.Count > 0 && visibleCount == 0;

        if (!needsSeed && !needsRepair)
            return false;

        if (needsRepair)
        {
            db.RolePermissions.RemoveRange(rows);
            logger?.LogWarning(
                "Repairing role {Role} for clinic {ClinicId}: {Count} permission rows had zero visible forms.",
                roleName, clinicId, rows.Count);
        }

        var addedVisible = 0;
        if (adminAllVisible)
        {
            foreach (var formKey in ClinicalFormKeys.All)
            {
                db.RolePermissions.Add(new RolePermission
                {
                    ClinicId = clinicId,
                    RoleName = "Admin",
                    FormKey = formKey,
                    IsVisible = true
                });
                addedVisible++;
            }
        }
        else
        {
            foreach (var seed in RolePermissionDefaults.SeedsForRole(roleName))
            {
                if (seed.IsVisible) addedVisible++;
                db.RolePermissions.Add(new RolePermission
                {
                    ClinicId = clinicId,
                    RoleName = roleName,
                    FormKey = seed.FormKey,
                    IsVisible = seed.IsVisible
                });
            }
        }

        await db.SaveChangesAsync();
        cache?.InvalidateVisibleForms(clinicId, roleName);

        var action = needsRepair ? "Repair Defaults" : "Seed Defaults";
        logger?.LogInformation(
            "{Action} for role {Role} clinic {ClinicId} ({VisibleCount} visible forms).",
            action, roleName, clinicId, addedVisible);

        if (audit is not null)
        {
            await audit.LogAsync(
                clinicId,
                "system",
                "Role Permissions",
                action,
                $"{action} for role {roleName} ({addedVisible} visible forms).");
        }

        return true;
    }
}
