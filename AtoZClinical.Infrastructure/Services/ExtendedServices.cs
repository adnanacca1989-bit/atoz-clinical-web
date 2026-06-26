using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public sealed class AuditService
{
    private readonly ClinicalDbContext _db;

    public AuditService(ClinicalDbContext db) => _db = db;

    public async Task LogAsync(Guid clinicId, string? user, string form, string type, string? details)
    {
        try
        {
            _db.AuditLogEntries.Add(new AuditLogEntry
            {
                Id = Guid.NewGuid(),
                ClinicId = clinicId,
                UserName = user,
                FormName = form,
                Type = type,
                Details = details,
                DateTime = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }
        catch
        {
            // Audit must never break clinical workflows.
        }
    }

    public Task<List<AuditLogEntry>> ListAsync(Guid clinicId, int take = 500) =>
        _db.AuditLogEntries
            .Where(a => a.ClinicId == clinicId)
            .OrderByDescending(a => a.DateTime)
            .Take(take)
            .ToListAsync();
}

public sealed class RadiologyTestService
{
    private readonly ClinicalDbContext _db;
    private readonly AuditService _audit;
    private readonly MasterDataPropagationService _propagation;

    public RadiologyTestService(ClinicalDbContext db, AuditService audit, MasterDataPropagationService propagation)
    {
        _db = db;
        _audit = audit;
        _propagation = propagation;
    }

    public Task<List<RadiologyTest>> ListAsync(Guid clinicId) =>
        _db.RadiologyTests.ForClinic(clinicId).OrderBy(t => t.TestNo).ToListAsync();

    public Task<RadiologyTest?> GetAsync(Guid clinicId, Guid id) =>
        _db.RadiologyTests.ForClinic(clinicId).FirstOrDefaultAsync(t => t.Id == id);

    public async Task<RadiologyTest> SaveAsync(Guid clinicId, RadiologyTest item, string? userName = null)
    {
        var isNew = item.Id == Guid.Empty;
        RadiologyTest? previous = null;
        if (!isNew)
        {
            previous = await _db.RadiologyTests.ForClinic(clinicId).AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == item.Id);
            isNew = previous is null;
        }

        item.ClinicId = clinicId;
        item.UpdatedAt = DateTime.UtcNow;
        if (isNew)
        {
            var template = item;
            item = await ClinicSaveHelper.InsertWithSequenceRetryAsync(
                _db,
                async attempt =>
                {
                    var row = new RadiologyTest
                    {
                        Id = Guid.NewGuid(),
                        ClinicId = clinicId,
                        TestCode = template.TestCode,
                        TestName = template.TestName,
                        Category = template.Category,
                        Fee = template.Fee,
                        Note = template.Note,
                        TestNo = await NextTestNoAsync(clinicId, attempt),
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    return row;
                },
                row => _db.RadiologyTests.Add(row),
                ex => ClinicSaveHelper.IsDuplicateKey(ex, "IX_RadiologyTests_ClinicId_TestNo"),
                failureMessage: "Could not save radiology test");
        }
        else
        {
            await ClinicSaveHelper.ExecuteIsolatedSaveAsync(_db, () =>
            {
                _db.RadiologyTests.Update(item);
                return Task.CompletedTask;
            });
        }

        if (previous is not null)
        {
            try { await _propagation.PropagateRadiologyTestAsync(clinicId, previous, item); }
            catch { }
        }

        await _audit.LogAsync(clinicId, userName, "Radiology Registration", isNew ? "Create" : "Update",
            $"Test #{item.TestNo}: {item.TestCode} - {item.TestName}");
        return item;
    }

    private async Task<int> NextTestNoAsync(Guid clinicId, int skip = 0)
    {
        var max = await _db.RadiologyTests.ForClinic(clinicId).MaxAsync(t => (int?)t.TestNo) ?? 0;
        return max + 1 + skip;
    }

    public async Task DeleteAsync(Guid clinicId, Guid id, string? userName = null)
    {
        var item = await GetAsync(clinicId, id);
        if (item is null) return;
        _db.RadiologyTests.Remove(item);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(clinicId, userName, "Radiology Registration", "Delete",
            $"Test #{item.TestNo}: {item.TestCode} - {item.TestName}");
    }
}

public sealed class RadiologyRequestService
{
    private readonly ClinicalDbContext _db;
    private readonly AuditService _audit;
    private readonly InvoiceDeleteGuardService _invoiceGuard;
    private readonly PatientVisitStatusService _visitStatus;
    private readonly BillingPropagationService _billing;

    public RadiologyRequestService(
        ClinicalDbContext db,
        AuditService audit,
        InvoiceDeleteGuardService invoiceGuard,
        PatientVisitStatusService visitStatus,
        BillingPropagationService billing)
    {
        _db = db;
        _audit = audit;
        _invoiceGuard = invoiceGuard;
        _visitStatus = visitStatus;
        _billing = billing;
    }

    public Task<List<RadiologyRequest>> ListAsync(Guid clinicId) =>
        _db.RadiologyRequests.Include(r => r.Lines).ForClinic(clinicId).OrderByDescending(r => r.RequestNo).ToListAsync();

    public Task<RadiologyRequest?> GetAsync(Guid clinicId, Guid id) =>
        _db.RadiologyRequests.Include(r => r.Lines).ForClinic(clinicId).FirstOrDefaultAsync(r => r.Id == id);

    public async Task<RadiologyRequest> SaveAsync(Guid clinicId, RadiologyRequest item, List<RadiologyRequestLine> lines, string? userName = null)
    {
        var isNew = item.Id == Guid.Empty;
        RadiologyRequest? previous = null;
        List<RadiologyRequestLine>? previousLines = null;
        if (!isNew)
        {
            previous = await _db.RadiologyRequests.ForClinic(clinicId).AsNoTracking()
                .Include(r => r.Lines)
                .FirstOrDefaultAsync(r => r.Id == item.Id);
            previousLines = previous?.Lines.OrderBy(l => l.LineNo).ToList();
        }

        if (!isNew)
        {
            item.ClinicId = clinicId;
            item.UpdatedAt = DateTime.UtcNow;
            item.TotalAmount = lines.Sum(l => l.Total);
            await ClinicSaveHelper.ExecuteIsolatedSaveAsync(_db, async () =>
            {
                var existing = await _db.RadiologyRequestLines.Where(l => l.RadiologyRequestId == item.Id).ToListAsync();
                _db.RadiologyRequestLines.RemoveRange(existing);
                _db.RadiologyRequests.Update(item);
                foreach (var line in lines)
                {
                    line.Id = Guid.NewGuid();
                    line.RadiologyRequestId = item.Id;
                    _db.RadiologyRequestLines.Add(line);
                }
            });
            try { await _billing.SyncRadiologyRequestAsync(clinicId, item, lines, previous, previousLines); }
            catch { }
            try { await _visitStatus.OnClinicalCheckInAsync(clinicId, item.PatientBarcode, item.PatientName); }
            catch { }
            await _audit.LogAsync(clinicId, userName, "Radiology Request", "Update",
                $"Request #{item.RequestNo} — {item.PatientName}, total {item.TotalAmount:N2}");
            return item;
        }

        var template = item;
        var lineTemplates = lines;
        item = await ClinicSaveHelper.InsertWithSequenceRetryAsync(
            _db,
            async attempt =>
            {
                var row = CloneRadiologyRequestShell(template);
                row.Id = Guid.NewGuid();
                row.ClinicId = clinicId;
                row.RequestNo = await NextRequestNoAsync(clinicId, attempt);
                row.TotalAmount = lineTemplates.Sum(l => l.Total);
                row.CreatedAt = DateTime.UtcNow;
                row.UpdatedAt = DateTime.UtcNow;
                return row;
            },
            row =>
            {
                _db.RadiologyRequests.Add(row);
                foreach (var src in lineTemplates)
                {
                    _db.RadiologyRequestLines.Add(new RadiologyRequestLine
                    {
                        Id = Guid.NewGuid(),
                        RadiologyRequestId = row.Id,
                        LineNo = src.LineNo,
                        TestCode = src.TestCode,
                        TestName = src.TestName,
                        Category = src.Category,
                        Qty = src.Qty,
                        Fee = src.Fee,
                        Total = src.Total
                    });
                }
            },
            ex => ClinicSaveHelper.IsDuplicateKey(ex, "IX_RadiologyRequests_ClinicId_RequestNo"),
            failureMessage: "Could not save radiology request");

        try { await _visitStatus.OnClinicalCheckInAsync(clinicId, item.PatientBarcode, item.PatientName); }
        catch { }
        await _audit.LogAsync(clinicId, userName, "Radiology Request", "Create",
            $"Request #{item.RequestNo} — {item.PatientName}, total {item.TotalAmount:N2}");
        return item;
    }

    private async Task<int> NextRequestNoAsync(Guid clinicId, int skip = 0)
    {
        var max = await _db.RadiologyRequests.ForClinic(clinicId).MaxAsync(r => (int?)r.RequestNo) ?? 0;
        return max + 1 + skip;
    }

    private static RadiologyRequest CloneRadiologyRequestShell(RadiologyRequest source) => new()
    {
        RequestDate = source.RequestDate,
        PatientName = source.PatientName,
        PatientBarcode = source.PatientBarcode,
        Age = source.Age,
        Gender = source.Gender,
        Phone = source.Phone,
        City = source.City,
        DoctorName = source.DoctorName,
        Specialty = source.Specialty
    };

    public async Task DeleteAsync(Guid clinicId, Guid id, string? userName = null)
    {
        var item = await GetAsync(clinicId, id);
        if (item is null) return;
        await _invoiceGuard.EnsureCanDeleteRadiologyRequestAsync(clinicId, item.RequestNo);
        _db.RadiologyRequests.Remove(item);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(clinicId, userName, "Radiology Request", "Delete", $"Request #{item.RequestNo}");
    }

    public Task<RadiologyRequest?> GetLatestByPatientAsync(Guid clinicId, string? patientName, string? patientBarcode) =>
        _db.RadiologyRequests
            .Include(r => r.Lines)
            .ForClinic(clinicId)
            .Where(r =>
                (!string.IsNullOrWhiteSpace(patientBarcode) && r.PatientBarcode != null &&
                 EF.Functions.ILike(r.PatientBarcode, patientBarcode.Trim())) ||
                (!string.IsNullOrWhiteSpace(patientName) && r.PatientName != null &&
                 EF.Functions.ILike(r.PatientName, patientName.Trim())))
            .OrderByDescending(r => r.RequestNo)
            .FirstOrDefaultAsync();
}

public sealed class RadiologyResultService
{
    private readonly ClinicalDbContext _db;
    private readonly AuditService _audit;
    private readonly InvoiceDeleteGuardService _invoiceGuard;
    private readonly PatientVisitStatusService _visitStatus;
    private readonly BillingPropagationService _billing;

    public RadiologyResultService(
        ClinicalDbContext db,
        AuditService audit,
        InvoiceDeleteGuardService invoiceGuard,
        PatientVisitStatusService visitStatus,
        BillingPropagationService billing)
    {
        _db = db;
        _audit = audit;
        _invoiceGuard = invoiceGuard;
        _visitStatus = visitStatus;
        _billing = billing;
    }

    public Task<List<RadiologyResult>> ListAsync(Guid clinicId) =>
        _db.RadiologyResults.Include(r => r.Lines).Where(r => r.ClinicId == clinicId).OrderByDescending(r => r.ResultNo).ToListAsync();

    public Task<RadiologyResult?> GetAsync(Guid clinicId, Guid id) =>
        _db.RadiologyResults.Include(r => r.Lines).FirstOrDefaultAsync(r => r.ClinicId == clinicId && r.Id == id);

    public async Task<RadiologyResult> SaveAsync(Guid clinicId, RadiologyResult item, List<RadiologyResultLine> lines, string? userName = null)
    {
        var isNew = item.Id == Guid.Empty;
        RadiologyResult? previous = null;
        if (!isNew)
        {
            previous = await _db.RadiologyResults.ForClinic(clinicId).AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == item.Id);
        }

        item.ClinicId = clinicId;
        item.UpdatedAt = DateTime.UtcNow;

        if (isNew)
        {
            item.Id = Guid.NewGuid();
            item.ResultNo = (await _db.RadiologyResults.Where(r => r.ClinicId == clinicId).MaxAsync(r => (int?)r.ResultNo) ?? 0) + 1;
            item.CreatedAt = DateTime.UtcNow;
            _db.RadiologyResults.Add(item);
        }
        else
        {
            var existing = await _db.RadiologyResultLines.Where(l => l.RadiologyResultId == item.Id).ToListAsync();
            _db.RadiologyResultLines.RemoveRange(existing);
            _db.RadiologyResults.Update(item);
        }

        foreach (var line in lines)
        {
            line.Id = Guid.NewGuid();
            line.RadiologyResultId = item.Id;
            _db.RadiologyResultLines.Add(line);
        }

        await _db.SaveChangesAsync();
        if (previous is not null)
        {
            try { await _billing.SyncRadiologyResultAsync(clinicId, item, previous); }
            catch { }
        }
        await _visitStatus.OnClinicalCheckInAsync(clinicId, null, item.PatientName);
        await _audit.LogAsync(clinicId, userName, "Radiology Result", isNew ? "Create" : "Update",
            $"Result #{item.ResultNo} — {item.PatientName}");
        return item;
    }

    public async Task DeleteAsync(Guid clinicId, Guid id, string? userName = null)
    {
        var item = await GetAsync(clinicId, id);
        if (item is null) return;
        await _invoiceGuard.EnsureCanDeleteRadiologyResultAsync(clinicId, item);
        _db.RadiologyResults.Remove(item);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(clinicId, userName, "Radiology Result", "Delete", $"Result #{item.ResultNo}");
    }
}

public sealed class PrescriptionService
{
    private readonly ClinicalDbContext _db;
    private readonly AuditService _audit;
    private readonly InvoiceDeleteGuardService _invoiceGuard;
    private readonly PatientVisitStatusService _visitStatus;
    private readonly BillingPropagationService _billing;

    public PrescriptionService(
        ClinicalDbContext db,
        AuditService audit,
        InvoiceDeleteGuardService invoiceGuard,
        PatientVisitStatusService visitStatus,
        BillingPropagationService billing)
    {
        _db = db;
        _audit = audit;
        _invoiceGuard = invoiceGuard;
        _visitStatus = visitStatus;
        _billing = billing;
    }

    public Task<List<Prescription>> ListAsync(Guid clinicId) =>
        _db.Prescriptions.Where(p => p.ClinicId == clinicId).OrderByDescending(p => p.PrescriptionNo).ToListAsync();

    public Task<Prescription?> GetAsync(Guid clinicId, Guid id) =>
        _db.Prescriptions.FirstOrDefaultAsync(p => p.ClinicId == clinicId && p.Id == id);

    public async Task<Prescription> SaveAsync(Guid clinicId, Prescription item, string? userName = null)
    {
        var isNew = item.Id == Guid.Empty;
        Prescription? previous = null;
        if (!isNew)
        {
            previous = await _db.Prescriptions.ForClinic(clinicId).AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == item.Id);
        }

        item.ClinicId = clinicId;
        item.UpdatedAt = DateTime.UtcNow;
        if (isNew)
        {
            item.Id = Guid.NewGuid();
            item.PrescriptionNo = (await _db.Prescriptions.Where(p => p.ClinicId == clinicId).MaxAsync(p => (int?)p.PrescriptionNo) ?? 0) + 1;
            item.CreatedAt = DateTime.UtcNow;
            _db.Prescriptions.Add(item);
        }
        else _db.Prescriptions.Update(item);

        await _db.SaveChangesAsync();
        if (previous is not null)
        {
            try { await _billing.SyncPrescriptionAsync(clinicId, item, previous); }
            catch { }
        }
        await _visitStatus.OnClinicalCheckInAsync(clinicId, null, item.PatientName);
        await _audit.LogAsync(clinicId, userName, "Prescription", isNew ? "Create" : "Update",
            $"Prescription #{item.PrescriptionNo} — {item.PatientName}");
        return item;
    }

    public async Task DeleteAsync(Guid clinicId, Guid id, string? userName = null)
    {
        var item = await GetAsync(clinicId, id);
        if (item is null) return;
        await _invoiceGuard.EnsureCanDeletePrescriptionAsync(clinicId, item);
        _db.Prescriptions.Remove(item);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(clinicId, userName, "Prescription", "Delete", $"Prescription #{item.PrescriptionNo}");
    }
}

