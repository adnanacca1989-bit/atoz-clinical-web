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
        _db.RadiologyTests.Where(t => t.ClinicId == clinicId).OrderBy(t => t.TestNo).ToListAsync();

    public Task<RadiologyTest?> GetAsync(Guid clinicId, Guid id) =>
        _db.RadiologyTests.FirstOrDefaultAsync(t => t.ClinicId == clinicId && t.Id == id);

    public async Task<RadiologyTest> SaveAsync(Guid clinicId, RadiologyTest item, string? userName = null)
    {
        var isNew = item.Id == Guid.Empty;
        RadiologyTest? previous = null;
        if (!isNew)
        {
            previous = await _db.RadiologyTests.AsNoTracking()
                .FirstOrDefaultAsync(t => t.ClinicId == clinicId && t.Id == item.Id);
        }

        item.ClinicId = clinicId;
        item.UpdatedAt = DateTime.UtcNow;
        if (isNew)
        {
            item.Id = Guid.NewGuid();
            item.TestNo = (await _db.RadiologyTests.Where(t => t.ClinicId == clinicId).MaxAsync(t => (int?)t.TestNo) ?? 0) + 1;
            item.CreatedAt = DateTime.UtcNow;
            _db.RadiologyTests.Add(item);
        }
        else _db.RadiologyTests.Update(item);

        await _db.SaveChangesAsync();

        if (previous is not null)
            await _propagation.PropagateRadiologyTestAsync(clinicId, previous, item);

        await _audit.LogAsync(clinicId, userName, "Radiology Registration", isNew ? "Create" : "Update",
            $"Test #{item.TestNo}: {item.TestCode} - {item.TestName}");
        return item;
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

    public RadiologyRequestService(
        ClinicalDbContext db,
        AuditService audit,
        InvoiceDeleteGuardService invoiceGuard,
        PatientVisitStatusService visitStatus)
    {
        _db = db;
        _audit = audit;
        _invoiceGuard = invoiceGuard;
        _visitStatus = visitStatus;
    }

    public Task<List<RadiologyRequest>> ListAsync(Guid clinicId) =>
        _db.RadiologyRequests.Include(r => r.Lines).Where(r => r.ClinicId == clinicId).OrderByDescending(r => r.RequestNo).ToListAsync();

    public Task<RadiologyRequest?> GetAsync(Guid clinicId, Guid id) =>
        _db.RadiologyRequests.Include(r => r.Lines).FirstOrDefaultAsync(r => r.ClinicId == clinicId && r.Id == id);

    public async Task<RadiologyRequest> SaveAsync(Guid clinicId, RadiologyRequest item, List<RadiologyRequestLine> lines, string? userName = null)
    {
        var isNew = item.Id == Guid.Empty;
        item.ClinicId = clinicId;
        item.UpdatedAt = DateTime.UtcNow;
        item.TotalAmount = lines.Sum(l => l.Total);

        if (isNew)
        {
            item.Id = Guid.NewGuid();
            item.RequestNo = (await _db.RadiologyRequests.Where(r => r.ClinicId == clinicId).MaxAsync(r => (int?)r.RequestNo) ?? 0) + 1;
            item.CreatedAt = DateTime.UtcNow;
            _db.RadiologyRequests.Add(item);
        }
        else
        {
            var existing = await _db.RadiologyRequestLines.Where(l => l.RadiologyRequestId == item.Id).ToListAsync();
            _db.RadiologyRequestLines.RemoveRange(existing);
            _db.RadiologyRequests.Update(item);
        }

        foreach (var line in lines)
        {
            line.Id = Guid.NewGuid();
            line.RadiologyRequestId = item.Id;
            _db.RadiologyRequestLines.Add(line);
        }

        await _db.SaveChangesAsync();
        await _visitStatus.OnClinicalCheckInAsync(clinicId, item.PatientBarcode, item.PatientName);
        await _audit.LogAsync(clinicId, userName, "Radiology Request", isNew ? "Create" : "Update",
            $"Request #{item.RequestNo} — {item.PatientName}, total {item.TotalAmount:N2}");
        return item;
    }

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
            .Where(r => r.ClinicId == clinicId)
            .Where(r =>
                (!string.IsNullOrWhiteSpace(patientBarcode) && r.PatientBarcode == patientBarcode.Trim()) ||
                (!string.IsNullOrWhiteSpace(patientName) && r.PatientName == patientName.Trim()))
            .OrderByDescending(r => r.RequestNo)
            .FirstOrDefaultAsync();
}

public sealed class RadiologyResultService
{
    private readonly ClinicalDbContext _db;
    private readonly AuditService _audit;
    private readonly InvoiceDeleteGuardService _invoiceGuard;
    private readonly PatientVisitStatusService _visitStatus;

