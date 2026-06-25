using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public sealed class PharmacyItemRegistrationService
{
    private readonly ClinicalDbContext _db;
    private readonly AuditService _audit;
    private readonly MasterDataPropagationService _propagation;

    public PharmacyItemRegistrationService(ClinicalDbContext db, AuditService audit, MasterDataPropagationService propagation)
    {
        _db = db;
        _audit = audit;
        _propagation = propagation;
    }

    public Task<List<PharmacyItem>> ListAsync(Guid clinicId) =>
        _db.PharmacyItems.ForClinic(clinicId).OrderBy(i => i.ItemNo).ToListAsync();

    public Task<List<PharmacyItem>> ListActiveAsync(Guid clinicId) =>
        _db.PharmacyItems.ForClinic(clinicId).Where(i => i.IsActive).OrderBy(i => i.MedicineName).ToListAsync();

    public Task<PharmacyItem?> GetAsync(Guid clinicId, Guid id) =>
        _db.PharmacyItems.ForClinic(clinicId).FirstOrDefaultAsync(i => i.Id == id);

    public Task<PharmacyItem?> GetByBarcodeAsync(Guid clinicId, string barcode) =>
        _db.PharmacyItems.ForClinic(clinicId).FirstOrDefaultAsync(i => i.Barcode == barcode.Trim());

    public Task<int> NextItemNoAsync(Guid clinicId) =>
        _db.PharmacyItems.ForClinic(clinicId).Select(i => (int?)i.ItemNo).MaxAsync()
            .ContinueWith(t => (t.Result ?? 0) + 1);

    public async Task<PharmacyItem> SaveAsync(Guid clinicId, PharmacyItem item, string? userName = null)
    {
        var isNew = item.Id == Guid.Empty;
        PharmacyItem? previous = null;
        if (!isNew)
        {
            previous = await _db.PharmacyItems.ForClinic(clinicId).AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == item.Id);
            isNew = previous is null;
        }

        item.ClinicId = clinicId;
        item.UpdatedAt = DateTime.UtcNow;
        item.Barcode = (item.Barcode ?? string.Empty).Trim();
        item.MedicineCode = (item.MedicineCode ?? string.Empty).Trim();
        item.MedicineName = (item.MedicineName ?? string.Empty).Trim();
        item.BaseUom = string.IsNullOrWhiteSpace(item.BaseUom) ? "Pcs" : item.BaseUom.Trim();
        if (item.ConversionFactor <= 0) item.ConversionFactor = 1;

        if (await _db.PharmacyItems.ForClinic(clinicId).AnyAsync(i =>
                i.Barcode == item.Barcode && i.Id != item.Id))
            throw new InvalidOperationException($"Barcode '{item.Barcode}' is already registered.");

        if (isNew)
        {
            var template = item;
            item = await ClinicSaveHelper.InsertWithSequenceRetryAsync(
                _db,
                async attempt =>
                {
                    var row = ClonePharmacyItemShell(template);
                    row.Id = Guid.NewGuid();
                    row.ClinicId = clinicId;
                    row.ItemNo = await NextItemNoWithSkipAsync(clinicId, attempt);
                    row.CreatedAt = DateTime.UtcNow;
                    row.UpdatedAt = DateTime.UtcNow;
                    return row;
                },
                row => _db.PharmacyItems.Add(row),
                ex => ClinicSaveHelper.IsDuplicateKey(ex, "IX_PharmacyItems_ClinicId_ItemNo"),
                failureMessage: "Could not save pharmacy item");
        }
        else
        {
            var existing = await GetAsync(clinicId, item.Id);
            if (existing is not null)
            {
                item.QuantityOnHand = existing.QuantityOnHand;
                if (item.MovingAverageCost <= 0)
                    item.MovingAverageCost = existing.MovingAverageCost;
            }

            await ClinicSaveHelper.ExecuteIsolatedSaveAsync(_db, () =>
            {
                _db.PharmacyItems.Update(item);
                return Task.CompletedTask;
            });
        }

        if (previous is not null)
        {
            try { await _propagation.PropagatePharmacyItemAsync(clinicId, previous, item); }
            catch { }
        }

        await _audit.LogAsync(clinicId, userName, "Pharmacy Registration", isNew ? "Create" : "Update",
            $"Item #{item.ItemNo} — {item.MedicineName} ({item.Barcode})");
        return item;
    }

    private async Task<int> NextItemNoWithSkipAsync(Guid clinicId, int skip = 0)
    {
        var max = await _db.PharmacyItems.ForClinic(clinicId).MaxAsync(i => (int?)i.ItemNo) ?? 0;
        return max + 1 + skip;
    }

    private static PharmacyItem ClonePharmacyItemShell(PharmacyItem source) => new()
    {
        Barcode = source.Barcode,
        MedicineCode = source.MedicineCode,
        MedicineName = source.MedicineName,
        Dosage = source.Dosage,
        BaseUom = source.BaseUom,
        AlternateUom = source.AlternateUom,
        ConversionFactor = source.ConversionFactor,
        DefaultUnitPrice = source.DefaultUnitPrice,
        MovingAverageCost = source.MovingAverageCost,
        ReorderPoint = source.ReorderPoint,
        IncomeAccountName = source.IncomeAccountName,
        CostAccountName = source.CostAccountName,
        InventoryAccountName = source.InventoryAccountName,
        IsActive = source.IsActive,
        ExpiryDate = source.ExpiryDate
    };

    public async Task DeleteAsync(Guid clinicId, Guid id, string? userName = null)
    {
        var item = await GetAsync(clinicId, id);
        if (item is null) return;
        if (item.QuantityOnHand > 0)
            throw new InvalidOperationException("Cannot delete an item with stock on hand. Set inactive instead.");

        _db.PharmacyItems.Remove(item);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(clinicId, userName, "Pharmacy Registration", "Delete",
            $"Item #{item.ItemNo} — {item.MedicineName}");
    }
}