public sealed class InvoiceService
{
    private readonly ClinicalDbContext _db;
    private readonly AuditService _audit;
    private readonly PatientVisitStatusService _visitStatus;

    public InvoiceService(ClinicalDbContext db, AuditService audit, PatientVisitStatusService visitStatus)
    {
        _db = db;
        _audit = audit;
        _visitStatus = visitStatus;
    }

    public Task<List<Invoice>> ListAsync(Guid clinicId) =>
        _db.Invoices.Include(i => i.Lines).ForClinic(clinicId).OrderByDescending(i => i.InvoiceNo).ToListAsync();

    public Task<Invoice?> GetAsync(Guid clinicId, Guid id) =>
        _db.Invoices.Include(i => i.Lines).ForClinic(clinicId).FirstOrDefaultAsync(i => i.Id == id);

    public async Task<int> NextInvoiceNoAsync(Guid clinicId) =>
        await NextInvoiceNoWithSkipAsync(clinicId, 0);

    public async Task<Invoice> SaveAsync(Guid clinicId, Invoice item, List<InvoiceLine> lines, string? userName = null)
    {
        var isNew = item.Id == Guid.Empty;
        item.ClinicId = clinicId;
        item.UpdatedAt = DateTime.UtcNow;
        item.SubTotal = lines.Sum(l => l.LineTotal);
        item.TotalAmount = item.SubTotal - item.Discount + item.TaxAmount;
        item.BalanceDue = item.TotalAmount - item.AmountPaid;

        if (isNew)
        {
            var template = item;
            var lineTemplates = lines;
            item = await ClinicSaveHelper.InsertWithSequenceRetryAsync(
                _db,
                async attempt =>
                {
                    var row = CloneInvoiceShell(template);
                    row.Id = Guid.NewGuid();
                    row.ClinicId = clinicId;
                    row.InvoiceNo = await NextInvoiceNoWithSkipAsync(clinicId, attempt);
                    row.CreatedAt = DateTime.UtcNow;
                    row.UpdatedAt = DateTime.UtcNow;
                    return row;
                },
                row =>
                {
                    _db.Invoices.Add(row);
                    foreach (var line in lineTemplates)
                    {
                        line.Id = Guid.NewGuid();
                        line.InvoiceId = row.Id;
                        _db.InvoiceLines.Add(line);
                    }
                },
                ex => ClinicSaveHelper.IsDuplicateKey(ex, "IX_Invoices_ClinicId_InvoiceNo"),
                failureMessage: "Could not save invoice");
        }
        else
        {
            await ClinicSaveHelper.ExecuteIsolatedSaveAsync(_db, async () =>
            {
                var existing = await _db.InvoiceLines.Where(l => l.InvoiceId == item.Id).ToListAsync();
                _db.InvoiceLines.RemoveRange(existing);
                _db.Invoices.Update(item);
                foreach (var line in lines)
                {
                    line.Id = Guid.NewGuid();
                    line.InvoiceId = item.Id;
                    _db.InvoiceLines.Add(line);
                }
            });
        }

        try { await _visitStatus.OnInvoiceBillingAsync(clinicId, item.PatientId, item.PatientName); }
        catch { }

        await _audit.LogAsync(clinicId, userName, "Invoice", isNew ? "Create" : "Update",
            $"Invoice #{item.InvoiceNo} — {item.PatientName}, total {item.TotalAmount:N2}");
        return item;
    }

