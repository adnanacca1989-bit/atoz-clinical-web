using AtoZClinical.Core.Entities;
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

        var lines = new List<PatientChargeLine>();

        await AddServiceIncomeRequestLinesAsync(clinicId, barcode, name, doctor, lines);
        await AddLabRequestLinesAsync(clinicId, barcode, name, doctor, lines);
        await AddRadiologyRequestLinesAsync(clinicId, barcode, name, doctor, lines);
        await AddPharmacyRequestLinesAsync(clinicId, barcode, name, doctor, lines);
        await AddPharmacyBillLinesAsync(clinicId, barcode, name, doctor, lines);

        var receipts = await LoadCashReceiptsAsync(clinicId, barcode, name, doctor);
        var patientPayments = await LoadCashPaymentsAsync(clinicId, barcode, name, doctor);

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

    private async Task AddServiceIncomeRequestLinesAsync(
        Guid clinicId, string? barcode, string? name, string? doctor, List<PatientChargeLine> lines)
    {
        try
        {
            var serviceRequests = await _db.ServiceIncomeRequests
                .Include(r => r.Lines)
                .ForClinic(clinicId)
                .Where(r =>
                    (barcode != null && r.PatientBarcode != null &&
                     (EF.Functions.ILike(r.PatientBarcode, barcode) || EF.Functions.ILike(r.PatientBarcode, $"%{barcode}%"))) ||
                    (name != null && r.PatientName != null &&
                     (EF.Functions.ILike(r.PatientName, name) || EF.Functions.ILike(r.PatientName, $"%{name}%"))))
                .OrderByDescending(r => r.RequestDate)
                .ToListAsync();

            AppendMatchedRequestLines(serviceRequests, barcode, name, doctor,
                r => r.PatientBarcode, null, r => r.PatientName, r => r.DoctorName,
                req => req.Lines.OrderBy(l => l.LineNo),
                (req, line) =>
                {
                    if (string.IsNullOrWhiteSpace(line.ServiceName) && line.Fee <= 0) return null;
                    return new PatientChargeLine(
                        $"Service #{req.RequestNo}: {line.ServiceName ?? "Service"}",
                        line.Qty, line.Fee, "Service Income");
                },
                lines);
        }
        catch
        {
            // Table may not exist until migration is applied.
        }
    }

    private async Task AddLabRequestLinesAsync(
        Guid clinicId, string? barcode, string? name, string? doctor, List<PatientChargeLine> lines)
    {
        var labRequests = await _db.LabRequests
            .Include(r => r.Lines)
            .ForClinic(clinicId)
            .Where(r =>
                (barcode != null && r.PatientBarcode != null &&
                 (EF.Functions.ILike(r.PatientBarcode, barcode) || EF.Functions.ILike(r.PatientBarcode, $"%{barcode}%"))) ||
                (name != null && r.PatientName != null &&
                 (EF.Functions.ILike(r.PatientName, name) || EF.Functions.ILike(r.PatientName, $"%{name}%"))))
            .OrderByDescending(r => r.RequestDate)
            .ToListAsync();

        AppendMatchedRequestLines(labRequests, barcode, name, doctor,
            r => r.PatientBarcode, null, r => r.PatientName, r => r.DoctorName,
            req => req.Lines.OrderBy(l => l.LineNo),
            (req, line) =>
            {
                if (string.IsNullOrWhiteSpace(line.TestName) && line.Fee <= 0) return null;
                return new PatientChargeLine(
                    $"Lab #{req.RequestNo}: {line.TestName ?? line.TestCode ?? "Test"}",
                    line.Qty, line.Fee, "Laboratory");
            },
            lines);
    }

    private async Task AddRadiologyRequestLinesAsync(
        Guid clinicId, string? barcode, string? name, string? doctor, List<PatientChargeLine> lines)
    {
        var radioRequests = await _db.RadiologyRequests
            .Include(r => r.Lines)
            .ForClinic(clinicId)
            .Where(r =>
                (barcode != null && r.PatientBarcode != null &&
                 (EF.Functions.ILike(r.PatientBarcode, barcode) || EF.Functions.ILike(r.PatientBarcode, $"%{barcode}%"))) ||
                (name != null && r.PatientName != null &&
                 (EF.Functions.ILike(r.PatientName, name) || EF.Functions.ILike(r.PatientName, $"%{name}%"))))
            .OrderByDescending(r => r.RequestDate)
            .ToListAsync();

        AppendMatchedRequestLines(radioRequests, barcode, name, doctor,
            r => r.PatientBarcode, null, r => r.PatientName, r => r.DoctorName,
            req => req.Lines.OrderBy(l => l.LineNo),
            (req, line) =>
            {
                if (string.IsNullOrWhiteSpace(line.TestName) && line.Fee <= 0) return null;
                return new PatientChargeLine(
                    $"Radiology #{req.RequestNo}: {line.TestName ?? line.TestCode ?? "Test"}",
                    line.Qty, line.Fee, "Radiology");
            },
            lines);
    }

    private async Task AddPharmacyRequestLinesAsync(
        Guid clinicId, string? barcode, string? name, string? doctor, List<PatientChargeLine> lines)
    {
        var billedRequestNos = await _db.PharmacyBills
            .ForClinic(clinicId)
            .Where(b => b.RequestNo != null)
            .Select(b => b.RequestNo!.Value)
            .ToListAsync();

        var pharmacyRequests = await _db.PharmacyRequests
            .Include(r => r.Lines)
            .ForClinic(clinicId)
            .Where(r =>
                (barcode != null && r.PatientId != null &&
                 (EF.Functions.ILike(r.PatientId, barcode) || EF.Functions.ILike(r.PatientId, $"%{barcode}%"))) ||
                (name != null && r.PatientName != null &&
                 (EF.Functions.ILike(r.PatientName, name) || EF.Functions.ILike(r.PatientName, $"%{name}%"))))
            .OrderByDescending(r => r.RequestDate)
            .ToListAsync();

        pharmacyRequests = pharmacyRequests
            .Where(r => !billedRequestNos.Contains(r.RequestNo))
            .ToList();

        AppendMatchedRequestLines(pharmacyRequests, barcode, name, doctor,
            _ => null, r => r.PatientId, r => r.PatientName, r => r.DoctorName,
            req => req.Lines.OrderBy(l => l.LineNo),
            (req, line) =>
            {
                if (string.IsNullOrWhiteSpace(line.MedicineName) && line.UnitPrice <= 0) return null;
                return new PatientChargeLine(
                    $"Pharmacy Req #{req.RequestNo}: {line.MedicineName ?? line.MedicineCode ?? "Medicine"}",
                    line.Qty, line.UnitPrice, "Pharmacy");
            },
            lines);
    }

    private async Task AddPharmacyBillLinesAsync(
        Guid clinicId, string? barcode, string? name, string? doctor, List<PatientChargeLine> lines)
    {
        var pharmacyBills = await _db.PharmacyBills
            .Include(b => b.Lines)
            .ForClinic(clinicId)
            .Where(b =>
                (barcode != null && b.PatientId != null &&
                 (EF.Functions.ILike(b.PatientId, barcode) || EF.Functions.ILike(b.PatientId, $"%{barcode}%"))) ||
                (name != null && b.PatientName != null &&
                 (EF.Functions.ILike(b.PatientName, name) || EF.Functions.ILike(b.PatientName, $"%{name}%"))))
            .OrderByDescending(b => b.BillDate)
            .ToListAsync();

        AppendMatchedRequestLines(pharmacyBills, barcode, name, doctor,
            _ => null, b => b.PatientId, b => b.PatientName, b => b.DoctorName,
            bill => bill.Lines.OrderBy(l => l.LineNo),
            (bill, line) =>
            {
                if (string.IsNullOrWhiteSpace(line.MedicineName) && line.UnitPrice <= 0) return null;
                return new PatientChargeLine(
                    $"Pharmacy Bill #{bill.BillNo}: {line.MedicineName ?? line.MedicineCode ?? "Medicine"}",
                    line.Qty, line.UnitPrice, "Pharmacy");
            },
            lines);
    }

    private async Task<List<CashReceipt>> LoadCashReceiptsAsync(
        Guid clinicId, string? barcode, string? name, string? doctor)
    {
        var receipts = await _db.CashReceipts
            .ForClinic(clinicId)
            .Where(r =>
                (barcode != null && r.PatientId != null &&
                 (EF.Functions.ILike(r.PatientId, barcode) || EF.Functions.ILike(r.PatientId, $"%{barcode}%"))) ||
                (name != null && r.PatientName != null &&
                 (EF.Functions.ILike(r.PatientName, name) || EF.Functions.ILike(r.PatientName, $"%{name}%"))))
            .ToListAsync();
        return receipts
            .Where(r => PatientChargeMatcher.MatchesPatient(barcode, name, null, r.PatientId, r.PatientName))
            .Where(r => PatientChargeMatcher.MatchesDoctor(doctor, r.DoctorName))
            .ToList();
    }

    private async Task<List<CashPayment>> LoadCashPaymentsAsync(
        Guid clinicId, string? barcode, string? name, string? doctor)
    {
        var patientPayments = await _db.CashPayments
            .ForClinic(clinicId)
            .Where(p =>
                (barcode != null && p.PatientId != null &&
                 (EF.Functions.ILike(p.PatientId, barcode) || EF.Functions.ILike(p.PatientId, $"%{barcode}%"))) ||
                (name != null && p.PayeeName != null &&
                 (EF.Functions.ILike(p.PayeeName, name) || EF.Functions.ILike(p.PayeeName, $"%{name}%"))))
            .ToListAsync();
        return patientPayments
            .Where(p => PatientChargeMatcher.MatchesPatient(barcode, name, null, p.PatientId, p.PayeeName))
            .Where(p => PatientChargeMatcher.MatchesDoctor(doctor, p.DoctorName))
            .ToList();
    }

    private static void AppendMatchedRequestLines<T, TLine>(
        IEnumerable<T> records,
        string? barcode,
        string? name,
        string? doctor,
        Func<T, string?> getBarcode,
        Func<T, string?>? getPatientId,
        Func<T, string?> getPatientName,
        Func<T, string?> getDoctorName,
        Func<T, IEnumerable<TLine>> getLines,
        Func<T, TLine, PatientChargeLine?> mapLine,
        List<PatientChargeLine> target)
    {
        foreach (var record in records)
        {
            if (!PatientChargeMatcher.MatchesPatient(barcode, name, getBarcode(record), getPatientId?.Invoke(record), getPatientName(record)))
                continue;
            if (!PatientChargeMatcher.MatchesDoctor(doctor, getDoctorName(record)))
                continue;

            foreach (var line in getLines(record))
            {
                var mapped = mapLine(record, line);
                if (mapped is not null) target.Add(mapped);
            }
        }
    }

    private async Task AddConsultationFeeLineAsync(
        Guid clinicId, string? barcode, string? name, string? doctorName, List<PatientChargeLine> lines)
    {
        IQueryable<Patient> patientQuery = _db.Patients.ForClinic(clinicId);
        if (!string.IsNullOrWhiteSpace(barcode))
            patientQuery = patientQuery.Where(p => p.PatientNo != null &&
                (EF.Functions.ILike(p.PatientNo, barcode) || EF.Functions.ILike(p.PatientNo, $"%{barcode}%")));
        else if (!string.IsNullOrWhiteSpace(name))
            patientQuery = patientQuery.Where(p => p.FullName != null &&
                (EF.Functions.ILike(p.FullName, name) || EF.Functions.ILike(p.FullName, $"%{name}%")));
        else
            return;

        if (!string.IsNullOrWhiteSpace(doctorName))
            patientQuery = patientQuery.Where(p => p.DoctorName != null &&
                (EF.Functions.ILike(p.DoctorName, doctorName) || EF.Functions.ILike(p.DoctorName, $"%{doctorName}%")));

        var patient = await patientQuery.OrderByDescending(p => p.CreatedAt).FirstOrDefaultAsync();
        if (patient is null || string.IsNullOrWhiteSpace(patient.DoctorName)) return;

        var doctor = await _db.Doctors
            .ForClinic(clinicId)
            .Where(d => d.Name != null && EF.Functions.ILike(d.Name, patient.DoctorName))
            .FirstOrDefaultAsync();
        if (doctor is null || doctor.ConsultationFee <= 0) return;

        var hasConsultation = lines.Any(l =>
            l.ServiceName.Contains("Consultation", StringComparison.OrdinalIgnoreCase) ||
            l.ServiceName.Contains(doctor.Name, StringComparison.OrdinalIgnoreCase));
        if (hasConsultation) return;

        var services = await _db.ServiceIncomes
            .ForClinic(clinicId)
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

        var barcode = patientId?.Trim();
        var name = patientName?.Trim();
        var doctor = doctorName?.Trim();

        var invoices = await _db.Invoices
            .ForClinic(clinicId)
            .Where(i =>
                (barcode != null && i.PatientId != null &&
                 (EF.Functions.ILike(i.PatientId, barcode) || EF.Functions.ILike(i.PatientId, $"%{barcode}%"))) ||
                (name != null && i.PatientName != null &&
                 (EF.Functions.ILike(i.PatientName, name) || EF.Functions.ILike(i.PatientName, $"%{name}%"))))
            .OrderBy(i => i.InvoiceDate).ThenBy(i => i.InvoiceNo)
            .ToListAsync();
        invoices = invoices
            .Where(i => PatientChargeMatcher.MatchesPatient(barcode, name, null, i.PatientId, i.PatientName))
            .Where(i => PatientChargeMatcher.MatchesDoctor(doctor, i.DoctorName))
            .ToList();

        if (invoices.Count == 0) return;

        var receipts = await LoadCashReceiptsAsync(clinicId, barcode, name, doctor);
        var patientPayments = await LoadCashPaymentsAsync(clinicId, barcode, name, doctor);

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

        await ClinicSaveHelper.ExecuteIsolatedSaveAsync(_db, () =>
        {
            _db.Invoices.UpdateRange(invoices);
            return Task.CompletedTask;
        });
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
