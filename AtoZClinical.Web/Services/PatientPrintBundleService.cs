using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Web.Services;

public sealed class PatientPrintBundleService
{
    private readonly ClinicalDbContext _db;

    public PatientPrintBundleService(ClinicalDbContext db) => _db = db;

    public async Task<PatientPrintBundle> BuildAsync(Guid clinicId, string? patientName, string? patientId, string? doctorName)
    {
        var bundle = new PatientPrintBundle
        {
            PatientName = patientName ?? "",
            PatientId = patientId ?? "",
            DoctorName = doctorName ?? "",
            GeneratedAt = DateTime.Now
        };

        if (string.IsNullOrWhiteSpace(patientName) && string.IsNullOrWhiteSpace(patientId))
            return bundle;

        bool MatchPatient(string? name, string? barcode) =>
            (string.IsNullOrWhiteSpace(patientName) || (name?.Contains(patientName, StringComparison.OrdinalIgnoreCase) == true)) &&
            (string.IsNullOrWhiteSpace(patientId) || (barcode?.Contains(patientId, StringComparison.OrdinalIgnoreCase) == true));

        bool MatchDoctor(string? doc) =>
            string.IsNullOrWhiteSpace(doctorName) || (doc?.Contains(doctorName, StringComparison.OrdinalIgnoreCase) == true);

        var labRequests = await _db.LabRequests.Include(r => r.Lines)
            .Where(r => r.ClinicId == clinicId).OrderByDescending(r => r.RequestDate).ToListAsync();
        foreach (var r in labRequests.Where(r => MatchPatient(r.PatientName, r.PatientBarcode) && MatchDoctor(r.DoctorName)))
            bundle.Sections.Add(BuildLabRequest(r));

        var labResults = await _db.LabResults.Include(r => r.Lines)
            .Where(r => r.ClinicId == clinicId).OrderByDescending(r => r.ResultDate).ToListAsync();
        foreach (var r in labResults.Where(r => MatchPatient(r.PatientName, null) && MatchDoctor(r.DoctorName)))
            bundle.Sections.Add(BuildLabResult(r));

        var radRequests = await _db.RadiologyRequests.Include(r => r.Lines)
            .Where(r => r.ClinicId == clinicId).OrderByDescending(r => r.RequestDate).ToListAsync();
        foreach (var r in radRequests.Where(r => MatchPatient(r.PatientName, r.PatientBarcode) && MatchDoctor(r.DoctorName)))
            bundle.Sections.Add(BuildRadRequest(r));

        var radResults = await _db.RadiologyResults.Include(r => r.Lines)
            .Where(r => r.ClinicId == clinicId).OrderByDescending(r => r.ResultDate).ToListAsync();
        foreach (var r in radResults.Where(r => MatchPatient(r.PatientName, null) && MatchDoctor(r.DoctorName)))
            bundle.Sections.Add(BuildRadResult(r));

        var pharmReqs = await _db.PharmacyRequests.Include(r => r.Lines)
            .Where(r => r.ClinicId == clinicId).OrderByDescending(r => r.RequestDate).ToListAsync();
        foreach (var r in pharmReqs.Where(r => MatchPatient(r.PatientName, r.PatientId) && MatchDoctor(r.DoctorName)))
            bundle.Sections.Add(BuildPharmacyRequest(r));

        var pharmBills = await _db.PharmacyBills.Include(r => r.Lines)
            .Where(r => r.ClinicId == clinicId).OrderByDescending(r => r.BillDate).ToListAsync();
        foreach (var r in pharmBills.Where(r => MatchPatient(r.PatientName, r.PatientId) && MatchDoctor(r.DoctorName)))
            bundle.Sections.Add(BuildPharmacyBill(r));

        var invoices = await _db.Invoices.Include(r => r.Lines)
            .Where(r => r.ClinicId == clinicId).OrderByDescending(r => r.InvoiceDate).ToListAsync();
        foreach (var r in invoices.Where(r => MatchPatient(r.PatientName, r.PatientId) && MatchDoctor(r.DoctorName)))
            bundle.Sections.Add(BuildInvoice(r));

        var prescriptions = await _db.Prescriptions
            .Where(r => r.ClinicId == clinicId).OrderByDescending(r => r.DatePrescription).ToListAsync();
        foreach (var r in prescriptions.Where(r => MatchPatient(r.PatientName, null) && MatchDoctor(r.DoctorName)))
            bundle.Sections.Add(BuildPrescription(r));

        var receipts = await _db.CashReceipts
            .Where(r => r.ClinicId == clinicId).OrderByDescending(r => r.ReceiptDate).ToListAsync();
        foreach (var r in receipts.Where(r => MatchPatient(r.PatientName, r.PatientId) && MatchDoctor(r.DoctorName)))
            bundle.Sections.Add(BuildCashReceipt(r));

        var payments = await _db.CashPayments
            .Where(r => r.ClinicId == clinicId).OrderByDescending(r => r.PaymentDate).ToListAsync();
        foreach (var r in payments.Where(r => MatchPatient(r.PayeeName, null)))
            bundle.Sections.Add(BuildCashPayment(r));

        var patientInvoices = invoices.Where(r => MatchPatient(r.PatientName, r.PatientId) && MatchDoctor(r.DoctorName)).ToList();
        if (patientInvoices.Count > 0)
            bundle.Sections.Add(BuildArStatement(patientName ?? patientInvoices[0].PatientName ?? "", patientInvoices, receipts));

        return bundle;
    }

