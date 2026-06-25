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
