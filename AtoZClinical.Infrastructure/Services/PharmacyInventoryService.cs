using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public static class PharmacyInventoryTypes
{
    public const string OpeningBalance = "OpeningBalance";
    public const string BillOut = "BillOut";
    public const string PurchaseIn = "PurchaseIn";
    public const string ReferenceOpeningBalance = "PharmacyOpeningBalance";
    public const string ReferenceBill = "PharmacyBill";
    public const string ReferencePurchase = "PharmacyPurchaseBill";
}

public sealed class PharmacyInventoryService
{
    private readonly ClinicalDbContext _db;

    public PharmacyInventoryService(ClinicalDbContext db) => _db = db;

    public Task<PharmacyItem?> LookupByBarcodeAsync(Guid clinicId, string barcode) =>
        _db.PharmacyItems.FirstOrDefaultAsync(i =>
            i.ClinicId == clinicId && i.Barcode == barcode.Trim());

    public async Task<PharmacyItem> GetOrCreateItemAsync(
        Guid clinicId,
        string? barcode,
        string? code,
        string? name,
        string? dosage,
        List<PharmacyItem>? activeCatalog = null)
    {
        var items = activeCatalog ?? await LoadActiveItemsAsync(clinicId);
        var resolved = PharmacyItemRegistrationService.ResolveForLine(items, barcode, code, name);
        if (resolved is not null)
        {
            if (!string.IsNullOrWhiteSpace(name)) resolved.MedicineName = name.Trim();
            if (!string.IsNullOrWhiteSpace(code)) resolved.MedicineCode = code.Trim();
            if (!string.IsNullOrWhiteSpace(dosage)) resolved.Dosage = dosage;
            return resolved;
        }

        var bc = (barcode ?? string.Empty).Trim();
        var display = !string.IsNullOrEmpty(bc) ? bc : code ?? name ?? "unknown";
        throw new InvalidOperationException(
            $"Pharmacy item '{display}' is not registered. Add it in Pharmacy Registration first.");
    }

    public static decimal ToBaseUnitCost(decimal enteredCost, string? uom, PharmacyItem item)
    {
        if (enteredCost <= 0) return enteredCost;
        if (!string.IsNullOrWhiteSpace(item.AlternateUom) &&
            string.Equals(uom?.Trim(), item.AlternateUom, StringComparison.OrdinalIgnoreCase) &&
            item.ConversionFactor > 0)
            return enteredCost / item.ConversionFactor;
        return enteredCost;
    }

    private Task<List<PharmacyItem>> LoadActiveItemsAsync(Guid clinicId) =>
        _db.PharmacyItems.ForClinic(clinicId).Where(i => i.IsActive).ToListAsync();

    public async Task SyncOpeningBalanceAsync(Guid clinicId, PharmacyOpeningBalance header, List<PharmacyOpeningBalanceLine> lines)
    {
        await RemoveReferenceMovementsAsync(clinicId, PharmacyInventoryTypes.ReferenceOpeningBalance, header.Id);
        var catalog = await LoadActiveItemsAsync(clinicId);

        foreach (var line in lines)
        {
            if (line.Qty <= 0) continue;
            var item = await GetOrCreateItemAsync(clinicId, line.Barcode, line.MedicineCode, line.MedicineName, line.Dosage, catalog);
            var baseQty = item.ToBaseQuantity(line.Qty, line.Uom);
            if (baseQty <= 0) continue;
            var unitCostBase = ToBaseUnitCost(line.UnitCost, line.Uom, item);
            _db.PharmacyInventoryMovements.Add(new PharmacyInventoryMovement
            {
                Id = Guid.NewGuid(),
                ClinicId = clinicId,
                PharmacyItemId = item.Id,
                MovementDate = header.BalanceDate,
                MovementType = PharmacyInventoryTypes.OpeningBalance,
                ReferenceType = PharmacyInventoryTypes.ReferenceOpeningBalance,
                ReferenceId = header.Id,
                ReferenceNo = header.BalanceNo,
                LineNo = line.LineNo,
                Barcode = item.Barcode,
                MedicineCode = item.MedicineCode,
                MedicineName = item.MedicineName,
                QtyIn = baseQty,
                QtyOut = 0,
                UnitCost = unitCostBase,
                TotalValue = baseQty * unitCostBase
            });
        }

        await _db.SaveChangesAsync();

        var openingItemIds = await _db.PharmacyInventoryMovements
            .ForClinic(clinicId)
            .Where(m => m.ReferenceType == PharmacyInventoryTypes.ReferenceOpeningBalance && m.ReferenceId == header.Id)
            .Select(m => m.PharmacyItemId)
            .Distinct()
            .ToListAsync();
        await RecalculateItemsAsync(openingItemIds);
    }

