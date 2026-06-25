using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public abstract class ClinicCrudServiceBase<T> where T : class
{
    protected ClinicalDbContext Db { get; }
    protected ClinicCrudServiceBase(ClinicalDbContext db) => Db = db;

    protected abstract IQueryable<T> ForClinic(Guid clinicId);
    protected abstract void SetClinicId(T entity, Guid clinicId);
    protected abstract Guid GetId(T entity);

    public Task<List<T>> ListAsync(Guid clinicId, int maxRows = PaginationDefaults.ListCap) =>
        ApplyListOrder(ForClinic(clinicId)).Take(maxRows).ToListAsync();

    public Task<PagedResult<T>> ListPagedAsync(Guid clinicId, int page = 1, int pageSize = PaginationDefaults.DefaultPageSize) =>
        ApplyListOrder(ForClinic(clinicId)).ToPagedResultAsync(page, pageSize);

    public async Task<Guid?> GetAdjacentIdAsync(Guid clinicId, Guid currentId, int direction)
    {
        var ids = await ApplyListOrder(ForClinic(clinicId))
            .Select(e => EF.Property<Guid>(e, "Id"))
            .ToListAsync();

        if (ids.Count == 0) return null;

        var idx = ids.IndexOf(currentId);
        if (idx < 0)
            return direction > 0 ? ids[0] : ids[^1];

        var newIdx = idx + direction;
        if (newIdx < 0 || newIdx >= ids.Count) return null;
        return ids[newIdx];
    }

    protected virtual IQueryable<T> ApplyListOrder(IQueryable<T> query) =>
        query.OrderByDescending(e => EF.Property<Guid>(e, "Id"));

    public Task<T?> GetAsync(Guid clinicId, Guid id) =>
        ForClinic(clinicId).FirstOrDefaultAsync(e => EF.Property<Guid>(e, "Id") == id);
}

public static class ClinicLookup
{
    public static readonly string[] Specialties =
    [
        "ENT", "Cardiology", "Pediatrics", "Dermatology", "Orthopedics", "General Medicine",
        "Gynecology", "Obstetrics", "Neurology", "Dental", "Dentistry", "Oral Surgery",
        "Ophthalmology", "Urology", "Psychiatry", "Oncology", "Emergency Medicine",
        "Anesthesiology", "Internal Medicine", "Pulmonology", "Gastroenterology",
        "Nephrology", "Endocrinology", "Rheumatology", "Plastic Surgery", "General Surgery",
        "Vascular Surgery", "Pediatric Surgery", "Radiology", "Pathology", "Physical Therapy",
        "Nutrition", "Family Medicine", "Sports Medicine", "Allergy & Immunology"
    ];

    public static readonly string[] Cities =
        ["Baghdad", "Basra", "Erbil", "Mosul", "Najaf", "Karbala", "Kirkuk", "Sulaymaniyah"];

    public static readonly string[] BloodGroups =
        ["A+", "A-", "B+", "B-", "AB+", "AB-", "O+", "O-"];

    public static readonly string[] MarriedStatuses =
        ["Single", "Married", "Divorced", "Widowed"];

    public static readonly string[] LabCategories =
        ["Hematology", "Microbiology", "Biochemistry", "Immunology", "Radiology", "Pathology"];

    public static readonly string[] RadiologyCategories =
        ["X-Ray", "CT", "MRI", "Ultrasound", "Mammography", "PET", "Fluoroscopy", "Nuclear Medicine", "DEXA"];

    public static readonly string[] AccountCategoryTypes =
        ["Asset", "Liability", "Equity", "Income", "Expense"];