    public RadiologyResultService(
        ClinicalDbContext db,
        AuditService audit,
        InvoiceDeleteGuardService invoiceGuard,
        PatientVisitStatusService visitStatus)
    {
        _db = db;
        _audit = audit;
        _invoiceGuard = invoiceGuard;
        _visitStatus = visitStatus;
    }

    public Task<List<RadiologyResult>> ListAsync(Guid clinicId) =>
        _db.RadiologyResults.Include(r => r.Lines).Where(r => r.ClinicId == clinicId).OrderByDescending(r => r.ResultNo).ToListAsync();

    public Task<RadiologyResult?> GetAsync(Guid clinicId, Guid id) =>
        _db.RadiologyResults.Include(r => r.Lines).FirstOrDefaultAsync(r => r.ClinicId == clinicId && r.Id == id);

    public async Task<RadiologyResult> SaveAsync(Guid clinicId, RadiologyResult item, List<RadiologyResultLine> lines, string? userName = null)
    {
        var isNew = item.Id == Guid.Empty;
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

    public PrescriptionService(
        ClinicalDbContext db,
        AuditService audit,
        InvoiceDeleteGuardService invoiceGuard,
        PatientVisitStatusService visitStatus)
    {
        _db = db;
        _audit = audit;
        _invoiceGuard = invoiceGuard;
        _visitStatus = visitStatus;
    }

    public Task<List<Prescription>> ListAsync(Guid clinicId) =>
        _db.Prescriptions.Where(p => p.ClinicId == clinicId).OrderByDescending(p => p.PrescriptionNo).ToListAsync();

    public Task<Prescription?> GetAsync(Guid clinicId, Guid id) =>
        _db.Prescriptions.FirstOrDefaultAsync(p => p.ClinicId == clinicId && p.Id == id);

    public async Task<Prescription> SaveAsync(Guid clinicId, Prescription item, string? userName = null)
    {
        var isNew = item.Id == Guid.Empty;
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
        _db.Invoices.Include(i => i.Lines).Where(i => i.ClinicId == clinicId).OrderByDescending(i => i.InvoiceNo).ToListAsync();

    public Task<Invoice?> GetAsync(Guid clinicId, Guid id) =>
        _db.Invoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.ClinicId == clinicId && i.Id == id);

    public async Task<int> NextInvoiceNoAsync(Guid clinicId) =>
        (await _db.Invoices.Where(i => i.ClinicId == clinicId).MaxAsync(i => (int?)i.InvoiceNo) ?? 0) + 1;

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
            item.Id = Guid.NewGuid();
            item.InvoiceNo = (await _db.Invoices.Where(i => i.ClinicId == clinicId).MaxAsync(i => (int?)i.InvoiceNo) ?? 0) + 1;
            item.CreatedAt = DateTime.UtcNow;
            _db.Invoices.Add(item);
        }
        else
        {
            var existing = await _db.InvoiceLines.Where(l => l.InvoiceId == item.Id).ToListAsync();
            _db.InvoiceLines.RemoveRange(existing);
            _db.Invoices.Update(item);
        }

        foreach (var line in lines)
        {
            line.Id = Guid.NewGuid();
            line.InvoiceId = item.Id;
            _db.InvoiceLines.Add(line);
        }