    private async Task<int> NextInvoiceNoWithSkipAsync(Guid clinicId, int skip = 0)
    {
        var max = await _db.Invoices.ForClinic(clinicId).MaxAsync(i => (int?)i.InvoiceNo) ?? 0;
        return max + 1 + skip;
    }

    private static Invoice CloneInvoiceShell(Invoice source) => new()
    {
        InvoiceDate = source.InvoiceDate,
        PatientId = source.PatientId,
        PatientName = source.PatientName,
        Phone = source.Phone,
        Age = source.Age,
        Gender = source.Gender,
        City = source.City,
        DoctorName = source.DoctorName,
        Specialty = source.Specialty,
        Discount = source.Discount,
        TaxAmount = source.TaxAmount,
        AmountPaid = source.AmountPaid,
        SubTotal = source.SubTotal,
        TotalAmount = source.TotalAmount,
        BalanceDue = source.BalanceDue,
        PaymentMethod = source.PaymentMethod,
        PaymentStatus = source.PaymentStatus,
        Notes = source.Notes
    };

    public async Task DeleteAsync(Guid clinicId, Guid id, string? userName = null)
    {
        var item = await GetAsync(clinicId, id);
        if (item is null) return;
        _db.Invoices.Remove(item);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(clinicId, userName, "Invoice", "Delete", $"Invoice #{item.InvoiceNo}");
    }
}