    public static readonly IReadOnlyDictionary<string, string[]> AccountDetailTypesByCategory =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Asset"] =
            [
                "Inventory", "Cash", "Bank", "Visa Card", "Health Insurance", "Pre-Paid Expenses",
                "Fixed Assets", "Accumulated Depreciation", "Develop Cost"
            ],
            ["Liability"] = ["Account Payable", "Accrual Expenses"],
            ["Equity"] =
            [
                "Capital", "Withdrawal", "Additional Capital", "Retained Earnings"
            ],
            ["Income"] =
            [
                "Clinical Income", "Discount Income", "Pharmacy Income", "Laboratory Income",
                "Radiology Income", "Consultation Income", "Other Income", "Return to Patient"
            ],
            ["Expense"] =
            [
                "Cost of Goods Sold", "Salaries", "Incentive", "Rent", "Advertising & Marketing",
                "Utilities", "Maintenance", "Fuel & Oil", "Development cost", "Other cost"
            ]
        };

    public static string[] GetDetailTypesForCategory(string? categoryType)
    {
        if (string.IsNullOrWhiteSpace(categoryType))
            return [];
        return AccountDetailTypesByCategory.TryGetValue(categoryType.Trim(), out var types)
            ? types
            : [];
    }

    public static readonly string[] AccountDetailTypes =
        AccountDetailTypesByCategory.Values.SelectMany(t => t).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public sealed record ChartAccountTemplate(string CategoryType, string DetailType, string Name);

    public static IReadOnlyList<ChartAccountTemplate> StandardChartAccountTemplates { get; } =
        AccountDetailTypesByCategory
            .SelectMany(kvp => kvp.Value.Select(detail => new ChartAccountTemplate(kvp.Key, detail, detail)))
            .ToList();

    public static string[] GetAccountNamesForCategory(string? categoryType) =>
        GetDetailTypesForCategory(categoryType);

    public static readonly string[] AccountNames =
        StandardChartAccountTemplates.Select(t => t.Name).ToArray();

    public static int GetCategoryBaseAccountNo(string categoryType) =>
        categoryType.ToUpperInvariant() switch
        {
            "ASSET" => 1000,
            "LIABILITY" => 2000,
            "EQUITY" => 3000,
            "INCOME" => 4000,
            "EXPENSE" => 5000,
            _ => 9000
        };

    public static readonly string[] ChronicDiseaseTypes =
        ["Diabetes", "Hypertension", "Asthma", "COPD", "Heart Disease", "Kidney Disease", "Thyroid Disorder", "Arthritis", "Epilepsy"];

    public static readonly string[] PaymentMethods =
        ["Cash", "Card", "Bank Transfer", "Cheque", "Health Insurance Co"];

    public static readonly string[] PayeeTypes =
        ["Vendor", "Employee", "Supplier", "Other"];
}

public sealed class DoctorService
{
    private readonly ClinicalDbContext _db;
    private readonly MasterDataPropagationService _propagation;
    private readonly InvoiceDeleteGuardService _invoiceGuard;

    public DoctorService(ClinicalDbContext db, MasterDataPropagationService propagation, InvoiceDeleteGuardService invoiceGuard)
    {
        _db = db;
        _propagation = propagation;
        _invoiceGuard = invoiceGuard;
    }

    public Task<List<Doctor>> ListAsync(Guid clinicId) =>
        _db.Doctors.IgnoreQueryFilters()
            .Where(d => d.ClinicId == clinicId)
            .OrderBy(d => d.DoctorNo)
            .ToListAsync();

