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

    public async Task<PharmacyItem> GetOrCreateItemAsync(Guid clinicId, string? barcode, string? code, string? name, string? dosage)
    {
        var items = await _db.PharmacyItems
            .ForClinic(clinicId)
            .Where(i => i.IsActive)
            .ToListAsync();
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

    public async Task SyncOpeningBalanceAsync(Guid clinicId, PharmacyOpeningBalance header, List<PharmacyOpeningBalanceLine> lines)
    {
        await RemoveReferenceMovementsAsync(clinicId, PharmacyInventoryTypes.ReferenceOpeningBalance, header.Id);

        foreach (var line in lines)
        {
            if (line.Qty <= 0) continue;
            var item = await GetOrCreateItemAsync(clinicId, line.Barcode, line.MedicineCode, line.MedicineName, line.Dosage);
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
            .Where(m => m.ClinicId == clinicId && m.ReferenceType == PharmacyInventoryTypes.ReferenceOpeningBalance && m.ReferenceId == header.Id)
            .Select(m => m.PharmacyItemId)
            .Distinct()
            .ToListAsync();
        foreach (var itemId in openingItemIds)
            await RecalculateItemAsync(itemId);
    }

    public async Task SyncPurchaseInAsync(Guid clinicId, PharmacyPurchaseBill bill, List<PharmacyPurchaseBillLine> lines)
    {
        await RemoveReferenceMovementsAsync(clinicId, PharmacyInventoryTypes.ReferencePurchase, bill.Id);

        foreach (var line in lines)
        {
            if (line.Qty <= 0) continue;
            var item = await GetOrCreateItemAsync(clinicId, line.Barcode, line.MedicineCode, line.MedicineName, line.Dosage);
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
            .Where(m => m.ClinicId == clinicId && m.ReferenceType == PharmacyInventoryTypes.ReferencePurchase && m.ReferenceId == bill.Id)
            .Select(m => m.PharmacyItemId)
            .Distinct()
            .ToListAsync();
        foreach (var itemId in itemIds)
            await RecalculateItemAsync(itemId);
    }

    public async Task SyncBillOutAsync(Guid clinicId, PharmacyBill bill, List<PharmacyBillLine> lines)
    {
        await RemoveReferenceMovementsAsync(clinicId, PharmacyInventoryTypes.ReferenceBill, bill.Id);

        foreach (var line in lines)
        {
            if (line.Qty <= 0) continue;
            var item = await GetOrCreateItemAsync(clinicId, line.Barcode, line.MedicineCode, line.MedicineName, line.Dosage);
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
            .Where(m => m.ClinicId == clinicId && m.ReferenceType == PharmacyInventoryTypes.ReferenceBill && m.ReferenceId == bill.Id)
            .Select(m => m.PharmacyItemId)
            .Distinct()
            .ToListAsync();
        foreach (var itemId in billItemIds)
            await RecalculateItemAsync(itemId);
    }

    public async Task RemoveReferenceMovementsAsync(Guid clinicId, string referenceType, Guid referenceId)
    {
        var movements = await _db.PharmacyInventoryMovements
            .Where(m => m.ClinicId == clinicId && m.ReferenceType == referenceType && m.ReferenceId == referenceId)
            .ToListAsync();
        if (movements.Count == 0) return;

        var itemIds = movements.Select(m => m.PharmacyItemId).Distinct().ToList();
        _db.PharmacyInventoryMovements.RemoveRange(movements);
        await _db.SaveChangesAsync();

        foreach (var itemId in itemIds)
            await RecalculateItemAsync(itemId);
    }

    public async Task<List<PharmacyInventoryReportRow>> GetReportAsync(Guid clinicId, DateTime? fromDate, DateTime? toDate, string? search, bool expiredOnly = false)
    {
        var items = await _db.PharmacyItems.Where(i => i.ClinicId == clinicId).OrderBy(i => i.ItemNo).ToListAsync();
        if (!string.IsNullOrWhiteSpace(search))
        {
            items = items.Where(i =>
                i.Barcode.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                i.MedicineCode.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                i.MedicineName.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (expiredOnly)
            items = items.Where(i => i.ExpiryDate.HasValue && i.ExpiryDate.Value.Date < DateTime.Today).ToList();

        var movements = await _db.PharmacyInventoryMovements.Where(m => m.ClinicId == clinicId).ToListAsync();
        if (fromDate.HasValue)
            movements = movements.Where(m => m.MovementDate.Date >= fromDate.Value.Date).ToList();
        if (toDate.HasValue)
            movements = movements.Where(m => m.MovementDate.Date <= toDate.Value.Date).ToList();

        return items.Select(item =>
        {
            var itemMoves = movements.Where(m => m.PharmacyItemId == item.Id).ToList();
            var qtyIn = itemMoves.Sum(m => m.QtyIn);
            var qtyOut = itemMoves.Sum(m => m.QtyOut);
            return new PharmacyInventoryReportRow(
                item.ItemNo,
                item.Barcode,
                item.MedicineCode,
                item.MedicineName,
                item.Dosage,
                qtyIn,
                qtyOut,
                item.QuantityOnHand,
                item.MovingAverageCost,
                item.QuantityOnHand * item.MovingAverageCost,
                item.ExpiryDate);
        }).Where(r => r.QtyIn > 0 || r.QtyOut > 0 || r.QtyBalance > 0 || expiredOnly).ToList();
    }

    private async Task RecalculateItemAsync(Guid itemId)
    {
        var item = await _db.PharmacyItems.FindAsync(itemId);
        if (item is null) return;

        var movements = await _db.PharmacyInventoryMovements
            .Where(m => m.PharmacyItemId == itemId)
            .OrderBy(m => m.MovementDate)
            .ThenBy(m => m.CreatedAt)
            .ToListAsync();

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
        await _db.SaveChangesAsync();
    }

    public sealed record PharmacyInventoryReportRow(
        int ItemNo,
        string Barcode,
        string MedicineCode,
        string MedicineName,
        string? Dosage,
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

    public PharmacyOpeningBalanceService(ClinicalDbContext db, AuditService audit, PharmacyInventoryService inventory)
    {
        _db = db;
        _audit = audit;
        _inventory = inventory;
    }

    public Task<List<PharmacyOpeningBalance>> ListAsync(Guid clinicId) =>
        _db.PharmacyOpeningBalances.Include(b => b.Lines).Where(b => b.ClinicId == clinicId).OrderByDescending(b => b.BalanceNo).ToListAsync();

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
