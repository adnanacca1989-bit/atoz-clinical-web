using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public sealed class PharmacyPurchaseBillService
{
    private readonly ClinicalDbContext _db;
    private readonly AuditService _audit;
    private readonly PharmacyInventoryService _inventory;
    private readonly ClinicalJournalSyncService _journalSync;

    public PharmacyPurchaseBillService(
        ClinicalDbContext db,
        AuditService audit,
        PharmacyInventoryService inventory,
        ClinicalJournalSyncService journalSync)
    {
        _db = db;
        _audit = audit;
        _inventory = inventory;
        _journalSync = journalSync;
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
        _db.PharmacyPurchaseBills.ForClinic(clinicId).Include(b => b.Lines).OrderByDescending(b => b.PurchaseNo).ToListAsync();

    public Task<PharmacyPurchaseBill?> GetAsync(Guid clinicId, Guid id) =>
        _db.PharmacyPurchaseBills.Include(b => b.Lines).ForClinic(clinicId).FirstOrDefaultAsync(b => b.Id == id);

    public async Task<int> NextPurchaseNoAsync(Guid clinicId) =>
        (await _db.PharmacyPurchaseBills.ForClinic(clinicId).MaxAsync(b => (int?)b.PurchaseNo) ?? 0) + 1;

    public async Task<PharmacyPurchaseBill> SaveAsync(Guid clinicId, PharmacyPurchaseBill item, List<PharmacyPurchaseBillLine> lines, string? userName = null)
    {
        var isNew = item.Id == Guid.Empty;
        if (!isNew)
        {
            var exists = await _db.PharmacyPurchaseBills.ForClinic(clinicId).AsNoTracking()
                .AnyAsync(b => b.Id == item.Id);
            if (!exists) isNew = true;
        }

        var validLines = lines
            .Where(l => l.Qty > 0 && (!string.IsNullOrWhiteSpace(l.Barcode) || !string.IsNullOrWhiteSpace(l.MedicineName)))
            .ToList();
        if (validLines.Count == 0)
            throw new InvalidOperationException("Add at least one purchase line with quantity and a registered pharmacy item.");

        foreach (var line in validLines)
        {
            if (line.UnitCost <= 0)
                throw new InvalidOperationException($"Unit cost is required for {line.MedicineName ?? line.Barcode ?? "item"}.");

            var registered = await _inventory.GetOrCreateItemAsync(
                clinicId, line.Barcode, line.MedicineCode, line.MedicineName, line.Dosage);
            line.Barcode = registered.Barcode;
            line.MedicineCode = registered.MedicineCode;
            line.MedicineName = registered.MedicineName;
            if (string.IsNullOrWhiteSpace(line.Uom))
                line.Uom = registered.BaseUom;
            line.LineTotal = line.Qty * line.UnitCost;
        }

        item.ClinicId = clinicId;
        item.UpdatedAt = DateTime.UtcNow;
        item.Lines = validLines;
        ApplyDiscount(item);

        if (isNew)
        {
            var template = item;
            item = await ClinicSaveHelper.InsertWithSequenceRetryAsync(
                _db,
                async _ =>
                {
                    var row = new PharmacyPurchaseBill
                    {
                        Id = Guid.NewGuid(),
                        ClinicId = clinicId,
                        PurchaseNo = await NextPurchaseNoAsync(clinicId),
                        PurchaseDate = template.PurchaseDate,
                        SupplierName = template.SupplierName,
                        SupplierPhone = template.SupplierPhone,
                        SupplierInvoiceNo = template.SupplierInvoiceNo,
                        DiscountAmount = template.DiscountAmount,
                        DiscountPercent = template.DiscountPercent,
                        AmountPaid = template.AmountPaid,
                        PaymentMethod = template.PaymentMethod,
                        PaymentStatus = template.PaymentStatus,
                        Notes = template.Notes,
                        SubTotal = template.SubTotal,
                        NetAmount = template.NetAmount,
                        BalanceDue = template.BalanceDue,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    return row;
                },
                row => _db.PharmacyPurchaseBills.Add(row),
                ex => ClinicSaveHelper.IsDuplicateKey(ex, "IX_PharmacyPurchaseBills_ClinicId_PurchaseNo"),
                failureMessage: "Could not save pharmacy purchase bill");

            foreach (var line in validLines)
            {
                line.Id = Guid.NewGuid();
                line.PharmacyPurchaseBillId = item.Id;
                _db.PharmacyPurchaseBillLines.Add(line);
            }
            await _db.SaveChangesAsync();
        }
        else
        {
            var existing = await _db.PharmacyPurchaseBillLines.Where(l => l.PharmacyPurchaseBillId == item.Id).ToListAsync();
            _db.PharmacyPurchaseBillLines.RemoveRange(existing);
            _db.PharmacyPurchaseBills.Update(item);
            foreach (var line in validLines)
            {
                line.Id = Guid.NewGuid();
                line.PharmacyPurchaseBillId = item.Id;
                _db.PharmacyPurchaseBillLines.Add(line);
            }
            await _db.SaveChangesAsync();
        }

        await _inventory.SyncPurchaseInAsync(clinicId, item, validLines);

        try { await _journalSync.SyncPharmacyPurchaseBillAsync(clinicId, item); }
        catch { }

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
