using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure;

public static class QueryablePagingExtensions
{
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = PaginationDefaults.NormalizePage(page);
        pageSize = PaginationDefaults.NormalizePageSize(pageSize);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<T>(items, page, pageSize, total);
    }
}
