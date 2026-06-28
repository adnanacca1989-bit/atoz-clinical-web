using AtoZClinical.Core.Entities;
using AtoZClinical.Core.Webhooks;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public sealed class PatientService
{
    private readonly ClinicalDbContext _db;
    private readonly MasterDataPropagationService _propagation;
    private readonly InvoiceDeleteGuardService _invoiceGuard;
    private readonly PatientVisitStatusService _visitStatus;
    private readonly IWebhookDispatchService _webhooks;
    private readonly AuditService _audit;

    public PatientService(
        ClinicalDbContext db,
        MasterDataPropagationService propagation,
        InvoiceDeleteGuardService invoiceGuard,
        PatientVisitStatusService visitStatus,
        IWebhookDispatchService webhooks,
        AuditService audit)
    {
        _db = db;
        _propagation = propagation;
        _invoiceGuard = invoiceGuard;
        _visitStatus = visitStatus;
        _webhooks = webhooks;
        _audit = audit;
    }

    private IQueryable<Patient> ForClinic(Guid clinicId) => _db.Patients.ForClinic(clinicId);

    public Task<List<Patient>> ListAsync(Guid clinicId, string? search = null, int maxRows = PaginationDefaults.ListCap) =>
        ApplyListOrder(ApplySearch(ForClinic(clinicId), search))
            .Take(maxRows)
            .ToListAsync();

    public Task<PagedResult<Patient>> ListPagedAsync(
        Guid clinicId,
        int page = 1,
        int pageSize = PaginationDefaults.DefaultPageSize,
        string? search = null) =>
        ApplyListOrder(ApplySearch(ForClinic(clinicId), search))
            .ToPagedResultAsync(page, pageSize);

    public async Task<Guid?> GetAdjacentIdAsync(Guid clinicId, Guid currentId, int direction, string? search = null)
    {
        var ids = await ApplyListOrder(ApplySearch(ForClinic(clinicId), search))
            .Select(p => p.Id)
            .ToListAsync();

        if (ids.Count == 0) return null;

        var idx = ids.IndexOf(currentId);
        if (idx < 0)
            return direction > 0 ? ids[0] : ids[^1];

        var newIdx = idx + direction;
        if (newIdx < 0 || newIdx >= ids.Count) return null;
        return ids[newIdx];
    }

    public async Task<List<Patient>> ListForPickerAsync(
        Guid clinicId,
        string? search,
        DateTime? fromDate,
        DateTime? toDate,
        string? status,
        string? sortBy)
    {
        var query = ForClinic(clinicId);

        if (fromDate.HasValue)
            query = query.Where(p => p.AppointmentDate == null || p.AppointmentDate.Value.Date >= fromDate.Value.Date);
        if (toDate.HasValue)
            query = query.Where(p => p.AppointmentDate == null || p.AppointmentDate.Value.Date <= toDate.Value.Date);
        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "All", StringComparison.OrdinalIgnoreCase))
        {
            var normalized = PatientVisitStatuses.Normalize(status);
            query = query.Where(p => p.Status != null && p.Status.ToLower() == normalized.ToLower());
        }

        query = ApplySearch(query, search);

        return sortBy?.ToLowerInvariant() switch
        {
            "name" => await query.OrderBy(p => p.FirstName).ThenBy(p => p.LastName).Take(PaginationDefaults.ListCap).ToListAsync(),
            "city" => await query.OrderBy(p => p.City).ThenBy(p => p.FirstName).ThenBy(p => p.LastName).Take(PaginationDefaults.ListCap).ToListAsync(),
            _ => await ApplyListOrder(query).Take(PaginationDefaults.ListCap).ToListAsync()
        };
    }

    public Task<Patient?> GetAsync(Guid clinicId, Guid id) =>
        ForClinic(clinicId).FirstOrDefaultAsync(p => p.Id == id);

    public static bool IsDuplicatePatientKey(DbUpdateException ex)
    {
        var message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains("IX_Patients_ClinicId_PatientNo", StringComparison.OrdinalIgnoreCase)
            || (message.Contains("Patients", StringComparison.OrdinalIgnoreCase)
                && message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<Patient> SaveAsync(Guid clinicId, Patient patient, string? userName = null)
    {
        Patient? previous = null;
        var isNew = patient.Id == Guid.Empty;
        if (!isNew)
        {
            previous = await ForClinic(clinicId).AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == patient.Id);
            isNew = previous is null;
        }

        patient.ClinicId = clinicId;
        patient.UpdatedAt = DateTime.UtcNow;
        patient.UpdatedBy = userName;

        if (isNew)
        {
            const int maxAttempts = 15;
            var inserted = false;
            Patient? saved = null;
            var template = ClonePatientShell(patient);

            DbUpdateException? lastError = null;
            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                var row = ClonePatientShell(template);
                row.Id = Guid.NewGuid();
                row.ClinicId = clinicId;
                row.PatientNo = await GeneratePatientNoAsync(clinicId, attempt);
                row.AppointmentId = await GenerateAppointmentIdAsync(clinicId, attempt);
                row.CreatedAt = DateTime.UtcNow;
                row.CreatedBy = userName;
                row.UpdatedAt = DateTime.UtcNow;
                row.UpdatedBy = userName;
                await _visitStatus.OnPatientRegisteredAsync(clinicId, row);
                _db.ChangeTracker.Clear();
                _db.Patients.Add(row);
                try
                {
                    await _db.SaveChangesAsync();
                    _db.ChangeTracker.Clear();
                    inserted = true;
                    saved = row;
                    break;
                }
                catch (DbUpdateException ex) when (IsDuplicatePatientKey(ex))
                {
                    lastError = ex;
                }
                catch (DbUpdateException ex)
                {
                    throw new InvalidOperationException(
                        $"Could not save patient: {ex.InnerException?.Message ?? ex.Message}", ex);
                }
            }

            if (!inserted || saved is null)
            {
                var detail = lastError?.InnerException?.Message ?? lastError?.Message;
                throw string.IsNullOrWhiteSpace(detail)
                    ? new InvalidOperationException("Could not assign a new patient number. Please click + New and try again.")
                    : new InvalidOperationException($"Could not save patient ({detail}). Please click + New and try again.");
            }

            patient = saved;
        }
        else
        {
            var patientNo = string.IsNullOrWhiteSpace(patient.PatientNo)
                ? await GeneratePatientNoAsync(clinicId)
                : patient.PatientNo.Trim();

            if (await ForClinic(clinicId).AnyAsync(p => p.PatientNo == patientNo && p.Id != patient.Id))
                throw new InvalidOperationException($"Barcode/Patient No '{patientNo}' is already assigned to another patient.");

            patient.PatientNo = patientNo;
            if (previous is not null)
                patient.Status = previous.Status;

            _db.Patients.Update(patient);
            await _db.SaveChangesAsync();
        }

        await _audit.LogAsync(
            clinicId,
            userName,
            "Patient Registration",
            previous is null ? "Create" : "Update",
            $"Patient #{patient.PatientNo} — {patient.FullName}");

        if (previous is null)
        {
            try
            {
                await _webhooks.DispatchAsync(clinicId, WebhookEvents.PatientCreated, new
                {
                    patient.Id,
                    patient.PatientNo,
                    patient.FirstName,
                    patient.LastName
                });
            }
            catch { }
        }
        else
        {
            try
            {
                await _webhooks.DispatchAsync(clinicId, WebhookEvents.PatientUpdated, new
                {
                    patient.Id,
                    patient.PatientNo,
                    patient.FirstName,
                    patient.LastName
                });
            }
            catch { }

            try
            {
                await _propagation.PropagatePatientAsync(clinicId, previous, patient);
            }
            catch { }
        }

        return patient;
    }

    public Task<string> NextPatientNoAsync(Guid clinicId) => GeneratePatientNoAsync(clinicId);

    public Task<string> NextAppointmentIdAsync(Guid clinicId) => GenerateAppointmentIdAsync(clinicId);

    public Task<int> CountTotalAsync(Guid clinicId) =>
        ForClinic(clinicId).CountAsync();

    public Task<int> CountTodayAsync(Guid clinicId)
    {
        var today = DateTime.UtcNow.Date;
        return ForClinic(clinicId).CountAsync(p => p.CreatedAt.Date >= today);
    }

    public Task<int> CountActiveAsync(Guid clinicId) =>
        ForClinic(clinicId).CountAsync(p =>
            p.Status != PatientVisitStatuses.Cancelled &&
            p.Status != "Inactive");

    public Task<int> CountInactiveAsync(Guid clinicId) =>
        ForClinic(clinicId).CountAsync(p =>
            p.Status == PatientVisitStatuses.Cancelled || p.Status == "Inactive");

    public async Task DeleteAsync(Guid clinicId, Guid id)
    {
        var patient = await GetAsync(clinicId, id);
        if (patient is null) return;
        await _invoiceGuard.EnsureCanDeletePatientAsync(clinicId, patient);
        _db.Patients.Remove(patient);
        await _db.SaveChangesAsync();
    }

    public async Task<int> GetNextVisitNumberAsync(
        Guid clinicId,
        string? nationalId,
        string? phone,
        Guid? excludePatientId = null,
        string? patientName = null)
    {
        var query = ForClinic(clinicId);
        if (excludePatientId.HasValue)
            query = query.Where(p => p.Id != excludePatientId.Value);

        if (!string.IsNullOrWhiteSpace(nationalId))
        {
            var nid = nationalId.Trim();
            query = query.Where(p => p.NationalId != null && p.NationalId == nid);
        }
        else if (!string.IsNullOrWhiteSpace(phone))
        {
            var ph = phone.Trim();
            query = query.Where(p => p.Phone != null && p.Phone == ph);
        }
        else if (!string.IsNullOrWhiteSpace(patientName))
        {
            var name = patientName.Trim();
            query = query.Where(p => p.FirstName == name);
        }
        else
        {
            return 1;
        }

        var count = await query.CountAsync();
        var maxVisit = await query.Select(p => p.VisitNumber).ToListAsync();
        var parsedMax = maxVisit
            .Select(v => int.TryParse(v, out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();

        return Math.Max(count, parsedMax) + 1;
    }

    public Task<Patient?> FindByNationalIdOrPhoneAsync(Guid clinicId, string? nationalId, string? phone) =>
        ForClinic(clinicId)
            .Where(p =>
                (!string.IsNullOrWhiteSpace(nationalId) && p.NationalId == nationalId.Trim()) ||
                (!string.IsNullOrWhiteSpace(phone) && p.Phone == phone.Trim()))
            .OrderByDescending(p => p.UpdatedAt)
            .FirstOrDefaultAsync();

    private static IQueryable<Patient> ApplyListOrder(IQueryable<Patient> query) =>
        query.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.FirstName).ThenBy(p => p.DoctorName);

    private IQueryable<Patient> ApplySearch(IQueryable<Patient> query, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return query;

        var term = search.Trim();
        if (_db.Database.IsNpgsql())
        {
            var pattern = $"%{term}%";
            return query.Where(p =>
                EF.Functions.ILike(p.PatientNo, pattern) ||
                EF.Functions.ILike(p.FirstName, pattern) ||
                EF.Functions.ILike(p.LastName, pattern) ||
                (p.Phone != null && EF.Functions.ILike(p.Phone, pattern)));
        }

        return query.Where(p =>
            p.PatientNo.Contains(term) ||
            p.FirstName.Contains(term) ||
            p.LastName.Contains(term) ||
            (p.Phone != null && p.Phone.Contains(term)));
    }

    private async Task<string> GeneratePatientNoAsync(Guid clinicId, int skip = 0)
    {
        var highest = await GetHighestPatientNoAsync(clinicId);
        var candidate = highest + skip;
        string no;
        do
        {
            candidate++;
            no = $"PAT-{candidate:D5}";
        } while (await ForClinic(clinicId).AnyAsync(p => p.PatientNo == no));

        return no;
    }

    private async Task<string> GenerateAppointmentIdAsync(Guid clinicId, int skip = 0)
    {
        var highest = await GetHighestAppointmentIdAsync(clinicId);
        var candidate = highest + skip;
        string aptId;
        do
        {
            candidate++;
            aptId = $"APT-{candidate:D6}";
        } while (await ForClinic(clinicId).AnyAsync(p => p.AppointmentId == aptId));

        return aptId;
    }

    private async Task<int> GetHighestPatientNoAsync(Guid clinicId)
    {
        var numbers = await ForClinic(clinicId).Select(p => p.PatientNo).ToListAsync();
        return numbers
            .Select(no =>
            {
                if (no.StartsWith("PAT-", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(no.AsSpan(4), out var n))
                    return n;
                return 0;
            })
            .DefaultIfEmpty(0)
            .Max();
    }

    private async Task<int> GetHighestAppointmentIdAsync(Guid clinicId)
    {
        var numbers = await ForClinic(clinicId).Select(p => p.AppointmentId).ToListAsync();
        return numbers
            .Select(id =>
            {
                if (!string.IsNullOrEmpty(id)
                    && id.StartsWith("APT-", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(id.AsSpan(4), out var n))
                    return n;
                return 0;
            })
            .DefaultIfEmpty(0)
            .Max();
    }

    private static Patient ClonePatientShell(Patient source) => new()
    {
        FirstName = source.FirstName,
        LastName = source.LastName,
        Gender = source.Gender,
        DateOfBirth = source.DateOfBirth,
        Phone = source.Phone,
        Email = source.Email,
        Address = source.Address,
        City = source.City,
        BloodGroup = source.BloodGroup,
        NationalId = source.NationalId,
        EmergencyContact = source.EmergencyContact,
        HealthInsuranceName = source.HealthInsuranceName,
        HealthInsuranceNumber = source.HealthInsuranceNumber,
        DoctorName = source.DoctorName,
        Specialty = source.Specialty,
        VisitNumber = source.VisitNumber,
        AppointmentDate = source.AppointmentDate,
        AppointmentTime = source.AppointmentTime,
        Status = source.Status,
        MarriedStatus = source.MarriedStatus,
        MotherName = source.MotherName
    };
}
