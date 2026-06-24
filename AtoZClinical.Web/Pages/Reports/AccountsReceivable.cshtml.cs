using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Web.Pages.Reports;

public class AccountsReceivableModel : PageModel
{
    private readonly ClinicalDbContext _db;
    private readonly ClinicContextService _clinicContext;

    public AccountsReceivableModel(ClinicalDbContext db, ClinicContextService clinicContext)
    {
        _db = db;
        _clinicContext = clinicContext;
    }

    [BindProperty(SupportsGet = true)]
    public DateTime FromDate { get; set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    [BindProperty(SupportsGet = true)]
    public DateTime ToDate { get; set; } = DateTime.Today;

    [BindProperty(SupportsGet = true)]
    public string? PatientName { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? PatientBarcode { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? DoctorName { get; set; }

    public List<ArRow> Results { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync() => await RunAsync();

    public Task<IActionResult> OnPostRunAsync() => RunAsync();

    public IActionResult OnPostClearAsync() => RedirectToPage();

    private async Task<IActionResult> RunAsync()
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();

        var invoices = await _db.Invoices
            .Where(i => i.ClinicId == clinicId && i.InvoiceDate >= FromDate.Date && i.InvoiceDate <= ToDate.Date)
            .OrderByDescending(i => i.InvoiceDate)
            .ToListAsync();

        if (!string.IsNullOrWhiteSpace(PatientName))
            invoices = invoices.Where(i => i.PatientName?.Contains(PatientName, StringComparison.OrdinalIgnoreCase) == true).ToList();
        if (!string.IsNullOrWhiteSpace(PatientBarcode))
            invoices = invoices.Where(i => i.PatientId?.Equals(PatientBarcode.Trim(), StringComparison.OrdinalIgnoreCase) == true).ToList();
        if (!string.IsNullOrWhiteSpace(DoctorName))
            invoices = invoices.Where(i => i.DoctorName?.Contains(DoctorName, StringComparison.OrdinalIgnoreCase) == true).ToList();

        var patients = await _db.Patients
            .Where(p => p.ClinicId == clinicId)
            .AsNoTracking()
            .ToListAsync();

        var receipts = await _db.CashReceipts
            .Where(c => c.ClinicId == clinicId)
            .ToListAsync();

        var patientPayments = await _db.CashPayments
            .Where(p => p.ClinicId == clinicId)
            .Where(p => p.PayeeName != null || p.PatientId != null)
            .ToListAsync();

        Results = invoices.Select(i =>
        {
            var totals = InvoiceArCalculator.ForInvoice(i, receipts, patientPayments, invoices);
            var aging = (DateTime.Today - i.InvoiceDate.Date).Days;
            var status = totals.EndingBalance <= 0 ? "Paid" : (totals.AmountApplied > 0 ? "Partial" : (i.PaymentStatus ?? "Unpaid"));
            var patient = ResolvePatient(patients, i);

            return new ArRow(
                i.InvoiceNo,
                i.InvoiceDate,
                i.PatientName ?? "",
                i.DoctorName ?? "",
                i.Specialty ?? patient?.Specialty ?? "",
                i.Gender ?? patient?.Gender ?? "",
                i.City ?? patient?.City ?? "",
                patient?.MotherName ?? "",
                patient?.MarriedStatus ?? "",
                patient?.HealthInsuranceName ?? "",
                patient?.HealthInsuranceNumber ?? "",
                patient?.AppointmentDate,
                patient?.AppointmentTime,
                totals.CashReceipt,
                totals.CashPayment,
                totals.TotalInvoice,
                totals.Discount,
                totals.EndingBalance,
                aging,
                status);
        }).ToList();

        return Page();
    }

    private static Patient? ResolvePatient(List<Patient> patients, Invoice invoice)
    {
        if (!string.IsNullOrWhiteSpace(invoice.PatientId))
        {
            var byId = patients.FirstOrDefault(p =>
                string.Equals(p.PatientNo, invoice.PatientId, StringComparison.OrdinalIgnoreCase));
            if (byId is not null) return byId;
        }

        if (!string.IsNullOrWhiteSpace(invoice.PatientName))
        {
            return patients.FirstOrDefault(p =>
                string.Equals(p.FullName, invoice.PatientName, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    public async Task<IActionResult> OnPostExportAsync()
    {
        await RunAsync();
        var bytes = ReportExcelService.Export("Accounts Receivable",
            [
                "Invoice ID", "Invoice Date", "Patient", "Doctor", "Specialty", "Gender", "City",
                "Mother Name", "Married Status", "Health Insurance", "Health Insurance No",
                "Appointment Date", "Appointment Time", "Cash Receipt", "Cash Payment",
                "Total Invoice", "Discount", "Ending Balance", "Aging Days", "Status"
            ],
            Results.Select(r => new object?[]
            {
                r.InvoiceId, r.InvoiceDate, r.Patient, r.Doctor, r.Specialty, r.Gender, r.City,
                r.MotherName, r.MarriedStatus, r.HealthInsurance, r.HealthInsuranceNo,
                r.AppointmentDate?.ToString("d"), FormatTime(r.AppointmentTime),
                r.CashReceipt, r.CashPayment, r.TotalInvoice, r.Discount, r.EndingBalance,
                r.AgingDays, r.Status
            }));
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"AccountsReceivable_{DateTime.Now:yyyyMMdd}.xlsx");
    }

    private static string FormatTime(TimeSpan? time) =>
        time is null ? "" : DateTime.Today.Add(time.Value).ToString("t");

    public sealed record ArRow(
        int InvoiceId,
        DateTime InvoiceDate,
        string Patient,
        string Doctor,
        string Specialty,
        string Gender,
        string City,
        string MotherName,
        string MarriedStatus,
        string HealthInsurance,
        string HealthInsuranceNo,
        DateTime? AppointmentDate,
        TimeSpan? AppointmentTime,
        decimal CashReceipt,
        decimal CashPayment,
        decimal TotalInvoice,
        decimal Discount,
        decimal EndingBalance,
        int AgingDays,
        string Status);
}
