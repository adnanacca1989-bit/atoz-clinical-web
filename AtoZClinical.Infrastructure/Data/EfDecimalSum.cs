using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Data;

/// <summary>SQLite does not translate decimal aggregates; sum client-side when needed.</summary>
public static class EfDecimalSum
{
    public static async Task<decimal> SumAsync<T>(
        this IQueryable<T> query,
        DbContext db,
        Expression<Func<T, decimal>> selector,
        CancellationToken ct = default)
    {
        if (db.Database.IsSqlite())
        {
            var values = await query.Select(selector).ToListAsync(ct);
            return values.Sum();
        }

        return await query.SumAsync(selector, ct);
    }

    public static async Task<decimal> SumNullableAsync<T>(
        this IQueryable<T> query,
        DbContext db,
        Expression<Func<T, decimal?>> selector,
        CancellationToken ct = default)
    {
        if (db.Database.IsSqlite())
        {
            var values = await query.Select(selector).ToListAsync(ct);
            return values.Sum(v => v ?? 0m);
        }

        return await query.SumAsync(selector, ct) ?? 0m;
    }
}
