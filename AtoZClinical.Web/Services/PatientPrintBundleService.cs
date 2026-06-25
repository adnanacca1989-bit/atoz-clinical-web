using System.Text.Json;
using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Services;
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

        var allPatients = await _db.Patients
            .ForClinic(clinicId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        Patient? resolvedPatient = null;
        if (!string.IsNullOrWhiteSpace(patientId))
        {
            resolvedPatient = allPatients.FirstOrDefault(p =>
                string.Equals(p.PatientNo, patientId.Trim(), StringComparison.OrdinalIgnoreCase));
        }
        if (resolvedPatient is null && !string.IsNullOrWhiteSpace(patientName))
        {
            resolvedPatient = allPatients.FirstOrDefault(p =>
                p.FullName?.Contains(patientName.Trim(), StringComparison.OrdinalIgnoreCase) == true);
        }

        var effectiveName = resolvedPatient?.FullName ?? patientName?.Trim() ?? "";
        var effectiveId = resolvedPatient?.PatientNo ?? patientId?.Trim() ?? "";
        bundle.PatientName = effectiveName;
        bundle.PatientId = effectiveId;

        bool NamesMatch(string? recordName, string? filterName) =>
            !string.IsNullOrWhiteSpace(recordName) &&
            !string.IsNullOrWhiteSpace(filterName) &&
            recordName.Contains(filterName, StringComparison.OrdinalIgnoreCase);

        bool MatchPatient(string? name, string? barcode)
        {
            if (!string.IsNullOrWhiteSpace(effectiveId) && !string.IsNullOrWhiteSpace(barcode) &&
                string.Equals(barcode.Trim(), effectiveId, StringComparison.OrdinalIgnoreCase))
                return true;
            if (!string.IsNullOrWhiteSpace(effectiveId) && !string.IsNullOrWhiteSpace(name) &&
                string.Equals(name.Trim(), effectiveName, StringComparison.OrdinalIgnoreCase))
                return true;
            if (NamesMatch(name, effectiveName))
                return true;
            if (resolvedPatient is not null && NamesMatch(name, resolvedPatient.FullName))
                return true;
            if (!string.IsNullOrWhiteSpace(effectiveName) && !string.IsNullOrWhiteSpace(name) &&
                name.Trim().Equals(effectiveName, StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        bool MatchDoctor(string? doc) =>
            string.IsNullOrWhiteSpace(doctorName) || (doc?.Contains(doctorName, StringComparison.OrdinalIgnoreCase) == true);

        bool MatchCashPaymentPatient(CashPayment r) =>
            MatchPatient(r.PayeeName, r.PatientId) ||
            (!string.IsNullOrWhiteSpace(effectiveId) && r.PatientId == effectiveId);

        foreach (var p in allPatients.Where(p => MatchPatient(p.FullName, p.PatientNo) && MatchDoctor(p.DoctorName)))
            bundle.Sections.Add(BuildPatientRegistration(p));

        var labRequests = await _db.LabRequests.Include(r => r.Lines)
            .ForClinic(clinicId).OrderByDescending(r => r.RequestDate).ToListAsync();
        foreach (var r in labRequests.Where(r => MatchPatient(r.PatientName, r.PatientBarcode) && MatchDoctor(r.DoctorName)))
            bundle.Sections.Add(BuildLabRequest(r));

        var labResults = await _db.LabResults.Include(r => r.Lines)
            .ForClinic(clinicId).OrderByDescending(r => r.ResultDate).ToListAsync();
        foreach (var r in labResults.Where(r => MatchPatient(r.PatientName, effectiveId) && MatchDoctor(r.DoctorName)))
            bundle.Sections.Add(BuildLabResult(r));

        var radRequests = await _db.RadiologyRequests.Include(r => r.Lines)
            .ForClinic(clinicId).OrderByDescending(r => r.RequestDate).ToListAsync();
        foreach (var r in radRequests.Where(r => MatchPatient(r.PatientName, r.PatientBarcode) && MatchDoctor(r.DoctorName)))
            bundle.Sections.Add(BuildRadRequest(r));

        var radResults = await _db.RadiologyResults.Include(r => r.Lines)
            .ForClinic(clinicId).OrderByDescending(r => r.ResultDate).ToListAsync();
        foreach (var r in radResults.Where(r => MatchPatient(r.PatientName, effectiveId) && MatchDoctor(r.DoctorName)))
            bundle.Sections.Add(BuildRadResult(r));

        var pharmReqs = await _db.PharmacyRequests.Include(r => r.Lines)
            .ForClinic(clinicId).OrderByDescending(r => r.RequestDate).ToListAsync();
        foreach (var r in pharmReqs.Where(r => MatchPatient(r.PatientName, r.PatientId) && MatchDoctor(r.DoctorName)))
            bundle.Sections.Add(BuildPharmacyRequest(r));

        var pharmBills = await _db.PharmacyBills.Include(r => r.Lines)
            .ForClinic(clinicId).OrderByDescending(r => r.BillDate).ToListAsync();
        foreach (var r in pharmBills.Where(r => MatchPatient(r.PatientName, r.PatientId) && MatchDoctor(r.DoctorName)))
            bundle.Sections.Add(BuildPharmacyBill(r));

        var invoices = await _db.Invoices.Include(r => r.Lines)
            .ForClinic(clinicId).OrderByDescending(r => r.InvoiceDate).ToListAsync();
        foreach (var r in invoices.Where(r => MatchPatient(r.PatientName, r.PatientId) && MatchDoctor(r.DoctorName)))
            bundle.Sections.Add(BuildInvoice(r));

        var prescriptions = await _db.Prescriptions
            .ForClinic(clinicId).OrderByDescending(r => r.DatePrescription).ToListAsync();
        foreach (var r in prescriptions.Where(r => MatchPatient(r.PatientName, effectiveId) && MatchDoctor(r.DoctorName)))
            bundle.Sections.Add(BuildPrescription(r));

        var receipts = await _db.CashReceipts
            .ForClinic(clinicId).OrderByDescending(r => r.ReceiptDate).ToListAsync();
        foreach (var r in receipts.Where(r => MatchPatient(r.PatientName, r.PatientId) && MatchDoctor(r.DoctorName)))
            bundle.Sections.Add(BuildCashReceipt(r));

        var payments = await _db.CashPayments
            .ForClinic(clinicId).OrderByDescending(r => r.PaymentDate).ToListAsync();
        foreach (var r in payments.Where(MatchCashPaymentPatient))
            bundle.Sections.Add(BuildCashPayment(r));

        var patientInvoices = invoices.Where(r => MatchPatient(r.PatientName, r.PatientId) && MatchDoctor(r.DoctorName)).ToList();
        var patientReceipts = receipts.Where(r => MatchPatient(r.PatientName, r.PatientId) && MatchDoctor(r.DoctorName)).ToList();
        var patientCashPayments = payments.Where(r => MatchCashPaymentPatient(r) && MatchDoctor(r.DoctorName)).ToList();
        if (patientInvoices.Count > 0)
            bundle.Sections.Add(BuildArStatement(
                effectiveName ?? patientInvoices[0].PatientName ?? "",
                patientInvoices,
                patientReceipts,
                patientCashPayments));

        return bundle;
    }

    private static PrintSection BuildPatientRegistration(Patient p) => new(
        "Patient Registration",
        Meta(
            ("Patient No", p.PatientNo),
            ("Patient Name", p.FullName),
            ("Gender", p.Gender ?? ""),
            ("Date of Birth", p.DateOfBirth?.ToString("d") ?? ""),
            ("Age", p.AgeYears?.ToString() ?? ""),
            ("Phone", p.Phone ?? ""),
            ("City", p.City ?? ""),
            ("Doctor", p.DoctorName ?? ""),
            ("Specialty", p.Specialty ?? ""),
            ("National ID", p.NationalId ?? ""),
            ("Visit Number", p.VisitNumber ?? ""),
            ("Appointment Date", p.AppointmentDate?.ToString("d") ?? ""),
            ("Status", p.Status)),
        null, [], Footer(("Address", p.Address ?? ""), ("Emergency Contact", p.EmergencyContact ?? "")));

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

    private static PrintSection BuildPrescription(Prescription r)
    {
        var rows = ParseChronicRows(r.ChronicDiseasesJson);
        return new PrintSection(
            "Doctor's Prescription",
            Meta(
                ("Prescription No", r.PrescriptionNo.ToString()),
                ("Date", r.DatePrescription.ToString("d")),
                ("Patient", r.PatientName ?? ""),
                ("Doctor", r.DoctorName ?? ""),
                ("Specialty", r.Specialty ?? ""),
                ("Disease", r.DiseaseName ?? "")),
            rows.Count > 0 ? ["Chronic Disease", "Details"] : null,
            rows,
            Footer(("Diagnosis", r.DiagnosisText ?? "")));
    }

    private static List<string[]> ParseChronicRows(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            var items = JsonSerializer.Deserialize<List<ChronicDiseasePrintRow>>(json) ?? [];
            return items
                .Where(c => !string.IsNullOrWhiteSpace(c.Details))
                .Select(c => new[] { c.DiseaseType ?? "", c.Details ?? "" })
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private sealed class ChronicDiseasePrintRow
    {
        public string? DiseaseType { get; set; }
        public string? Details { get; set; }
    }

    private static PrintSection BuildCashReceipt(CashReceipt r) => new(
        "Cash Receipt",
        Meta(("Receipt No", r.ReceiptNo.ToString()), ("Date", r.ReceiptDate.ToString("d")), ("Patient", r.PatientName ?? ""), ("Doctor", r.DoctorName ?? ""), ("Amount", r.Amount.ToString("N2")), ("Written Amount", r.WrittenAmount)),
        null, [], Footer(("Payment Method", r.PaymentMethod), ("Reference", r.ReferenceNo ?? "")));

    private static PrintSection BuildCashPayment(CashPayment r) => new(
        "Cash Payment",
        Meta(
            ("Payment No", r.PaymentNo.ToString()),
            ("Date", r.PaymentDate.ToString("d")),
            ("Patient", r.PayeeName ?? ""),
            ("MRN", r.PatientId ?? ""),
            ("Doctor", r.DoctorName ?? ""),
            ("Amount", r.Amount.ToString("N2")),
            ("Written Amount", r.WrittenAmount)),
        null, [], Footer(("Payment Method", r.PaymentMethod), ("Description", r.Description ?? "")));

    private static PrintSection BuildArStatement(
        string patient,
        List<Invoice> invoices,
        List<CashReceipt> receipts,
        List<CashPayment> cashPayments)
    {
        var lineTotals = invoices.Select(i => InvoiceArCalculator.ForInvoice(i, receipts, cashPayments, invoices)).ToList();
        var rows = invoices.Select((i, idx) =>
        {
            var totals = lineTotals[idx];
            return new[]
            {
                i.InvoiceNo.ToString(),
                i.InvoiceDate.ToString("d"),
                i.DoctorName ?? "",
                totals.TotalInvoice.ToString("N2"),
                totals.Discount.ToString("N2"),
                totals.NetInvoice.ToString("N2"),
                totals.CashReceipt.ToString("N2"),
                totals.CashPayment.ToString("N2"),
                ArBalanceFormatter.Format(totals.EndingBalance),
                totals.PatientCredit.ToString("N2")
            };
        }).ToList();

        var totalCashReceipt = lineTotals.Sum(t => t.CashReceipt);
        var totalCashPayment = lineTotals.Sum(t => t.CashPayment);
        var totalInvoice = lineTotals.Sum(t => t.TotalInvoice);
        var totalDiscount = lineTotals.Sum(t => t.Discount);
        var totalEndingBalance = invoices
            .Zip(lineTotals, (inv, totals) => (Doctor: inv.DoctorName ?? "", totals))
            .GroupBy(x => x.Doctor)
            .Sum(g =>
            {
                var debits = g.Where(x => x.totals.EndingBalance > 0).ToList();
                if (debits.Count > 0)
                    return debits.Sum(x => x.totals.EndingBalance);
                if (g.Any(x => x.totals.EndingBalance < 0))
                    return g.First(x => x.totals.EndingBalance < 0).totals.EndingBalance;
                return 0m;
            });
        var patientCredit = lineTotals.Count > 0 ? lineTotals.Max(t => t.PatientCredit) : 0m;

        rows.Add(
        [
            "", "", "Totals", totalInvoice.ToString("N2"), totalDiscount.ToString("N2"), "",
            totalCashReceipt.ToString("N2"), totalCashPayment.ToString("N2"),
            ArBalanceFormatter.Format(totalEndingBalance), patientCredit.ToString("N2")
        ]);

        return new PrintSection(
            "Accounts Receivable Statement",
            Meta(("Patient", patient), ("Statement Date", DateTime.Today.ToString("d"))),
            ["Invoice No", "Date", "Doctor", "Total Invoice", "Discount", "Net Invoice", "Cash Receipt (Applied)", "Cash Payment", "Ending Balance (Dr/Cr)", "Patient Credit"],
            rows,
            Footer(
                ("Net Patient Balance", ArBalanceFormatter.Format(totalEndingBalance)),
                ("Patient Credit", patientCredit.ToString("N2"))));
    }

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