    public async Task SyncPurchaseInAsync(Guid clinicId, PharmacyPurchaseBill bill, List<PharmacyPurchaseBillLine> lines)
    {
        await RemoveReferenceMovementsAsync(clinicId, PharmacyInventoryTypes.ReferencePurchase, bill.Id);
        var catalog = await LoadActiveItemsAsync(clinicId);

        foreach (var line in lines)
        {
            if (line.Qty <= 0) continue;
            var item = await GetOrCreateItemAsync(clinicId, line.Barcode, line.MedicineCode, line.MedicineName, line.Dosage, catalog);
            var baseQty = item.ToBaseQuantity(line.Qty, line.Uom);
            if (baseQty <= 0) continue;
            var unitCostBase = ToBaseUnitCost(line.UnitCost, line.Uom, item);
            _db.PharmacyInventoryMovements.Add(new PharmacyInventoryMovement
            {
                Id = Guid.NewGuid(),
                ClinicId = clinicId,
                PharmacyItemId = item.Id,
                MovementDate = bill.PurchaseDate,
                MovementType = PharmacyInventoryTypes.PurchaseIn,
                ReferenceType = PharmacyInventoryTypes.ReferencePurchase,
                ReferenceId = bill.Id,
                ReferenceNo = bill.PurchaseNo,
                LineNo = line.LineNo,
                Barcode = item.Barcode,
                MedicineCode = item.MedicineCode,
                MedicineName = item.MedicineName,
                QtyIn = baseQty,
                QtyOut = 0,
                UnitCost = unitCostBase,
                TotalValue = baseQty * unitCostBase
            });
        }

        await _db.SaveChangesAsync();

        var itemIds = await _db.PharmacyInventoryMovements
            .ForClinic(clinicId)
            .Where(m => m.ReferenceType == PharmacyInventoryTypes.ReferencePurchase && m.ReferenceId == bill.Id)
            .Select(m => m.PharmacyItemId)
            .Distinct()
            .ToListAsync();
        await RecalculateItemsAsync(itemIds);
    }

    public async Task SyncBillOutAsync(Guid clinicId, PharmacyBill bill, List<PharmacyBillLine> lines)
    {
        await RemoveReferenceMovementsAsync(clinicId, PharmacyInventoryTypes.ReferenceBill, bill.Id);
        var catalog = await LoadActiveItemsAsync(clinicId);

        foreach (var line in lines)
        {
            if (line.Qty <= 0) continue;
            var item = await GetOrCreateItemAsync(clinicId, line.Barcode, line.MedicineCode, line.MedicineName, line.Dosage, catalog);
            var baseQty = item.ToBaseQuantity(line.Qty, line.Uom);
            if (baseQty <= 0) continue;
            var avgBefore = item.MovingAverageCost;
            _db.PharmacyInventoryMovements.Add(new PharmacyInventoryMovement
            {
                Id = Guid.NewGuid(),
                ClinicId = clinicId,
                PharmacyItemId = item.Id,
                MovementDate = bill.BillDate,
                MovementType = PharmacyInventoryTypes.BillOut,
                ReferenceType = PharmacyInventoryTypes.ReferenceBill,
                ReferenceId = bill.Id,
                ReferenceNo = bill.BillNo,
                LineNo = line.LineNo,
                Barcode = item.Barcode,
                MedicineCode = item.MedicineCode,
                MedicineName = item.MedicineName,
                QtyIn = 0,
                QtyOut = baseQty,
                UnitCost = avgBefore,
                TotalValue = baseQty * avgBefore
            });
        }

        await _db.SaveChangesAsync();

        var billItemIds = await _db.PharmacyInventoryMovements
            .ForClinic(clinicId)
            .Where(m => m.ReferenceType == PharmacyInventoryTypes.ReferenceBill && m.ReferenceId == bill.Id)
            .Select(m => m.PharmacyItemId)
            .Distinct()
            .ToListAsync();
        await RecalculateItemsAsync(billItemIds);
    }

    public async Task RemoveReferenceMovementsAsync(Guid clinicId, string referenceType, Guid referenceId)
    {
        var movements = await _db.PharmacyInventoryMovements
            .ForClinic(clinicId)
            .Where(m => m.ReferenceType == referenceType && m.ReferenceId == referenceId)
            .ToListAsync();
        if (movements.Count == 0) return;

        var itemIds = movements.Select(m => m.PharmacyItemId).Distinct().ToList();
        _db.PharmacyInventoryMovements.RemoveRange(movements);
        await _db.SaveChangesAsync();

        await RecalculateItemsAsync(itemIds);
    }

    public async Task RecalculateClinicInventoryAsync(Guid clinicId)
    {
        var itemIds = await _db.PharmacyInventoryMovements
            .ForClinic(clinicId)
            .Select(m => m.PharmacyItemId)
            .Distinct()
            .ToListAsync();
        await RecalculateItemsAsync(itemIds);
    }