        await _db.SaveChangesAsync();
        await _visitStatus.OnInvoiceBillingAsync(clinicId, item.PatientId, item.PatientName);
        await _audit.LogAsync(clinicId, userName, "Invoice", isNew ? "Create" : "Update",
            $"Invoice #{item.InvoiceNo} — {item.PatientName}, total {item.TotalAmount:N2}");
        return item;
    }

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

    public CashPaymentService(ClinicalDbContext db, AuditService audit, InvoiceDeleteGuardService invoiceGuard)
    {
        _db = db;
        _audit = audit;
        _invoiceGuard = invoiceGuard;
    }

    public Task<List<CashPayment>> ListAsync(Guid clinicId) =>
        _db.CashPayments.Where(c => c.ClinicId == clinicId).OrderByDescending(c => c.PaymentNo).ToListAsync();

    public Task<CashPayment?> GetAsync(Guid clinicId, Guid id) =>
        _db.CashPayments.FirstOrDefaultAsync(c => c.ClinicId == clinicId && c.Id == id);

    public async Task<int> NextPaymentNoAsync(Guid clinicId) =>
        (await _db.CashPayments.Where(c => c.ClinicId == clinicId).MaxAsync(c => (int?)c.PaymentNo) ?? 0) + 1;

    public async Task<CashPayment> SaveAsync(Guid clinicId, CashPayment item, string? userName = null)
    {
        var isNew = item.Id == Guid.Empty;
        item.ClinicId = clinicId;
        item.UpdatedAt = DateTime.UtcNow;
        if (isNew)
        {
            item.Id = Guid.NewGuid();
            item.PaymentNo = (await _db.CashPayments.Where(c => c.ClinicId == clinicId).MaxAsync(c => (int?)c.PaymentNo) ?? 0) + 1;
            item.CreatedAt = DateTime.UtcNow;
            _db.CashPayments.Add(item);
        }
        else _db.CashPayments.Update(item);

        await _db.SaveChangesAsync();
        await _audit.LogAsync(clinicId, userName, "Cash Payment", isNew ? "Create" : "Update",
            $"Payment #{item.PaymentNo} — {item.PayeeName}, {item.Amount:N2}");
        return item;
    }

    public async Task DeleteAsync(Guid clinicId, Guid id, string? userName = null)
    {
        var item = await GetAsync(clinicId, id);
        if (item is null) return;
        await _invoiceGuard.EnsureCanDeleteCashPaymentAsync(clinicId, item);
        _db.CashPayments.Remove(item);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(clinicId, userName, "Cash Payment", "Delete", $"Payment #{item.PaymentNo}");
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
        _db.ChartAccounts.Where(a => a.ClinicId == clinicId).OrderBy(a => a.AccountNo).ToListAsync();

    public Task<ChartAccount?> GetAsync(Guid clinicId, Guid id) =>
        _db.ChartAccounts.FirstOrDefaultAsync(a => a.ClinicId == clinicId && a.Id == id);

    public async Task<int> NextAccountNoAsync(Guid clinicId) =>
        (await _db.ChartAccounts.Where(a => a.ClinicId == clinicId).MaxAsync(a => (int?)a.AccountNo) ?? 1000) + 100;

    public async Task<ChartAccount> SaveAsync(Guid clinicId, ChartAccount item, string? userName = null)
    {
        var isNew = item.Id == Guid.Empty;
        item.ClinicId = clinicId;
        item.UpdatedAt = DateTime.UtcNow;
        if (isNew)
        {
            item.Id = Guid.NewGuid();
            if (item.AccountNo == 0)
                item.AccountNo = (await _db.ChartAccounts.Where(a => a.ClinicId == clinicId).MaxAsync(a => (int?)a.AccountNo) ?? 1000) + 100;
            item.CreatedAt = DateTime.UtcNow;
            _db.ChartAccounts.Add(item);
        }
        else _db.ChartAccounts.Update(item);

        await _db.SaveChangesAsync();
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

    public RolePermissionService(ClinicalDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
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
        await _audit.LogAsync(clinicId, userName, "Role Permissions", "Update", $"Bulk update for role {roleName}");
    }

    public async Task DeleteAsync(Guid clinicId, Guid id, string? userName = null)
    {
        var item = await GetAsync(clinicId, id);
        if (item is null) return;
        _db.RolePermissions.Remove(item);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(clinicId, userName, "Role Permissions", "Delete", $"{item.RoleName} — {item.FormKey}");
    }
}

public sealed class PharmacyRequestService
{
    private readonly ClinicalDbContext _db;
    private readonly AuditService _audit;
    private readonly InvoiceDeleteGuardService _invoiceGuard;
    private readonly PatientVisitStatusService _visitStatus;

    public PharmacyRequestService(
        ClinicalDbContext db,
        AuditService audit,
        InvoiceDeleteGuardService invoiceGuard,
        PatientVisitStatusService visitStatus)
    {
        _db = db;
        _audit = audit;
        _invoiceGuard = invoiceGuard;
        _visitStatus = visitStatus;
    }

    public Task<List<PharmacyRequest>> ListAsync(Guid clinicId) =>
        _db.PharmacyRequests.Include(r => r.Lines).Where(r => r.ClinicId == clinicId).OrderByDescending(r => r.RequestNo).ToListAsync();

    public Task<PharmacyRequest?> GetAsync(Guid clinicId, Guid id) =>
        _db.PharmacyRequests.Include(r => r.Lines).FirstOrDefaultAsync(r => r.ClinicId == clinicId && r.Id == id);

    public Task<PharmacyRequest?> GetLatestByPatientAsync(Guid clinicId, string? patientName, string? patientId) =>
        _db.PharmacyRequests
            .Include(r => r.Lines)
            .Where(r => r.ClinicId == clinicId)
            .Where(r =>
                (!string.IsNullOrWhiteSpace(patientId) && r.PatientId == patientId.Trim()) ||
                (!string.IsNullOrWhiteSpace(patientName) && r.PatientName == patientName.Trim()))
            .OrderByDescending(r => r.RequestNo)
            .FirstOrDefaultAsync();

    public async Task<PharmacyRequest> SaveAsync(Guid clinicId, PharmacyRequest item, List<PharmacyRequestLine> lines, string? userName = null)
    {
        var isNew = item.Id == Guid.Empty;
        item.ClinicId = clinicId;
        item.UpdatedAt = DateTime.UtcNow;
        item.TotalAmount = lines.Sum(l => l.Total);

        if (isNew)
        {
            item.Id = Guid.NewGuid();
            item.RequestNo = (await _db.PharmacyRequests.Where(r => r.ClinicId == clinicId).MaxAsync(r => (int?)r.RequestNo) ?? 0) + 1;
            item.CreatedAt = DateTime.UtcNow;
            _db.PharmacyRequests.Add(item);
        }
        else
        {
            var existing = await _db.PharmacyRequestLines.Where(l => l.PharmacyRequestId == item.Id).ToListAsync();
            _db.PharmacyRequestLines.RemoveRange(existing);
            _db.PharmacyRequests.Update(item);
        }

        foreach (var line in lines)
        {
            line.Id = Guid.NewGuid();
            line.PharmacyRequestId = item.Id;
            _db.PharmacyRequestLines.Add(line);
        }

        await _db.SaveChangesAsync();
        await _visitStatus.OnClinicalCheckInAsync(clinicId, item.PatientId, item.PatientName);
        await _audit.LogAsync(clinicId, userName, "Pharmacy Request", isNew ? "Create" : "Update",
            $"Request #{item.RequestNo} — {item.PatientName}, total {item.TotalAmount:N2}");
        return item;
    }

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

    public PharmacyBillService(
        ClinicalDbContext db,
        AuditService audit,
        PharmacyInventoryService inventory,
        InvoiceDeleteGuardService invoiceGuard,
        PatientVisitStatusService visitStatus)
    {
        _db = db;
        _audit = audit;
        _inventory = inventory;
        _invoiceGuard = invoiceGuard;
        _visitStatus = visitStatus;
    }

    public Task<List<PharmacyBill>> ListAsync(Guid clinicId) =>
        _db.PharmacyBills.Include(b => b.Lines).Where(b => b.ClinicId == clinicId).OrderByDescending(b => b.BillNo).ToListAsync();

    public Task<PharmacyBill?> GetAsync(Guid clinicId, Guid id) =>
        _db.PharmacyBills.Include(b => b.Lines).FirstOrDefaultAsync(b => b.ClinicId == clinicId && b.Id == id);

    public async Task<PharmacyBill> SaveAsync(Guid clinicId, PharmacyBill item, List<PharmacyBillLine> lines, string? userName = null)
    {
        var isNew = item.Id == Guid.Empty;
        item.ClinicId = clinicId;
        item.UpdatedAt = DateTime.UtcNow;
        item.SubTotal = lines.Sum(l => l.LineTotal);
        item.TotalAmount = item.SubTotal - item.Discount;
        item.BalanceDue = item.TotalAmount - item.AmountPaid;

        if (isNew)
        {
            item.Id = Guid.NewGuid();
            item.BillNo = (await _db.PharmacyBills.Where(b => b.ClinicId == clinicId).MaxAsync(b => (int?)b.BillNo) ?? 0) + 1;
            item.CreatedAt = DateTime.UtcNow;
            _db.PharmacyBills.Add(item);
        }
        else
        {
            var existing = await _db.PharmacyBillLines.Where(l => l.PharmacyBillId == item.Id).ToListAsync();
            _db.PharmacyBillLines.RemoveRange(existing);
            _db.PharmacyBills.Update(item);
        }

        foreach (var line in lines)
        {
            line.Id = Guid.NewGuid();
            line.PharmacyBillId = item.Id;
            _db.PharmacyBillLines.Add(line);
        }

        await _db.SaveChangesAsync();

        var validLines = lines.Where(l => l.Qty > 0 && (!string.IsNullOrWhiteSpace(l.Barcode) || !string.IsNullOrWhiteSpace(l.MedicineName))).ToList();
        await _inventory.SyncBillOutAsync(clinicId, item, validLines);
        await _visitStatus.OnClinicalCheckInAsync(clinicId, item.PatientId, item.PatientName);

        await _audit.LogAsync(clinicId, userName, "Pharmacy Bill", isNew ? "Create" : "Update",
            $"Bill #{item.BillNo} — {item.PatientName}, total {item.TotalAmount:N2}");
        return item;
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
