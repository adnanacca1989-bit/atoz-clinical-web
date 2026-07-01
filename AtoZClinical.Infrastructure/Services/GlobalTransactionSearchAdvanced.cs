using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public static class GlobalSearchTypes
{
    public const string All = "All";
    public const string Invoice = "Invoice / Billing";
    public const string CashReceipt = "Cash Receipt";
    public const string CashPayment = "Cash Payment";
    public const string Prescription = "Doctor's Prescription";
    public const string LabRequest = "Laboratory Request";
    public const string LabResult = "Laboratory Result";
    public const string PharmacyRequest = "Pharmacy Request";
    public const string PharmacyBill = "Pharmacy Bill";
    public const string PharmacyPurchase = "Pharmacy Purchase Bill";
    public const string PharmacyOpening = "Pharmacy Opening Balance";
    public const string RadiologyRequest = "Radiology Request";
    public const string RadiologyResult = "Radiology Result";

    public static readonly string[] AllOptions =
    [
        All, Invoice, CashReceipt, CashPayment, Prescription,
        LabRequest, LabResult, PharmacyRequest, PharmacyBill,
        PharmacyPurchase, PharmacyOpening, RadiologyRequest, RadiologyResult
    ];
}

public sealed class GlobalSearchCriteria
{
    public DateTime FromDate { get; set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    public DateTime ToDate { get; set; } = DateTime.Today;
    public string TransactionType { get; set; } = GlobalSearchTypes.All;
    public string? PatientName { get; set; }
    public string? DoctorName { get; set; }
    public decimal? Amount { get; set; }
    public bool UseDateOfBirth { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? QuickTerm { get; set; }
}

public sealed partial class GlobalTransactionSearchService
{
    public async Task<List<GlobalSearchHit>> SearchAdvancedAsync(
        Guid clinicId,
        GlobalSearchCriteria criteria,
        int limit = 200)
    {
        if (criteria.FromDate > criteria.ToDate)
            (criteria.FromDate, criteria.ToDate) = (criteria.ToDate, criteria.FromDate);

        var types = ResolveTransactionTypes(criteria.TransactionType);
        var perType = Math.Max(10, limit / Math.Max(1, types.Count));
        var hits = new List<GlobalSearchHit>();

        if (types.Contains(GlobalSearchTypes.Invoice))
            hits.AddRange(await SafeSearchAsync(() => SearchInvoicesAdvancedAsync(clinicId, criteria, perType)));
        if (types.Contains(GlobalSearchTypes.CashReceipt))
            hits.AddRange(await SafeSearchAsync(() => SearchCashReceiptsAdvancedAsync(clinicId, criteria, perType)));
        if (types.Contains(GlobalSearchTypes.CashPayment))
            hits.AddRange(await SafeSearchAsync(() => SearchCashPaymentsAdvancedAsync(clinicId, criteria, perType)));
        if (types.Contains(GlobalSearchTypes.Prescription))
            hits.AddRange(await SafeSearchAsync(() => SearchPrescriptionsAdvancedAsync(clinicId, criteria, perType)));
        if (types.Contains(GlobalSearchTypes.LabRequest))
            hits.AddRange(await SafeSearchAsync(() => SearchLabRequestsAdvancedAsync(clinicId, criteria, perType)));
        if (types.Contains(GlobalSearchTypes.LabResult))
            hits.AddRange(await SafeSearchAsync(() => SearchLabResultsAdvancedAsync(clinicId, criteria, perType)));
        if (types.Contains(GlobalSearchTypes.PharmacyRequest))
            hits.AddRange(await SafeSearchAsync(() => SearchPharmacyRequestsAdvancedAsync(clinicId, criteria, perType)));
        if (types.Contains(GlobalSearchTypes.PharmacyBill))
            hits.AddRange(await SafeSearchAsync(() => SearchPharmacyBillsAdvancedAsync(clinicId, criteria, perType)));
        if (types.Contains(GlobalSearchTypes.PharmacyPurchase))
            hits.AddRange(await SafeSearchAsync(() => SearchPharmacyPurchasesAdvancedAsync(clinicId, criteria, perType)));
        if (types.Contains(GlobalSearchTypes.PharmacyOpening))
            hits.AddRange(await SafeSearchAsync(() => SearchPharmacyOpeningBalancesAdvancedAsync(clinicId, criteria, perType)));
        if (types.Contains(GlobalSearchTypes.RadiologyRequest))
            hits.AddRange(await SafeSearchAsync(() => SearchRadiologyRequestsAdvancedAsync(clinicId, criteria, perType)));
        if (types.Contains(GlobalSearchTypes.RadiologyResult))
            hits.AddRange(await SafeSearchAsync(() => SearchRadiologyResultsAdvancedAsync(clinicId, criteria, perType)));

        if (criteria.UseDateOfBirth && criteria.DateOfBirth.HasValue)
            hits = await FilterByPatientDobAsync(clinicId, hits, criteria.DateOfBirth.Value);

        return hits
            .OrderByDescending(h => h.TransactionDate)
            .ThenByDescending(h => h.Reference)
            .Take(limit)
            .ToList();
    }

    private static HashSet<string> ResolveTransactionTypes(string? transactionType)
    {
        if (string.IsNullOrWhiteSpace(transactionType) ||
            string.Equals(transactionType, GlobalSearchTypes.All, StringComparison.OrdinalIgnoreCase))
        {
            return GlobalSearchTypes.AllOptions
                .Where(t => t != GlobalSearchTypes.All)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        return [transactionType.Trim()];
    }

    private async Task<List<GlobalSearchHit>> FilterByPatientDobAsync(
        Guid clinicId,
        List<GlobalSearchHit> hits,
        DateTime dateOfBirth)
    {
        var dob = dateOfBirth.Date;
        var patientNames = await _db.Patients.ForClinic(clinicId).AsNoTracking()
            .Where(p => p.DateOfBirth != null && p.DateOfBirth.Value.Date == dob)
            .Select(p => p.FullName)
            .Where(n => n != null)
            .ToListAsync();

        var nameSet = patientNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return hits.Where(h => nameSet.Contains(h.PatientOrParty)).ToList();
    }

    private async Task<List<GlobalSearchHit>> SearchInvoicesAdvancedAsync(
        Guid clinicId, GlobalSearchCriteria c, int take)
    {
        var from = c.FromDate.Date;
        var to = c.ToDate.Date;
        var query = _db.Invoices.AsNoTracking()
            .Where(i => i.ClinicId == clinicId)
            .Where(i => i.InvoiceDate.Date >= from && i.InvoiceDate.Date <= to)
            .Apply(_doctorScope.Filter);

        if (!string.IsNullOrWhiteSpace(c.PatientName))
        {
            var patient = c.PatientName.Trim();
            var pattern = LikePattern(patient);
            query = _useILike
                ? query.Where(i => EF.Functions.ILike(i.PatientName!, pattern) || EF.Functions.ILike(i.PatientId!, pattern))
                : query.Where(i =>
                    (i.PatientName != null && i.PatientName.Contains(patient)) ||
                    (i.PatientId != null && i.PatientId.Contains(patient)));
        }

        if (!string.IsNullOrWhiteSpace(c.DoctorName))
        {
            var doctor = c.DoctorName.Trim();
            var pattern = LikePattern(doctor);
            query = _useILike
                ? query.Where(i => EF.Functions.ILike(i.DoctorName!, pattern))
                : query.Where(i => i.DoctorName != null && i.DoctorName.Contains(doctor));
        }

        if (c.Amount is > 0) query = query.Where(i => i.TotalAmount == c.Amount.Value);
        query = ApplyQuickTermToInvoices(query, c.QuickTerm);

        return await query.OrderByDescending(i => i.InvoiceDate).Take(take)
            .Select(i => new GlobalSearchHit(
                GlobalSearchTypes.Invoice,
                $"#{i.InvoiceNo}",
                i.InvoiceDate,
                i.PatientName ?? "",
                i.DoctorName ?? "",
                i.TotalAmount,
                $"/Invoices?RecordId={i.Id}",
                i.Lines.OrderBy(l => l.LineNo).Select(l => l.ServiceName).FirstOrDefault() ?? ""))
            .ToListAsync();
    }

    private async Task<List<GlobalSearchHit>> SearchCashReceiptsAdvancedAsync(
        Guid clinicId, GlobalSearchCriteria c, int take)
    {
        var from = c.FromDate.Date;
        var to = c.ToDate.Date;
        var query = _db.CashReceipts.AsNoTracking()
            .Where(r => r.ClinicId == clinicId)
            .Where(r => r.ReceiptDate.Date >= from && r.ReceiptDate.Date <= to)
            .Apply(_doctorScope.Filter);

        if (!string.IsNullOrWhiteSpace(c.PatientName))
        {
            var patient = c.PatientName.Trim();
            var pattern = LikePattern(patient);
            query = _useILike
                ? query.Where(r => EF.Functions.ILike(r.PatientName!, pattern) || EF.Functions.ILike(r.PatientId!, pattern))
                : query.Where(r =>
                    (r.PatientName != null && r.PatientName.Contains(patient)) ||
                    (r.PatientId != null && r.PatientId.Contains(patient)));
        }

        if (!string.IsNullOrWhiteSpace(c.DoctorName))
        {
            var doctor = c.DoctorName.Trim();
            var pattern = LikePattern(doctor);
            query = _useILike
                ? query.Where(r => EF.Functions.ILike(r.DoctorName!, pattern))
                : query.Where(r => r.DoctorName != null && r.DoctorName.Contains(doctor));
        }

        if (c.Amount is > 0) query = query.Where(r => r.Amount == c.Amount.Value);
        query = ApplyQuickTermToCashReceipts(query, c.QuickTerm);

        return await query.OrderByDescending(r => r.ReceiptDate).Take(take)
            .Select(r => new GlobalSearchHit(
                GlobalSearchTypes.CashReceipt,
                $"#{r.ReceiptNo}",
                r.ReceiptDate,
                r.PatientName ?? "",
                r.DoctorName ?? "",
                r.Amount,
                $"/CashReceipts?RecordId={r.Id}",
                r.Description ?? ""))
            .ToListAsync();
    }

    private async Task<List<GlobalSearchHit>> SearchCashPaymentsAdvancedAsync(
        Guid clinicId, GlobalSearchCriteria c, int take)
    {
        var from = c.FromDate.Date;
        var to = c.ToDate.Date;
        var query = _db.CashPayments.AsNoTracking()
            .Where(p => p.ClinicId == clinicId)
            .Where(p => p.PaymentDate.Date >= from && p.PaymentDate.Date <= to)
            .Apply(_doctorScope.Filter);

        if (!string.IsNullOrWhiteSpace(c.PatientName))
        {
            var patient = c.PatientName.Trim();
            var pattern = LikePattern(patient);
            query = _useILike
                ? query.Where(p => EF.Functions.ILike(p.PayeeName!, pattern))
                : query.Where(p => p.PayeeName != null && p.PayeeName.Contains(patient));
        }

        if (c.Amount is > 0) query = query.Where(p => p.Amount == c.Amount.Value);
        query = ApplyQuickTermToCashPayments(query, c.QuickTerm);

        return await query.OrderByDescending(p => p.PaymentDate).Take(take)
            .Select(p => new GlobalSearchHit(
                GlobalSearchTypes.CashPayment,
                $"#{p.PaymentNo}",
                p.PaymentDate,
                p.PayeeName ?? "",
                "",
                p.Amount,
                $"/CashPayments?RecordId={p.Id}",
                p.Description ?? ""))
            .ToListAsync();
    }

    private async Task<List<GlobalSearchHit>> SearchPrescriptionsAdvancedAsync(
        Guid clinicId, GlobalSearchCriteria c, int take)
    {
        var from = c.FromDate.Date;
        var to = c.ToDate.Date;
        var query = _db.Prescriptions.AsNoTracking()
            .Where(p => p.ClinicId == clinicId)
            .Where(p => p.DatePrescription.Date >= from && p.DatePrescription.Date <= to)
            .Apply(_doctorScope.Filter);

        if (!string.IsNullOrWhiteSpace(c.PatientName))
        {
            var patient = c.PatientName.Trim();
            var pattern = LikePattern(patient);
            query = _useILike
                ? query.Where(p => EF.Functions.ILike(p.PatientName!, pattern))
                : query.Where(p => p.PatientName != null && p.PatientName.Contains(patient));
        }

        if (!string.IsNullOrWhiteSpace(c.DoctorName))
        {
            var doctor = c.DoctorName.Trim();
            var pattern = LikePattern(doctor);
            query = _useILike
                ? query.Where(p => EF.Functions.ILike(p.DoctorName!, pattern))
                : query.Where(p => p.DoctorName != null && p.DoctorName.Contains(doctor));
        }

        query = ApplyQuickTermToPrescriptions(query, c.QuickTerm);

        return await query.OrderByDescending(p => p.DatePrescription).Take(take)
            .Select(p => new GlobalSearchHit(
                GlobalSearchTypes.Prescription,
                $"#{p.PrescriptionNo}",
                p.DatePrescription,
                p.PatientName ?? "",
                p.DoctorName ?? "",
                0m,
                $"/Prescriptions?RecordId={p.Id}",
                p.DiseaseName ?? ""))
            .ToListAsync();
    }

    private async Task<List<GlobalSearchHit>> SearchLabRequestsAdvancedAsync(
        Guid clinicId, GlobalSearchCriteria c, int take)
    {
        var from = c.FromDate.Date;
        var to = c.ToDate.Date;
        var query = _db.LabRequests.AsNoTracking()
            .Where(r => r.ClinicId == clinicId)
            .Where(r => r.RequestDate.Date >= from && r.RequestDate.Date <= to)
            .Apply(_doctorScope.Filter);

        if (!string.IsNullOrWhiteSpace(c.PatientName))
        {
            var patient = c.PatientName.Trim();
            var pattern = LikePattern(patient);
            query = _useILike
                ? query.Where(r => EF.Functions.ILike(r.PatientName!, pattern) || EF.Functions.ILike(r.PatientBarcode!, pattern))
                : query.Where(r =>
                    (r.PatientName != null && r.PatientName.Contains(patient)) ||
                    (r.PatientBarcode != null && r.PatientBarcode.Contains(patient)));
        }

        if (!string.IsNullOrWhiteSpace(c.DoctorName))
        {
            var doctor = c.DoctorName.Trim();
            var pattern = LikePattern(doctor);
            query = _useILike
                ? query.Where(r => EF.Functions.ILike(r.DoctorName!, pattern))
                : query.Where(r => r.DoctorName != null && r.DoctorName.Contains(doctor));
        }

        if (c.Amount is > 0) query = query.Where(r => r.TotalAmount == c.Amount.Value);
        query = ApplyQuickTermToLabRequests(query, c.QuickTerm);

        return await query.OrderByDescending(r => r.RequestDate).Take(take)
            .Select(r => new GlobalSearchHit(
                GlobalSearchTypes.LabRequest,
                $"#{r.RequestNo}",
                r.RequestDate,
                r.PatientName ?? "",
                r.DoctorName ?? "",
                r.TotalAmount,
                $"/Laboratory/Request?RecordId={r.Id}",
                r.Lines.OrderBy(l => l.LineNo).Select(l => l.TestName).FirstOrDefault() ?? ""))
            .ToListAsync();
    }

    private async Task<List<GlobalSearchHit>> SearchLabResultsAdvancedAsync(
        Guid clinicId, GlobalSearchCriteria c, int take)
    {
        var from = c.FromDate.Date;
        var to = c.ToDate.Date;
        var query = _db.LabResults.AsNoTracking()
            .Where(r => r.ClinicId == clinicId)
            .Where(r => r.ResultDate.Date >= from && r.ResultDate.Date <= to)
            .Apply(_doctorScope.Filter);

        if (!string.IsNullOrWhiteSpace(c.PatientName))
        {
            var patient = c.PatientName.Trim();
            var pattern = LikePattern(patient);
            query = _useILike
                ? query.Where(r => EF.Functions.ILike(r.PatientName!, pattern))
                : query.Where(r => r.PatientName != null && r.PatientName.Contains(patient));
        }

        if (!string.IsNullOrWhiteSpace(c.DoctorName))
        {
            var doctor = c.DoctorName.Trim();
            var pattern = LikePattern(doctor);
            query = _useILike
                ? query.Where(r => EF.Functions.ILike(r.DoctorName!, pattern))
                : query.Where(r => r.DoctorName != null && r.DoctorName.Contains(doctor));
        }

        query = ApplyQuickTermToLabResults(query, c.QuickTerm);

        return await query.OrderByDescending(r => r.ResultDate).Take(take)
            .Select(r => new GlobalSearchHit(
                GlobalSearchTypes.LabResult,
                $"#{r.ResultNo}",
                r.ResultDate,
                r.PatientName ?? "",
                r.DoctorName ?? "",
                0m,
                $"/Laboratory/Result?RecordId={r.Id}",
                r.Lines.OrderBy(l => l.LineNo).Select(l => l.TestName).FirstOrDefault() ?? ""))
            .ToListAsync();
    }

    private async Task<List<GlobalSearchHit>> SearchPharmacyRequestsAdvancedAsync(
        Guid clinicId, GlobalSearchCriteria c, int take)
    {
        var from = c.FromDate.Date;
        var to = c.ToDate.Date;
        var query = _db.PharmacyRequests.AsNoTracking()
            .Where(r => r.ClinicId == clinicId)
            .Where(r => r.RequestDate.Date >= from && r.RequestDate.Date <= to)
            .Apply(_doctorScope.Filter);

        if (!string.IsNullOrWhiteSpace(c.PatientName))
        {
            var patient = c.PatientName.Trim();
            var pattern = LikePattern(patient);
            query = _useILike
                ? query.Where(r => EF.Functions.ILike(r.PatientName!, pattern) || EF.Functions.ILike(r.PatientId!, pattern))
                : query.Where(r =>
                    (r.PatientName != null && r.PatientName.Contains(patient)) ||
                    (r.PatientId != null && r.PatientId.Contains(patient)));
        }

        if (!string.IsNullOrWhiteSpace(c.DoctorName))
        {
            var doctor = c.DoctorName.Trim();
            var pattern = LikePattern(doctor);
            query = _useILike
                ? query.Where(r => EF.Functions.ILike(r.DoctorName!, pattern))
                : query.Where(r => r.DoctorName != null && r.DoctorName.Contains(doctor));
        }

        if (c.Amount is > 0) query = query.Where(r => r.TotalAmount == c.Amount.Value);
        query = ApplyQuickTermToPharmacyRequests(query, c.QuickTerm);

        return await query.OrderByDescending(r => r.RequestDate).Take(take)
            .Select(r => new GlobalSearchHit(
                GlobalSearchTypes.PharmacyRequest,
                $"#{r.RequestNo}",
                r.RequestDate,
                r.PatientName ?? "",
                r.DoctorName ?? "",
                r.TotalAmount,
                $"/Pharmacy/Request?RecordId={r.Id}",
                r.Lines.OrderBy(l => l.LineNo).Select(l => l.MedicineName).FirstOrDefault() ?? ""))
            .ToListAsync();
    }

    private async Task<List<GlobalSearchHit>> SearchPharmacyBillsAdvancedAsync(
        Guid clinicId, GlobalSearchCriteria c, int take)
    {
        var from = c.FromDate.Date;
        var to = c.ToDate.Date;
        var query = _db.PharmacyBills.AsNoTracking()
            .Where(r => r.ClinicId == clinicId)
            .Where(r => r.BillDate.Date >= from && r.BillDate.Date <= to)
            .Apply(_doctorScope.Filter);

        if (!string.IsNullOrWhiteSpace(c.PatientName))
        {
            var patient = c.PatientName.Trim();
            var pattern = LikePattern(patient);
            query = _useILike
                ? query.Where(r => EF.Functions.ILike(r.PatientName!, pattern) || EF.Functions.ILike(r.PatientId!, pattern))
                : query.Where(r =>
                    (r.PatientName != null && r.PatientName.Contains(patient)) ||
                    (r.PatientId != null && r.PatientId.Contains(patient)));
        }

        if (!string.IsNullOrWhiteSpace(c.DoctorName))
        {
            var doctor = c.DoctorName.Trim();
            var pattern = LikePattern(doctor);
            query = _useILike
                ? query.Where(r => EF.Functions.ILike(r.DoctorName!, pattern))
                : query.Where(r => r.DoctorName != null && r.DoctorName.Contains(doctor));
        }

        if (c.Amount is > 0) query = query.Where(r => r.TotalAmount == c.Amount.Value);
        query = ApplyQuickTermToPharmacyBills(query, c.QuickTerm);

        return await query.OrderByDescending(r => r.BillDate).Take(take)
            .Select(r => new GlobalSearchHit(
                GlobalSearchTypes.PharmacyBill,
                $"#{r.BillNo}",
                r.BillDate,
                r.PatientName ?? "",
                r.DoctorName ?? "",
                r.TotalAmount,
                $"/Pharmacy/Bill?RecordId={r.Id}",
                r.Lines.OrderBy(l => l.LineNo).Select(l => l.MedicineName).FirstOrDefault() ?? ""))
            .ToListAsync();
    }

    private async Task<List<GlobalSearchHit>> SearchPharmacyPurchasesAdvancedAsync(
        Guid clinicId, GlobalSearchCriteria c, int take)
    {
        var from = c.FromDate.Date;
        var to = c.ToDate.Date;
        var query = _db.PharmacyPurchaseBills.AsNoTracking()
            .Where(r => r.ClinicId == clinicId)
            .Where(r => r.PurchaseDate.Date >= from && r.PurchaseDate.Date <= to);

        if (!string.IsNullOrWhiteSpace(c.PatientName))
        {
            var party = c.PatientName.Trim();
            var pattern = LikePattern(party);
            query = _useILike
                ? query.Where(r => EF.Functions.ILike(r.SupplierName!, pattern))
                : query.Where(r => r.SupplierName != null && r.SupplierName.Contains(party));
        }

        if (c.Amount is > 0) query = query.Where(r => r.NetAmount == c.Amount.Value);
        query = ApplyQuickTermToPharmacyPurchases(query, c.QuickTerm);

        return await query.OrderByDescending(r => r.PurchaseDate).Take(take)
            .Select(r => new GlobalSearchHit(
                GlobalSearchTypes.PharmacyPurchase,
                $"#{r.PurchaseNo}",
                r.PurchaseDate,
                r.SupplierName ?? "",
                "",
                r.NetAmount,
                $"/Pharmacy/Purchase?RecordId={r.Id}",
                r.SupplierInvoiceNo ?? ""))
            .ToListAsync();
    }

    private async Task<List<GlobalSearchHit>> SearchPharmacyOpeningBalancesAdvancedAsync(
        Guid clinicId, GlobalSearchCriteria c, int take)
    {
        var from = c.FromDate.Date;
        var to = c.ToDate.Date;
        var query = _db.PharmacyOpeningBalances.AsNoTracking()
            .Where(r => r.ClinicId == clinicId)
            .Where(r => r.BalanceDate.Date >= from && r.BalanceDate.Date <= to);

        if (c.Amount is > 0)
            query = query.Where(r => r.Lines.Sum(l => l.Total) == c.Amount.Value);
        query = ApplyQuickTermToPharmacyOpeningBalances(query, c.QuickTerm);

        return await query.OrderByDescending(r => r.BalanceDate).Take(take)
            .Select(r => new GlobalSearchHit(
                GlobalSearchTypes.PharmacyOpening,
                $"#{r.BalanceNo}",
                r.BalanceDate,
                "",
                "",
                r.Lines.Sum(l => l.Total),
                $"/Pharmacy/OpeningBalance?RecordId={r.Id}",
                r.Notes ?? ""))
            .ToListAsync();
    }

    private async Task<List<GlobalSearchHit>> SearchRadiologyRequestsAdvancedAsync(
        Guid clinicId, GlobalSearchCriteria c, int take)
    {
        var from = c.FromDate.Date;
        var to = c.ToDate.Date;
        var query = _db.RadiologyRequests.AsNoTracking()
            .Where(r => r.ClinicId == clinicId)
            .Where(r => r.RequestDate.Date >= from && r.RequestDate.Date <= to)
            .Apply(_doctorScope.Filter);

        if (!string.IsNullOrWhiteSpace(c.PatientName))
        {
            var patient = c.PatientName.Trim();
            var pattern = LikePattern(patient);
            query = _useILike
                ? query.Where(r => EF.Functions.ILike(r.PatientName!, pattern) || EF.Functions.ILike(r.PatientBarcode!, pattern))
                : query.Where(r =>
                    (r.PatientName != null && r.PatientName.Contains(patient)) ||
                    (r.PatientBarcode != null && r.PatientBarcode.Contains(patient)));
        }

        if (!string.IsNullOrWhiteSpace(c.DoctorName))
        {
            var doctor = c.DoctorName.Trim();
            var pattern = LikePattern(doctor);
            query = _useILike
                ? query.Where(r => EF.Functions.ILike(r.DoctorName!, pattern))
                : query.Where(r => r.DoctorName != null && r.DoctorName.Contains(doctor));
        }

        if (c.Amount is > 0) query = query.Where(r => r.TotalAmount == c.Amount.Value);
        query = ApplyQuickTermToRadiologyRequests(query, c.QuickTerm);

        return await query.OrderByDescending(r => r.RequestDate).Take(take)
            .Select(r => new GlobalSearchHit(
                GlobalSearchTypes.RadiologyRequest,
                $"#{r.RequestNo}",
                r.RequestDate,
                r.PatientName ?? "",
                r.DoctorName ?? "",
                r.TotalAmount,
                $"/Radiology/Request?RecordId={r.Id}",
                r.Lines.OrderBy(l => l.LineNo).Select(l => l.TestName).FirstOrDefault() ?? ""))
            .ToListAsync();
    }

    private async Task<List<GlobalSearchHit>> SearchRadiologyResultsAdvancedAsync(
        Guid clinicId, GlobalSearchCriteria c, int take)
    {
        var from = c.FromDate.Date;
        var to = c.ToDate.Date;
        var query = _db.RadiologyResults.AsNoTracking()
            .Where(r => r.ClinicId == clinicId)
            .Where(r => r.ResultDate.Date >= from && r.ResultDate.Date <= to)
            .Apply(_doctorScope.Filter);

        if (!string.IsNullOrWhiteSpace(c.PatientName))
        {
            var patient = c.PatientName.Trim();
            var pattern = LikePattern(patient);
            query = _useILike
                ? query.Where(r => EF.Functions.ILike(r.PatientName!, pattern))
                : query.Where(r => r.PatientName != null && r.PatientName.Contains(patient));
        }

        if (!string.IsNullOrWhiteSpace(c.DoctorName))
        {
            var doctor = c.DoctorName.Trim();
            var pattern = LikePattern(doctor);
            query = _useILike
                ? query.Where(r => EF.Functions.ILike(r.DoctorName!, pattern))
                : query.Where(r => r.DoctorName != null && r.DoctorName.Contains(doctor));
        }

        query = ApplyQuickTermToRadiologyResults(query, c.QuickTerm);

        return await query.OrderByDescending(r => r.ResultDate).Take(take)
            .Select(r => new GlobalSearchHit(
                GlobalSearchTypes.RadiologyResult,
                $"#{r.ResultNo}",
                r.ResultDate,
                r.PatientName ?? "",
                r.DoctorName ?? "",
                0m,
                $"/Radiology/Result?RecordId={r.Id}",
                r.Lines.OrderBy(l => l.LineNo).Select(l => l.TestName).FirstOrDefault() ?? ""))
            .ToListAsync();
    }
}
