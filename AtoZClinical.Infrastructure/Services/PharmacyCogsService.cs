using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public sealed class PharmacyCogsService
{
    private readonly ClinicalDbContext _db;
    private readonly PharmacyInventoryService _inventory;

    public PharmacyCogsService(ClinicalDbContext db, PharmacyInventoryService inventory)
    {
        _db = db;
        _inventory = inventory;
    }

    public async Task<List<CogsRow>> GetCogsRowsAsync(
        Guid clinicId,
        DateTime fromDate,
        DateTime toDate,
        string? doctorName,
        string? patientName)
    {
        await _inventory.RecalculateClinicInventoryAsync(clinicId);

        var from = fromDate.Date;
        var to = toDate.Date;

        var bills = await _db.PharmacyBills
            .Include(b => b.Lines)
            .ForClinic(clinicId)
            .Where(b => b.BillDate >= from && b.BillDate <= to)
            .OrderBy(b => b.BillDate)
            .ThenBy(b => b.BillNo)
            .ToListAsync();

        if (!string.IsNullOrWhiteSpace(doctorName))
            bills = bills.Where(b => b.DoctorName?.Contains(doctorName, StringComparison.OrdinalIgnoreCase) == true).ToList();
        if (!string.IsNullOrWhiteSpace(patientName))
            bills = bills.Where(b => b.PatientName?.Contains(patientName, StringComparison.OrdinalIgnoreCase) == true).ToList();

        if (bills.Count == 0) return [];

        var billIds = bills.Select(b => b.Id).ToList();
        var movements = await _db.PharmacyInventoryMovements
            .ForClinic(clinicId)
            .Where(m => m.ReferenceType == PharmacyInventoryTypes.ReferenceBill &&
                        m.MovementType == PharmacyInventoryTypes.BillOut &&
                        billIds.Contains(m.ReferenceId))
            .ToListAsync();

        var costByBillLine = movements
            .GroupBy(m => (m.ReferenceId, m.LineNo))
            .ToDictionary(g => g.Key, g => g.First().UnitCost);

        var items = await _db.PharmacyItems.ForClinic(clinicId).Where(i => i.IsActive).ToListAsync();

        var rows = new List<CogsRow>();
        foreach (var bill in bills)
        {
            foreach (var line in bill.Lines.OrderBy(l => l.LineNo))
            {
                if (line.Qty <= 0) continue;
                if (string.IsNullOrWhiteSpace(line.MedicineName) && string.IsNullOrWhiteSpace(line.Barcode)) continue;

                decimal unitCost;
                if (costByBillLine.TryGetValue((bill.Id, line.LineNo), out var movementCost) && movementCost > 0)
                    unitCost = movementCost;
                else
                {
                    var registered = PharmacyItemRegistrationService.ResolveForLine(
                        items, line.Barcode, line.MedicineCode, line.MedicineName);
                    unitCost = registered?.MovingAverageCost ?? 0m;
                }

                rows.Add(new CogsRow(
                    bill.BillDate,
                    bill.BillNo,
                    bill.PatientName ?? "",
                    bill.DoctorName ?? "",
                    line.MedicineName ?? line.MedicineCode ?? "Item",
                    line.Qty,
                    unitCost,
                    line.Qty * unitCost,
                    line.LineTotal));
            }
        }

        return rows;
    }

    public async Task<decimal> GetTotalCogsAsync(
        Guid clinicId,
        DateTime fromDate,
        DateTime toDate,
        string? doctorName,
        string? patientName)
    {
        var rows = await GetCogsRowsAsync(clinicId, fromDate, toDate, doctorName, patientName);
        return rows.Sum(r => r.TotalCost);
    }

    public sealed record CogsRow(
        DateTime BillDate,
        int BillNo,
        string PatientName,
        string DoctorName,
        string ItemName,
        int Qty,
        decimal UnitCost,
        decimal TotalCost,
        decimal SalesAmount);
}
