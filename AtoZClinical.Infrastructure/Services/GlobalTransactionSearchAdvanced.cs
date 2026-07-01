using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public static class GlobalSearchTypes
{
    public const string All = "All";
    public const string Invoice = "Invoice";
    public const string Patient = "Patient";
    public const string Doctor = "Doctor";
    public const string Appointment = "Appointment";
    public const string CashReceipt = "Cash Receipt";
    public const string CashPayment = "Cash Payment";
    public const string LabRequest = "Lab Request";
    public const string LabResult = "Lab Result";
    public const string RadiologyRequest = "Radiology Request";
    public const string RadiologyResult = "Radiology Result";

    public static readonly string[] DropdownOptions =
    [
        All, Invoice, Patient, Doctor, CashReceipt, CashPayment,
        RadiologyResult, RadiologyRequest, LabResult, LabRequest
    ];

    public static readonly string[] AllSearchTypes =
    [
        Invoice, Patient, Doctor, Appointment, CashReceipt, CashPayment,
        LabRequest, LabResult, RadiologyRequest, RadiologyResult
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
        int limit = 500)
    {
        if (criteria.FromDate > criteria.ToDate)
            (criteria.FromDate, criteria.ToDate) = (criteria.ToDate, criteria.FromDate);

        var types = ResolveTransactionTypes(criteria.TransactionType);
        var perType = Math.Max(25, limit / Math.Max(1, types.Count));
        var hits = new List<GlobalSearchHit>();

        if (types.Contains(GlobalSearchTypes.Invoice))
            hits.AddRange(await SafeSearchAsync(() => SearchInvoicesAdvancedAsync(clinicId, criteria, perType)));
        if (types.Contains(GlobalSearchTypes.Patient))
            hits.AddRange(await SafeSearchAsync(() => SearchPatientsAdvancedAsync(clinicId, criteria, perType)));
        if (types.Contains(GlobalSearchTypes.Doctor))
            hits.AddRange(await SafeSearchAsync(() => SearchDoctorsAdvancedAsync(clinicId, criteria, perType)));
        if (types.Contains(GlobalSearchTypes.Appointment))
            hits.AddRange(await SafeSearchAsync(() => SearchPatientAppointmentsAdvancedAsync(clinicId, criteria, perType)));
        if (types.Contains(GlobalSearchTypes.CashReceipt))
            hits.AddRange(await SafeSearchAsync(() => SearchCashReceiptsAdvancedAsync(clinicId, criteria, perType)));
        if (types.Contains(GlobalSearchTypes.CashPayment))
            hits.AddRange(await SafeSearchAsync(() => SearchCashPaymentsAdvancedAsync(clinicId, criteria, perType)));
        if (types.Contains(GlobalSearchTypes.LabRequest))
            hits.AddRange(await SafeSearchAsync(() => SearchLabRequestsAdvancedAsync(clinicId, criteria, perType)));
        if (types.Contains(GlobalSearchTypes.LabResult))
            hits.AddRange(await SafeSearchAsync(() => SearchLabResultsAdvancedAsync(clinicId, criteria, perType)));
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
            return GlobalSearchTypes.AllSearchTypes
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

    private async Task<List<GlobalSearchHit>> SearchPatientsAdvancedAsync(
        Guid clinicId, GlobalSearchCriteria c, int take)
    {
        var from = c.FromDate.Date;
        var to = c.ToDate.Date;
        var query = _db.Patients.ForClinic(clinicId).AsNoTracking()
            .Where(p => p.CreatedAt.Date >= from && p.CreatedAt.Date <= to)
            .Apply(_doctorScope.Filter);

        if (!string.IsNullOrWhiteSpace(c.PatientName))
        {
            var patient = c.PatientName.Trim();
            var pattern = LikePattern(patient);
            query = _useILike
                ? query.Where(p =>
                    EF.Functions.ILike(p.FirstName, pattern) ||
                    EF.Functions.ILike(p.LastName, pattern) ||
                    (p.PatientNo != null && EF.Functions.ILike(p.PatientNo, pattern)) ||
                    (p.NationalId != null && EF.Functions.ILike(p.NationalId, pattern)))
                : query.Where(p =>
                    p.FirstName.Contains(patient) ||
                    p.LastName.Contains(patient) ||
                    (p.PatientNo != null && p.PatientNo.Contains(patient)) ||
                    (p.NationalId != null && p.NationalId.Contains(patient)));
        }

        if (!string.IsNullOrWhiteSpace(c.DoctorName))
        {
            var doctor = c.DoctorName.Trim();
            var pattern = LikePattern(doctor);
            query = _useILike
                ? query.Where(p => p.DoctorName != null && EF.Functions.ILike(p.DoctorName, pattern))
                : query.Where(p => p.DoctorName != null && p.DoctorName.Contains(doctor));
        }

        if (c.UseDateOfBirth && c.DateOfBirth.HasValue)
            query = query.Where(p => p.DateOfBirth != null && p.DateOfBirth.Value.Date == c.DateOfBirth.Value.Date);

        return await query.OrderByDescending(p => p.CreatedAt).Take(take)
            .Select(p => new GlobalSearchHit(
                GlobalSearchTypes.Patient,
                $"#{p.PatientNo}",
                p.CreatedAt,
                (p.FirstName + " " + p.LastName).Trim(),
                p.DoctorName ?? "",
                0m,
                $"/PatientRegistration/Index?RecordId={p.Id}",
                (p.NationalId ?? "") + (p.City != null ? " | " + p.City : "")))
            .ToListAsync();
    }

    private async Task<List<GlobalSearchHit>> SearchPatientAppointmentsAdvancedAsync(
        Guid clinicId, GlobalSearchCriteria c, int take)
    {
        var from = c.FromDate.Date;
        var to = c.ToDate.Date;
        var query = _db.Patients.ForClinic(clinicId).AsNoTracking()
            .Where(p => p.AppointmentDate != null &&
                        p.AppointmentDate.Value.Date >= from &&
                        p.AppointmentDate.Value.Date <= to)
            .Apply(_doctorScope.Filter);

        if (!string.IsNullOrWhiteSpace(c.PatientName))
        {
            var patient = c.PatientName.Trim();
            var pattern = LikePattern(patient);
            query = _useILike
                ? query.Where(p =>
                    EF.Functions.ILike(p.FirstName, pattern) ||
                    EF.Functions.ILike(p.LastName, pattern))
                : query.Where(p =>
                    p.FirstName.Contains(patient) ||
                    p.LastName.Contains(patient));
        }

        if (!string.IsNullOrWhiteSpace(c.DoctorName))
        {
            var doctor = c.DoctorName.Trim();
            var pattern = LikePattern(doctor);
            query = _useILike
                ? query.Where(p => p.DoctorName != null && EF.Functions.ILike(p.DoctorName, pattern))
                : query.Where(p => p.DoctorName != null && p.DoctorName.Contains(doctor));
        }

        return await query.OrderByDescending(p => p.AppointmentDate).Take(take)
            .Select(p => new GlobalSearchHit(
                GlobalSearchTypes.Appointment,
                p.AppointmentId ?? $"#{p.PatientNo}",
                p.AppointmentDate!.Value,
                (p.FirstName + " " + p.LastName).Trim(),
                p.DoctorName ?? "",
                0m,
                $"/PatientRegistration/Index?RecordId={p.Id}",
                p.Status ?? ""))
            .ToListAsync();
    }

    private async Task<List<GlobalSearchHit>> SearchDoctorsAdvancedAsync(
        Guid clinicId, GlobalSearchCriteria c, int take)
    {
        var from = c.FromDate.Date;
        var to = c.ToDate.Date;
        var query = _db.Doctors.ForClinic(clinicId).AsNoTracking()
            .Where(d => d.CreatedAt.Date >= from && d.CreatedAt.Date <= to);

        if (!string.IsNullOrWhiteSpace(c.DoctorName))
        {
            var doctor = c.DoctorName.Trim();
            var pattern = LikePattern(doctor);
            query = _useILike
                ? query.Where(d => EF.Functions.ILike(d.Name, pattern))
                : query.Where(d => d.Name.Contains(doctor));
        }

        if (c.Amount is > 0)
            query = query.Where(d => d.ConsultationFee == c.Amount.Value);

        return await query.OrderByDescending(d => d.CreatedAt).Take(take)
            .Select(d => new GlobalSearchHit(
                GlobalSearchTypes.Doctor,
                $"#{d.DoctorNo}",
                d.CreatedAt,
                "",
                d.Name,
                d.ConsultationFee,
                $"/Doctors/Index?RecordId={d.Id}",
                (d.Specialty ?? "") + (d.Phone != null ? " | " + d.Phone : "")))
            .ToListAsync();
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
