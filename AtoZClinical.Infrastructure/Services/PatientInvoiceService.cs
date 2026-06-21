using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public sealed class PatientInvoiceService
{
    private readonly ClinicalDbContext _db;

    public PatientInvoiceService(ClinicalDbContext db) => _db = db;

    public async Task<PatientChargeSummary> GetChargesAsync(Guid clinicId, string? patientBarcode, string? patientName)
    {
        var barcode = patientBarcode?.Trim();
        var name = patientName?.Trim();
        if (string.IsNullOrEmpty(barcode) && string.IsNullOrEmpty(name))
            return new PatientChargeSummary();

        var lines = new List<PatientChargeLine>();

        var labRequests = await _db.LabRequests
            .Include(r => r.Lines)
            .Where(r => r.ClinicId == clinicId &&
                        ((barcode != null && r.PatientBarcode == barcode) ||
                         (name != null && r.PatientName == name)))
            .OrderByDescending(r => r.RequestDate)
            .ToListAsync();

        foreach (var req in labRequests)
        {
            foreach (var line in req.Lines.OrderBy(l => l.LineNo))
            {
                if (string.IsNullOrWhiteSpace(line.TestName) && line.Fee <= 0) continue;
                lines.Add(new PatientChargeLine(
                    $"Lab #{req.RequestNo}: {line.TestName ?? line.TestCode ?? "Test"}",
                    line.Qty,
                    line.Fee,
                    "Laboratory"));
            }
        }

        var radioRequests = await _db.RadiologyRequests
            .Include(r => r.Lines)
            .Where(r => r.ClinicId == clinicId &&
                        ((barcode != null && r.PatientBarcode == barcode) ||
                         (name != null && r.PatientName == name)))
            .OrderByDescending(r => r.RequestDate)
            .ToListAsync();

        foreach (var req in radioRequests)
        {
            foreach (var line in req.Lines.OrderBy(l => l.LineNo))
            {
                if (string.IsNullOrWhiteSpace(line.TestName) && line.Fee <= 0) continue;
                lines.Add(new PatientChargeLine(
                    $"Radiology #{req.RequestNo}: {line.TestName ?? line.TestCode ?? "Test"}",
                    line.Qty,
                    line.Fee,
                    "Radiology"));
            }
        }

        var pharmacyBills = await _db.PharmacyBills
            .Include(b => b.Lines)
            .Where(b => b.ClinicId == clinicId &&
                        ((barcode != null && b.PatientId == barcode) ||
                         (name != null && b.PatientName == name)))
            .OrderByDescending(b => b.BillDate)
            .ToListAsync();

        foreach (var bill in pharmacyBills)
        {
            foreach (var line in bill.Lines.OrderBy(l => l.LineNo))
            {
                if (string.IsNullOrWhiteSpace(line.MedicineName) && line.UnitPrice <= 0) continue;
                lines.Add(new PatientChargeLine(
                    $"Pharmacy Bill #{bill.BillNo}: {line.MedicineName ?? line.MedicineCode ?? "Medicine"}",
                    line.Qty,
                    line.UnitPrice,
                    "Pharmacy"));
            }
        }

        var pharmacyRequests = await _db.PharmacyRequests
            .Include(r => r.Lines)
            .Where(r => r.ClinicId == clinicId &&
                        ((barcode != null && r.PatientId == barcode) ||
                         (name != null && r.PatientName == name)))
            .OrderByDescending(r => r.RequestDate)
            .ToListAsync();

        foreach (var req in pharmacyRequests)
        {
            foreach (var line in req.Lines.OrderBy(l => l.LineNo))
            {
                if (string.IsNullOrWhiteSpace(line.MedicineName) && line.UnitPrice <= 0) continue;
                lines.Add(new PatientChargeLine(
                    $"Pharmacy Req #{req.RequestNo}: {line.MedicineName ?? line.MedicineCode ?? "Medicine"}",
                    line.Qty,
                    line.UnitPrice,
                    "Pharmacy"));
            }
        }

        var receipts = await _db.CashReceipts
            .Where(r => r.ClinicId == clinicId &&
                        ((barcode != null && r.PatientId == barcode) ||
                         (name != null && r.PatientName == name)))
            .ToListAsync();

        var totalPaid = receipts.Sum(r => r.Amount);
        var subTotal = lines.Sum(l => l.Qty * l.UnitFee);

        return new PatientChargeSummary
        {
            Lines = lines,
            SubTotal = subTotal,
            TotalPaid = totalPaid,
            Balance = subTotal - totalPaid
        };
    }
}

public sealed record PatientChargeLine(string ServiceName, int Qty, decimal UnitFee, string Category);

public sealed class PatientChargeSummary
{
    public List<PatientChargeLine> Lines { get; set; } = [];
    public decimal SubTotal { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal Balance { get; set; }
}
