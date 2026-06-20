using System.IO.Compression;
using AtoZClinical.Infrastructure.Data;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Web.Services;

public sealed class ClinicBackupService
{
    private readonly ClinicalDbContext _db;

    public ClinicBackupService(ClinicalDbContext db) => _db = db;

    public async Task<byte[]> ExportExcelFilesZipAsync(Guid clinicId, string clinicName)
    {
        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            await AddSheetFileAsync(archive, "Patients.xlsx", await ExportPatientsAsync(clinicId));
            await AddSheetFileAsync(archive, "Doctors.xlsx", ExportDoctors(await _db.Doctors.Where(d => d.ClinicId == clinicId).ToListAsync()));
            await AddSheetFileAsync(archive, "ServiceIncomes.xlsx", ExportServiceIncomes(await _db.ServiceIncomes.Where(s => s.ClinicId == clinicId).ToListAsync()));
            await AddSheetFileAsync(archive, "LabTests.xlsx", ExportLabTests(await _db.LabTests.Where(t => t.ClinicId == clinicId).ToListAsync()));
            await AddSheetFileAsync(archive, "LabRequests.xlsx", await ExportLabRequestsAsync(clinicId));
            await AddSheetFileAsync(archive, "RadiologyTests.xlsx", ExportRadiologyTests(await _db.RadiologyTests.Where(t => t.ClinicId == clinicId).ToListAsync()));
            await AddSheetFileAsync(archive, "RadiologyRequests.xlsx", await ExportRadiologyRequestsAsync(clinicId));
            await AddSheetFileAsync(archive, "Prescriptions.xlsx", ExportPrescriptions(await _db.Prescriptions.Where(p => p.ClinicId == clinicId).ToListAsync()));
            await AddSheetFileAsync(archive, "Invoices.xlsx", await ExportInvoicesAsync(clinicId));
            await AddSheetFileAsync(archive, "CashReceipts.xlsx", ExportCashReceipts(await _db.CashReceipts.Where(c => c.ClinicId == clinicId).ToListAsync()));
            await AddSheetFileAsync(archive, "CashPayments.xlsx", ExportCashPayments(await _db.CashPayments.Where(c => c.ClinicId == clinicId).ToListAsync()));
            await AddSheetFileAsync(archive, "PharmacyRequests.xlsx", await ExportPharmacyRequestsAsync(clinicId));
            await AddSheetFileAsync(archive, "PharmacyBills.xlsx", await ExportPharmacyBillsAsync(clinicId));
            await AddSheetFileAsync(archive, "ChartOfAccounts.xlsx", ExportChartAccounts(await _db.ChartAccounts.Where(a => a.ClinicId == clinicId).ToListAsync()));
            await AddSheetFileAsync(archive, "AuditLog.xlsx", ExportAuditLog(await _db.AuditLogEntries.Where(a => a.ClinicId == clinicId).OrderByDescending(a => a.DateTime).ToListAsync()));
        }

