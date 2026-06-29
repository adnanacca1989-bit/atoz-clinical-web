using System.Text.RegularExpressions;
using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public sealed class RequestReportService
{
    public const string StatusCreated = "Created";
    public const string StatusNotYet = "Not Yet";

    private static readonly Regex AuditNumberRegex = new(@"#(\d+)", RegexOptions.Compiled);

    private readonly ClinicalDbContext _db;
    private readonly DoctorScopeContext _doctorScope;

    public RequestReportService(ClinicalDbContext db, DoctorScopeContext doctorScope)
    {
        _db = db;
        _doctorScope = doctorScope;
    }

    public async Task<RequestReportResult> BuildAsync(
        Guid clinicId,
        DateTime fromDate,
        DateTime toDate,
        string? patientName = null,
        string? doctorName = null,
        bool pendingOnly = false,
        bool nonZeroOnly = false)
    {
        var from = fromDate.Date;
        var to = toDate.Date;
        if (from > to)
            (from, to) = (to, from);

        var patients = await _db.Patients.ForClinic(clinicId).AsNoTracking().ToListAsync();
        var demographics = new ClinicalDemographicsSyncService(_db);
        var auditUsers = await BuildAuditUserLookupAsync(clinicId);

        var labResults = await _db.LabResults.ForClinic(clinicId).Apply(_doctorScope.Filter).AsNoTracking().ToListAsync();
        var radResults = await _db.RadiologyResults.ForClinic(clinicId).Apply(_doctorScope.Filter).AsNoTracking().ToListAsync();
        var pharmacyBills = await _db.PharmacyBills.ForClinic(clinicId).Apply(_doctorScope.Filter).AsNoTracking().ToListAsync();
        var serviceInvoices = await BuildServiceInvoiceLookupAsync(clinicId);

        var labByRequest = labResults
            .Where(r => r.RequestNo.HasValue)
            .GroupBy(r => r.RequestNo!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.ResultDate).ThenByDescending(x => x.ResultNo).First());

        var radByRequest = radResults
            .Where(r => r.RequestNo.HasValue)
            .GroupBy(r => r.RequestNo!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.ResultDate).ThenByDescending(x => x.ResultNo).First());

        var billByRequest = pharmacyBills
            .Where(b => b.RequestNo.HasValue)
            .GroupBy(b => b.RequestNo!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.BillDate).ThenByDescending(x => x.BillNo).First());

        var rows = new List<RequestReportRow>();

        foreach (var req in await _db.LabRequests.ForClinic(clinicId).Apply(_doctorScope.Filter).AsNoTracking().ToListAsync())
        {
            if (!InRequestDateRange(req.RequestDate, from, to)) continue;
            if (!MatchesPatient(req.PatientName, req.PatientBarcode, patientName)) continue;
            if (!MatchesDoctor(req.DoctorName, doctorName)) continue;
            if (nonZeroOnly && req.TotalAmount == 0) continue;

            var patient = ResolvePatient(patients, demographics, req.PatientRecordId, req.PatientBarcode, req.PatientName);
            labByRequest.TryGetValue(req.RequestNo, out var result);
            var status = result is not null ? StatusCreated : StatusNotYet;
            if (pendingOnly && status == StatusCreated) continue;

            rows.Add(BuildRow(
                req.RequestDate, req.RequestNo, "Laboratory",
                req.PatientBarcode, req.PatientName, patient, req.Age, req.Gender, req.Phone, req.City,
                req.DoctorName, req.Specialty, req.TotalAmount, status,
                result is null ? null : ResolveAuditUser(auditUsers, "Laboratory Result", result.ResultNo),
                result?.ResultDate, result?.ResultNo.ToString(), result?.Id, "/Laboratory/Result"));
        }

        foreach (var req in await _db.RadiologyRequests.ForClinic(clinicId).Apply(_doctorScope.Filter).AsNoTracking().ToListAsync())
        {
            if (!InRequestDateRange(req.RequestDate, from, to)) continue;
            if (!MatchesPatient(req.PatientName, req.PatientBarcode, patientName)) continue;
            if (!MatchesDoctor(req.DoctorName, doctorName)) continue;
            if (nonZeroOnly && req.TotalAmount == 0) continue;

            var patient = ResolvePatient(patients, demographics, req.PatientRecordId, req.PatientBarcode, req.PatientName);
            radByRequest.TryGetValue(req.RequestNo, out var result);
            var status = result is not null ? StatusCreated : StatusNotYet;
            if (pendingOnly && status == StatusCreated) continue;

            rows.Add(BuildRow(
                req.RequestDate, req.RequestNo, "Radiology",
                req.PatientBarcode, req.PatientName, patient, req.Age, req.Gender, req.Phone, req.City,
                req.DoctorName, req.Specialty, req.TotalAmount, status,
                result is null ? null : ResolveAuditUser(auditUsers, "Radiology Result", result.ResultNo),
                result?.ResultDate, result?.ResultNo.ToString(), result?.Id, "/Radiology/Result"));
        }

        foreach (var req in await _db.PharmacyRequests.ForClinic(clinicId).Apply(_doctorScope.Filter).AsNoTracking().ToListAsync())
        {
            if (!InRequestDateRange(req.RequestDate, from, to)) continue;
            if (!MatchesPatient(req.PatientName, req.PatientId, patientName)) continue;
            if (!MatchesDoctor(req.DoctorName, doctorName)) continue;
            if (nonZeroOnly && req.TotalAmount == 0) continue;

            var patient = ResolvePatient(patients, demographics, req.PatientRecordId, req.PatientId, req.PatientName);
            billByRequest.TryGetValue(req.RequestNo, out var bill);
            var status = bill is not null ? StatusCreated : StatusNotYet;
            if (pendingOnly && status == StatusCreated) continue;

            rows.Add(BuildRow(
                req.RequestDate, req.RequestNo, "Pharmacy Request",
                req.PatientId, req.PatientName, patient, req.Age, req.Gender, req.Phone, req.City,
                req.DoctorName, req.Specialty, req.TotalAmount, status,
                bill is null ? null : ResolveAuditUser(auditUsers, "Pharmacy Bill", bill.BillNo),
                bill?.BillDate, bill?.BillNo.ToString(), bill?.Id, "/Pharmacy/Bill"));
        }

        foreach (var req in await _db.ServiceIncomeRequests.ForClinic(clinicId).Apply(_doctorScope.Filter).AsNoTracking().ToListAsync())
        {
            if (!InRequestDateRange(req.RequestDate, from, to)) continue;
            if (!MatchesPatient(req.PatientName, req.PatientBarcode, patientName)) continue;
            if (!MatchesDoctor(req.DoctorName, doctorName)) continue;
            if (nonZeroOnly && req.TotalAmount == 0) continue;

            var patient = ResolvePatient(patients, demographics, req.PatientRecordId, req.PatientBarcode, req.PatientName);
            serviceInvoices.TryGetValue(req.RequestNo, out var invoice);
            var status = invoice is not null ? StatusCreated : StatusNotYet;
            if (pendingOnly && status == StatusCreated) continue;

            rows.Add(BuildRow(
                req.RequestDate, req.RequestNo, "Service Income Request",
                req.PatientBarcode, req.PatientName, patient, req.Age, req.Gender, req.Phone, req.City,
                req.DoctorName, req.Specialty, req.TotalAmount, status,
                invoice is null ? null : ResolveAuditUser(auditUsers, "Invoice", invoice.InvoiceNo),
                invoice?.InvoiceDate, invoice?.InvoiceNo.ToString(), invoice?.Id, "/Invoices/Index"));
        }

        rows = rows
            .OrderByDescending(r => r.RequestDate)
            .ThenByDescending(r => r.RequestNo)
            .ThenBy(r => r.TransactionType)
            .ToList();

        return new RequestReportResult(rows, rows.Sum(r => r.AmountRequest));
    }

    private static RequestReportRow BuildRow(
        DateTime requestDate,
        int requestNo,
        string transactionType,
        string? patientId,
        string? patientName,
        Patient? patient,
        int? age,
        string? sex,
        string? phone,
        string? city,
        string? doctorName,
        string? specialty,
        decimal amount,
        string status,
        string? createdByUser,
        DateTime? resultDate,
        string? resultId,
        Guid? resultRecordId,
        string resultPagePath) =>
        new(
            requestDate,
            requestNo,
            transactionType,
            patientId ?? patient?.PatientNo,
            patient?.FullName ?? patientName ?? "",
            patient?.AppointmentDate,
            patient?.AppointmentTime,
            age ?? patient?.AgeYears,
            sex ?? patient?.Gender,
            phone ?? patient?.Phone,
            city ?? patient?.City,
            doctorName,
            specialty,
            amount,
            status,
            createdByUser,
            resultDate,
            resultId,
            resultRecordId,
            resultPagePath);

    private async Task<Dictionary<int, Invoice>> BuildServiceInvoiceLookupAsync(Guid clinicId)
    {
        var lines = await _db.InvoiceLines
            .Include(l => l.Invoice)
            .Where(l => l.Invoice!.ClinicId == clinicId && l.ServiceName != null)
            .AsNoTracking()
            .ToListAsync();

        var map = new Dictionary<int, Invoice>();
        foreach (var line in lines)
        {
            var requestNo = ParseServiceRequestNo(line.ServiceName!);
            if (requestNo is null || line.Invoice is null) continue;
            map.TryAdd(requestNo.Value, line.Invoice);
        }

        return map;
    }

    private static int? ParseServiceRequestNo(string serviceName)
    {
        if (!serviceName.StartsWith("Service #", StringComparison.OrdinalIgnoreCase))
            return null;

        var end = serviceName.IndexOf(':', 9);
        if (end < 0) return null;

        return int.TryParse(serviceName[9..end], out var no) ? no : null;
    }

    private async Task<Dictionary<(string FormName, int Number), string>> BuildAuditUserLookupAsync(Guid clinicId)
    {
        var entries = await _db.AuditLogEntries
            .ForClinic(clinicId)
            .Where(a => a.FormName != null && a.Details != null)
            .AsNoTracking()
            .OrderByDescending(a => a.DateTime)
            .ToListAsync();

        var lookup = new Dictionary<(string, int), string>();
        foreach (var entry in entries)
        {
            var match = AuditNumberRegex.Match(entry.Details!);
            if (!match.Success || string.IsNullOrWhiteSpace(entry.FormName)) continue;

            if (!int.TryParse(match.Groups[1].Value, out var number)) continue;
            var key = (entry.FormName, number);
            lookup.TryAdd(key, entry.UserName ?? "");
        }

        return lookup;
    }

    private static string? ResolveAuditUser(
        IReadOnlyDictionary<(string FormName, int Number), string> lookup,
        string formName,
        int number) =>
        lookup.TryGetValue((formName, number), out var user) && !string.IsNullOrWhiteSpace(user) ? user : null;

    private static Patient? ResolvePatient(
        IReadOnlyList<Patient> patients,
        ClinicalDemographicsSyncService demographics,
        Guid? patientRecordId,
        string? barcode,
        string? name) =>
        demographics.ResolvePatientFromList(patients, patientRecordId, barcode, name);

    private static bool InRequestDateRange(DateTime requestDate, DateTime from, DateTime to) =>
        requestDate.Date >= from && requestDate.Date <= to;

    private static bool MatchesPatient(string? storedName, string? storedId, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;
        var term = filter.Trim();
        return storedName?.Contains(term, StringComparison.OrdinalIgnoreCase) == true
            || storedId?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool MatchesDoctor(string? storedDoctor, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;
        return storedDoctor?.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase) == true;
    }

    public sealed record RequestReportRow(
        DateTime RequestDate,
        int RequestNo,
        string TransactionType,
        string? PatientId,
        string PatientName,
        DateTime? AppointmentDate,
        TimeSpan? AppointmentTime,
        int? Age,
        string? Sex,
        string? Phone,
        string? City,
        string? DoctorName,
        string? Specialty,
        decimal AmountRequest,
        string ResultInvoiceStatus,
        string? CreatedByUser,
        DateTime? ResultDate,
        string? ResultId,
        Guid? ResultRecordId,
        string? ResultPagePath);

    public sealed record RequestReportResult(
        IReadOnlyList<RequestReportRow> Rows,
        decimal TotalAmount);
}
