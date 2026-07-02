using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Data;

/// <summary>
/// Isolated EF saves for clinic-scoped entities. Clears the change tracker so stray
/// tracked rows (e.g. ClinicConfiguration from branding) are not persisted together.
/// </summary>
public static class ClinicSaveHelper
{
    public static string DbMessage(DbUpdateException ex) =>
        ex.InnerException?.Message ?? ex.Message;

    public static bool IsDuplicateKey(DbUpdateException ex, string indexOrTableHint) =>
        DbMessage(ex).Contains(indexOrTableHint, StringComparison.OrdinalIgnoreCase)
        || (DbMessage(ex).Contains("duplicate key", StringComparison.OrdinalIgnoreCase)
            && DbMessage(ex).Contains(indexOrTableHint, StringComparison.OrdinalIgnoreCase));

    /// <summary>Clear tracker, stage entities, save, clear again.</summary>
    public static async Task ExecuteIsolatedSaveAsync(
        ClinicalDbContext db,
        Func<Task> stage,
        CancellationToken ct = default)
    {
        db.ChangeTracker.Clear();
        await stage();
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    /// <summary>
    /// Copy scalar values from a detached instance onto an already-tracked row.
    /// Preserves primary key and audit columns by default.
    /// </summary>
    public static void CopyTrackedScalars<T>(
        ClinicalDbContext db,
        T tracked,
        T incoming,
        params string[] preservePropertyNames) where T : class
    {
        ArgumentNullException.ThrowIfNull(tracked);
        ArgumentNullException.ThrowIfNull(incoming);

        var entry = db.Entry(tracked);
        var preserve = new HashSet<string>(preservePropertyNames, StringComparer.Ordinal)
        {
            "Id",
            "CreatedAt",
            "CreatedBy"
        };

        var saved = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var prop in entry.Properties)
        {
            if (preserve.Contains(prop.Metadata.Name))
                saved[prop.Metadata.Name] = prop.CurrentValue;
        }

        entry.CurrentValues.SetValues(incoming);

        foreach (var (name, value) in saved)
            entry.Property(name).CurrentValue = value;
    }

    /// <summary>Load a tracked row for update or return null when missing.</summary>
    public static Task<T?> FindTrackedAsync<T>(
        ClinicalDbContext db,
        IQueryable<T> query,
        CancellationToken ct = default) where T : class =>
        query.FirstOrDefaultAsync(ct);

    public static async Task ExecuteInTransactionAsync(
        ClinicalDbContext db,
        Func<Task> action,
        CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await action();
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public static async Task<T> InsertWithSequenceRetryAsync<T>(
        ClinicalDbContext db,
        Func<int, Task<T>> buildRow,
        Action<T> addToContext,
        Func<DbUpdateException, bool> isSequenceConflict,
        int maxAttempts = 15,
        string? failureMessage = null) where T : class
    {
        DbUpdateException? last = null;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var row = await buildRow(attempt);
            db.ChangeTracker.Clear();
            addToContext(row);
            try
            {
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
                return row;
            }
            catch (DbUpdateException ex) when (isSequenceConflict(ex))
            {
                last = ex;
            }
            catch (DbUpdateException ex)
            {
                throw new InvalidOperationException(
                    $"{failureMessage ?? "Could not save"}: {DbMessage(ex)}", ex);
            }
        }

        var detail = last is null ? null : DbMessage(last);
        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(detail)
                ? failureMessage ?? "Could not save. Please click + New and try again."
                : $"{failureMessage ?? "Could not save"} ({detail}). Please click + New and try again.");
    }
}
