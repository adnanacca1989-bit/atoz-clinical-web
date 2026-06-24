using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public sealed class PatientInvoiceService
{
    private readonly ClinicalDbContext _db;

    public PatientInvoiceService(ClinicalDbContext db) => _db = db;

    public async Task<PatientChargeSummary> GetChargesAsync(
        Guid clinicId, string? patientBarcode, string? patientName, string? doctorName = null)
    {
        var barcode = patientBarcode?.Trim();
        var name = patientName?.Trim();
        var doctor = doctorName?.Trim();
        if (string.IsNullOrEmpty(barcode) && string.IsNullOrEmpty(name))
            return new PatientChargeSummary();

        bool MatchDoctor(string? doc) =>
            string.IsNullOrWhiteSpace(doctor) ||
            string.Equals(doc?.Trim(), doctor, StringComparison.OrdinalIgnoreCase);

        var lines = new List<PatientChargeLine>();

        var labRequests = await _db.LabRequests
            .Include(r => r.Lines)
            .Where(r => r.ClinicId == clinicId &&
                        ((barcode != null && r.PatientBarcode == barcode) ||
                         (name != null && r.PatientName == name)))
            .OrderByDescending(r => r.RequestDate)
            .ToListAsync();

        foreach (var req in labRequests.Where(r => MatchDoctor(r.DoctorName)))
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

        foreach (var req in radioRequests.Where(r => MatchDoctor(r.DoctorName)))
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

        foreach (var bill in pharmacyBills.Where(b => MatchDoctor(b.DoctorName)))
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

        var receipts = await _db.CashReceipts
            .Where(r => r.ClinicId == clinicId &&
                        ((barcode != null && r.PatientId == barcode) ||
                         (name != null && r.PatientName == name)))
            .ToListAsync();
        receipts = receipts.Where(r => MatchDoctor(r.DoctorName)).ToList();

        var patientPayments = await _db.CashPayments
            .Where(p => p.ClinicId == clinicId &&
                        ((barcode != null && p.PatientId == barcode) ||
                         (name != null && p.PayeeName == name)))
            .ToListAsync();
        patientPayments = patientPayments.Where(p => MatchDoctor(p.DoctorName)).ToList();

        await AddConsultationFeeLineAsync(clinicId, barcode, name, doctor, lines);

        var totalPaid = receipts.Sum(r => r.Amount) + patientPayments.Sum(p => p.Amount);
        var subTotal = lines.Sum(l => l.Qty * l.UnitFee);
        var balance = Math.Max(0, subTotal - totalPaid);

        return new PatientChargeSummary
        {
            Lines = lines,
            SubTotal = subTotal,
            TotalPaid = totalPaid,
            Balance = balance
        };
    }

    private async Task AddConsultationFeeLineAsync(
        Guid clinicId, string? barcode, string? name, string? doctorName, List<PatientChargeLine> lines)
    {
        var patientQuery = _db.Patients.Where(p => p.ClinicId == clinicId);
        if (!string.IsNullOrWhiteSpace(barcode))
            patientQuery = patientQuery.Where(p => p.PatientNo == barcode);
        else if (!string.IsNullOrWhiteSpace(name))
            patientQuery = patientQuery.Where(p => p.FullName == name);
        else
            return;

        if (!string.IsNullOrWhiteSpace(doctorName))
            patientQuery = patientQuery.Where(p => p.DoctorName == doctorName);

        var patient = await patientQuery.OrderByDescending(p => p.CreatedAt).FirstOrDefaultAsync();
        if (patient is null || string.IsNullOrWhiteSpace(patient.DoctorName)) return;

        var doctor = await _db.Doctors
            .Where(d => d.ClinicId == clinicId && d.Name == patient.DoctorName)
            .FirstOrDefaultAsync();
        if (doctor is null || doctor.ConsultationFee <= 0) return;

        var hasConsultation = lines.Any(l =>
            l.ServiceName.Contains("Consultation", StringComparison.OrdinalIgnoreCase) ||
            l.ServiceName.Contains(doctor.Name, StringComparison.OrdinalIgnoreCase));
        if (hasConsultation) return;

        var services = await _db.ServiceIncomes
            .Where(s => s.ClinicId == clinicId)
            .OrderBy(s => s.ServiceNo)
            .ToListAsync();
        var serviceIncome = services.FirstOrDefault(s =>
            s.Name.Contains("Consultation", StringComparison.OrdinalIgnoreCase));

        var serviceName = serviceIncome?.Name ?? $"Consultation Fee - {doctor.Name}";
        var fee = doctor.ConsultationFee > 0 ? doctor.ConsultationFee : (serviceIncome?.Fee ?? 0);
        if (fee <= 0) return;

        lines.Insert(0, new PatientChargeLine(serviceName, 1, fee, "Consultation"));
    }

    public async Task RecalculateInvoicePaymentsAsync(
        Guid clinicId, string? patientName, string? patientId, string? doctorName = null)
    {
        if (string.IsNullOrWhiteSpace(patientName) && string.IsNullOrWhiteSpace(patientId))
            return;

        bool MatchDoctor(string? doc) =>
            string.IsNullOrWhiteSpace(doctorName) ||
            string.Equals(doc?.Trim(), doctorName.Trim(), StringComparison.OrdinalIgnoreCase);

        var invoices = await _db.Invoices
            .Where(i => i.ClinicId == clinicId)
            .Where(i =>
                (!string.IsNullOrWhiteSpace(patientId) && i.PatientId == patientId) ||
                (!string.IsNullOrWhiteSpace(patientName) && i.PatientName == patientName))
            .OrderBy(i => i.InvoiceDate).ThenBy(i => i.InvoiceNo)
            .ToListAsync();
        invoices = invoices.Where(i => MatchDoctor(i.DoctorName)).ToList();

        if (invoices.Count == 0) return;

        var receipts = await _db.CashReceipts
            .Where(r => r.ClinicId == clinicId)
            .Where(r =>
                (!string.IsNullOrWhiteSpace(patientId) && r.PatientId == patientId) ||
                (!string.IsNullOrWhiteSpace(patientName) && r.PatientName == patientName))
            .OrderBy(r => r.ReceiptDate).ThenBy(r => r.ReceiptNo)
            .ToListAsync();
        receipts = receipts.Where(r => MatchDoctor(r.DoctorName)).ToList();

        var patientPayments = await _db.CashPayments
            .Where(p => p.ClinicId == clinicId)
            .Where(p =>
                (!string.IsNullOrWhiteSpace(patientId) && p.PatientId == patientId) ||
                (!string.IsNullOrWhiteSpace(patientName) && p.PayeeName == patientName))
            .OrderBy(p => p.PaymentDate).ThenBy(p => p.PaymentNo)
            .ToListAsync();
        patientPayments = patientPayments.Where(p => MatchDoctor(p.DoctorName)).ToList();

        foreach (var inv in invoices)
        {
            inv.AmountPaid = 0;
            inv.BalanceDue = inv.TotalAmount;
            inv.PaymentStatus = inv.BalanceDue > 0 ? "Unpaid" : "Paid";
        }

        var credits = receipts
            .Select(r => new { Date = r.ReceiptDate, r.Amount, SortKey = r.ReceiptNo })
            .Concat(patientPayments.Select(p => new { Date = p.PaymentDate, p.Amount, SortKey = p.PaymentNo }))
            .OrderBy(c => c.Date).ThenBy(c => c.SortKey)
            .ToList();

        foreach (var credit in credits)
        {
            var remaining = credit.Amount;
            foreach (var inv in invoices.Where(i => i.BalanceDue > 0))
            {
                if (remaining <= 0) break;
                var apply = Math.Min(remaining, inv.BalanceDue);
                inv.AmountPaid += apply;
                inv.BalanceDue -= apply;
                remaining -= apply;
                inv.PaymentStatus = inv.BalanceDue <= 0 ? "Paid" : "Partial";
                if (inv.BalanceDue < 0) inv.BalanceDue = 0;
            }
        }

        await _db.SaveChangesAsync();
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