public sealed class CashPaymentService
{
    private readonly ClinicalDbContext _db;
    private readonly AuditService _audit;
    private readonly InvoiceDeleteGuardService _invoiceGuard;
    private readonly PatientInvoiceService _invoices;
    private readonly BillingPropagationService _billing;
    private readonly PatientVisitStatusService _visitStatus;

    public CashPaymentService(
        ClinicalDbContext db,
        AuditService audit,
        InvoiceDeleteGuardService invoiceGuard,
        PatientInvoiceService invoices,
        BillingPropagationService billing,
        PatientVisitStatusService visitStatus)
    {
        _db = db;
        _audit = audit;
        _invoiceGuard = invoiceGuard;
        _invoices = invoices;
        _billing = billing;
        _visitStatus = visitStatus;
    }

    public Task<List<CashPayment>> ListAsync(Guid clinicId) =>
        _db.CashPayments.ForClinic(clinicId).OrderByDescending(c => c.PaymentNo).ToListAsync();

    public Task<CashPayment?> GetAsync(Guid clinicId, Guid id) =>
        _db.CashPayments.ForClinic(clinicId).FirstOrDefaultAsync(c => c.Id == id);

    public async Task<int> NextPaymentNoAsync(Guid clinicId) =>
        (await _db.CashPayments.ForClinic(clinicId).MaxAsync(c => (int?)c.PaymentNo) ?? 0) + 1;

