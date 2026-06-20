using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public sealed class PharmacyPurchaseBillService
{
    private readonly ClinicalDbContext _db;
    private readonly AuditService _audit;
    private readonly PharmacyInventoryService _inventory;

    public PharmacyPurchaseBillService(ClinicalDbContext db, AuditService audit, PharmacyInventoryService inventory)
    {
        _db = db;
        _audit = audit;
        _inventory = inventory;
    }

    public static void ApplyDiscount(PharmacyPurchaseBill bill)
    {
        bill.SubTotal = bill.Lines.Sum(l => l.LineTotal);
        if (bill.DiscountPercent > 0 && bill.DiscountAmount <= 0)
            bill.DiscountAmount = Math.Round(bill.SubTotal * bill.DiscountPercent / 100m, 2);
        else if (bill.DiscountAmount > 0 && bill.SubTotal > 0)
            bill.DiscountPercent = Math.Round(bill.DiscountAmount / bill.SubTotal * 100m, 2);
        bill.NetAmount = bill.SubTotal - bill.DiscountAmount;
        bill.BalanceDue = bill.NetAmount - bill.AmountPaid;
    }

    public Task<List<PharmacyPurchaseBill>> ListAsync(Guid clinicId) =>
        _db.PharmacyPurchaseBills.Include(b => b.Lines).Where(b => b.ClinicId == clinicId).OrderByDescending(b => b.PurchaseNo).ToListAsync();

    public Task<PharmacyPurchaseBill?> GetAsync(Guid clinicId, Guid id) =>
        _db.PharmacyPurchaseBills.Include(b => b.Lines).FirstOrDefaultAsync(b => b.ClinicId == clinicId && b.Id == id);

    public async Task<PharmacyPurchaseBill> SaveAsync(Guid clinicId, PharmacyPurchaseBill item, List<PharmacyPurchaseBillLine> lines, string? userName = null)
    {
        var isNew = item.Id == Guid.Empty;
        item.ClinicId = clinicId;
        item.UpdatedAt = DateTime.UtcNow;
        foreach (var line in lines)
            line.LineTotal = line.Qty * line.UnitCost;
        item.Lines = lines;
        ApplyDiscount(item);

        if (isNew)
        {
            item.Id = Guid.NewGuid();
            item.PurchaseNo = (await _db.PharmacyPurchaseBills.Where(b => b.ClinicId == clinicId).MaxAsync(b => (int?)b.PurchaseNo) ?? 0) + 1;
            item.CreatedAt = DateTime.UtcNow;
            _db.PharmacyPurchaseBills.Add(item);
        }
        else
        {
            var existing = await _db.PharmacyPurchaseBillLines.Where(l => l.PharmacyPurchaseBillId == item.Id).ToListAsync();
            _db.PharmacyPurchaseBillLines.RemoveRange(existing);
            _db.PharmacyPurchaseBills.Update(item);
        }

        foreach (var line in lines)
        {
            line.Id = Guid.NewGuid();
            line.PharmacyPurchaseBillId = item.Id;
            _db.PharmacyPurchaseBillLines.Add(line);
        }

        await _db.SaveChangesAsync();

        var validLines = lines.Where(l => l.Qty > 0 && (!string.IsNullOrWhiteSpace(l.Barcode) || !string.IsNullOrWhiteSpace(l.MedicineName))).ToList();
        await _inventory.SyncPurchaseInAsync(clinicId, item, validLines);

        await _audit.LogAsync(clinicId, userName, "Pharmacy Purchase Bill", isNew ? "Create" : "Update",
            $"Purchase #{item.PurchaseNo} — {item.SupplierName}, net {item.NetAmount:N2}");
        return item;
    }

    public async Task DeleteAsync(Guid clinicId, Guid id, string? userName = null)
    {
        var item = await GetAsync(clinicId, id);
        if (item is null) return;
        await _inventory.RemoveReferenceMovementsAsync(clinicId, PharmacyInventoryTypes.ReferencePurchase, id);
        _db.PharmacyPurchaseBills.Remove(item);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(clinicId, userName, "Pharmacy Purchase Bill", "Delete", $"Purchase #{item.PurchaseNo}");
    }
}
