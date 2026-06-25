using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public sealed class ClinicBackupHistoryService
{
    private readonly ClinicalDbContext _db;

    public ClinicBackupHistoryService(ClinicalDbContext db) => _db = db;

    public async Task RecordAsync(
        Guid clinicId,
        string action,
        string fileName,
        long fileSizeBytes,
        string? performedBy,
        string? notes = null,
        CancellationToken ct = default)
    {
        _db.ClinicBackupHistories.Add(new ClinicBackupHistory
        {
            ClinicId = clinicId,
            Action = action,
            FileName = fileName,
            FileSizeBytes = fileSizeBytes,
            PerformedBy = performedBy,
            Notes = notes,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);
    }

    public Task<List<ClinicBackupHistory>> ListAsync(Guid clinicId, int take = 50, CancellationToken ct = default) =>
        _db.ClinicBackupHistories.ForClinic(clinicId)
            .OrderByDescending(h => h.CreatedAt)
            .Take(take)
            .ToListAsync(ct);
}
