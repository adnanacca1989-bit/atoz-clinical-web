using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public sealed class PatientVisitHistoryService
{
    private readonly ClinicalDbContext _db;
    private readonly DoctorScopeContext _doctorScope;

    public PatientVisitHistoryService(ClinicalDbContext db, DoctorScopeContext doctorScope)
    {
        _db = db;
        _doctorScope = doctorScope;
    }

    public async Task<PatientVisitHistorySummary> GetHistoryAsync(
        Guid clinicId,
        string? patientNo,
        string? patientName,
        string? nationalId,
        string? phone)
    {
        var barcode = patientNo?.Trim();
        var name = patientName?.Trim();
        var nid = nationalId?.Trim();
        var mobile = phone?.Trim();

        if (string.IsNullOrEmpty(barcode) && string.IsNullOrEmpty(name) &&
            string.IsNullOrEmpty(nid) && string.IsNullOrEmpty(mobile))
            return new PatientVisitHistorySummary([], 0, 0);

        var rows = new Dictionary<(DateTime Date, string Doctor), (decimal Revenue, decimal Received)>();

        void Ensure(DateTime date, string? doctor)
        {
            var key = (date.Date, (doctor ?? string.Empty).Trim());
            if (!rows.ContainsKey(key))
                rows[key] = (0, 0);
        }

        void AddRevenue(DateTime date, string? doctor, decimal amount)
        {
            if (amount <= 0) return;
            var key = (date.Date, (doctor ?? string.Empty).Trim());
            rows.TryGetValue(key, out var current);
            rows[key] = (current.Revenue + amount, current.Received);
        }

        void AddReceived(DateTime date, string? doctor, decimal amount)
        {
            if (amount <= 0) return;
            var key = (date.Date, (doctor ?? string.Empty).Trim());
            rows.TryGetValue(key, out var current);
            rows[key] = (current.Revenue, current.Received + amount);
        }

        var patients = await _db.Patients
            .AsNoTracking()
            .Where(p => p.ClinicId == clinicId)
            .Apply(_doctorScope.Filter)
            .Select(p => new
            {
                p.PatientNo,
                p.FirstName,
                p.LastName,
                p.NationalId,
                p.Phone,
                p.DoctorName,
                p.AppointmentDate,
                p.CreatedAt
            })
            .ToListAsync();

        foreach (var p in patients.Where(p => Matches(p.PatientNo, p.FirstName, p.LastName, p.NationalId, p.Phone)))
        {
            var visitDate = p.AppointmentDate?.Date ?? p.CreatedAt.Date;
            Ensure(visitDate, p.DoctorName);
        }

        var invoices = await _db.Invoices
            .AsNoTracking()
            .Where(i => i.ClinicId == clinicId)
            .Apply(_doctorScope.Filter)
            .Select(i => new { i.PatientId, i.PatientName, i.DoctorName, i.InvoiceDate, i.TotalAmount })
            .ToListAsync();

        foreach (var inv in invoices.Where(i => Matches(i.PatientId, i.PatientName, null, null, null)))
            AddRevenue(inv.InvoiceDate, inv.DoctorName, inv.TotalAmount);

        var receipts = await _db.CashReceipts
            .AsNoTracking()
            .Where(r => r.ClinicId == clinicId)
            .Apply(_doctorScope.Filter)
            .Select(r => new { r.PatientId, r.PatientName, r.DoctorName, r.ReceiptDate, r.Amount })
            .ToListAsync();

        foreach (var cr in receipts.Where(r => Matches(r.PatientId, r.PatientName, null, null, null)))
            AddReceived(cr.ReceiptDate, cr.DoctorName, cr.Amount);

        var result = rows
            .Select(kv => new PatientVisitHistoryRow(kv.Key.Date, kv.Key.Doctor, kv.Value.Revenue, kv.Value.Received))
            .OrderByDescending(r => r.VisitDate)
            .ThenBy(r => r.DoctorName)
            .ToList();

        return new PatientVisitHistorySummary(
            result,
            result.Sum(r => r.TotalRevenue),
            result.Sum(r => r.AmountReceived));

        bool Matches(string? rowId, string? rowName, string? rowLastName, string? rowNid, string? rowPhone)
        {
            if (!string.IsNullOrEmpty(barcode) &&
                string.Equals(rowId?.Trim(), barcode, StringComparison.OrdinalIgnoreCase))
                return true;

            var fullName = string.IsNullOrWhiteSpace(rowLastName)
                ? rowName?.Trim()
                : $"{rowName} {rowLastName}".Trim();

            if (!string.IsNullOrEmpty(name) &&
                string.Equals(fullName, name, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrEmpty(nid) && !string.IsNullOrEmpty(rowNid) &&
                string.Equals(rowNid.Trim(), nid, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrEmpty(mobile) && !string.IsNullOrEmpty(rowPhone) &&
                string.Equals(rowPhone.Trim(), mobile, StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }
    }

    public sealed record PatientVisitHistoryRow(
        DateTime VisitDate,
        string DoctorName,
        decimal TotalRevenue,
        decimal AmountReceived);

    public sealed record PatientVisitHistorySummary(
        IReadOnlyList<PatientVisitHistoryRow> Rows,
        decimal GrandTotalRevenue,
        decimal GrandAmountReceived);
}