    private static PrintSection BuildLabRequest(LabRequest r) => new(
        "Laboratory Request",
        Meta(("Request No", r.RequestNo.ToString()), ("Date", r.RequestDate.ToString("d")), ("Patient", r.PatientName ?? ""), ("Doctor", r.DoctorName ?? ""), ("Specialty", r.Specialty ?? "")),
        ["No", "Test Code", "Test Name", "Category", "QTY", "Fee", "Total"],
        r.Lines.OrderBy(l => l.LineNo).Select(l => new[] { l.LineNo.ToString(), l.TestCode ?? "", l.TestName ?? "", l.Category ?? "", l.Qty.ToString(), l.Fee.ToString("N2"), l.Total.ToString("N2") }).ToList(),
        Footer(("Total", r.Lines.Sum(l => l.Total).ToString("N2"))));

    private static PrintSection BuildLabResult(LabResult r) => new(
        "Laboratory Result",
        Meta(("Result No", r.ResultNo.ToString()), ("Request No", r.RequestNo?.ToString() ?? ""), ("Date", r.ResultDate.ToString("d")), ("Patient", r.PatientName ?? ""), ("Doctor", r.DoctorName ?? "")),
        ["No", "Test Code", "Test Name", "Category", "Result", "Normal Range", "Unit"],
        r.Lines.OrderBy(l => l.LineNo).Select(l => new[] { l.LineNo.ToString(), l.TestCode ?? "", l.TestName ?? "", l.Category ?? "", l.Result ?? "", l.NormalRange ?? "", l.Unit ?? "" }).ToList(),
        null);

    private static PrintSection BuildRadRequest(RadiologyRequest r) => new(
        "Radiology Request",
        Meta(("Request No", r.RequestNo.ToString()), ("Date", r.RequestDate.ToString("d")), ("Patient", r.PatientName ?? ""), ("Doctor", r.DoctorName ?? ""), ("Specialty", r.Specialty ?? "")),
        ["No", "Code", "Name", "Category", "QTY", "Fee", "Total"],
        r.Lines.OrderBy(l => l.LineNo).Select(l => new[] { l.LineNo.ToString(), l.TestCode ?? "", l.TestName ?? "", l.Category ?? "", l.Qty.ToString(), l.Fee.ToString("N2"), l.Total.ToString("N2") }).ToList(),
        Footer(("Total", r.Lines.Sum(l => l.Total).ToString("N2"))));

    private static PrintSection BuildRadResult(RadiologyResult r) => new(
        "Radiology Result",
        Meta(("Result No", r.ResultNo.ToString()), ("Request No", r.RequestNo?.ToString() ?? ""), ("Date", r.ResultDate.ToString("d")), ("Patient", r.PatientName ?? ""), ("Doctor", r.DoctorName ?? "")),
        ["No", "Code", "Name", "Category", "Result", "Impression"],
        r.Lines.OrderBy(l => l.LineNo).Select(l => new[] { l.LineNo.ToString(), l.TestCode ?? "", l.TestName ?? "", l.Category ?? "", l.Result ?? "", l.Impression ?? l.Findings ?? "" }).ToList(),
        null);

    private static PrintSection BuildPharmacyRequest(PharmacyRequest r) => new(
        "Pharmacy Request",
        Meta(("Request No", r.RequestNo.ToString()), ("Date", r.RequestDate.ToString("d")), ("Patient", r.PatientName ?? ""), ("Doctor", r.DoctorName ?? "")),
        ["No", "Medicine", "Dosage", "UOM", "QTY"],
        r.Lines.OrderBy(l => l.LineNo).Select(l => new[] { l.LineNo.ToString(), l.MedicineName ?? "", l.Dosage ?? "", l.Uom ?? "", l.Qty.ToString() }).ToList(),
        null);

    private static PrintSection BuildPharmacyBill(PharmacyBill r) => new(
        "Pharmacy Bill",
        Meta(("Bill No", r.BillNo.ToString()), ("Date", r.BillDate.ToString("d")), ("Patient", r.PatientName ?? ""), ("Doctor", r.DoctorName ?? ""), ("Status", r.PaymentStatus ?? "")),
        ["No", "Medicine", "QTY", "Unit Price", "Total"],
        r.Lines.OrderBy(l => l.LineNo).Select(l => new[] { l.LineNo.ToString(), l.MedicineName ?? "", l.Qty.ToString(), l.UnitPrice.ToString("N2"), l.LineTotal.ToString("N2") }).ToList(),
        Footer(("Total", r.Lines.Sum(l => l.LineTotal).ToString("N2"))));

