using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public sealed class PharmacyCogsService
{
    private readonly ClinicalDbContext _db;

    public PharmacyCogsService(ClinicalDbContext db) => _db = db;

    public async Task<List<CogsRow>> GetCogsRowsAsync(
        Guid clinicId,
        DateTime fromDate,
        DateTime toDate,
        string? doctorName,
        string? patientName)
    {
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

        var items = await _db.PharmacyItems
            .ForClinic(clinicId)
            .ToListAsync();

        var costByBarcode = items
            .Where(i => !string.IsNullOrWhiteSpace(i.Barcode))
            .GroupBy(i => i.Barcode!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().MovingAverageCost, StringComparer.OrdinalIgnoreCase);

        var costByName = items
            .GroupBy(i => i.MedicineName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().MovingAverageCost, StringComparer.OrdinalIgnoreCase);

        var rows = new List<CogsRow>();
        foreach (var bill in bills)
        {
            foreach (var line in bill.Lines.OrderBy(l => l.LineNo))
            {
                if (line.Qty <= 0) continue;
                if (string.IsNullOrWhiteSpace(line.MedicineName) && string.IsNullOrWhiteSpace(line.Barcode)) continue;

                var unitCost = ResolveUnitCost(line, costByBarcode, costByName);
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

    private static decimal ResolveUnitCost(
        PharmacyBillLine line,
        IReadOnlyDictionary<string, decimal> costByBarcode,
        IReadOnlyDictionary<string, decimal> costByName)
    {
        if (!string.IsNullOrWhiteSpace(line.Barcode) &&
            costByBarcode.TryGetValue(line.Barcode, out var byBarcode))
            return byBarcode;

        if (!string.IsNullOrWhiteSpace(line.MedicineName) &&
            costByName.TryGetValue(line.MedicineName, out var byName))
            return byName;

        return 0;
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