    public async Task<CashPayment> SaveAsync(Guid clinicId, CashPayment item, string? userName = null)
    {
        var isNew = item.Id == Guid.Empty;
        CashPayment? previous = null;
        if (!isNew)
        {
            previous = await _db.CashPayments.ForClinic(clinicId).AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == item.Id);
        }

        item.ClinicId = clinicId;
        item.UpdatedAt = DateTime.UtcNow;
        if (isNew)
        {
            item.Id = Guid.NewGuid();
            item.PaymentNo = (await _db.CashPayments.ForClinic(clinicId).MaxAsync(c => (int?)c.PaymentNo) ?? 0) + 1;
            item.CreatedAt = DateTime.UtcNow;
            _db.CashPayments.Add(item);
        }
        else _db.CashPayments.Update(item);

        await _db.SaveChangesAsync();
        await _audit.LogAsync(clinicId, userName, "Cash Payment", isNew ? "Create" : "Update",
            $"Payment #{item.PaymentNo} — {item.PayeeName}, {item.Amount:N2}");
        try { await _visitStatus.OnCashPaymentAsync(clinicId, item); }
        catch { }
        if (previous is not null)
        {
            try { await _billing.SyncCashPaymentAsync(clinicId, item, previous); }
            catch { }
        }
        else if (!string.IsNullOrWhiteSpace(item.PayeeName) || !string.IsNullOrWhiteSpace(item.PatientId))
            await _invoices.RecalculateInvoicePaymentsAsync(clinicId, item.PayeeName, item.PatientId, item.DoctorName);
        return item;
    }

    public async Task DeleteAsync(Guid clinicId, Guid id, string? userName = null)
    {
        var item = await GetAsync(clinicId, id);
        if (item is null) return;
        await _invoiceGuard.EnsureCanDeleteCashPaymentAsync(clinicId, item);
        var patientName = item.PayeeName;
        var patientId = item.PatientId;
        var doctorName = item.DoctorName;
        _db.CashPayments.Remove(item);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(clinicId, userName, "Cash Payment", "Delete", $"Payment #{item.PaymentNo}");
        if (!string.IsNullOrWhiteSpace(patientName) || !string.IsNullOrWhiteSpace(patientId))
            await _invoices.RecalculateInvoicePaymentsAsync(clinicId, patientName, patientId, doctorName);
    }
}

public sealed class ChartAccountService
{
    private readonly ClinicalDbContext _db;
    private readonly AuditService _audit;