        zipStream.Position = 0;
        return zipStream.ToArray();
    }

    public async Task<byte[]> ExportWorkbookAsync(Guid clinicId)
    {
        using var workbook = new XLWorkbook();
        AddTableSheet(workbook, "Patients", (await _db.Patients.Where(p => p.ClinicId == clinicId).OrderBy(p => p.PatientNo).ToListAsync())
            .Select(p => new { p.PatientNo, Name = p.FullName, p.Gender, p.Phone, p.City, p.Status }));
        AddTableSheet(workbook, "Doctors", (await _db.Doctors.Where(d => d.ClinicId == clinicId).ToListAsync())
            .Select(d => new { d.DoctorNo, d.Name, d.Specialty, d.ConsultationFee }));
        AddTableSheet(workbook, "PharmacyRequests", (await _db.PharmacyRequests.Include(r => r.Lines).Where(r => r.ClinicId == clinicId).ToListAsync())
            .SelectMany(r => r.Lines.Select(l => new { r.RequestNo, r.RequestDate, r.PatientName, l.MedicineName, l.Qty, l.Total })));
        AddTableSheet(workbook, "PharmacyBills", (await _db.PharmacyBills.Include(b => b.Lines).Where(b => b.ClinicId == clinicId).ToListAsync())
            .SelectMany(b => b.Lines.Select(l => new { b.BillNo, b.BillDate, b.PatientName, l.MedicineName, l.Qty, l.LineTotal, b.TotalAmount })));
        AddTableSheet(workbook, "Invoices", (await _db.Invoices.Include(i => i.Lines).Where(i => i.ClinicId == clinicId).ToListAsync())
            .Select(i => new { i.InvoiceNo, i.InvoiceDate, i.PatientName, i.TotalAmount, i.BalanceDue }));
        AddTableSheet(workbook, "CashReceipts", (await _db.CashReceipts.Where(c => c.ClinicId == clinicId).ToListAsync())
            .Select(c => new { c.ReceiptNo, c.ReceiptDate, c.PatientName, c.Amount, c.PaymentMethod }));
        AddTableSheet(workbook, "AuditLog", (await _db.AuditLogEntries.Where(a => a.ClinicId == clinicId).OrderByDescending(a => a.DateTime).Take(5000).ToListAsync())
            .Select(a => new { a.Type, a.DateTime, a.UserName, a.FormName, a.Details }));

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static async Task AddSheetFileAsync(ZipArchive archive, string entryName, byte[] content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var entryStream = entry.Open();
        await entryStream.WriteAsync(content);
    }

    private static void AddTableSheet<T>(XLWorkbook workbook, string name, IEnumerable<T> rows)
    {
        var ws = workbook.Worksheets.Add(name);
        var list = rows.ToList();
        if (list.Count == 0)
        {
            ws.Cell(1, 1).Value = "No records";
            return;
        }
        ws.Cell(1, 1).InsertTable(list);
        ws.Columns().AdjustToContents();
    }

    private static byte[] ToBytes(Action<IXLWorksheet> build)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Data");
        build(ws);
        ws.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private async Task<byte[]> ExportPatientsAsync(Guid clinicId)
    {
        var rows = await _db.Patients.Where(p => p.ClinicId == clinicId).OrderBy(p => p.PatientNo).ToListAsync();
        return ToBytes(ws =>
        {
            ws.Cell(1, 1).InsertTable(rows.Select(p => new
            {
                p.PatientNo,
                Name = p.FullName,
                p.Gender,
                p.DateOfBirth,
                p.Phone,
                p.City,
                p.DoctorName,
                p.Specialty,
                p.Status
            }));
        });
    }

    private static byte[] ExportDoctors(List<Core.Entities.Doctor> rows) => ToBytes(ws =>
    {
        ws.Cell(1, 1).InsertTable(rows.Select(d => new { d.DoctorNo, d.Name, d.Specialty, d.Phone, d.Email, d.ConsultationFee }));
    });

    private static byte[] ExportServiceIncomes(List<Core.Entities.ServiceIncome> rows) => ToBytes(ws =>
    {
        ws.Cell(1, 1).InsertTable(rows.Select(s => new { s.ServiceNo, s.Name, s.AccountName, s.Fee }));
    });

    private static byte[] ExportLabTests(List<Core.Entities.LabTest> rows) => ToBytes(ws =>
    {
        ws.Cell(1, 1).InsertTable(rows.Select(t => new { t.TestNo, t.TestCode, t.TestName, t.Category, t.Fee, t.Note }));
    });

    private async Task<byte[]> ExportLabRequestsAsync(Guid clinicId)
    {
        var rows = await _db.LabRequests.Include(r => r.Lines).Where(r => r.ClinicId == clinicId).ToListAsync();
        return ToBytes(ws =>
        {
            ws.Cell(1, 1).InsertTable(rows.SelectMany(r => r.Lines.Select(l => new
            {
                r.RequestNo,
                r.RequestDate,
                r.PatientName,
                r.DoctorName,
                l.TestCode,
                l.TestName,
                l.Qty,
                l.Fee,
                l.Total
            })));
        });
    }

    private static byte[] ExportRadiologyTests(List<Core.Entities.RadiologyTest> rows) => ToBytes(ws =>
    {
        ws.Cell(1, 1).InsertTable(rows.Select(t => new { t.TestNo, t.TestCode, t.TestName, t.Category, t.Fee, t.Note }));
    });

    private async Task<byte[]> ExportRadiologyRequestsAsync(Guid clinicId)
    {
        var rows = await _db.RadiologyRequests.Include(r => r.Lines).Where(r => r.ClinicId == clinicId).ToListAsync();
        return ToBytes(ws =>
        {
            ws.Cell(1, 1).InsertTable(rows.SelectMany(r => r.Lines.Select(l => new
            {
                r.RequestNo,
                r.RequestDate,
                r.PatientName,
                r.DoctorName,
                l.TestCode,
                l.TestName,
                l.Qty,
                l.Fee,
                l.Total
            })));
        });
    }

    private static byte[] ExportPrescriptions(List<Core.Entities.Prescription> rows) => ToBytes(ws =>
    {
        ws.Cell(1, 1).InsertTable(rows.Select(p => new
        {
            p.PrescriptionNo,
            p.DatePrescription,
            p.PatientName,
            p.DoctorName,
            p.Specialty,
            p.DiseaseName,
            p.DiagnosisText
        }));
    });

    private async Task<byte[]> ExportInvoicesAsync(Guid clinicId)
    {
        var rows = await _db.Invoices.Include(i => i.Lines).Where(i => i.ClinicId == clinicId).ToListAsync();
        return ToBytes(ws =>
        {
            ws.Cell(1, 1).InsertTable(rows.SelectMany(i => i.Lines.Select(l => new
            {
                i.InvoiceNo,
                i.InvoiceDate,
                i.PatientName,
                i.DoctorName,
                l.ServiceName,
                l.Qty,
                l.UnitFee,
                l.LineTotal,
                i.TotalAmount,
                i.BalanceDue
            })));
        });
    }

    private static byte[] ExportCashReceipts(List<Core.Entities.CashReceipt> rows) => ToBytes(ws =>
    {
        ws.Cell(1, 1).InsertTable(rows.Select(c => new
        {
            c.ReceiptNo,
            c.ReceiptDate,
            c.PatientName,
            c.PatientId,
            c.DoctorName,
            c.Amount,
            c.PaymentMethod,
            c.BalanceDue
        }));
    });

    private static byte[] ExportCashPayments(List<Core.Entities.CashPayment> rows) => ToBytes(ws =>
    {
        ws.Cell(1, 1).InsertTable(rows.Select(c => new
        {
            c.PaymentNo,
            c.PaymentDate,
            c.PayeeName,
            c.ChartAccountName,
            c.Amount,
            c.PaymentMethod,
            c.ReferenceNo
        }));
    });

    private async Task<byte[]> ExportPharmacyRequestsAsync(Guid clinicId)
    {
        var rows = await _db.PharmacyRequests.Include(r => r.Lines).Where(r => r.ClinicId == clinicId).ToListAsync();
        return ToBytes(ws =>
        {
            ws.Cell(1, 1).InsertTable(rows.SelectMany(r => r.Lines.Select(l => new
            {
                r.RequestNo,
                r.RequestDate,
                r.PrescriptionNo,
                r.PatientName,
                r.DoctorName,
                l.MedicineCode,
                l.MedicineName,
                l.Dosage,
                l.Qty,
                l.UnitPrice,
                l.Total
            })));
        });
    }

    private async Task<byte[]> ExportPharmacyBillsAsync(Guid clinicId)
    {
        var rows = await _db.PharmacyBills.Include(b => b.Lines).Where(b => b.ClinicId == clinicId).ToListAsync();
        return ToBytes(ws =>
        {
            ws.Cell(1, 1).InsertTable(rows.SelectMany(b => b.Lines.Select(l => new
            {
                b.BillNo,
                b.BillDate,
                b.RequestNo,
                b.PatientName,
                b.DoctorName,
                l.MedicineCode,
                l.MedicineName,
                l.Dosage,
                l.Qty,
                l.UnitPrice,
                l.LineTotal,
                b.TotalAmount,
                b.BalanceDue
            })));
        });
    }

    private static byte[] ExportChartAccounts(List<Core.Entities.ChartAccount> rows) => ToBytes(ws =>
    {
        ws.Cell(1, 1).InsertTable(rows.Select(a => new { a.AccountNo, a.Name, a.CategoryType, a.DetailType, a.Description }));
    });

    private static byte[] ExportAuditLog(List<Core.Entities.AuditLogEntry> rows) => ToBytes(ws =>
    {
        ws.Cell(1, 1).InsertTable(rows.Select(a => new { a.Type, a.DateTime, a.UserName, a.FormName, a.Details }));
    });
}
