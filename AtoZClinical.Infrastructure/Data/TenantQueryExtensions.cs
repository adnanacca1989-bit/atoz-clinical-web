using AtoZClinical.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Data;

/// <summary>Defense-in-depth tenant queries when ClinicId is known from an authenticated context.</summary>
public static class TenantQueryExtensions
{
    public static IQueryable<T> ForClinic<T>(this IQueryable<T> query, Guid clinicId)
        where T : class, IClinicScoped =>
        query.IgnoreQueryFilters().Where(e => e.ClinicId == clinicId);

    public static Task<bool> BelongsToClinicAsync<T>(
        this DbSet<T> set,
        Guid clinicId,
        Guid entityId,
        CancellationToken ct = default)
        where T : class, IClinicScoped =>
        set.IgnoreQueryFilters()
            .AnyAsync(e => e.ClinicId == clinicId && EF.Property<Guid>(e, "Id") == entityId, ct);
}