    private static PrintSection BuildInvoice(Invoice r) => new(
        "Invoice / Billing",
        Meta(("Invoice No", r.InvoiceNo.ToString()), ("Date", r.InvoiceDate.ToString("d")), ("Patient", r.PatientName ?? ""), ("MRN", r.PatientId ?? ""), ("Doctor", r.DoctorName ?? ""), ("Specialty", r.Specialty ?? ""), ("Payment Method", r.PaymentMethod), ("Status", r.PaymentStatus ?? "")),
        ["No", "Service", "QTY", "Rate", "Amount"],
        r.Lines.OrderBy(l => l.LineNo).Select(l => new[] { l.LineNo.ToString(), l.ServiceName ?? "", l.Qty.ToString(), l.UnitFee.ToString("N2"), l.LineTotal.ToString("N2") }).ToList(),
        Footer(("Subtotal", r.SubTotal.ToString("N2")), ("Discount", r.Discount.ToString("N2")), ("Total", r.TotalAmount.ToString("N2")), ("Amount Paid", r.AmountPaid.ToString("N2")), ("Balance Due", r.BalanceDue.ToString("N2"))));

    private static PrintSection BuildPrescription(Prescription r) => new(
        "Doctor's Prescription",
        Meta(("Prescription No", r.PrescriptionNo.ToString()), ("Date", r.DatePrescription.ToString("d")), ("Patient", r.PatientName ?? ""), ("Doctor", r.DoctorName ?? ""), ("Specialty", r.Specialty ?? ""), ("Disease", r.DiseaseName ?? "")),
        null,
        [],
        Footer(("Diagnosis", r.DiagnosisText ?? "")));

    private static PrintSection BuildCashReceipt(CashReceipt r) => new(
        "Cash Receipt",
        Meta(("Receipt No", r.ReceiptNo.ToString()), ("Date", r.ReceiptDate.ToString("d")), ("Patient", r.PatientName ?? ""), ("Doctor", r.DoctorName ?? ""), ("Amount", r.Amount.ToString("N2")), ("Written Amount", r.WrittenAmount)),
        null, [], Footer(("Payment Method", r.PaymentMethod), ("Reference", r.ReferenceNo ?? "")));

    private static PrintSection BuildCashPayment(CashPayment r) => new(
        "Cash Payment",
        Meta(("Payment No", r.PaymentNo.ToString()), ("Date", r.PaymentDate.ToString("d")), ("Payee", r.PayeeName ?? ""), ("Amount", r.Amount.ToString("N2")), ("Written Amount", r.WrittenAmount)),
        null, [], Footer(("Payment Method", r.PaymentMethod), ("Description", r.Description ?? "")));

    private static PrintSection BuildArStatement(string patient, List<Invoice> invoices, List<CashReceipt> receipts) => new(
        "Accounts Receivable Statement",
        Meta(("Patient", patient), ("Statement Date", DateTime.Today.ToString("d"))),
        ["Invoice No", "Date", "Doctor", "Invoice Amount", "Paid", "Balance"],
        invoices.Select(i =>
        {
            var paid = receipts.Where(r => r.PatientName == i.PatientName).Sum(r => r.Amount);
            return new[] { i.InvoiceNo.ToString(), i.InvoiceDate.ToString("d"), i.DoctorName ?? "", i.TotalAmount.ToString("N2"), paid.ToString("N2"), i.BalanceDue.ToString("N2") };
        }).ToList(),
        Footer(("Total Balance Due", invoices.Sum(i => i.BalanceDue).ToString("N2"))));

    private static Dictionary<string, string> Meta(params (string Key, string Value)[] items) =>
        items.ToDictionary(x => x.Key, x => x.Value);

    private static Dictionary<string, string>? Footer(params (string Key, string Value)[] items) =>
        items.Length == 0 ? null : items.ToDictionary(x => x.Key, x => x.Value);
}

public sealed class PatientPrintBundle
{
    public string PatientName { get; set; } = "";
    public string PatientId { get; set; } = "";
    public string DoctorName { get; set; } = "";
    public DateTime GeneratedAt { get; set; }
    public List<PrintSection> Sections { get; set; } = [];
}

public sealed class PrintSection
{
    public PrintSection(string title, Dictionary<string, string> headerFields, string[]? columns, List<string[]> rows, Dictionary<string, string>? footerFields)
    {
        Title = title;
        HeaderFields = headerFields;
        Columns = columns;
        Rows = rows;
        FooterFields = footerFields;
    }

    public string Title { get; }
    public Dictionary<string, string> HeaderFields { get; }
    public string[]? Columns { get; }
    public List<string[]> Rows { get; }
    public Dictionary<string, string>? FooterFields { get; }
}
