using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public sealed class GlobalTransactionSearchService
{
    private readonly ClinicalDbContext _db;

    public GlobalTransactionSearchService(ClinicalDbContext db) => _db = db;

    public async Task<List<GlobalSearchHit>> SearchAsync(Guid clinicId, string? term, int limit = 80)
    {
        if (string.IsNullOrWhiteSpace(term)) return [];
        var t = term.Trim();
        var perType = Math.Max(5, limit / 12);
        var hits = new List<GlobalSearchHit>();

        hits.AddRange(await SafeSearchAsync(() => SearchInvoicesAsync(clinicId, t, perType)));
        hits.AddRange(await SafeSearchAsync(() => SearchCashReceiptsAsync(clinicId, t, perType)));
        hits.AddRange(await SafeSearchAsync(() => SearchCashPaymentsAsync(clinicId, t, perType)));
        hits.AddRange(await SafeSearchAsync(() => SearchPrescriptionsAsync(clinicId, t, perType)));
        hits.AddRange(await SafeSearchAsync(() => SearchLabRequestsAsync(clinicId, t, perType)));
        hits.AddRange(await SafeSearchAsync(() => SearchLabResultsAsync(clinicId, t, perType)));
        hits.AddRange(await SafeSearchAsync(() => SearchPharmacyRequestsAsync(clinicId, t, perType)));
        hits.AddRange(await SafeSearchAsync(() => SearchPharmacyBillsAsync(clinicId, t, perType)));
        hits.AddRange(await SafeSearchAsync(() => SearchPharmacyPurchasesAsync(clinicId, t, perType)));
        hits.AddRange(await SafeSearchAsync(() => SearchPharmacyOpeningBalancesAsync(clinicId, t, perType)));
        hits.AddRange(await SafeSearchAsync(() => SearchRadiologyRequestsAsync(clinicId, t, perType)));
        hits.AddRange(await SafeSearchAsync(() => SearchRadiologyResultsAsync(clinicId, t, perType)));

        return hits
            .OrderByDescending(h => h.TransactionDate)
            .Take(limit)
            .ToList();
    }

    private async Task<List<GlobalSearchHit>> SearchInvoicesAsync(Guid clinicId, string term, int take)
    {
        var rows = await _db.Invoices.AsNoTracking()
            .Where(i => i.ClinicId == clinicId)
            .OrderByDescending(i => i.InvoiceDate)
            .Take(250)
            .ToListAsync();
        return rows.Where(i => Matches(term, i.InvoiceNo.ToString(), i.PatientName, i.PatientId, i.DoctorName, i.Phone))
            .Take(take)
            .Select(i => new GlobalSearchHit(
                "Invoice / Billing",
                $"#{i.InvoiceNo}",
                i.InvoiceDate,
                i.PatientName ?? "",
                i.DoctorName ?? "",
                i.TotalAmount,
                $"/Invoices?RecordId={i.Id}"))
            .ToList();
    }

    private async Task<List<GlobalSearchHit>> SearchCashReceiptsAsync(Guid clinicId, string term, int take)
    {
        var rows = await _db.CashReceipts.AsNoTracking()
            .Where(r => r.ClinicId == clinicId)
            .OrderByDescending(r => r.ReceiptDate)
            .Take(250)
            .ToListAsync();
        return rows.Where(r => Matches(term, r.ReceiptNo.ToString(), r.PatientName, r.PatientId, r.DoctorName, r.Phone, r.Description))
            .Take(take)
            .Select(r => new GlobalSearchHit(
                "Cash Receipt",
                $"#{r.ReceiptNo}",
                r.ReceiptDate,
                r.PatientName ?? "",
                r.DoctorName ?? "",
                r.Amount,
                $"/CashReceipts?RecordId={r.Id}"))
            .ToList();
    }

    private async Task<List<GlobalSearchHit>> SearchCashPaymentsAsync(Guid clinicId, string term, int take)
    {
        var rows = await _db.CashPayments.AsNoTracking()
            .Where(p => p.ClinicId == clinicId)
            .OrderByDescending(p => p.PaymentDate)
            .Take(250)
            .ToListAsync();
        return rows.Where(p => Matches(term, p.PaymentNo.ToString(), p.PayeeName, p.ChartAccountName, p.Description, p.ReferenceNo))
            .Take(take)
            .Select(p => new GlobalSearchHit(
                "Cash Payment",
                $"#{p.PaymentNo}",
                p.PaymentDate,
                p.PayeeName ?? "",
                "",
                p.Amount,
                $"/CashPayments?RecordId={p.Id}"))
            .ToList();
    }

    private async Task<List<GlobalSearchHit>> SearchPrescriptionsAsync(Guid clinicId, string term, int take)
    {
        var rows = await _db.Prescriptions.AsNoTracking()
            .Where(p => p.ClinicId == clinicId)
            .OrderByDescending(p => p.DatePrescription)
            .Take(250)
            .ToListAsync();
        return rows.Where(p => Matches(term, p.PrescriptionNo.ToString(), p.PatientName, p.DoctorName, p.Specialty, p.DiseaseName))
            .Take(take)
            .Select(p => new GlobalSearchHit(
                "Doctor's Prescription",
                $"#{p.PrescriptionNo}",
                p.DatePrescription,
                p.PatientName ?? "",
                p.DoctorName ?? "",
                0m,
                $"/Prescriptions?RecordId={p.Id}"))
            .ToList();
    }

    private async Task<List<GlobalSearchHit>> SearchLabRequestsAsync(Guid clinicId, string term, int take)
    {
        var rows = await _db.LabRequests.AsNoTracking()
            .Include(r => r.Lines)
            .Where(r => r.ClinicId == clinicId)
            .OrderByDescending(r => r.RequestDate)
            .Take(200)
            .ToListAsync();
        return rows.Where(r => Matches(term, r.RequestNo.ToString(), r.PatientName, r.PatientBarcode, r.DoctorName, r.Specialty,
                string.Join(" ", r.Lines.Select(l => $"{l.TestName} {l.TestCode}"))))
            .Take(take)
            .Select(r => new GlobalSearchHit(
                "Laboratory Request",
                $"#{r.RequestNo}",
                r.RequestDate,
                r.PatientName ?? "",
                r.DoctorName ?? "",
                r.TotalAmount,
                $"/Laboratory/Request?RecordId={r.Id}"))
            .ToList();
    }

    private async Task<List<GlobalSearchHit>> SearchLabResultsAsync(Guid clinicId, string term, int take)
    {
        var rows = await _db.LabResults.AsNoTracking()
            .Include(r => r.Lines)
            .Where(r => r.ClinicId == clinicId)
            .OrderByDescending(r => r.ResultDate)
            .Take(200)
            .ToListAsync();
        return rows.Where(r => Matches(term, r.ResultNo.ToString(), r.RequestNo?.ToString(), r.PatientName, r.DoctorName,
                string.Join(" ", r.Lines.Select(l => $"{l.TestName} {l.Result}"))))
            .Take(take)
            .Select(r => new GlobalSearchHit(
                "Laboratory Result",
                $"#{r.ResultNo}",
                r.ResultDate,
                r.PatientName ?? "",
                r.DoctorName ?? "",
                0m,
                $"/Laboratory/Result?RecordId={r.Id}"))
            .ToList();
    }

    private async Task<List<GlobalSearchHit>> SearchPharmacyRequestsAsync(Guid clinicId, string term, int take)
    {
        var rows = await _db.PharmacyRequests.AsNoTracking()
            .Include(r => r.Lines)
            .Where(r => r.ClinicId == clinicId)
            .OrderByDescending(r => r.RequestDate)
            .Take(200)
            .ToListAsync();
        return rows.Where(r => Matches(term, r.RequestNo.ToString(), r.PatientName, r.PatientId, r.DoctorName,
                string.Join(" ", r.Lines.Select(l => l.MedicineName))))
            .Take(take)
            .Select(r => new GlobalSearchHit(
                "Pharmacy Request",
                $"#{r.RequestNo}",
                r.RequestDate,
                r.PatientName ?? "",
                r.DoctorName ?? "",
                r.TotalAmount,
                $"/Pharmacy/Request?RecordId={r.Id}"))
            .ToList();
    }

    private async Task<List<GlobalSearchHit>> SearchPharmacyBillsAsync(Guid clinicId, string term, int take)
    {
        var rows = await _db.PharmacyBills.AsNoTracking()
            .Include(r => r.Lines)
            .Where(r => r.ClinicId == clinicId)
            .OrderByDescending(r => r.BillDate)
            .Take(200)
            .ToListAsync();
        return rows.Where(r => Matches(term, r.BillNo.ToString(), r.RequestNo?.ToString(), r.PatientName, r.PatientId, r.DoctorName,
                string.Join(" ", r.Lines.Select(l => l.MedicineName))))
            .Take(take)
            .Select(r => new GlobalSearchHit(
                "Pharmacy Bill",
                $"#{r.BillNo}",
                r.BillDate,
                r.PatientName ?? "",
                r.DoctorName ?? "",
                r.TotalAmount,
                $"/Pharmacy/Bill?RecordId={r.Id}"))
            .ToList();
    }

    private async Task<List<GlobalSearchHit>> SearchPharmacyPurchasesAsync(Guid clinicId, string term, int take)
    {
        var rows = await _db.PharmacyPurchaseBills.AsNoTracking()
            .Include(r => r.Lines)
            .Where(r => r.ClinicId == clinicId)
            .OrderByDescending(r => r.PurchaseDate)
            .Take(200)
            .ToListAsync();
        return rows.Where(r => Matches(term, r.PurchaseNo.ToString(), r.SupplierName, r.SupplierInvoiceNo, r.SupplierPhone,
                string.Join(" ", r.Lines.Select(l => l.MedicineName))))
            .Take(take)
            .Select(r => new GlobalSearchHit(
                "Pharmacy Purchase Bill",
                $"#{r.PurchaseNo}",
                r.PurchaseDate,
                r.SupplierName ?? "",
                "",
                r.NetAmount,
                $"/Pharmacy/Purchase?RecordId={r.Id}"))
            .ToList();
    }

    private async Task<List<GlobalSearchHit>> SearchPharmacyOpeningBalancesAsync(Guid clinicId, string term, int take)
    {
        var rows = await _db.PharmacyOpeningBalances.AsNoTracking()
            .Include(r => r.Lines)
            .Where(r => r.ClinicId == clinicId)
            .OrderByDescending(r => r.BalanceDate)
            .Take(200)
            .ToListAsync();
        return rows.Where(r => Matches(term, r.BalanceNo.ToString(), r.Notes,
                string.Join(" ", r.Lines.Select(l => $"{l.MedicineName} {l.Barcode}"))))
            .Take(take)
            .Select(r => new GlobalSearchHit(
                "Pharmacy Opening Balance",
                $"#{r.BalanceNo}",
                r.BalanceDate,
                "",
                "",
                r.Lines.Sum(l => l.Total),
                $"/Pharmacy/OpeningBalance?RecordId={r.Id}"))
            .ToList();
    }

    private async Task<List<GlobalSearchHit>> SearchRadiologyRequestsAsync(Guid clinicId, string term, int take)
    {
        var rows = await _db.RadiologyRequests.AsNoTracking()
            .Include(r => r.Lines)
            .Where(r => r.ClinicId == clinicId)
            .OrderByDescending(r => r.RequestDate)
            .Take(200)
            .ToListAsync();
        return rows.Where(r => Matches(term, r.RequestNo.ToString(), r.PatientName, r.PatientBarcode, r.DoctorName,
                string.Join(" ", r.Lines.Select(l => l.TestName))))
            .Take(take)
            .Select(r => new GlobalSearchHit(
                "Radiology Request",
                $"#{r.RequestNo}",
                r.RequestDate,
                r.PatientName ?? "",
                r.DoctorName ?? "",
                r.TotalAmount,
                $"/Radiology/Request?RecordId={r.Id}"))
            .ToList();
    }

    private async Task<List<GlobalSearchHit>> SearchRadiologyResultsAsync(Guid clinicId, string term, int take)
    {
        var rows = await _db.RadiologyResults.AsNoTracking()
            .Include(r => r.Lines)
            .Where(r => r.ClinicId == clinicId)
            .OrderByDescending(r => r.ResultDate)
            .Take(200)
            .ToListAsync();
        return rows.Where(r => Matches(term, r.ResultNo.ToString(), r.RequestNo?.ToString(), r.PatientName, r.DoctorName,
                string.Join(" ", r.Lines.Select(l => $"{l.TestName} {l.Result}"))))
            .Take(take)
            .Select(r => new GlobalSearchHit(
                "Radiology Result",
                $"#{r.ResultNo}",
                r.ResultDate,
                r.PatientName ?? "",
                r.DoctorName ?? "",
                0m,
                $"/Radiology/Result?RecordId={r.Id}"))
            .ToList();
    }

    private static async Task<List<GlobalSearchHit>> SafeSearchAsync(Func<Task<List<GlobalSearchHit>>> search)
    {
        try
        {
            return await search();
        }
        catch
        {
            return [];
        }
    }

    private static bool Matches(string term, params string?[] fields)
    {
        foreach (var field in fields)
        {
            if (!string.IsNullOrWhiteSpace(field) &&
                field.Contains(term, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public sealed record GlobalSearchHit(
        string TransactionType,
        string Reference,
        DateTime TransactionDate,
        string PatientOrParty,
        string DoctorName,
        decimal Amount,
        string Link);
}
