using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public sealed class ServiceIncomeRequestService
{
    private readonly ClinicalDbContext _db;
    private readonly BillingPropagationService _billing;
    private readonly PatientVisitStatusService _visitStatus;
    private readonly AuditService _audit;

    public ServiceIncomeRequestService(
        ClinicalDbContext db,
        BillingPropagationService billing,
        PatientVisitStatusService visitStatus,
        AuditService audit)
    {
        _db = db;
        _billing = billing;
        _visitStatus = visitStatus;
        _audit = audit;
    }

    public Task<List<ServiceIncomeRequest>> ListAsync(Guid clinicId) =>
        _db.ServiceIncomeRequests.Include(r => r.Lines).ForClinic(clinicId).OrderByDescending(r => r.RequestNo).ToListAsync();

    public Task<ServiceIncomeRequest?> GetAsync(Guid clinicId, Guid id) =>
        _db.ServiceIncomeRequests.Include(r => r.Lines).ForClinic(clinicId).FirstOrDefaultAsync(r => r.Id == id);

    public async Task<ServiceIncomeRequest> SaveAsync(Guid clinicId, ServiceIncomeRequest item, List<ServiceIncomeRequestLine> lines, string? userName = null)
    {
        var isNew = item.Id == Guid.Empty;
        ServiceIncomeRequest? previous = null;
        List<ServiceIncomeRequestLine>? previousLines = null;
        if (!isNew)
        {
            previous = await _db.ServiceIncomeRequests.ForClinic(clinicId).AsNoTracking()
                .Include(r => r.Lines)
                .FirstOrDefaultAsync(r => r.Id == item.Id);
            previousLines = previous?.Lines.OrderBy(l => l.LineNo).ToList();
        }

        var validLines = lines
            .Where(l => !string.IsNullOrWhiteSpace(l.ServiceName))
            .ToList();
        if (validLines.Count == 0)
            throw new InvalidOperationException("Add at least one service line.");

        if (!isNew)
        {
            item.ClinicId = clinicId;
            item.UpdatedAt = DateTime.UtcNow;
            item.TotalAmount = validLines.Sum(l => l.Total);
            await ClinicSaveHelper.ExecuteIsolatedSaveAsync(_db, async () =>
            {
                var existing = await _db.ServiceIncomeRequestLines.Where(l => l.ServiceIncomeRequestId == item.Id).ToListAsync();
                _db.ServiceIncomeRequestLines.RemoveRange(existing);
                _db.ServiceIncomeRequests.Update(item);
                foreach (var line in validLines)
                {
                    line.Id = Guid.NewGuid();
                    line.ServiceIncomeRequestId = item.Id;
                    _db.ServiceIncomeRequestLines.Add(line);
                }
            });
            await SyncBillingAsync(clinicId, item, validLines, previous, previousLines);
            try { await _visitStatus.OnClinicalCheckInAsync(clinicId, item.PatientBarcode, item.PatientName); }
            catch { }
            await _audit.LogAsync(clinicId, userName, "Service Income Request", "Update",
                $"Request #{item.RequestNo} — {item.PatientName}");
            return item;
        }

        var template = item;
        var lineTemplates = validLines;
        item = await ClinicSaveHelper.InsertWithSequenceRetryAsync(
            _db,
            async attempt =>
            {
                var row = CloneShell(template);
                row.Id = Guid.NewGuid();
                row.ClinicId = clinicId;
                row.RequestNo = await NextRequestNoAsync(clinicId, attempt);
                row.TotalAmount = lineTemplates.Sum(l => l.Total);
                row.CreatedAt = DateTime.UtcNow;
                row.UpdatedAt = DateTime.UtcNow;
                return row;
            },
            row =>
            {
                _db.ServiceIncomeRequests.Add(row);
                foreach (var src in lineTemplates)
                {
                    _db.ServiceIncomeRequestLines.Add(new ServiceIncomeRequestLine
                    {
                        Id = Guid.NewGuid(),
                        ServiceIncomeRequestId = row.Id,
                        LineNo = src.LineNo,
                        ServiceNo = src.ServiceNo,
                        ServiceName = src.ServiceName,
                        AccountName = src.AccountName,
                        Qty = src.Qty,
                        Fee = src.Fee,
                        Total = src.Total
                    });
                }
            },
            ex => ClinicSaveHelper.IsDuplicateKey(ex, "IX_ServiceIncomeRequests_ClinicId_RequestNo"),
            failureMessage: "Could not save service income request");

        var savedLines = await _db.ServiceIncomeRequestLines
            .Where(l => l.ServiceIncomeRequestId == item.Id)
            .OrderBy(l => l.LineNo)
            .ToListAsync();
        await SyncBillingAsync(clinicId, item, savedLines, null, null);

        try { await _visitStatus.OnClinicalCheckInAsync(clinicId, item.PatientBarcode, item.PatientName); }
        catch { }

        await _audit.LogAsync(clinicId, userName, "Service Income Request", "Create",
            $"Request #{item.RequestNo} — {item.PatientName}");

        return item;
    }

    public async Task DeleteAsync(Guid clinicId, Guid id, string? userName = null)
    {
        var item = await GetAsync(clinicId, id);
        if (item is null) return;
        _db.ServiceIncomeRequests.Remove(item);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(clinicId, userName, "Service Income Request", "Delete",
            $"Request #{item.RequestNo} — {item.PatientName}");
    }

    private async Task SyncBillingAsync(
        Guid clinicId,
        ServiceIncomeRequest current,
        List<ServiceIncomeRequestLine> lines,
        ServiceIncomeRequest? previous,
        List<ServiceIncomeRequestLine>? previousLines)
    {
        try
        {
            await _billing.SyncServiceIncomeRequestAsync(clinicId, current, lines, previous, previousLines);
        }
        catch { /* billing sync is best-effort */ }
    }

    private async Task<int> NextRequestNoAsync(Guid clinicId, int skip = 0)
    {
        var max = await _db.ServiceIncomeRequests.ForClinic(clinicId).MaxAsync(r => (int?)r.RequestNo) ?? 0;
        return max + 1 + skip;
    }

    private static ServiceIncomeRequest CloneShell(ServiceIncomeRequest source) => new()
    {
        RequestDate = source.RequestDate,
        PatientName = source.PatientName,
        PatientBarcode = source.PatientBarcode,
        Age = source.Age,
        Gender = source.Gender,
        Phone = source.Phone,
        City = source.City,
        DoctorName = source.DoctorName,
        Specialty = source.Specialty
    };
}