    public Task<Doctor?> GetAsync(Guid clinicId, Guid id) =>
        _db.Doctors.IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.ClinicId == clinicId && d.Id == id);

    public async Task<Doctor> SaveAsync(Guid clinicId, Doctor doctor, string? userName)
    {
        Doctor? previous = null;
        var isNew = doctor.Id == Guid.Empty;
        if (!isNew)
        {
            previous = await _db.Doctors.IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.ClinicId == clinicId && d.Id == doctor.Id);
            isNew = previous is null;
        }

        doctor.ClinicId = clinicId;
        doctor.UpdatedAt = DateTime.UtcNow;
        doctor.UpdatedBy = userName;
        if (isNew)
        {
            const int maxAttempts = 15;
            var inserted = false;
            Doctor? saved = null;
            var template = CloneDoctorShell(doctor);

            DbUpdateException? lastError = null;
            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                var row = CloneDoctorShell(template);
                row.Id = Guid.NewGuid();
                row.ClinicId = clinicId;
                row.DoctorNo = await NextNoAsync(clinicId, attempt);
                row.CreatedAt = DateTime.UtcNow;
                row.CreatedBy = userName;
                row.UpdatedAt = DateTime.UtcNow;
                row.UpdatedBy = userName;
                _db.ChangeTracker.Clear();
                _db.Doctors.Add(row);
                try
                {
                    await _db.SaveChangesAsync();
                    _db.ChangeTracker.Clear();
                    inserted = true;
                    saved = row;
                    break;
                }
                catch (DbUpdateException ex) when (IsDuplicateDoctorNo(ex))
                {
                    lastError = ex;
                }
                catch (DbUpdateException ex)
                {
                    throw new InvalidOperationException(
                        $"Could not save doctor: {ex.InnerException?.Message ?? ex.Message}", ex);
                }
            }

            if (!inserted || saved is null)
            {
                var detail = lastError?.InnerException?.Message ?? lastError?.Message;
                throw string.IsNullOrWhiteSpace(detail)
                    ? new InvalidOperationException("Could not assign a new doctor ID. Please click + New and try again.")
                    : new InvalidOperationException($"Could not save doctor ({detail}). Please click + New and try again.");
            }

            return saved;
        }
        else
        {
            if (previous is not null)
                doctor.DoctorNo = previous.DoctorNo;
            _db.Doctors.Update(doctor);
        }

        await _db.SaveChangesAsync();

        try
        {
            if (previous is not null)
                await _propagation.PropagateDoctorAsync(clinicId, previous, doctor);
        }
        catch
        {
            // Doctor row is already saved.
        }

        return doctor;
    }

    public static bool IsDuplicateDoctorNo(DbUpdateException ex)
    {
        var message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains("IX_Doctors_ClinicId_DoctorNo", StringComparison.OrdinalIgnoreCase)
            || (message.Contains("Doctors", StringComparison.OrdinalIgnoreCase)
                && message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase));
    }

    public async Task DeleteAsync(Guid clinicId, Guid id)
    {
        var item = await GetAsync(clinicId, id);
        if (item is null) return;
        await _invoiceGuard.EnsureCanDeleteDoctorAsync(clinicId, item.Name);
        _db.Doctors.Remove(item);
        await _db.SaveChangesAsync();
    }

    public async Task<int> NextNoAsync(Guid clinicId, int skip = 0)
    {
        var max = await _db.Doctors.ForClinic(clinicId).MaxAsync(d => (int?)d.DoctorNo) ?? 0;
        var candidate = max + 1 + skip;
        while (await _db.Doctors.ForClinic(clinicId).AnyAsync(d => d.DoctorNo == candidate))
            candidate++;
        return candidate;
    }

    private static Doctor CloneDoctorShell(Doctor source) => new()
    {
        Name = source.Name,
        Specialty = source.Specialty,
        Phone = source.Phone,
        Email = source.Email,
        ConsultationFee = source.ConsultationFee,
        Status = source.Status,
        PhotoBase64 = source.PhotoBase64
    };
}

public sealed class ServiceIncomeService
{
    private readonly ClinicalDbContext _db;
    private readonly MasterDataPropagationService _propagation;

    public ServiceIncomeService(ClinicalDbContext db, MasterDataPropagationService propagation)
    {
        _db = db;
        _propagation = propagation;
    }

    public Task<List<ServiceIncome>> ListAsync(Guid clinicId) =>
        _db.ServiceIncomes.ForClinic(clinicId).OrderBy(s => s.ServiceNo).ToListAsync();

    public Task<ServiceIncome?> GetAsync(Guid clinicId, Guid id) =>
        _db.ServiceIncomes.ForClinic(clinicId).FirstOrDefaultAsync(s => s.Id == id);

    public async Task<ServiceIncome> SaveAsync(Guid clinicId, ServiceIncome item)
    {
        ServiceIncome? previous = null;
        var isNew = item.Id == Guid.Empty;
        if (!isNew)
        {
            previous = await _db.ServiceIncomes.ForClinic(clinicId).AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == item.Id);
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
                    var row = new ServiceIncome
                    {
                        Id = Guid.NewGuid(),
                        ClinicId = clinicId,
                        Name = template.Name,
                        AccountName = template.AccountName,
                        Fee = template.Fee,
                        ServiceNo = await NextServiceNoAsync(clinicId, attempt),
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    return row;
                },
                row => _db.ServiceIncomes.Add(row),
                ex => ClinicSaveHelper.IsDuplicateKey(ex, "IX_ServiceIncomes_ClinicId_ServiceNo"),
                failureMessage: "Could not save service income");
        }
        else
        {
            await ClinicSaveHelper.ExecuteIsolatedSaveAsync(_db, () =>
            {
                _db.ServiceIncomes.Update(item);
                return Task.CompletedTask;
            });
        }

        if (previous is not null)
        {
            try { await _propagation.PropagateServiceIncomeAsync(clinicId, previous, item); }
            catch { }
        }

        return item;
    }

    private async Task<int> NextServiceNoAsync(Guid clinicId, int skip = 0)
    {
        var max = await _db.ServiceIncomes.ForClinic(clinicId).MaxAsync(s => (int?)s.ServiceNo) ?? 0;
        return max + 1 + skip;
    }

    public async Task DeleteAsync(Guid clinicId, Guid id)
    {
        var item = await GetAsync(clinicId, id);
        if (item is null) return;
        _db.ServiceIncomes.Remove(item);
        await _db.SaveChangesAsync();
    }
}

