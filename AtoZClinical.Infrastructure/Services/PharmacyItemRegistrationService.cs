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
        _db.PharmacyItems.Where(i => i.ClinicId == clinicId).OrderBy(i => i.ItemNo).ToListAsync();

    public Task<List<PharmacyItem>> ListActiveAsync(Guid clinicId) =>
        _db.PharmacyItems.Where(i => i.ClinicId == clinicId && i.IsActive).OrderBy(i => i.MedicineName).ToListAsync();

    public Task<PharmacyItem?> GetAsync(Guid clinicId, Guid id) =>
        _db.PharmacyItems.FirstOrDefaultAsync(i => i.ClinicId == clinicId && i.Id == id);

    public Task<PharmacyItem?> GetByBarcodeAsync(Guid clinicId, string barcode) =>
        _db.PharmacyItems.FirstOrDefaultAsync(i => i.ClinicId == clinicId && i.Barcode == barcode.Trim());

    public Task<int> NextItemNoAsync(Guid clinicId) =>
        _db.PharmacyItems.Where(i => i.ClinicId == clinicId).Select(i => (int?)i.ItemNo).MaxAsync()
            .ContinueWith(t => (t.Result ?? 0) + 1);

    public async Task<PharmacyItem> SaveAsync(Guid clinicId, PharmacyItem item, string? userName = null)
    {
        var isNew = item.Id == Guid.Empty;
        PharmacyItem? previous = null;
        if (!isNew)
        {
            previous = await _db.PharmacyItems.AsNoTracking()
                .FirstOrDefaultAsync(i => i.ClinicId == clinicId && i.Id == item.Id);
        }

        item.ClinicId = clinicId;
        item.UpdatedAt = DateTime.UtcNow;
        item.Barcode = (item.Barcode ?? string.Empty).Trim();
        item.MedicineCode = (item.MedicineCode ?? string.Empty).Trim();
        item.MedicineName = (item.MedicineName ?? string.Empty).Trim();
        item.BaseUom = string.IsNullOrWhiteSpace(item.BaseUom) ? "Pcs" : item.BaseUom.Trim();
        if (item.ConversionFactor <= 0) item.ConversionFactor = 1;

        if (await _db.PharmacyItems.AnyAsync(i =>
                i.ClinicId == clinicId && i.Barcode == item.Barcode && i.Id != item.Id))
            throw new InvalidOperationException($"Barcode '{item.Barcode}' is already registered.");

        if (isNew)
        {
            item.Id = Guid.NewGuid();
            item.ItemNo = (await _db.PharmacyItems.Where(i => i.ClinicId == clinicId).MaxAsync(i => (int?)i.ItemNo) ?? 0) + 1;
            item.CreatedAt = DateTime.UtcNow;
            _db.PharmacyItems.Add(item);
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
            _db.PharmacyItems.Update(item);
        }

        await _db.SaveChangesAsync();

        if (previous is not null)
            await _propagation.PropagatePharmacyItemAsync(clinicId, previous, item);

        await _audit.LogAsync(clinicId, userName, "Pharmacy Registration", isNew ? "Create" : "Update",
            $"Item #{item.ItemNo} — {item.MedicineName} ({item.Barcode})");
        return item;
    }

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
