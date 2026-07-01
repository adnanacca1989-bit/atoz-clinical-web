using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public sealed partial class GlobalTransactionSearchService
{
    private readonly ClinicalDbContext _db;
    private readonly DoctorScopeContext _doctorScope;
    private readonly bool _useILike;

    public GlobalTransactionSearchService(ClinicalDbContext db, DoctorScopeContext doctorScope)
    {
        _db = db;
        _doctorScope = doctorScope;
        _useILike = db.Database.IsNpgsql();
    }

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
        var pattern = LikePattern(term);
        var query = _db.Invoices.AsNoTracking().Where(i => i.ClinicId == clinicId).Apply(_doctorScope.Filter);

        if (int.TryParse(term, out var invoiceNo))
        {
            if (_useILike)
                query = query.Where(i => i.InvoiceNo == invoiceNo ||
                    EF.Functions.ILike(i.PatientName!, pattern) ||
                    EF.Functions.ILike(i.PatientId!, pattern) ||
                    EF.Functions.ILike(i.DoctorName!, pattern) ||
                    (i.Phone != null && EF.Functions.ILike(i.Phone, pattern)));
            else
                query = query.Where(i => i.InvoiceNo == invoiceNo ||
                    (i.PatientName != null && i.PatientName.Contains(term)) ||
                    (i.PatientId != null && i.PatientId.Contains(term)) ||
                    (i.DoctorName != null && i.DoctorName.Contains(term)) ||
                    (i.Phone != null && i.Phone.Contains(term)));
        }
        else if (_useILike)
        {
            query = query.Where(i =>
                EF.Functions.ILike(i.PatientName!, pattern) ||
                EF.Functions.ILike(i.PatientId!, pattern) ||
                EF.Functions.ILike(i.DoctorName!, pattern) ||
                (i.Phone != null && EF.Functions.ILike(i.Phone, pattern)));
        }
        else
        {
            query = query.Where(i =>
                (i.PatientName != null && i.PatientName.Contains(term)) ||
                (i.PatientId != null && i.PatientId.Contains(term)) ||
                (i.DoctorName != null && i.DoctorName.Contains(term)) ||
                (i.Phone != null && i.Phone.Contains(term)));
        }

        return await query
            .OrderByDescending(i => i.InvoiceDate)
            .Take(take)
            .Select(i => new GlobalSearchHit(
                "Invoice / Billing",
                $"#{i.InvoiceNo}",
                i.InvoiceDate,
                i.PatientName ?? "",
                i.DoctorName ?? "",
                i.TotalAmount,
                $"/Invoices?RecordId={i.Id}",
                ""))
            .ToListAsync();
    }

    private async Task<List<GlobalSearchHit>> SearchCashReceiptsAsync(Guid clinicId, string term, int take)
    {
        var pattern = LikePattern(term);
        var query = _db.CashReceipts.AsNoTracking().Where(r => r.ClinicId == clinicId).Apply(_doctorScope.Filter);

        if (int.TryParse(term, out var receiptNo))
            query = query.Where(r => r.ReceiptNo == receiptNo);
        else if (_useILike)
            query = query.Where(r =>
                EF.Functions.ILike(r.PatientName!, pattern) ||
                EF.Functions.ILike(r.PatientId!, pattern) ||
                EF.Functions.ILike(r.DoctorName!, pattern) ||
                (r.Phone != null && EF.Functions.ILike(r.Phone, pattern)) ||
                (r.Description != null && EF.Functions.ILike(r.Description, pattern)));
        else
            query = query.Where(r =>
                (r.PatientName != null && r.PatientName.Contains(term)) ||
                (r.PatientId != null && r.PatientId.Contains(term)) ||
                (r.DoctorName != null && r.DoctorName.Contains(term)) ||
                (r.Phone != null && r.Phone.Contains(term)) ||
                (r.Description != null && r.Description.Contains(term)));

        return await query
            .OrderByDescending(r => r.ReceiptDate)
            .Take(take)
            .Select(r => new GlobalSearchHit(
                "Cash Receipt",
                $"#{r.ReceiptNo}",
                r.ReceiptDate,
                r.PatientName ?? "",
                r.DoctorName ?? "",
                r.Amount,
                $"/CashReceipts?RecordId={r.Id}",
                ""))
            .ToListAsync();
    }

    private async Task<List<GlobalSearchHit>> SearchCashPaymentsAsync(Guid clinicId, string term, int take)
    {
        var pattern = LikePattern(term);
        var query = _db.CashPayments.AsNoTracking().Where(p => p.ClinicId == clinicId).Apply(_doctorScope.Filter);

        if (int.TryParse(term, out var paymentNo))
            query = query.Where(p => p.PaymentNo == paymentNo);
        else if (_useILike)
            query = query.Where(p =>
                EF.Functions.ILike(p.PayeeName!, pattern) ||
                EF.Functions.ILike(p.ChartAccountName!, pattern) ||
                (p.Description != null && EF.Functions.ILike(p.Description, pattern)) ||
                (p.ReferenceNo != null && EF.Functions.ILike(p.ReferenceNo, pattern)));
        else
            query = query.Where(p =>
                (p.PayeeName != null && p.PayeeName.Contains(term)) ||
                (p.ChartAccountName != null && p.ChartAccountName.Contains(term)) ||
                (p.Description != null && p.Description.Contains(term)) ||
                (p.ReferenceNo != null && p.ReferenceNo.Contains(term)));

        return await query
            .OrderByDescending(p => p.PaymentDate)
            .Take(take)
            .Select(p => new GlobalSearchHit(
                "Cash Payment",
                $"#{p.PaymentNo}",
                p.PaymentDate,
                p.PayeeName ?? "",
                "",
                p.Amount,
                $"/CashPayments?RecordId={p.Id}",
                ""))
            .ToListAsync();
    }

    private async Task<List<GlobalSearchHit>> SearchPrescriptionsAsync(Guid clinicId, string term, int take)
    {
        var pattern = LikePattern(term);
        var query = _db.Prescriptions.AsNoTracking().Where(p => p.ClinicId == clinicId).Apply(_doctorScope.Filter);

        if (int.TryParse(term, out var prescriptionNo))
            query = query.Where(p => p.PrescriptionNo == prescriptionNo);
        else if (_useILike)
            query = query.Where(p =>
                EF.Functions.ILike(p.PatientName!, pattern) ||
                EF.Functions.ILike(p.DoctorName!, pattern) ||
                (p.Specialty != null && EF.Functions.ILike(p.Specialty, pattern)) ||
                (p.DiseaseName != null && EF.Functions.ILike(p.DiseaseName, pattern)));
        else
            query = query.Where(p =>
                (p.PatientName != null && p.PatientName.Contains(term)) ||
                (p.DoctorName != null && p.DoctorName.Contains(term)) ||
                (p.Specialty != null && p.Specialty.Contains(term)) ||
                (p.DiseaseName != null && p.DiseaseName.Contains(term)));

        return await query
            .OrderByDescending(p => p.DatePrescription)
            .Take(take)
            .Select(p => new GlobalSearchHit(
                "Doctor's Prescription",
                $"#{p.PrescriptionNo}",
                p.DatePrescription,
                p.PatientName ?? "",
                p.DoctorName ?? "",
                0m,
                $"/Prescriptions?RecordId={p.Id}",
                ""))
            .ToListAsync();
    }

    private async Task<List<GlobalSearchHit>> SearchLabRequestsAsync(Guid clinicId, string term, int take)
    {
        var pattern = LikePattern(term);
        var query = _db.LabRequests.AsNoTracking().Where(r => r.ClinicId == clinicId).Apply(_doctorScope.Filter);

        if (int.TryParse(term, out var requestNo))
            query = query.Where(r => r.RequestNo == requestNo);
        else if (_useILike)
            query = query.Where(r =>
                EF.Functions.ILike(r.PatientName!, pattern) ||
                EF.Functions.ILike(r.PatientBarcode!, pattern) ||
                EF.Functions.ILike(r.DoctorName!, pattern) ||
                (r.Specialty != null && EF.Functions.ILike(r.Specialty, pattern)) ||
                r.Lines.Any(l => EF.Functions.ILike(l.TestName, pattern) || EF.Functions.ILike(l.TestCode, pattern)));
        else
            query = query.Where(r =>
                (r.PatientName != null && r.PatientName.Contains(term)) ||
                (r.PatientBarcode != null && r.PatientBarcode.Contains(term)) ||
                (r.DoctorName != null && r.DoctorName.Contains(term)) ||
                (r.Specialty != null && r.Specialty.Contains(term)) ||
                r.Lines.Any(l =>
                    (l.TestName != null && l.TestName.Contains(term)) ||
                    (l.TestCode != null && l.TestCode.Contains(term))));

        return await query
            .OrderByDescending(r => r.RequestDate)
            .Take(take)
            .Select(r => new GlobalSearchHit(
                "Laboratory Request",
                $"#{r.RequestNo}",
                r.RequestDate,
                r.PatientName ?? "",
                r.DoctorName ?? "",
                r.TotalAmount,
                $"/Laboratory/Request?RecordId={r.Id}",
                ""))
            .ToListAsync();
    }

    private async Task<List<GlobalSearchHit>> SearchLabResultsAsync(Guid clinicId, string term, int take)
    {
        var pattern = LikePattern(term);
        var query = _db.LabResults.AsNoTracking().Where(r => r.ClinicId == clinicId).Apply(_doctorScope.Filter);

        if (int.TryParse(term, out var resultNo))
            query = query.Where(r => r.ResultNo == resultNo);
        else if (_useILike)
            query = query.Where(r =>
                EF.Functions.ILike(r.PatientName!, pattern) ||
                EF.Functions.ILike(r.DoctorName!, pattern) ||
                r.Lines.Any(l =>
                    EF.Functions.ILike(l.TestName, pattern) ||
                    (l.Result != null && EF.Functions.ILike(l.Result, pattern))));
        else
            query = query.Where(r =>
                (r.PatientName != null && r.PatientName.Contains(term)) ||
                (r.DoctorName != null && r.DoctorName.Contains(term)) ||
                r.Lines.Any(l =>
                    (l.TestName != null && l.TestName.Contains(term)) ||
                    (l.Result != null && l.Result.Contains(term))));

        return await query
            .OrderByDescending(r => r.ResultDate)
            .Take(take)
            .Select(r => new GlobalSearchHit(
                "Laboratory Result",
                $"#{r.ResultNo}",
                r.ResultDate,
                r.PatientName ?? "",
                r.DoctorName ?? "",
                0m,
                $"/Laboratory/Result?RecordId={r.Id}",
                ""))
            .ToListAsync();
    }

    private async Task<List<GlobalSearchHit>> SearchPharmacyRequestsAsync(Guid clinicId, string term, int take)
    {
        var pattern = LikePattern(term);
        var query = _db.PharmacyRequests.AsNoTracking().Where(r => r.ClinicId == clinicId).Apply(_doctorScope.Filter);

        if (int.TryParse(term, out var requestNo))
            query = query.Where(r => r.RequestNo == requestNo);
        else if (_useILike)
            query = query.Where(r =>
                EF.Functions.ILike(r.PatientName!, pattern) ||
                EF.Functions.ILike(r.PatientId!, pattern) ||
                EF.Functions.ILike(r.DoctorName!, pattern) ||
                r.Lines.Any(l => EF.Functions.ILike(l.MedicineName, pattern)));
        else
            query = query.Where(r =>
                (r.PatientName != null && r.PatientName.Contains(term)) ||
                (r.PatientId != null && r.PatientId.Contains(term)) ||
                (r.DoctorName != null && r.DoctorName.Contains(term)) ||
                r.Lines.Any(l => l.MedicineName != null && l.MedicineName.Contains(term)));

        return await query
            .OrderByDescending(r => r.RequestDate)
            .Take(take)
            .Select(r => new GlobalSearchHit(
                "Pharmacy Request",
                $"#{r.RequestNo}",
                r.RequestDate,
                r.PatientName ?? "",
                r.DoctorName ?? "",
                r.TotalAmount,
                $"/Pharmacy/Request?RecordId={r.Id}",
                ""))
            .ToListAsync();
    }

    private async Task<List<GlobalSearchHit>> SearchPharmacyBillsAsync(Guid clinicId, string term, int take)
    {
        var pattern = LikePattern(term);
        var query = _db.PharmacyBills.AsNoTracking().Where(r => r.ClinicId == clinicId).Apply(_doctorScope.Filter);

        if (int.TryParse(term, out var billNo))
            query = query.Where(r => r.BillNo == billNo);
        else if (_useILike)
            query = query.Where(r =>
                EF.Functions.ILike(r.PatientName!, pattern) ||
                EF.Functions.ILike(r.PatientId!, pattern) ||
                EF.Functions.ILike(r.DoctorName!, pattern) ||
                r.Lines.Any(l => EF.Functions.ILike(l.MedicineName, pattern)));
        else
            query = query.Where(r =>
                (r.PatientName != null && r.PatientName.Contains(term)) ||
                (r.PatientId != null && r.PatientId.Contains(term)) ||
                (r.DoctorName != null && r.DoctorName.Contains(term)) ||
                r.Lines.Any(l => l.MedicineName != null && l.MedicineName.Contains(term)));

        return await query
            .OrderByDescending(r => r.BillDate)
            .Take(take)
            .Select(r => new GlobalSearchHit(
                "Pharmacy Bill",
                $"#{r.BillNo}",
                r.BillDate,
                r.PatientName ?? "",
                r.DoctorName ?? "",
                r.TotalAmount,
                $"/Pharmacy/Bill?RecordId={r.Id}",
                ""))
            .ToListAsync();
    }

    private async Task<List<GlobalSearchHit>> SearchPharmacyPurchasesAsync(Guid clinicId, string term, int take)
    {
        var pattern = LikePattern(term);
        var query = _db.PharmacyPurchaseBills.AsNoTracking().Where(r => r.ClinicId == clinicId);

        if (int.TryParse(term, out var purchaseNo))
            query = query.Where(r => r.PurchaseNo == purchaseNo);
        else if (_useILike)
            query = query.Where(r =>
                EF.Functions.ILike(r.SupplierName!, pattern) ||
                (r.SupplierInvoiceNo != null && EF.Functions.ILike(r.SupplierInvoiceNo, pattern)) ||
                (r.SupplierPhone != null && EF.Functions.ILike(r.SupplierPhone, pattern)) ||
                r.Lines.Any(l => EF.Functions.ILike(l.MedicineName, pattern)));
        else
            query = query.Where(r =>
                (r.SupplierName != null && r.SupplierName.Contains(term)) ||
                (r.SupplierInvoiceNo != null && r.SupplierInvoiceNo.Contains(term)) ||
                (r.SupplierPhone != null && r.SupplierPhone.Contains(term)) ||
                r.Lines.Any(l => l.MedicineName != null && l.MedicineName.Contains(term)));

        return await query
            .OrderByDescending(r => r.PurchaseDate)
            .Take(take)
            .Select(r => new GlobalSearchHit(
                "Pharmacy Purchase Bill",
                $"#{r.PurchaseNo}",
                r.PurchaseDate,
                r.SupplierName ?? "",
                "",
                r.NetAmount,
                $"/Pharmacy/Purchase?RecordId={r.Id}",
                ""))
            .ToListAsync();
    }

    private async Task<List<GlobalSearchHit>> SearchPharmacyOpeningBalancesAsync(Guid clinicId, string term, int take)
    {
        var pattern = LikePattern(term);
        var query = _db.PharmacyOpeningBalances.AsNoTracking().Where(r => r.ClinicId == clinicId);

        if (int.TryParse(term, out var balanceNo))
            query = query.Where(r => r.BalanceNo == balanceNo);
        else if (_useILike)
            query = query.Where(r =>
                (r.Notes != null && EF.Functions.ILike(r.Notes, pattern)) ||
                r.Lines.Any(l =>
                    EF.Functions.ILike(l.MedicineName, pattern) ||
                    (l.Barcode != null && EF.Functions.ILike(l.Barcode, pattern))));
        else
            query = query.Where(r =>
                (r.Notes != null && r.Notes.Contains(term)) ||
                r.Lines.Any(l =>
                    (l.MedicineName != null && l.MedicineName.Contains(term)) ||
                    (l.Barcode != null && l.Barcode.Contains(term))));

        return await query
            .OrderByDescending(r => r.BalanceDate)
            .Take(take)
            .Select(r => new GlobalSearchHit(
                "Pharmacy Opening Balance",
                $"#{r.BalanceNo}",
                r.BalanceDate,
                "",
                "",
                r.Lines.Sum(l => l.Total),
                $"/Pharmacy/OpeningBalance?RecordId={r.Id}",
                ""))
            .ToListAsync();
    }

    private async Task<List<GlobalSearchHit>> SearchRadiologyRequestsAsync(Guid clinicId, string term, int take)
    {
        var pattern = LikePattern(term);
        var query = _db.RadiologyRequests.AsNoTracking().Where(r => r.ClinicId == clinicId).Apply(_doctorScope.Filter);

        if (int.TryParse(term, out var requestNo))
            query = query.Where(r => r.RequestNo == requestNo);
        else if (_useILike)
            query = query.Where(r =>
                EF.Functions.ILike(r.PatientName!, pattern) ||
                EF.Functions.ILike(r.PatientBarcode!, pattern) ||
                EF.Functions.ILike(r.DoctorName!, pattern) ||
                r.Lines.Any(l => EF.Functions.ILike(l.TestName, pattern)));
        else
            query = query.Where(r =>
                (r.PatientName != null && r.PatientName.Contains(term)) ||
                (r.PatientBarcode != null && r.PatientBarcode.Contains(term)) ||
                (r.DoctorName != null && r.DoctorName.Contains(term)) ||
                r.Lines.Any(l => l.TestName != null && l.TestName.Contains(term)));

        return await query
            .OrderByDescending(r => r.RequestDate)
            .Take(take)
            .Select(r => new GlobalSearchHit(
                "Radiology Request",
                $"#{r.RequestNo}",
                r.RequestDate,
                r.PatientName ?? "",
                r.DoctorName ?? "",
                r.TotalAmount,
                $"/Radiology/Request?RecordId={r.Id}",
                ""))
            .ToListAsync();
    }

    private async Task<List<GlobalSearchHit>> SearchRadiologyResultsAsync(Guid clinicId, string term, int take)
    {
        var pattern = LikePattern(term);
        var query = _db.RadiologyResults.AsNoTracking().Where(r => r.ClinicId == clinicId).Apply(_doctorScope.Filter);

        if (int.TryParse(term, out var resultNo))
            query = query.Where(r => r.ResultNo == resultNo);
        else if (_useILike)
            query = query.Where(r =>
                EF.Functions.ILike(r.PatientName!, pattern) ||
                EF.Functions.ILike(r.DoctorName!, pattern) ||
                r.Lines.Any(l =>
                    EF.Functions.ILike(l.TestName, pattern) ||
                    (l.Result != null && EF.Functions.ILike(l.Result, pattern))));
        else
            query = query.Where(r =>
                (r.PatientName != null && r.PatientName.Contains(term)) ||
                (r.DoctorName != null && r.DoctorName.Contains(term)) ||
                r.Lines.Any(l =>
                    (l.TestName != null && l.TestName.Contains(term)) ||
                    (l.Result != null && l.Result.Contains(term))));

        return await query
            .OrderByDescending(r => r.ResultDate)
            .Take(take)
            .Select(r => new GlobalSearchHit(
                "Radiology Result",
                $"#{r.ResultNo}",
                r.ResultDate,
                r.PatientName ?? "",
                r.DoctorName ?? "",
                0m,
                $"/Radiology/Result?RecordId={r.Id}",
                ""))
            .ToListAsync();
    }

    private static string LikePattern(string term) => $"%{term}%";

    private IQueryable<Core.Entities.Invoice> ApplyQuickTermToInvoices(
        IQueryable<Core.Entities.Invoice> query, string? term)
    {
        if (string.IsNullOrWhiteSpace(term)) return query;
        var t = term.Trim();
        var pattern = LikePattern(t);
        if (int.TryParse(t, out var invoiceNo))
        {
            return _useILike
                ? query.Where(i => i.InvoiceNo == invoiceNo ||
                    EF.Functions.ILike(i.PatientName!, pattern) ||
                    EF.Functions.ILike(i.PatientId!, pattern) ||
                    EF.Functions.ILike(i.DoctorName!, pattern) ||
                    (i.Phone != null && EF.Functions.ILike(i.Phone, pattern)))
                : query.Where(i => i.InvoiceNo == invoiceNo ||
                    (i.PatientName != null && i.PatientName.Contains(t)) ||
                    (i.PatientId != null && i.PatientId.Contains(t)) ||
                    (i.DoctorName != null && i.DoctorName.Contains(t)) ||
                    (i.Phone != null && i.Phone.Contains(t)));
        }

        return _useILike
            ? query.Where(i =>
                EF.Functions.ILike(i.PatientName!, pattern) ||
                EF.Functions.ILike(i.PatientId!, pattern) ||
                EF.Functions.ILike(i.DoctorName!, pattern) ||
                (i.Phone != null && EF.Functions.ILike(i.Phone, pattern)))
            : query.Where(i =>
                (i.PatientName != null && i.PatientName.Contains(t)) ||
                (i.PatientId != null && i.PatientId.Contains(t)) ||
                (i.DoctorName != null && i.DoctorName.Contains(t)) ||
                (i.Phone != null && i.Phone.Contains(t)));
    }

    private IQueryable<Core.Entities.CashReceipt> ApplyQuickTermToCashReceipts(
        IQueryable<Core.Entities.CashReceipt> query, string? term)
    {
        if (string.IsNullOrWhiteSpace(term)) return query;
        var t = term.Trim();
        var pattern = LikePattern(t);
        if (int.TryParse(t, out var receiptNo))
            return query.Where(r => r.ReceiptNo == receiptNo);
        return _useILike
            ? query.Where(r =>
                EF.Functions.ILike(r.PatientName!, pattern) ||
                EF.Functions.ILike(r.PatientId!, pattern) ||
                EF.Functions.ILike(r.DoctorName!, pattern) ||
                (r.Phone != null && EF.Functions.ILike(r.Phone, pattern)) ||
                (r.Description != null && EF.Functions.ILike(r.Description, pattern)))
            : query.Where(r =>
                (r.PatientName != null && r.PatientName.Contains(t)) ||
                (r.PatientId != null && r.PatientId.Contains(t)) ||
                (r.DoctorName != null && r.DoctorName.Contains(t)) ||
                (r.Phone != null && r.Phone.Contains(t)) ||
                (r.Description != null && r.Description.Contains(t)));
    }

    private IQueryable<Core.Entities.CashPayment> ApplyQuickTermToCashPayments(
        IQueryable<Core.Entities.CashPayment> query, string? term)
    {
        if (string.IsNullOrWhiteSpace(term)) return query;
        var t = term.Trim();
        var pattern = LikePattern(t);
        if (int.TryParse(t, out var paymentNo))
            return query.Where(p => p.PaymentNo == paymentNo);
        return _useILike
            ? query.Where(p =>
                EF.Functions.ILike(p.PayeeName!, pattern) ||
                EF.Functions.ILike(p.ChartAccountName!, pattern) ||
                (p.Description != null && EF.Functions.ILike(p.Description, pattern)) ||
                (p.ReferenceNo != null && EF.Functions.ILike(p.ReferenceNo, pattern)))
            : query.Where(p =>
                (p.PayeeName != null && p.PayeeName.Contains(t)) ||
                (p.ChartAccountName != null && p.ChartAccountName.Contains(t)) ||
                (p.Description != null && p.Description.Contains(t)) ||
                (p.ReferenceNo != null && p.ReferenceNo.Contains(t)));
    }

    private IQueryable<Core.Entities.Prescription> ApplyQuickTermToPrescriptions(
        IQueryable<Core.Entities.Prescription> query, string? term)
    {
        if (string.IsNullOrWhiteSpace(term)) return query;
        var t = term.Trim();
        var pattern = LikePattern(t);
        if (int.TryParse(t, out var prescriptionNo))
            return query.Where(p => p.PrescriptionNo == prescriptionNo);
        return _useILike
            ? query.Where(p =>
                EF.Functions.ILike(p.PatientName!, pattern) ||
                EF.Functions.ILike(p.DoctorName!, pattern) ||
                (p.Specialty != null && EF.Functions.ILike(p.Specialty, pattern)) ||
                (p.DiseaseName != null && EF.Functions.ILike(p.DiseaseName, pattern)))
            : query.Where(p =>
                (p.PatientName != null && p.PatientName.Contains(t)) ||
                (p.DoctorName != null && p.DoctorName.Contains(t)) ||
                (p.Specialty != null && p.Specialty.Contains(t)) ||
                (p.DiseaseName != null && p.DiseaseName.Contains(t)));
    }

    private IQueryable<Core.Entities.LabRequest> ApplyQuickTermToLabRequests(
        IQueryable<Core.Entities.LabRequest> query, string? term)
    {
        if (string.IsNullOrWhiteSpace(term)) return query;
        var t = term.Trim();
        var pattern = LikePattern(t);
        if (int.TryParse(t, out var requestNo))
            return query.Where(r => r.RequestNo == requestNo);
        return _useILike
            ? query.Where(r =>
                EF.Functions.ILike(r.PatientName!, pattern) ||
                EF.Functions.ILike(r.PatientBarcode!, pattern) ||
                EF.Functions.ILike(r.DoctorName!, pattern) ||
                (r.Specialty != null && EF.Functions.ILike(r.Specialty, pattern)) ||
                r.Lines.Any(l => EF.Functions.ILike(l.TestName, pattern) || EF.Functions.ILike(l.TestCode, pattern)))
            : query.Where(r =>
                (r.PatientName != null && r.PatientName.Contains(t)) ||
                (r.PatientBarcode != null && r.PatientBarcode.Contains(t)) ||
                (r.DoctorName != null && r.DoctorName.Contains(t)) ||
                (r.Specialty != null && r.Specialty.Contains(t)) ||
                r.Lines.Any(l =>
                    (l.TestName != null && l.TestName.Contains(t)) ||
                    (l.TestCode != null && l.TestCode.Contains(t))));
    }

    private IQueryable<Core.Entities.LabResult> ApplyQuickTermToLabResults(
        IQueryable<Core.Entities.LabResult> query, string? term)
    {
        if (string.IsNullOrWhiteSpace(term)) return query;
        var t = term.Trim();
        var pattern = LikePattern(t);
        if (int.TryParse(t, out var resultNo))
            return query.Where(r => r.ResultNo == resultNo);
        return _useILike
            ? query.Where(r =>
                EF.Functions.ILike(r.PatientName!, pattern) ||
                EF.Functions.ILike(r.DoctorName!, pattern) ||
                r.Lines.Any(l =>
                    EF.Functions.ILike(l.TestName, pattern) ||
                    (l.Result != null && EF.Functions.ILike(l.Result, pattern))))
            : query.Where(r =>
                (r.PatientName != null && r.PatientName.Contains(t)) ||
                (r.DoctorName != null && r.DoctorName.Contains(t)) ||
                r.Lines.Any(l =>
                    (l.TestName != null && l.TestName.Contains(t)) ||
                    (l.Result != null && l.Result.Contains(t))));
    }

    private IQueryable<Core.Entities.PharmacyRequest> ApplyQuickTermToPharmacyRequests(
        IQueryable<Core.Entities.PharmacyRequest> query, string? term)
    {
        if (string.IsNullOrWhiteSpace(term)) return query;
        var t = term.Trim();
        var pattern = LikePattern(t);
        if (int.TryParse(t, out var requestNo))
            return query.Where(r => r.RequestNo == requestNo);
        return _useILike
            ? query.Where(r =>
                EF.Functions.ILike(r.PatientName!, pattern) ||
                EF.Functions.ILike(r.PatientId!, pattern) ||
                EF.Functions.ILike(r.DoctorName!, pattern) ||
                r.Lines.Any(l => EF.Functions.ILike(l.MedicineName, pattern)))
            : query.Where(r =>
                (r.PatientName != null && r.PatientName.Contains(t)) ||
                (r.PatientId != null && r.PatientId.Contains(t)) ||
                (r.DoctorName != null && r.DoctorName.Contains(t)) ||
                r.Lines.Any(l => l.MedicineName != null && l.MedicineName.Contains(t)));
    }

    private IQueryable<Core.Entities.PharmacyBill> ApplyQuickTermToPharmacyBills(
        IQueryable<Core.Entities.PharmacyBill> query, string? term)
    {
        if (string.IsNullOrWhiteSpace(term)) return query;
        var t = term.Trim();
        var pattern = LikePattern(t);
        if (int.TryParse(t, out var billNo))
            return query.Where(r => r.BillNo == billNo);
        return _useILike
            ? query.Where(r =>
                EF.Functions.ILike(r.PatientName!, pattern) ||
                EF.Functions.ILike(r.PatientId!, pattern) ||
                EF.Functions.ILike(r.DoctorName!, pattern) ||
                r.Lines.Any(l => EF.Functions.ILike(l.MedicineName, pattern)))
            : query.Where(r =>
                (r.PatientName != null && r.PatientName.Contains(t)) ||
                (r.PatientId != null && r.PatientId.Contains(t)) ||
                (r.DoctorName != null && r.DoctorName.Contains(t)) ||
                r.Lines.Any(l => l.MedicineName != null && l.MedicineName.Contains(t)));
    }

    private IQueryable<Core.Entities.PharmacyPurchaseBill> ApplyQuickTermToPharmacyPurchases(
        IQueryable<Core.Entities.PharmacyPurchaseBill> query, string? term)
    {
        if (string.IsNullOrWhiteSpace(term)) return query;
        var t = term.Trim();
        var pattern = LikePattern(t);
        if (int.TryParse(t, out var purchaseNo))
            return query.Where(r => r.PurchaseNo == purchaseNo);
        return _useILike
            ? query.Where(r =>
                EF.Functions.ILike(r.SupplierName!, pattern) ||
                (r.SupplierInvoiceNo != null && EF.Functions.ILike(r.SupplierInvoiceNo, pattern)) ||
                (r.SupplierPhone != null && EF.Functions.ILike(r.SupplierPhone, pattern)) ||
                r.Lines.Any(l => EF.Functions.ILike(l.MedicineName, pattern)))
            : query.Where(r =>
                (r.SupplierName != null && r.SupplierName.Contains(t)) ||
                (r.SupplierInvoiceNo != null && r.SupplierInvoiceNo.Contains(t)) ||
                (r.SupplierPhone != null && r.SupplierPhone.Contains(t)) ||
                r.Lines.Any(l => l.MedicineName != null && l.MedicineName.Contains(t)));
    }

    private IQueryable<Core.Entities.PharmacyOpeningBalance> ApplyQuickTermToPharmacyOpeningBalances(
        IQueryable<Core.Entities.PharmacyOpeningBalance> query, string? term)
    {
        if (string.IsNullOrWhiteSpace(term)) return query;
        var t = term.Trim();
        var pattern = LikePattern(t);
        if (int.TryParse(t, out var balanceNo))
            return query.Where(r => r.BalanceNo == balanceNo);
        return _useILike
            ? query.Where(r =>
                (r.Notes != null && EF.Functions.ILike(r.Notes, pattern)) ||
                r.Lines.Any(l =>
                    EF.Functions.ILike(l.MedicineName, pattern) ||
                    (l.Barcode != null && EF.Functions.ILike(l.Barcode, pattern))))
            : query.Where(r =>
                (r.Notes != null && r.Notes.Contains(t)) ||
                r.Lines.Any(l =>
                    (l.MedicineName != null && l.MedicineName.Contains(t)) ||
                    (l.Barcode != null && l.Barcode.Contains(t))));
    }

    private IQueryable<Core.Entities.RadiologyRequest> ApplyQuickTermToRadiologyRequests(
        IQueryable<Core.Entities.RadiologyRequest> query, string? term)
    {
        if (string.IsNullOrWhiteSpace(term)) return query;
        var t = term.Trim();
        var pattern = LikePattern(t);
        if (int.TryParse(t, out var requestNo))
            return query.Where(r => r.RequestNo == requestNo);
        return _useILike
            ? query.Where(r =>
                EF.Functions.ILike(r.PatientName!, pattern) ||
                EF.Functions.ILike(r.PatientBarcode!, pattern) ||
                EF.Functions.ILike(r.DoctorName!, pattern) ||
                r.Lines.Any(l => EF.Functions.ILike(l.TestName, pattern)))
            : query.Where(r =>
                (r.PatientName != null && r.PatientName.Contains(t)) ||
                (r.PatientBarcode != null && r.PatientBarcode.Contains(t)) ||
                (r.DoctorName != null && r.DoctorName.Contains(t)) ||
                r.Lines.Any(l => l.TestName != null && l.TestName.Contains(t)));
    }

    private IQueryable<Core.Entities.RadiologyResult> ApplyQuickTermToRadiologyResults(
        IQueryable<Core.Entities.RadiologyResult> query, string? term)
    {
        if (string.IsNullOrWhiteSpace(term)) return query;
        var t = term.Trim();
        var pattern = LikePattern(t);
        if (int.TryParse(t, out var resultNo))
            return query.Where(r => r.ResultNo == resultNo);
        return _useILike
            ? query.Where(r =>
                EF.Functions.ILike(r.PatientName!, pattern) ||
                EF.Functions.ILike(r.DoctorName!, pattern) ||
                r.Lines.Any(l =>
                    EF.Functions.ILike(l.TestName, pattern) ||
                    (l.Result != null && EF.Functions.ILike(l.Result, pattern))))
            : query.Where(r =>
                (r.PatientName != null && r.PatientName.Contains(t)) ||
                (r.DoctorName != null && r.DoctorName.Contains(t)) ||
                r.Lines.Any(l =>
                    (l.TestName != null && l.TestName.Contains(t)) ||
                    (l.Result != null && l.Result.Contains(t))));
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

    public sealed record GlobalSearchHit(
        string TransactionType,
        string Reference,
        DateTime TransactionDate,
        string PatientOrParty,
        string DoctorName,
        decimal Amount,
        string Link,
        string Details);
}
