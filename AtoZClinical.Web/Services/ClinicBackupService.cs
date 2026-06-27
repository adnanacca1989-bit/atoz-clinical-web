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
            await AddSheetFileAsync(archive, "Doctors.xlsx", ExportDoctors(await _db.Doctors.ForClinic(clinicId).ToListAsync()));
            await AddSheetFileAsync(archive, "ServiceIncomes.xlsx", ExportServiceIncomes(await _db.ServiceIncomes.ForClinic(clinicId).ToListAsync()));
            await AddSheetFileAsync(archive, "LabTests.xlsx", ExportLabTests(await _db.LabTests.ForClinic(clinicId).ToListAsync()));
            await AddSheetFileAsync(archive, "LabRequests.xlsx", await ExportLabRequestsAsync(clinicId));
            await AddSheetFileAsync(archive, "RadiologyTests.xlsx", ExportRadiologyTests(await _db.RadiologyTests.ForClinic(clinicId).ToListAsync()));
            await AddSheetFileAsync(archive, "RadiologyRequests.xlsx", await ExportRadiologyRequestsAsync(clinicId));
            await AddSheetFileAsync(archive, "Prescriptions.xlsx", ExportPrescriptions(await _db.Prescriptions.ForClinic(clinicId).ToListAsync()));
            await AddSheetFileAsync(archive, "Invoices.xlsx", await ExportInvoicesAsync(clinicId));
            await AddSheetFileAsync(archive, "CashReceipts.xlsx", ExportCashReceipts(await _db.CashReceipts.ForClinic(clinicId).ToListAsync()));
            await AddSheetFileAsync(archive, "CashPayments.xlsx", ExportCashPayments(await _db.CashPayments.ForClinic(clinicId).ToListAsync()));
            await AddSheetFileAsync(archive, "PharmacyRequests.xlsx", await ExportPharmacyRequestsAsync(clinicId));
            await AddSheetFileAsync(archive, "PharmacyBills.xlsx", await ExportPharmacyBillsAsync(clinicId));
            await AddSheetFileAsync(archive, "ChartOfAccounts.xlsx", ExportChartAccounts(await _db.ChartAccounts.ForClinic(clinicId).ToListAsync()));
            await AddSheetFileAsync(archive, "AuditLog.xlsx", ExportAuditLog(await _db.AuditLogEntries.ForClinic(clinicId).OrderByDescending(a => a.DateTime).ToListAsync()));
        }

        zipStream.Position = 0;
        return zipStream.ToArray();
    }

    public async Task<byte[]> ExportWorkbookAsync(Guid clinicId)
    {
        using var workbook = new XLWorkbook();
        AddTableSheet(workbook, "Patients", (await _db.Patients.ForClinic(clinicId).OrderBy(p => p.PatientNo).ToListAsync())
            .Select(p => new { p.PatientNo, Name = p.FullName, p.Gender, p.Phone, p.City, p.Status }));
        AddTableSheet(workbook, "Doctors", (await _db.Doctors.ForClinic(clinicId).ToListAsync())
            .Select(d => new { d.DoctorNo, d.Name, d.Specialty, d.ConsultationFee }));
        AddTableSheet(workbook, "PharmacyRequests", (await _db.PharmacyRequests.Include(r => r.Lines).ForClinic(clinicId).ToListAsync())
            .SelectMany(r => r.Lines.Count > 0
                ? r.Lines.Select(l => new { r.RequestNo, r.RequestDate, r.PatientName, l.MedicineName, l.Qty, l.Total })
                : new[] { new { r.RequestNo, r.RequestDate, r.PatientName, MedicineName = "", Qty = 0, Total = 0m } }));
        AddTableSheet(workbook, "PharmacyBills", (await _db.PharmacyBills.Include(b => b.Lines).ForClinic(clinicId).ToListAsync())
            .SelectMany(b => b.Lines.Count > 0
                ? b.Lines.Select(l => new { b.BillNo, b.BillDate, b.PatientName, l.MedicineName, l.Qty, l.LineTotal, b.TotalAmount })
                : new[] { new { b.BillNo, b.BillDate, b.PatientName, MedicineName = "", Qty = 0, LineTotal = 0m, b.TotalAmount } }));
        AddTableSheet(workbook, "Invoices", (await _db.Invoices.Include(i => i.Lines).ForClinic(clinicId).ToListAsync())
            .Select(i => new { i.InvoiceNo, i.InvoiceDate, i.PatientName, i.TotalAmount, i.BalanceDue }));
        AddTableSheet(workbook, "CashReceipts", (await _db.CashReceipts.ForClinic(clinicId).ToListAsync())
            .Select(c => new { c.ReceiptNo, c.ReceiptDate, c.PatientName, c.Amount, c.PaymentMethod }));
        AddTableSheet(workbook, "AuditLog", (await _db.AuditLogEntries.ForClinic(clinicId).OrderByDescending(a => a.DateTime).Take(5000).ToListAsync())
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

    private static void InsertTableOrEmpty<T>(IXLWorksheet ws, IEnumerable<T> rows)
    {
        var list = rows.ToList();
        if (list.Count == 0)
        {
            ws.Cell(1, 1).Value = "No records";
            return;
        }
        ws.Cell(1, 1).InsertTable(list);
    }

    private async Task<byte[]> ExportPatientsAsync(Guid clinicId)
    {
        var rows = await _db.Patients.ForClinic(clinicId).OrderBy(p => p.PatientNo).ToListAsync();
        return ToBytes(ws =>
        {
            InsertTableOrEmpty(ws, rows.Select(p => new
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
        InsertTableOrEmpty(ws, rows.Select(d => new { d.DoctorNo, d.Name, d.Specialty, d.Phone, d.Email, d.ConsultationFee }));
    });

    private static byte[] ExportServiceIncomes(List<Core.Entities.ServiceIncome> rows) => ToBytes(ws =>
    {
        InsertTableOrEmpty(ws, rows.Select(s => new { s.ServiceNo, s.Name, s.AccountName, s.Fee }));
    });

    private static byte[] ExportLabTests(List<Core.Entities.LabTest> rows) => ToBytes(ws =>
    {
        InsertTableOrEmpty(ws, rows.Select(t => new { t.TestNo, t.TestCode, t.TestName, t.Category, t.Fee, t.Note }));
    });

    private async Task<byte[]> ExportLabRequestsAsync(Guid clinicId)
    {
        var rows = await _db.LabRequests.Include(r => r.Lines).ForClinic(clinicId).ToListAsync();
        return ToBytes(ws =>
        {
            InsertTableOrEmpty(ws, rows.SelectMany(r => r.Lines.Count > 0
                ? r.Lines.Select(l => new
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
                })
                : new[]
                {
                    new
                    {
                        r.RequestNo,
                        r.RequestDate,
                        r.PatientName,
                        r.DoctorName,
                        TestCode = "",
                        TestName = "",
                        Qty = 0,
                        Fee = 0m,
                        Total = 0m
                    }
                }));
        });
    }

    private static byte[] ExportRadiologyTests(List<Core.Entities.RadiologyTest> rows) => ToBytes(ws =>
    {
        InsertTableOrEmpty(ws, rows.Select(t => new { t.TestNo, t.TestCode, t.TestName, t.Category, t.Fee, t.Note }));
    });

    private async Task<byte[]> ExportRadiologyRequestsAsync(Guid clinicId)
    {
        var rows = await _db.RadiologyRequests.Include(r => r.Lines).ForClinic(clinicId).ToListAsync();
        return ToBytes(ws =>
        {
            InsertTableOrEmpty(ws, rows.SelectMany(r => r.Lines.Count > 0
                ? r.Lines.Select(l => new
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
                })
                : new[]
                {
                    new
                    {
                        r.RequestNo,
                        r.RequestDate,
                        r.PatientName,
                        r.DoctorName,
                        TestCode = "",
                        TestName = "",
                        Qty = 0,
                        Fee = 0m,
                        Total = 0m
                    }
                }));
        });
    }

    private static byte[] ExportPrescriptions(List<Core.Entities.Prescription> rows) => ToBytes(ws =>
    {
        InsertTableOrEmpty(ws, rows.Select(p => new
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
        var rows = await _db.Invoices.Include(i => i.Lines).ForClinic(clinicId).ToListAsync();
        return ToBytes(ws =>
        {
            InsertTableOrEmpty(ws, rows.SelectMany(i => i.Lines.Count > 0
                ? i.Lines.Select(l => new
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
                })
                : new[]
                {
                    new
                    {
                        i.InvoiceNo,
                        i.InvoiceDate,
                        i.PatientName,
                        i.DoctorName,
                        ServiceName = "",
                        Qty = 0,
                        UnitFee = 0m,
                        LineTotal = 0m,
                        i.TotalAmount,
                        i.BalanceDue
                    }
                }));
        });
    }

    private static byte[] ExportCashReceipts(List<Core.Entities.CashReceipt> rows) => ToBytes(ws =>
    {
        InsertTableOrEmpty(ws, rows.Select(c => new
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
        InsertTableOrEmpty(ws, rows.Select(c => new
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
        var rows = await _db.PharmacyRequests.Include(r => r.Lines).ForClinic(clinicId).ToListAsync();
        return ToBytes(ws =>
        {
            InsertTableOrEmpty(ws, rows.SelectMany(r => r.Lines.Count > 0
                ? r.Lines.Select(l => new
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
                })
                : new[]
                {
                    new
                    {
                        r.RequestNo,
                        r.RequestDate,
                        r.PrescriptionNo,
                        r.PatientName,
                        r.DoctorName,
                        MedicineCode = "",
                        MedicineName = "",
                        Dosage = "",
                        Qty = 0,
                        UnitPrice = 0m,
                        Total = 0m
                    }
                }));
        });
    }

    private async Task<byte[]> ExportPharmacyBillsAsync(Guid clinicId)
    {
        var rows = await _db.PharmacyBills.Include(b => b.Lines).ForClinic(clinicId).ToListAsync();
        return ToBytes(ws =>
        {
            InsertTableOrEmpty(ws, rows.SelectMany(b => b.Lines.Count > 0
                ? b.Lines.Select(l => new
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
                })
                : new[]
                {
                    new
                    {
                        b.BillNo,
                        b.BillDate,
                        b.RequestNo,
                        b.PatientName,
                        b.DoctorName,
                        MedicineCode = "",
                        MedicineName = "",
                        Dosage = "",
                        Qty = 0,
                        UnitPrice = 0m,
                        LineTotal = 0m,
                        b.TotalAmount,
                        b.BalanceDue
                    }
                }));
        });
    }

    private static byte[] ExportChartAccounts(List<Core.Entities.ChartAccount> rows) => ToBytes(ws =>
    {
        InsertTableOrEmpty(ws, rows.Select(a => new { a.AccountNo, a.Name, a.CategoryType, a.DetailType, a.Description }));
    });

    private static byte[] ExportAuditLog(List<Core.Entities.AuditLogEntry> rows) => ToBytes(ws =>
    {
        InsertTableOrEmpty(ws, rows.Select(a => new { a.Type, a.DateTime, a.UserName, a.FormName, a.Details }));
    });

    public async Task<RestoreSummary> RestoreFromZipAsync(Guid clinicId, Stream zipStream)
    {
        var summary = new RestoreSummary();
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);

        foreach (var entry in archive.Entries)
        {
            if (entry.Length == 0) continue;
            await using var entryStream = entry.Open();
            switch (entry.Name.ToLowerInvariant())
            {
                case "patients.xlsx":
                    summary.PatientsImported += await ImportPatientsAsync(clinicId, entryStream);
                    break;
                case "doctors.xlsx":
                    summary.DoctorsImported += await ImportDoctorsAsync(clinicId, entryStream);
                    break;
                case "chartofaccounts.xlsx":
                    summary.ChartAccountsImported += await ImportChartAccountsAsync(clinicId, entryStream);
                    break;
                default:
                    summary.SkippedFiles.Add(entry.Name);
                    break;
            }
        }

        await _db.SaveChangesAsync();
        return summary;
    }

    private async Task<int> ImportPatientsAsync(Guid clinicId, Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var ws = workbook.Worksheet(1);
        var rows = ws.RangeUsed()?.RowsUsed().Skip(1);
        if (rows is null) return 0;

        var count = 0;
        foreach (var row in rows)
        {
            if (row.Cell(1).GetString().Equals("No records", StringComparison.OrdinalIgnoreCase)) break;
            var patientNo = row.Cell(1).GetString().Trim();
            if (string.IsNullOrWhiteSpace(patientNo)) continue;

            var fullName = row.Cell(2).GetString().Trim();
            var existing = await _db.Patients.ForClinic(clinicId).FirstOrDefaultAsync(p => p.PatientNo == patientNo);
            if (existing is null)
            {
                existing = new Core.Entities.Patient { Id = Guid.NewGuid(), ClinicId = clinicId, PatientNo = patientNo, CreatedAt = DateTime.UtcNow };
                _db.Patients.Add(existing);
            }

            var nameParts = fullName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            existing.FirstName = nameParts.Length > 0 ? nameParts[0] : fullName;
            existing.LastName = nameParts.Length > 1 ? nameParts[1] : "";
            existing.Gender = NullIfEmpty(row.Cell(3).GetString());
            if (row.Cell(4).TryGetValue(out DateTime dob)) existing.DateOfBirth = dob;
            existing.Phone = NullIfEmpty(row.Cell(5).GetString());
            existing.City = NullIfEmpty(row.Cell(6).GetString());
            existing.DoctorName = NullIfEmpty(row.Cell(7).GetString());
            existing.Specialty = NullIfEmpty(row.Cell(8).GetString());
            existing.Status = NullIfEmpty(row.Cell(9).GetString()) ?? "Active";
            existing.UpdatedAt = DateTime.UtcNow;
            count++;
        }
        return count;
    }

    private async Task<int> ImportDoctorsAsync(Guid clinicId, Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var ws = workbook.Worksheet(1);
        var rows = ws.RangeUsed()?.RowsUsed().Skip(1);
        if (rows is null) return 0;

        var count = 0;
        foreach (var row in rows)
        {
            if (row.Cell(1).GetString().Equals("No records", StringComparison.OrdinalIgnoreCase)) break;
            if (!row.Cell(1).TryGetValue(out int doctorNo) || doctorNo <= 0) continue;

            var name = row.Cell(2).GetString().Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;

            var existing = await _db.Doctors.ForClinic(clinicId).FirstOrDefaultAsync(d => d.DoctorNo == doctorNo);
            if (existing is null)
            {
                existing = new Core.Entities.Doctor { Id = Guid.NewGuid(), ClinicId = clinicId, DoctorNo = doctorNo, CreatedAt = DateTime.UtcNow };
                _db.Doctors.Add(existing);
            }

            existing.Name = name;
            existing.Specialty = NullIfEmpty(row.Cell(3).GetString());
            existing.Phone = NullIfEmpty(row.Cell(4).GetString());
            existing.Email = NullIfEmpty(row.Cell(5).GetString());
            if (row.Cell(6).TryGetValue(out decimal fee)) existing.ConsultationFee = fee;
            existing.UpdatedAt = DateTime.UtcNow;
            count++;
        }
        return count;
    }

    private async Task<int> ImportChartAccountsAsync(Guid clinicId, Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var ws = workbook.Worksheet(1);
        var rows = ws.RangeUsed()?.RowsUsed().Skip(1);
        if (rows is null) return 0;

        var count = 0;
        foreach (var row in rows)
        {
            if (row.Cell(1).GetString().Equals("No records", StringComparison.OrdinalIgnoreCase)) break;
            if (!row.Cell(1).TryGetValue(out int accountNo) || accountNo <= 0) continue;

            var name = row.Cell(2).GetString().Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;

            var existing = await _db.ChartAccounts.ForClinic(clinicId).FirstOrDefaultAsync(a => a.AccountNo == accountNo);
            if (existing is null)
            {
                existing = new Core.Entities.ChartAccount { Id = Guid.NewGuid(), ClinicId = clinicId, AccountNo = accountNo, CreatedAt = DateTime.UtcNow };
                _db.ChartAccounts.Add(existing);
            }

            existing.Name = name;
            existing.CategoryType = row.Cell(3).GetString().Trim();
            existing.DetailType = NullIfEmpty(row.Cell(4).GetString()) ?? existing.DetailType;
            existing.Description = NullIfEmpty(row.Cell(5).GetString());
            existing.UpdatedAt = DateTime.UtcNow;
            count++;
        }
        return count;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class RestoreSummary
{
    public int PatientsImported { get; set; }
    public int DoctorsImported { get; set; }
    public int ChartAccountsImported { get; set; }
    public List<string> SkippedFiles { get; set; } = [];
    public List<string> Messages { get; set; } = [];
}
