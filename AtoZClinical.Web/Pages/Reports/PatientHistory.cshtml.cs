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

        var patients = await _db.Patients.Where(p => p.ClinicId == clinicId).ToListAsync();
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

        if (patients.Count == 1)
        {
            var p = patients[0];
            if (string.IsNullOrWhiteSpace(PatientName)) PatientName = p.FullName;
            if (string.IsNullOrWhiteSpace(PatientId)) PatientId = p.PatientNo;
            if (string.IsNullOrWhiteSpace(Age)) Age = p.AgeYears?.ToString();
            if (string.IsNullOrWhiteSpace(City)) City = p.City;
            if (string.IsNullOrWhiteSpace(PhoneNumber)) PhoneNumber = p.Phone;
            if (string.IsNullOrWhiteSpace(DateOfBirth) && p.DateOfBirth.HasValue)
                DateOfBirth = p.DateOfBirth.Value.ToString("d");
        }

        foreach (var p in patients)
            Results.Add(new HistoryRow(p.FullName, "Patient Registration", p.UpdatedAt, p.DoctorName ?? "", $"Status: {p.Status}"));

        foreach (var rx in await _db.Prescriptions.Where(x => x.ClinicId == clinicId).ToListAsync())
            if (MatchPatient(rx.PatientName, null) && MatchDoctor(rx.DoctorName))
                Results.Add(new HistoryRow(rx.PatientName ?? "", "Doctor's Prescription", rx.DatePrescription, rx.DoctorName ?? "", rx.DiseaseName ?? rx.DiagnosisText));

        foreach (var inv in await _db.Invoices.Where(x => x.ClinicId == clinicId).ToListAsync())
            if (MatchPatient(inv.PatientName, inv.PatientId) && MatchDoctor(inv.DoctorName))
                Results.Add(new HistoryRow(inv.PatientName ?? "", "Invoice / Billing", inv.InvoiceDate, inv.DoctorName ?? "",
                    $"Total {inv.TotalAmount:N2}, Paid {inv.AmountPaid:N2}, Balance {inv.BalanceDue:N2}"));

        foreach (var lr in await _db.LabRequests.Where(x => x.ClinicId == clinicId).ToListAsync())
            if (MatchPatient(lr.PatientName, lr.PatientBarcode) && MatchDoctor(lr.DoctorName))
                Results.Add(new HistoryRow(lr.PatientName ?? "", "Laboratory Request", lr.RequestDate, lr.DoctorName ?? "", $"Request #{lr.RequestNo}"));

        foreach (var lr in await _db.LabResults.Where(x => x.ClinicId == clinicId).ToListAsync())
            if (MatchPatient(lr.PatientName, null) && MatchDoctor(lr.DoctorName))
                Results.Add(new HistoryRow(lr.PatientName ?? "", "Laboratory Result", lr.ResultDate, lr.DoctorName ?? "", $"Result #{lr.ResultNo}"));

        foreach (var rr in await _db.RadiologyRequests.Where(x => x.ClinicId == clinicId).ToListAsync())
            if (MatchPatient(rr.PatientName, rr.PatientBarcode) && MatchDoctor(rr.DoctorName))
                Results.Add(new HistoryRow(rr.PatientName ?? "", "Radiology Request", rr.RequestDate, rr.DoctorName ?? "", $"Request #{rr.RequestNo}"));

        foreach (var rr in await _db.RadiologyResults.Where(x => x.ClinicId == clinicId).ToListAsync())
            if (MatchPatient(rr.PatientName, null) && MatchDoctor(rr.DoctorName))
                Results.Add(new HistoryRow(rr.PatientName ?? "", "Radiology Result", rr.ResultDate, rr.DoctorName ?? "", $"Result #{rr.ResultNo}"));

        foreach (var pr in await _db.PharmacyRequests.Where(x => x.ClinicId == clinicId).ToListAsync())
            if (MatchPatient(pr.PatientName, pr.PatientId) && MatchDoctor(pr.DoctorName))
                Results.Add(new HistoryRow(pr.PatientName ?? "", "Pharmacy Request", pr.RequestDate, pr.DoctorName ?? "", $"Request #{pr.RequestNo}"));

        foreach (var pb in await _db.PharmacyBills.Where(x => x.ClinicId == clinicId).ToListAsync())
            if (MatchPatient(pb.PatientName, pb.PatientId) && MatchDoctor(pb.DoctorName))
                Results.Add(new HistoryRow(pb.PatientName ?? "", "Pharmacy Bill", pb.BillDate, pb.DoctorName ?? "", $"Bill #{pb.BillNo}, Total {pb.TotalAmount:N2}"));

        foreach (var sir in await _db.ServiceIncomeRequests.Where(x => x.ClinicId == clinicId).ToListAsync())
            if (MatchPatient(sir.PatientName, sir.PatientBarcode) && MatchDoctor(sir.DoctorName))
                Results.Add(new HistoryRow(sir.PatientName ?? "", "Service Income Request", sir.RequestDate, sir.DoctorName ?? "",
                    $"Request #{sir.RequestNo}, Total {sir.TotalAmount:N2}"));

        foreach (var cr in await _db.CashReceipts.Where(x => x.ClinicId == clinicId).ToListAsync())
            if (MatchPatient(cr.PatientName, cr.PatientId) && MatchDoctor(cr.DoctorName))
                Results.Add(new HistoryRow(cr.PatientName ?? "", "Cash Receipt", cr.ReceiptDate, cr.DoctorName ?? "", $"Amount {cr.Amount:N2}"));

        foreach (var cp in await _db.CashPayments.Where(x => x.ClinicId == clinicId).ToListAsync())
            if (MatchPatient(cp.PayeeName, cp.PatientId) && MatchDoctor(cp.DoctorName))
                Results.Add(new HistoryRow(cp.PayeeName ?? "", "Cash Payment", cp.PaymentDate, cp.DoctorName ?? "", $"Amount {cp.Amount:N2}"));

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