    public async Task<List<PharmacyInventoryReportRow>> GetReportAsync(Guid clinicId, DateTime? fromDate, DateTime? toDate, string? search, bool expiredOnly = false)
    {
        var itemsQuery = _db.PharmacyItems.ForClinic(clinicId).AsNoTracking().OrderBy(i => i.ItemNo).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            itemsQuery = itemsQuery.Where(i =>
                i.Barcode.Contains(term) ||
                i.MedicineCode.Contains(term) ||
                i.MedicineName.Contains(term));
        }

        if (expiredOnly)
            itemsQuery = itemsQuery.Where(i => i.ExpiryDate.HasValue && i.ExpiryDate.Value.Date < DateTime.Today);

        var items = await itemsQuery.ToListAsync();

        var movementQuery = _db.PharmacyInventoryMovements.ForClinic(clinicId).AsNoTracking();
        if (fromDate.HasValue)
            movementQuery = movementQuery.Where(m => m.MovementDate >= fromDate.Value.Date);
        if (toDate.HasValue)
            movementQuery = movementQuery.Where(m => m.MovementDate <= toDate.Value.Date);

        var allMovements = await movementQuery.ToListAsync();
        var openingMovements = allMovements
            .Where(m => m.MovementType == PharmacyInventoryTypes.OpeningBalance)
            .ToList();

        var periodMovements = allMovements;

        return items.Select(item =>
        {
            var qtyOpening = openingMovements.Where(m => m.PharmacyItemId == item.Id).Sum(m => m.QtyIn);
            var qtyPurchase = periodMovements.Where(m => m.PharmacyItemId == item.Id && m.MovementType == PharmacyInventoryTypes.PurchaseIn).Sum(m => m.QtyIn);
            var qtyIssued = periodMovements.Where(m => m.PharmacyItemId == item.Id && m.MovementType == PharmacyInventoryTypes.BillOut).Sum(m => m.QtyOut);
            var qtyIn = qtyOpening + qtyPurchase;
            var qtyOut = qtyIssued;
            var qtyBalance = item.QuantityOnHand > 0
                ? item.QuantityOnHand
                : Math.Max(0, qtyOpening + qtyPurchase - qtyIssued);
            var avgCost = item.MovingAverageCost;
            if (avgCost <= 0)
            {
                var lastMove = allMovements
                    .Where(m => m.PharmacyItemId == item.Id)
                    .OrderBy(m => m.MovementDate)
                    .ThenBy(m => m.CreatedAt)
                    .LastOrDefault();
                avgCost = lastMove?.MovingAvgCostAfter ?? 0m;
            }

            return new PharmacyInventoryReportRow(
                item.ItemNo,
                item.Barcode,
                item.MedicineCode,
                item.MedicineName,
                item.Dosage,
                qtyOpening,
                qtyPurchase,
                qtyIssued,
                qtyIn,
                qtyOut,
                qtyBalance,
                avgCost,
                qtyBalance * avgCost,
                item.ExpiryDate);
        }).Where(r => expiredOnly
            ? r.ExpiryDate.HasValue && r.ExpiryDate.Value.Date < DateTime.Today
            : true).ToList();
    }

    private async Task RecalculateItemsAsync(IReadOnlyList<Guid> itemIds)
    {
        if (itemIds.Count == 0) return;

        var distinctIds = itemIds.Distinct().ToList();
        var items = await _db.PharmacyItems.IgnoreQueryFilters()
            .Where(i => distinctIds.Contains(i.Id))
            .ToListAsync();
        if (items.Count == 0) return;

        var movements = await _db.PharmacyInventoryMovements
            .Where(m => distinctIds.Contains(m.PharmacyItemId))
            .OrderBy(m => m.MovementDate)
            .ThenBy(m => m.CreatedAt)
            .ToListAsync();

        var movementsByItem = movements.GroupBy(m => m.PharmacyItemId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var item in items)
        {
            if (!movementsByItem.TryGetValue(item.Id, out var itemMovements))
            {
                item.QuantityOnHand = 0;
                item.MovingAverageCost = 0;
                item.UpdatedAt = DateTime.UtcNow;
                continue;
            }

            RecalculateItemInMemory(item, itemMovements);
        }

        await _db.SaveChangesAsync();
    }

    private static void RecalculateItemInMemory(PharmacyItem item, List<PharmacyInventoryMovement> movements)
    {
        var qty = 0;
        var avg = 0m;

        foreach (var m in movements)
        {
            if (m.QtyIn > 0)
            {
                var totalCost = qty * avg + m.QtyIn * m.UnitCost;
                qty += m.QtyIn;
                avg = qty > 0 ? totalCost / qty : 0;
                m.TotalValue = m.QtyIn * m.UnitCost;
            }
            else if (m.QtyOut > 0)
            {
                m.UnitCost = avg;
                m.TotalValue = m.QtyOut * avg;
                qty -= m.QtyOut;
                if (qty < 0) qty = 0;
            }

            m.BalanceQtyAfter = qty;
            m.MovingAvgCostAfter = avg;
        }

        item.QuantityOnHand = qty;
        item.MovingAverageCost = avg;
        item.UpdatedAt = DateTime.UtcNow;
    }

    private async Task RecalculateItemAsync(Guid itemId) =>
        await RecalculateItemsAsync([itemId]);

    public sealed record PharmacyInventoryReportRow(
        int ItemNo,
        string Barcode,
        string MedicineCode,
        string MedicineName,
        string? Dosage,
        int QtyOpeningBalance,
        int QtyPurchase,
        int QtyIssued,
        int QtyIn,
        int QtyOut,
        int QtyBalance,
        decimal MovingAverageCost,
        decimal TotalValue,
        DateTime? ExpiryDate);
}