    public ChartAccountService(ClinicalDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public Task<List<ChartAccount>> ListAsync(Guid clinicId) =>
        _db.ChartAccounts.ForClinic(clinicId).OrderBy(a => a.AccountNo).ToListAsync();

    public Task<ChartAccount?> GetAsync(Guid clinicId, Guid id) =>
        _db.ChartAccounts.ForClinic(clinicId).FirstOrDefaultAsync(a => a.Id == id);

    public async Task<int> NextAccountNoAsync(Guid clinicId) =>
        (await _db.ChartAccounts.ForClinic(clinicId).MaxAsync(a => (int?)a.AccountNo) ?? 1000) + 100;

    public async Task<ChartAccount> SaveAsync(Guid clinicId, ChartAccount item, string? userName = null)
    {
        var isNew = item.Id == Guid.Empty;
        item.ClinicId = clinicId;
        item.UpdatedAt = DateTime.UtcNow;
        if (isNew)
        {
            var template = item;
            item = await ClinicSaveHelper.InsertWithSequenceRetryAsync(
                _db,
                async attempt =>
                {
                    var row = new ChartAccount
                    {
                        Id = Guid.NewGuid(),
                        ClinicId = clinicId,
                        Name = template.Name,
                        CategoryType = template.CategoryType,
                        DetailType = template.DetailType,
                        Description = template.Description,
                        AccountNo = template.AccountNo == 0
                            ? (await _db.ChartAccounts.ForClinic(clinicId).MaxAsync(a => (int?)a.AccountNo) ?? 1000) + 100 + (attempt * 100)
                            : template.AccountNo + (attempt * 100),
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    return row;
                },
                row => _db.ChartAccounts.Add(row),
                ex => ClinicSaveHelper.IsDuplicateKey(ex, "IX_ChartAccounts_ClinicId_AccountNo"),
                failureMessage: "Could not save chart account");
        }
        else
        {
            await ClinicSaveHelper.ExecuteIsolatedSaveAsync(_db, () =>
            {
                _db.ChartAccounts.Update(item);
                return Task.CompletedTask;
            });
        }

        await _audit.LogAsync(clinicId, userName, "Chart of Accounts", isNew ? "Create" : "Update",
            $"Account #{item.AccountNo} — {item.Name}");
        return item;
    }

    public async Task DeleteAsync(Guid clinicId, Guid id, string? userName = null)
    {
        var item = await GetAsync(clinicId, id);
        if (item is null) return;
        _db.ChartAccounts.Remove(item);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(clinicId, userName, "Chart of Accounts", "Delete", $"Account #{item.AccountNo} — {item.Name}");
    }
}

public sealed class RolePermissionService
{
    private readonly ClinicalDbContext _db;
    private readonly AuditService _audit;
    private readonly ClinicRuntimeCache _cache;

    public RolePermissionService(ClinicalDbContext db, AuditService audit, ClinicRuntimeCache cache)
    {
        _db = db;
        _audit = audit;
        _cache = cache;
    }

    public Task<List<RolePermission>> ListAsync(Guid clinicId) =>
        _db.RolePermissions.Where(r => r.ClinicId == clinicId).OrderBy(r => r.RoleName).ThenBy(r => r.FormKey).ToListAsync();

    public Task<List<RolePermission>> ListForRoleAsync(Guid clinicId, string roleName) =>
        _db.RolePermissions.Where(r => r.ClinicId == clinicId && r.RoleName == roleName).OrderBy(r => r.FormKey).ToListAsync();

    public Task<RolePermission?> GetAsync(Guid clinicId, Guid id) =>
        _db.RolePermissions.FirstOrDefaultAsync(r => r.ClinicId == clinicId && r.Id == id);

    public async Task<RolePermission> SaveAsync(Guid clinicId, RolePermission item, string? userName = null)
    {
        var isNew = item.Id == Guid.Empty;
        item.ClinicId = clinicId;
        if (isNew)
        {
            item.Id = Guid.NewGuid();
            _db.RolePermissions.Add(item);
        }
        else _db.RolePermissions.Update(item);

        await _db.SaveChangesAsync();
        _cache.InvalidateVisibleForms(clinicId, item.RoleName);
        await _audit.LogAsync(clinicId, userName, "Role Permissions", isNew ? "Create" : "Update",
            $"{item.RoleName} — {item.FormKey}, visible={item.IsVisible}");
        return item;
    }

    public async Task SaveBulkAsync(Guid clinicId, string roleName, IEnumerable<RolePermission> items, string? userName = null)
    {
        var existing = await _db.RolePermissions.Where(r => r.ClinicId == clinicId && r.RoleName == roleName).ToListAsync();
        _db.RolePermissions.RemoveRange(existing);

        foreach (var item in items)
        {
            item.Id = Guid.NewGuid();
            item.ClinicId = clinicId;
            item.RoleName = roleName;
            _db.RolePermissions.Add(item);
        }

        await _db.SaveChangesAsync();
        _cache.InvalidateVisibleForms(clinicId, roleName);
        await _audit.LogAsync(clinicId, userName, "Role Permissions", "Update", $"Bulk update for role {roleName}");
    }

    public async Task DeleteAsync(Guid clinicId, Guid id, string? userName = null)
    {
        var item = await GetAsync(clinicId, id);
        if (item is null) return;
        _db.RolePermissions.Remove(item);
        await _db.SaveChangesAsync();
        _cache.InvalidateVisibleForms(clinicId, item.RoleName);
        await _audit.LogAsync(clinicId, userName, "Role Permissions", "Delete", $"{item.RoleName} — {item.FormKey}");
    }
}

public sealed class PharmacyRequestService
{
    private readonly ClinicalDbContext _db;
    private readonly AuditService _audit;
    private readonly InvoiceDeleteGuardService _invoiceGuard;
    private readonly PatientVisitStatusService _visitStatus;
    private readonly BillingPropagationService _billing;

    public PharmacyRequestService(
        ClinicalDbContext db,
        AuditService audit,
        InvoiceDeleteGuardService invoiceGuard,
        PatientVisitStatusService visitStatus,
        BillingPropagationService billing)
    {
        _db = db;
        _audit = audit;
        _invoiceGuard = invoiceGuard;
        _visitStatus = visitStatus;
        _billing = billing;
    }

    public Task<List<PharmacyRequest>> ListAsync(Guid clinicId) =>
        _db.PharmacyRequests.Include(r => r.Lines).ForClinic(clinicId).OrderByDescending(r => r.RequestNo).ToListAsync();

    public Task<PharmacyRequest?> GetAsync(Guid clinicId, Guid id) =>
        _db.PharmacyRequests.Include(r => r.Lines).ForClinic(clinicId).FirstOrDefaultAsync(r => r.Id == id);

    public Task<PharmacyRequest?> GetByRequestNoAsync(Guid clinicId, int requestNo) =>
        _db.PharmacyRequests
            .Include(r => r.Lines)
            .ForClinic(clinicId)
            .FirstOrDefaultAsync(r => r.RequestNo == requestNo);

    public async Task<PharmacyRequest?> GetLatestByPatientAsync(Guid clinicId, string? patientName, string? patientId)
    {
        var query = _db.PharmacyRequests
            .Include(r => r.Lines)
            .ForClinic(clinicId);

        if (!string.IsNullOrWhiteSpace(patientId))
        {
            var id = patientId.Trim();
            var byId = await query
                .Where(r => r.PatientId != null && EF.Functions.ILike(r.PatientId, id))
                .OrderByDescending(r => r.RequestNo)
                .FirstOrDefaultAsync();
            if (byId is not null) return byId;
        }

        if (!string.IsNullOrWhiteSpace(patientName))
        {
            var name = patientName.Trim();
            var exact = await query
                .Where(r => r.PatientName != null && EF.Functions.ILike(r.PatientName, name))
                .OrderByDescending(r => r.RequestNo)
                .FirstOrDefaultAsync();
            if (exact is not null) return exact;

            return await query
                .Where(r => r.PatientName != null && EF.Functions.ILike(r.PatientName, $"%{name}%"))
                .OrderByDescending(r => r.RequestNo)
                .FirstOrDefaultAsync();
        }

        return null;
    }

    public async Task<PharmacyRequest> SaveAsync(Guid clinicId, PharmacyRequest item, List<PharmacyRequestLine> lines, string? userName = null)
    {
        var isNew = item.Id == Guid.Empty;
        PharmacyRequest? previous = null;
        List<PharmacyRequestLine>? previousLines = null;
        if (!isNew)
        {
            previous = await _db.PharmacyRequests.ForClinic(clinicId).AsNoTracking()
                .Include(r => r.Lines)
                .FirstOrDefaultAsync(r => r.Id == item.Id);
            previousLines = previous?.Lines.OrderBy(l => l.LineNo).ToList();
        }

        if (!isNew)
        {
            item.ClinicId = clinicId;
            item.UpdatedAt = DateTime.UtcNow;
            item.TotalAmount = lines.Sum(l => l.Total);
            await ClinicSaveHelper.ExecuteIsolatedSaveAsync(_db, async () =>
            {
                var existing = await _db.PharmacyRequestLines.Where(l => l.PharmacyRequestId == item.Id).ToListAsync();
                _db.PharmacyRequestLines.RemoveRange(existing);
                _db.PharmacyRequests.Update(item);
                foreach (var line in lines)
                {
                    line.Id = Guid.NewGuid();
                    line.PharmacyRequestId = item.Id;
                    _db.PharmacyRequestLines.Add(line);
                }
            });
            try { await _billing.SyncPharmacyRequestAsync(clinicId, item, lines, previous, previousLines); }
            catch { }
            try { await _visitStatus.OnClinicalCheckInAsync(clinicId, item.PatientId, item.PatientName); }
            catch { }
            await _audit.LogAsync(clinicId, userName, "Pharmacy Request", "Update",
                $"Request #{item.RequestNo} — {item.PatientName}, total {item.TotalAmount:N2}");
            return item;
        }

        var template = item;
        var lineTemplates = lines;
        item = await ClinicSaveHelper.InsertWithSequenceRetryAsync(
            _db,
            async attempt =>
            {
                var row = ClonePharmacyRequestShell(template);
                row.Id = Guid.NewGuid();
                row.ClinicId = clinicId;
                row.RequestNo = await NextRequestNoAsync(clinicId, attempt);
                row.TotalAmount = lineTemplates.Sum(l => l.Total);
                row.CreatedAt = DateTime.UtcNow;
                row.UpdatedAt = DateTime.UtcNow;
                return row;
            },
            row =>
            {
                _db.PharmacyRequests.Add(row);
                foreach (var src in lineTemplates)
                {
                    _db.PharmacyRequestLines.Add(new PharmacyRequestLine
                    {
                        Id = Guid.NewGuid(),
                        PharmacyRequestId = row.Id,
                        LineNo = src.LineNo,
                        Barcode = src.Barcode,
                        MedicineCode = src.MedicineCode,
                        MedicineName = src.MedicineName,
                        Dosage = src.Dosage,
                        Uom = src.Uom,
                        Qty = src.Qty,
                        UnitPrice = src.UnitPrice,
                        Total = src.Total
                    });
                }
            },
            ex => ClinicSaveHelper.IsDuplicateKey(ex, "IX_PharmacyRequests_ClinicId_RequestNo"),
            failureMessage: "Could not save pharmacy request");

        try { await _visitStatus.OnClinicalCheckInAsync(clinicId, item.PatientId, item.PatientName); }
        catch { }
        await _audit.LogAsync(clinicId, userName, "Pharmacy Request", "Create",
            $"Request #{item.RequestNo} — {item.PatientName}, total {item.TotalAmount:N2}");
        return item;
    }

    private async Task<int> NextRequestNoAsync(Guid clinicId, int skip = 0)
    {
        var max = await _db.PharmacyRequests.ForClinic(clinicId).MaxAsync(r => (int?)r.RequestNo) ?? 0;
        return max + 1 + skip;
    }

    private static PharmacyRequest ClonePharmacyRequestShell(PharmacyRequest source) => new()
    {
        RequestDate = source.RequestDate,
        PrescriptionNo = source.PrescriptionNo,
        PatientName = source.PatientName,
        PatientId = source.PatientId,
        Age = source.Age,
        Gender = source.Gender,
        Phone = source.Phone,
        City = source.City,
        DoctorName = source.DoctorName,
        Specialty = source.Specialty,
        Notes = source.Notes
    };

    public async Task DeleteAsync(Guid clinicId, Guid id, string? userName = null)
    {
        var item = await GetAsync(clinicId, id);
        if (item is null) return;
        await _invoiceGuard.EnsureCanDeletePharmacyRequestAsync(clinicId, item.RequestNo);
        _db.PharmacyRequests.Remove(item);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(clinicId, userName, "Pharmacy Request", "Delete", $"Request #{item.RequestNo}");
    }
}

public sealed class PharmacyBillService
{
    private readonly ClinicalDbContext _db;
    private readonly AuditService _audit;
    private readonly PharmacyInventoryService _inventory;
    private readonly InvoiceDeleteGuardService _invoiceGuard;
    private readonly PatientVisitStatusService _visitStatus;
    private readonly BillingPropagationService _billing;

    public PharmacyBillService(
        ClinicalDbContext db,
        AuditService audit,
        PharmacyInventoryService inventory,
        InvoiceDeleteGuardService invoiceGuard,
        PatientVisitStatusService visitStatus,
        BillingPropagationService billing)
    {
        _db = db;
        _audit = audit;
        _inventory = inventory;
        _invoiceGuard = invoiceGuard;
        _visitStatus = visitStatus;
        _billing = billing;
    }

    public Task<List<PharmacyBill>> ListAsync(Guid clinicId) =>
        _db.PharmacyBills.ForClinic(clinicId).Include(b => b.Lines).OrderByDescending(b => b.BillNo).ToListAsync();

    public Task<PharmacyBill?> GetAsync(Guid clinicId, Guid id) =>
        _db.PharmacyBills.Include(b => b.Lines).FirstOrDefaultAsync(b => b.ClinicId == clinicId && b.Id == id);

    public async Task<int> NextBillNoAsync(Guid clinicId) =>
        (await _db.PharmacyBills.ForClinic(clinicId).MaxAsync(b => (int?)b.BillNo) ?? 0) + 1;

    public async Task<PharmacyBill> SaveAsync(Guid clinicId, PharmacyBill item, List<PharmacyBillLine> lines, string? userName = null)
    {
        var isNew = item.Id == Guid.Empty;
        PharmacyBill? previous = null;
        List<PharmacyBillLine>? previousLines = null;
        if (!isNew)
        {
            previous = await _db.PharmacyBills.ForClinic(clinicId).AsNoTracking()
                .Include(b => b.Lines)
                .FirstOrDefaultAsync(b => b.Id == item.Id);
            previousLines = previous?.Lines.OrderBy(l => l.LineNo).ToList();
            if (previous is null)
                isNew = true;
        }

        var validLines = lines
            .Where(l => l.Qty > 0 && (!string.IsNullOrWhiteSpace(l.Barcode) || !string.IsNullOrWhiteSpace(l.MedicineName)))
            .ToList();
        if (validLines.Count == 0)
            throw new InvalidOperationException("Add at least one pharmacy line with a registered item.");

        foreach (var line in validLines)
        {
            var registered = await _inventory.GetOrCreateItemAsync(
                clinicId, line.Barcode, line.MedicineCode, line.MedicineName, line.Dosage);
            line.Barcode = registered.Barcode;
            line.MedicineCode = registered.MedicineCode;
            line.MedicineName = registered.MedicineName;
            if (string.IsNullOrWhiteSpace(line.Uom))
                line.Uom = registered.BaseUom;
        }

        item.ClinicId = clinicId;
        item.UpdatedAt = DateTime.UtcNow;
        item.SubTotal = validLines.Sum(l => l.LineTotal);
        item.TotalAmount = item.SubTotal - item.Discount;
        item.BalanceDue = item.TotalAmount - item.AmountPaid;

        if (isNew)
        {
            item.Id = Guid.NewGuid();
            item.CreatedAt = DateTime.UtcNow;
            item.BillNo = await NextBillNoAsync(clinicId);
            _db.PharmacyBills.Add(item);
        }
        else
        {
            var existing = await _db.PharmacyBillLines.Where(l => l.PharmacyBillId == item.Id).ToListAsync();
            _db.PharmacyBillLines.RemoveRange(existing);
            _db.PharmacyBills.Update(item);
        }

        foreach (var line in validLines)
        {
            line.Id = Guid.NewGuid();
            line.PharmacyBillId = item.Id;
            _db.PharmacyBillLines.Add(line);
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (isNew && attempt > 0)
            {
                item.BillNo = await NextBillNoAsync(clinicId);
                _db.Entry(item).Property(b => b.BillNo).IsModified = true;
            }

            try
            {
                await _db.SaveChangesAsync();
                break;
            }
            catch (DbUpdateException ex) when (isNew && attempt < 2 && IsDuplicateBillNo(ex))
            {
                // Another bill was saved with the same number; retry with the next available id.
            }
        }

        if (previous is not null)
        {
            try { await _billing.SyncPharmacyBillAsync(clinicId, item, validLines, previous, previousLines); }
            catch { }
        }

        await _inventory.SyncBillOutAsync(clinicId, item, validLines);
        await _visitStatus.OnClinicalCheckInAsync(clinicId, item.PatientId, item.PatientName);

        await _audit.LogAsync(clinicId, userName, "Pharmacy Bill", isNew ? "Create" : "Update",
            $"Bill #{item.BillNo} — {item.PatientName}, total {item.TotalAmount:N2}");
        return item;
    }

    private static bool IsDuplicateBillNo(DbUpdateException ex)
    {
        var message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains("IX_PharmacyBills_ClinicId_BillNo", StringComparison.OrdinalIgnoreCase)
               || message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase);
    }

    public async Task DeleteAsync(Guid clinicId, Guid id, string? userName = null)
    {
        var item = await GetAsync(clinicId, id);
        if (item is null) return;
        await _invoiceGuard.EnsureCanDeletePharmacyBillAsync(clinicId, item.BillNo);
        await _inventory.RemoveReferenceMovementsAsync(clinicId, PharmacyInventoryTypes.ReferenceBill, id);
        _db.PharmacyBills.Remove(item);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(clinicId, userName, "Pharmacy Bill", "Delete", $"Bill #{item.BillNo}");
    }
}