public sealed class CashReceiptService
{
    private readonly ClinicalDbContext _db;
    private readonly PatientInvoiceService _invoices;
    private readonly InvoiceDeleteGuardService _invoiceGuard;
    private readonly PatientVisitStatusService _visitStatus;

    public CashReceiptService(
        ClinicalDbContext db,
        PatientInvoiceService invoices,
        InvoiceDeleteGuardService invoiceGuard,
        PatientVisitStatusService visitStatus)
    {
        _db = db;
        _invoices = invoices;
        _invoiceGuard = invoiceGuard;
        _visitStatus = visitStatus;
    }

    public Task<List<CashReceipt>> ListAsync(Guid clinicId) =>
        _db.CashReceipts.ForClinic(clinicId).OrderByDescending(c => c.ReceiptNo).ToListAsync();

    public Task<CashReceipt?> GetAsync(Guid clinicId, Guid id) =>
        _db.CashReceipts.ForClinic(clinicId).FirstOrDefaultAsync(c => c.Id == id);

    public async Task<int> NextReceiptNoAsync(Guid clinicId) =>
        await NextReceiptNoWithSkipAsync(clinicId, 0);

    private async Task<int> NextReceiptNoWithSkipAsync(Guid clinicId, int skip = 0)
    {
        var max = await _db.CashReceipts.ForClinic(clinicId).MaxAsync(c => (int?)c.ReceiptNo) ?? 0;
        return max + 1 + skip;
    }

    public async Task<CashReceipt> SaveAsync(Guid clinicId, CashReceipt item)
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
                    var row = CloneReceiptShell(template);
                    row.Id = Guid.NewGuid();
                    row.ClinicId = clinicId;
                    row.ReceiptNo = await NextReceiptNoWithSkipAsync(clinicId, attempt);
                    row.CreatedAt = DateTime.UtcNow;
                    row.UpdatedAt = DateTime.UtcNow;
                    return row;
                },
                row => _db.CashReceipts.Add(row),
                ex => ClinicSaveHelper.IsDuplicateKey(ex, "IX_CashReceipts_ClinicId_ReceiptNo"),
                failureMessage: "Could not save cash receipt");
        }
        else
        {
            await ClinicSaveHelper.ExecuteIsolatedSaveAsync(_db, () =>
            {
                _db.CashReceipts.Update(item);
                return Task.CompletedTask;
            });
        }

        try
        {
            await _invoices.RecalculateInvoicePaymentsAsync(clinicId, item.PatientName, item.PatientId, item.DoctorName);
            await _visitStatus.OnCashReceiptAsync(clinicId, item);
        }
        catch
        {
            // Receipt row is already saved.
        }

        return item;
    }

    private static CashReceipt CloneReceiptShell(CashReceipt source) => new()
    {
        ReceiptDate = source.ReceiptDate,
        PatientName = source.PatientName,
        PatientId = source.PatientId,
        Age = source.Age,
        Gender = source.Gender,
        Phone = source.Phone,
        City = source.City,
        DoctorName = source.DoctorName,
        Specialty = source.Specialty,
        AppointmentDate = source.AppointmentDate,
        AppointmentTime = source.AppointmentTime,
        BalanceDue = source.BalanceDue,
        BalanceStatus = source.BalanceStatus,
        EndingBalance = source.EndingBalance,
        PatientCredit = source.PatientCredit,
        Amount = source.Amount,
        WrittenAmount = source.WrittenAmount,
        PaymentMethod = source.PaymentMethod,
        ChartAccountName = source.ChartAccountName,
        ReferenceNo = source.ReferenceNo,
        Description = source.Description,
        PatientSearch = source.PatientSearch
    };

    public async Task DeleteAsync(Guid clinicId, Guid id)
    {
        var item = await GetAsync(clinicId, id);
        if (item is null) return;
        await _invoiceGuard.EnsureCanDeleteCashReceiptAsync(clinicId, item);
        var patientName = item.PatientName;
        var patientId = item.PatientId;
        var doctorName = item.DoctorName;
        _db.CashReceipts.Remove(item);
        await _db.SaveChangesAsync();
        await _invoices.RecalculateInvoicePaymentsAsync(clinicId, patientName, patientId, doctorName);
    }
}

