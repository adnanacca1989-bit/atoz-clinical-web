using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Web.Pages.Reports;

public class PatientHistoryModel : PageModel
{
    private readonly ClinicalDbContext _db;
    private readonly ClinicContextService _clinicContext;
    private readonly ArReportService _ar;

    public PatientHistoryModel(ClinicalDbContext db, ClinicContextService clinicContext, ArReportService ar)
    {
        _db = db;
        _clinicContext = clinicContext;
        _ar = ar;
    }

    [BindProperty(SupportsGet = true)]
    public string? PatientName { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? DoctorName { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? PatientId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Age { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? DateOfBirth { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? City { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? PhoneNumber { get; set; }

    public List<HistoryRow> Results { get; private set; } = [];
    public List<ArReportRow> ArResults { get; private set; } = [];
    public decimal ArTotalCashReceipt { get; private set; }
    public decimal ArTotalCashPayment { get; private set; }
    public decimal ArTotalInvoiceAmount { get; private set; }
    public decimal ArTotalDiscount { get; private set; }
    public decimal ArTotalEndingBalance { get; private set; }
    public decimal ArTotalPatientCredit { get; private set; }

    private List<Patient> _matchedPatients = [];

    public async Task<IActionResult> OnGetAsync() => await RunSearchAsync();

    public Task<IActionResult> OnPostRunAsync() => RunSearchAsync();

    public IActionResult OnPostClearAsync() => RedirectToPage();

    private async Task<IActionResult> RunSearchAsync()
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();

        var hasFilter = !string.IsNullOrWhiteSpace(PatientName) || !string.IsNullOrWhiteSpace(DoctorName) ||
                        !string.IsNullOrWhiteSpace(PatientId) || !string.IsNullOrWhiteSpace(PhoneNumber);

        if (!hasFilter)
            return Page();

        bool MatchPatient(string? name, string? barcode) =>
            (string.IsNullOrWhiteSpace(PatientName) || (name?.Contains(PatientName, StringComparison.OrdinalIgnoreCase) == true)) &&
            (string.IsNullOrWhiteSpace(PatientId) || (barcode?.Contains(PatientId, StringComparison.OrdinalIgnoreCase) == true));

        bool MatchDoctor(string? doc) =>
            string.IsNullOrWhiteSpace(DoctorName) || (doc?.Contains(DoctorName, StringComparison.OrdinalIgnoreCase) == true);

        var allPatients = await _db.Patients.Where(p => p.ClinicId == clinicId).AsNoTracking().ToListAsync();
        var patients = allPatients;
        if (!string.IsNullOrWhiteSpace(PatientName))
            patients = patients.Where(p => p.FullName.Contains(PatientName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(PatientId))
            patients = patients.Where(p => p.PatientNo.Contains(PatientId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(PhoneNumber))
            patients = patients.Where(p => (p.Phone ?? "").Contains(PhoneNumber, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(City))
            patients = patients.Where(p => (p.City ?? "").Contains(City, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(DoctorName))
            patients = patients.Where(p => (p.DoctorName ?? "").Contains(DoctorName, StringComparison.OrdinalIgnoreCase)).ToList();

        _matchedPatients = patients;

        var doctors = await _db.Doctors.Where(d => d.ClinicId == clinicId).AsNoTracking().ToListAsync();
        var demographics = new ClinicalDemographicsSyncService(_db);

        if (patients.Count == 1)
        {
            var p = patients[0];
            ApplyLivePatientHeader(p);
        }
        else if (patients.Count > 1 && !string.IsNullOrWhiteSpace(PatientId))
        {
            var exact = patients.FirstOrDefault(p =>
                string.Equals(p.PatientNo, PatientId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
                ApplyLivePatientHeader(exact);
        }

        string DisplayPatientName(string? storedName, string? storedBarcode, Guid? patientRecordId = null) =>
            demographics.ResolvePatientFromList(allPatients, patientRecordId, storedBarcode, storedName)?.FullName
            ?? ResolveLivePatient(storedName, storedBarcode)?.FullName
            ?? storedName ?? "";

        string DisplayDoctorName(string? storedDoctor, Guid? doctorRecordId, string? storedName, string? storedBarcode) =>
            demographics.ResolveDoctorFromList(doctors, doctorRecordId, storedDoctor)?.Name
            ?? ResolveLivePatient(storedName, storedBarcode)?.DoctorName
            ?? storedDoctor ?? "";

        foreach (var p in patients)
        {
            var doctorName = DisplayDoctorName(p.DoctorName, p.DoctorRecordId, p.FullName, p.PatientNo);
            Results.Add(new HistoryRow(p.FullName, "Patient Registration", p.UpdatedAt, doctorName, $"Status: {p.Status}"));
        }

        foreach (var rx in await _db.Prescriptions.Where(x => x.ClinicId == clinicId).ToListAsync())
            if (MatchPatient(rx.PatientName, null) && MatchDoctor(rx.DoctorName))
                Results.Add(new HistoryRow(
                    DisplayPatientName(rx.PatientName, null, rx.PatientRecordId), "Doctor's Prescription", rx.DatePrescription,
                    DisplayDoctorName(rx.DoctorName, rx.DoctorRecordId, rx.PatientName, null), rx.DiseaseName ?? rx.DiagnosisText));

        foreach (var inv in await _db.Invoices.Where(x => x.ClinicId == clinicId).ToListAsync())
            if (MatchPatient(inv.PatientName, inv.PatientId) && MatchDoctor(inv.DoctorName))
                Results.Add(new HistoryRow(
                    DisplayPatientName(inv.PatientName, inv.PatientId, inv.PatientRecordId), "Invoice / Billing", inv.InvoiceDate,
                    DisplayDoctorName(inv.DoctorName, inv.DoctorRecordId, inv.PatientName, inv.PatientId),
                    $"Total {inv.TotalAmount:N2}, Paid {inv.AmountPaid:N2}, Balance {inv.BalanceDue:N2}"));

        foreach (var lr in await _db.LabRequests.Where(x => x.ClinicId == clinicId).ToListAsync())
            if (MatchPatient(lr.PatientName, lr.PatientBarcode) && MatchDoctor(lr.DoctorName))
                Results.Add(new HistoryRow(
                    DisplayPatientName(lr.PatientName, lr.PatientBarcode, lr.PatientRecordId), "Laboratory Request", lr.RequestDate,
                    DisplayDoctorName(lr.DoctorName, lr.DoctorRecordId, lr.PatientName, lr.PatientBarcode), $"Request #{lr.RequestNo}, Total {lr.TotalAmount:N2}"));

        foreach (var lr in await _db.LabResults.Where(x => x.ClinicId == clinicId).ToListAsync())
            if (MatchPatient(lr.PatientName, null) && MatchDoctor(lr.DoctorName))
                Results.Add(new HistoryRow(
                    DisplayPatientName(lr.PatientName, null, lr.PatientRecordId), "Laboratory Result", lr.ResultDate,
                    DisplayDoctorName(lr.DoctorName, lr.DoctorRecordId, lr.PatientName, null), $"Result #{lr.ResultNo}"));

        foreach (var rr in await _db.RadiologyRequests.Where(x => x.ClinicId == clinicId).ToListAsync())
            if (MatchPatient(rr.PatientName, rr.PatientBarcode) && MatchDoctor(rr.DoctorName))
                Results.Add(new HistoryRow(
                    DisplayPatientName(rr.PatientName, rr.PatientBarcode, rr.PatientRecordId), "Radiology Request", rr.RequestDate,
                    DisplayDoctorName(rr.DoctorName, rr.DoctorRecordId, rr.PatientName, rr.PatientBarcode), $"Request #{rr.RequestNo}, Total {rr.TotalAmount:N2}"));

        foreach (var rr in await _db.RadiologyResults.Where(x => x.ClinicId == clinicId).ToListAsync())
            if (MatchPatient(rr.PatientName, null) && MatchDoctor(rr.DoctorName))
                Results.Add(new HistoryRow(
                    DisplayPatientName(rr.PatientName, null, rr.PatientRecordId), "Radiology Result", rr.ResultDate,
                    DisplayDoctorName(rr.DoctorName, rr.DoctorRecordId, rr.PatientName, null), $"Result #{rr.ResultNo}"));

        foreach (var pr in await _db.PharmacyRequests.Where(x => x.ClinicId == clinicId).ToListAsync())
            if (MatchPatient(pr.PatientName, pr.PatientId) && MatchDoctor(pr.DoctorName))
                Results.Add(new HistoryRow(
                    DisplayPatientName(pr.PatientName, pr.PatientId, pr.PatientRecordId), "Pharmacy Request", pr.RequestDate,
                    DisplayDoctorName(pr.DoctorName, pr.DoctorRecordId, pr.PatientName, pr.PatientId), $"Request #{pr.RequestNo}, Total {pr.TotalAmount:N2}"));

        foreach (var pb in await _db.PharmacyBills.Where(x => x.ClinicId == clinicId).ToListAsync())
            if (MatchPatient(pb.PatientName, pb.PatientId) && MatchDoctor(pb.DoctorName))
                Results.Add(new HistoryRow(
                    DisplayPatientName(pb.PatientName, pb.PatientId, pb.PatientRecordId), "Pharmacy Bill", pb.BillDate,
                    DisplayDoctorName(pb.DoctorName, pb.DoctorRecordId, pb.PatientName, pb.PatientId), $"Bill #{pb.BillNo}, Total {pb.TotalAmount:N2}"));

        foreach (var sir in await _db.ServiceIncomeRequests.Where(x => x.ClinicId == clinicId).ToListAsync())
            if (MatchPatient(sir.PatientName, sir.PatientBarcode) && MatchDoctor(sir.DoctorName))
                Results.Add(new HistoryRow(
                    DisplayPatientName(sir.PatientName, sir.PatientBarcode, sir.PatientRecordId), "Service Income Request", sir.RequestDate,
                    DisplayDoctorName(sir.DoctorName, sir.DoctorRecordId, sir.PatientName, sir.PatientBarcode),
                    $"Request #{sir.RequestNo}, Total {sir.TotalAmount:N2}"));

        foreach (var cr in await _db.CashReceipts.Where(x => x.ClinicId == clinicId).ToListAsync())
            if (MatchPatient(cr.PatientName, cr.PatientId) && MatchDoctor(cr.DoctorName))
                Results.Add(new HistoryRow(
                    DisplayPatientName(cr.PatientName, cr.PatientId, cr.PatientRecordId), "Cash Receipt", cr.ReceiptDate,
                    DisplayDoctorName(cr.DoctorName, cr.DoctorRecordId, cr.PatientName, cr.PatientId), $"Amount {cr.Amount:N2}"));

        foreach (var cp in await _db.CashPayments.Where(x => x.ClinicId == clinicId).ToListAsync())
            if (MatchPatient(cp.PayeeName, cp.PatientId) && MatchDoctor(cp.DoctorName))
                Results.Add(new HistoryRow(
                    DisplayPatientName(cp.PayeeName, cp.PatientId, cp.PatientRecordId), "Cash Payment", cp.PaymentDate,
                    DisplayDoctorName(cp.DoctorName, cp.DoctorRecordId, cp.PayeeName, cp.PatientId), $"Amount {cp.Amount:N2}"));

        Results = Results.OrderByDescending(r => r.Date).ToList();

        var arReport = await _ar.BuildAsync(
            clinicId.Value, null, null, PatientName, PatientId, DoctorName);
        ArResults = arReport.Rows;
        ArTotalCashReceipt = arReport.TotalCashReceipt;
        ArTotalCashPayment = arReport.TotalCashPayment;
        ArTotalInvoiceAmount = arReport.TotalInvoiceAmount;
        ArTotalDiscount = arReport.TotalDiscount;
        ArTotalEndingBalance = arReport.TotalEndingBalance;
        ArTotalPatientCredit = arReport.TotalPatientCredit;

        return Page();
    }

    private void ApplyLivePatientHeader(Patient p)
    {
        PatientName = p.FullName;
        PatientId = p.PatientNo;
        Age = p.AgeYears?.ToString();
        City = p.City;
        PhoneNumber = p.Phone;
        if (string.IsNullOrWhiteSpace(DoctorName))
            DoctorName = p.DoctorName;
        if (p.DateOfBirth.HasValue)
            DateOfBirth = p.DateOfBirth.Value.ToString("d");
    }

    private Patient? ResolveLivePatient(string? storedName, string? storedBarcode)
    {
        if (_matchedPatients.Count == 0) return null;
        return _matchedPatients.FirstOrDefault(p =>
                   PatientChargeMatcher.MatchesPatient(storedBarcode, storedName, null, p.PatientNo, p.FullName))
               ?? _matchedPatients.FirstOrDefault();
    }

    public async Task<IActionResult> OnPostExportAsync()
    {
        await RunSearchAsync();
        var bytes = ReportExcelService.Export("Patient History",
            ["Patient", "Form", "Date", "Doctor", "Details"],
            Results.Select(r => new object?[] { r.Patient, r.Form, r.Date, r.Doctor, r.Details }));
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"PatientHistory_{DateTime.Now:yyyyMMdd}.xlsx");
    }

    public sealed record HistoryRow(string Patient, string Form, DateTime Date, string Doctor, string? Details);
}