public sealed class PharmacyOpeningBalanceService
{
    private readonly ClinicalDbContext _db;
    private readonly AuditService _audit;
    private readonly PharmacyInventoryService _inventory;
    private readonly ClinicalJournalSyncService _journalSync;

    public PharmacyOpeningBalanceService(
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

    public Task<List<PharmacyOpeningBalance>> ListAsync(Guid clinicId) =>
        _db.PharmacyOpeningBalances.ForClinic(clinicId).Include(b => b.Lines).OrderByDescending(b => b.BalanceNo).ToListAsync();

    public async Task<int> NextBalanceNoAsync(Guid clinicId) =>
        (await _db.PharmacyOpeningBalances.ForClinic(clinicId).MaxAsync(b => (int?)b.BalanceNo) ?? 0) + 1;

    public Task<PharmacyOpeningBalance?> GetAsync(Guid clinicId, Guid id) =>
        _db.PharmacyOpeningBalances.Include(b => b.Lines).FirstOrDefaultAsync(b => b.ClinicId == clinicId && b.Id == id);

    public async Task<PharmacyOpeningBalance> SaveAsync(Guid clinicId, PharmacyOpeningBalance item, List<PharmacyOpeningBalanceLine> lines, string? userName = null)
    {
        var isNew = item.Id == Guid.Empty;
        var validLines = lines
            .Where(l => l.Qty > 0 && (!string.IsNullOrWhiteSpace(l.Barcode) || !string.IsNullOrWhiteSpace(l.MedicineName)))
            .ToList();
        if (validLines.Count == 0)
            throw new InvalidOperationException("Add at least one opening balance line with quantity and a registered item.");

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
        }

        item.ClinicId = clinicId;
        item.UpdatedAt = DateTime.UtcNow;

        if (isNew)
        {
            item.Id = Guid.NewGuid();
            item.BalanceNo = (await _db.PharmacyOpeningBalances.ForClinic(clinicId).MaxAsync(b => (int?)b.BalanceNo) ?? 0) + 1;
            item.CreatedAt = DateTime.UtcNow;
            _db.PharmacyOpeningBalances.Add(item);
        }
        else
        {
            var existing = await _db.PharmacyOpeningBalanceLines.Where(l => l.PharmacyOpeningBalanceId == item.Id).ToListAsync();
            _db.PharmacyOpeningBalanceLines.RemoveRange(existing);
            _db.PharmacyOpeningBalances.Update(item);
        }

        foreach (var line in validLines)
        {
            line.Id = Guid.NewGuid();
            line.PharmacyOpeningBalanceId = item.Id;
            line.Total = line.Qty * line.UnitCost;
            _db.PharmacyOpeningBalanceLines.Add(line);
        }

        await _db.SaveChangesAsync();
        await _inventory.SyncOpeningBalanceAsync(clinicId, item, validLines);

        try { await _journalSync.SyncPharmacyOpeningBalanceAsync(clinicId, item); }
        catch { }

        await _audit.LogAsync(clinicId, userName, "Pharmacy Opening Balance", isNew ? "Create" : "Update",
            $"Opening Balance #{item.BalanceNo}, {validLines.Count} line(s)");
        return item;
    }

    public async Task DeleteAsync(Guid clinicId, Guid id, string? userName = null)
    {
        var item = await GetAsync(clinicId, id);
        if (item is null) return;
        await _inventory.RemoveReferenceMovementsAsync(clinicId, PharmacyInventoryTypes.ReferenceOpeningBalance, id);
        _db.PharmacyOpeningBalances.Remove(item);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(clinicId, userName, "Pharmacy Opening Balance", "Delete", $"Opening Balance #{item.BalanceNo}");
    }
}