public sealed class LabTestService
{
    private readonly ClinicalDbContext _db;
    private readonly MasterDataPropagationService _propagation;

    public LabTestService(ClinicalDbContext db, MasterDataPropagationService propagation)
    {
        _db = db;
        _propagation = propagation;
    }

    public Task<List<LabTest>> ListAsync(Guid clinicId) =>
        _db.LabTests.ForClinic(clinicId).OrderBy(t => t.TestNo).ToListAsync();

    public Task<LabTest?> GetAsync(Guid clinicId, Guid id) =>
        _db.LabTests.ForClinic(clinicId).FirstOrDefaultAsync(t => t.Id == id);

    public async Task<LabTest> SaveAsync(Guid clinicId, LabTest item)
    {
        LabTest? previous = null;
        var isNew = item.Id == Guid.Empty;
        if (!isNew)
        {
            previous = await _db.LabTests.ForClinic(clinicId).AsNoTracking()
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
                    var row = CloneLabTestShell(template);
                    row.Id = Guid.NewGuid();
                    row.ClinicId = clinicId;
                    row.TestNo = await NextTestNoAsync(clinicId, attempt);
                    row.CreatedAt = DateTime.UtcNow;
                    row.UpdatedAt = DateTime.UtcNow;
                    return row;
                },
                row => _db.LabTests.Add(row),
                ex => ClinicSaveHelper.IsDuplicateKey(ex, "IX_LabTests_ClinicId_TestNo"),
                failureMessage: "Could not save laboratory test");
        }
        else
        {
            await ClinicSaveHelper.ExecuteIsolatedSaveAsync(_db, () =>
            {
                _db.LabTests.Update(item);
                return Task.CompletedTask;
            });
        }

        if (previous is not null)
        {
            try { await _propagation.PropagateLabTestAsync(clinicId, previous, item); }
            catch { }
        }

        return item;
    }

    private async Task<int> NextTestNoAsync(Guid clinicId, int skip = 0)
    {
        var max = await _db.LabTests.ForClinic(clinicId).MaxAsync(t => (int?)t.TestNo) ?? 0;
        return max + 1 + skip;
    }

    private static LabTest CloneLabTestShell(LabTest source) => new()
    {
        TestCode = source.TestCode,
        TestName = source.TestName,
        Category = source.Category,
        Fee = source.Fee,
        Note = source.Note
    };

    public async Task DeleteAsync(Guid clinicId, Guid id)
    {
        var item = await GetAsync(clinicId, id);
        if (item is null) return;
        _db.LabTests.Remove(item);
        await _db.SaveChangesAsync();
    }
}

public sealed class LabRequestService
{
    private readonly ClinicalDbContext _db;
    private readonly InvoiceDeleteGuardService _invoiceGuard;
    private readonly PatientVisitStatusService _visitStatus;

    public LabRequestService(
        ClinicalDbContext db,
        InvoiceDeleteGuardService invoiceGuard,
        PatientVisitStatusService visitStatus)
    {
        _db = db;
        _invoiceGuard = invoiceGuard;
        _visitStatus = visitStatus;
    }

    public Task<List<LabRequest>> ListAsync(Guid clinicId) =>
        _db.LabRequests.Include(r => r.Lines).ForClinic(clinicId).OrderByDescending(r => r.RequestNo).ToListAsync();

    public Task<LabRequest?> GetAsync(Guid clinicId, Guid id) =>
        _db.LabRequests.Include(r => r.Lines).ForClinic(clinicId).FirstOrDefaultAsync(r => r.Id == id);

    public async Task<LabRequest> SaveAsync(Guid clinicId, LabRequest item, List<LabRequestLine> lines)
    {
        var isNew = item.Id == Guid.Empty;
        if (!isNew)
        {
            item.ClinicId = clinicId;
            item.UpdatedAt = DateTime.UtcNow;
            item.TotalAmount = lines.Sum(l => l.Total);
            await ClinicSaveHelper.ExecuteIsolatedSaveAsync(_db, async () =>
            {
                var existing = await _db.LabRequestLines.Where(l => l.LabRequestId == item.Id).ToListAsync();
                _db.LabRequestLines.RemoveRange(existing);
                _db.LabRequests.Update(item);
                foreach (var line in lines)
                {
                    line.Id = Guid.NewGuid();
                    line.LabRequestId = item.Id;
                    _db.LabRequestLines.Add(line);
                }
            });
            try { await _visitStatus.OnClinicalCheckInAsync(clinicId, item.PatientBarcode, item.PatientName); }
            catch { }
            return item;
        }

        var template = item;
        var lineTemplates = lines;
        item = await ClinicSaveHelper.InsertWithSequenceRetryAsync(
            _db,
            async attempt =>
            {
                var row = CloneLabRequestShell(template);
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
                _db.LabRequests.Add(row);
                foreach (var src in lineTemplates)
                {
                    _db.LabRequestLines.Add(new LabRequestLine
                    {
                        Id = Guid.NewGuid(),
                        LabRequestId = row.Id,
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
            ex => ClinicSaveHelper.IsDuplicateKey(ex, "IX_LabRequests_ClinicId_RequestNo"),
            failureMessage: "Could not save laboratory request");

        try { await _visitStatus.OnClinicalCheckInAsync(clinicId, item.PatientBarcode, item.PatientName); }
        catch { }
        return item;
    }

    private async Task<int> NextRequestNoAsync(Guid clinicId, int skip = 0)
    {
        var max = await _db.LabRequests.ForClinic(clinicId).MaxAsync(r => (int?)r.RequestNo) ?? 0;
        return max + 1 + skip;
    }

    private static LabRequest CloneLabRequestShell(LabRequest source) => new()
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

    public async Task DeleteAsync(Guid clinicId, Guid id)
    {
        var item = await GetAsync(clinicId, id);
        if (item is null) return;
        await _invoiceGuard.EnsureCanDeleteLabRequestAsync(clinicId, item.RequestNo);
        _db.LabRequests.Remove(item);
        await _db.SaveChangesAsync();
    }

    public Task<LabRequest?> GetLatestByPatientAsync(Guid clinicId, string? patientName, string? patientBarcode) =>
        _db.LabRequests
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

public sealed class LabResultService
{
    private readonly ClinicalDbContext _db;
    private readonly InvoiceDeleteGuardService _invoiceGuard;
    private readonly PatientVisitStatusService _visitStatus;

    public LabResultService(
        ClinicalDbContext db,
        InvoiceDeleteGuardService invoiceGuard,
        PatientVisitStatusService visitStatus)
    {
        _db = db;
        _invoiceGuard = invoiceGuard;
        _visitStatus = visitStatus;
    }

    public Task<List<LabResult>> ListAsync(Guid clinicId) =>
        _db.LabResults.Include(r => r.Lines).Where(r => r.ClinicId == clinicId).OrderByDescending(r => r.ResultNo).ToListAsync();

    public Task<LabResult?> GetAsync(Guid clinicId, Guid id) =>
        _db.LabResults.Include(r => r.Lines).FirstOrDefaultAsync(r => r.ClinicId == clinicId && r.Id == id);

    public async Task<LabResult> SaveAsync(Guid clinicId, LabResult item, List<LabResultLine> lines)
    {
        item.ClinicId = clinicId;
        item.UpdatedAt = DateTime.UtcNow;

        if (item.Id == Guid.Empty)
        {
            item.Id = Guid.NewGuid();
            item.ResultNo = (await _db.LabResults.Where(r => r.ClinicId == clinicId).MaxAsync(r => (int?)r.ResultNo) ?? 0) + 1;
            item.CreatedAt = DateTime.UtcNow;
            _db.LabResults.Add(item);
        }
        else
        {
            var existing = await _db.LabResultLines.Where(l => l.LabResultId == item.Id).ToListAsync();
            _db.LabResultLines.RemoveRange(existing);
            _db.LabResults.Update(item);
        }

        foreach (var line in lines)
        {
            line.Id = Guid.NewGuid();
            line.LabResultId = item.Id;
            _db.LabResultLines.Add(line);
        }

        await _db.SaveChangesAsync();
        await _visitStatus.OnClinicalCheckInAsync(clinicId, null, item.PatientName);
        return item;
    }

    public async Task DeleteAsync(Guid clinicId, Guid id)
    {
        var item = await GetAsync(clinicId, id);
        if (item is null) return;
        await _invoiceGuard.EnsureCanDeleteLabResultAsync(clinicId, item);
        _db.LabResults.Remove(item);
        await _db.SaveChangesAsync();
    }
}
