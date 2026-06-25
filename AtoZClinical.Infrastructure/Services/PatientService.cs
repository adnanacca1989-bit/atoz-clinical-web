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

    public PatientService(
        ClinicalDbContext db,
        MasterDataPropagationService propagation,
        InvoiceDeleteGuardService invoiceGuard,
        PatientVisitStatusService visitStatus,
        IWebhookDispatchService webhooks)
    {
        _db = db;
        _propagation = propagation;
        _invoiceGuard = invoiceGuard;
        _visitStatus = visitStatus;
        _webhooks = webhooks;
    }

    public Task<List<Patient>> ListAsync(Guid clinicId, string? search = null, int maxRows = PaginationDefaults.ListCap) =>
        ApplyListOrder(ApplySearch(_db.Patients.Where(p => p.ClinicId == clinicId), search))
            .Take(maxRows)
            .ToListAsync();

    public Task<PagedResult<Patient>> ListPagedAsync(
        Guid clinicId,
        int page = 1,
        int pageSize = PaginationDefaults.DefaultPageSize,
        string? search = null) =>
        ApplyListOrder(ApplySearch(_db.Patients.Where(p => p.ClinicId == clinicId), search))
            .ToPagedResultAsync(page, pageSize);

    public async Task<Guid?> GetAdjacentIdAsync(Guid clinicId, Guid currentId, int direction, string? search = null)
    {
        var ids = await ApplyListOrder(ApplySearch(_db.Patients.Where(p => p.ClinicId == clinicId), search))
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
        var query = _db.Patients.Where(p => p.ClinicId == clinicId);

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
        _db.Patients.FirstOrDefaultAsync(p => p.ClinicId == clinicId && p.Id == id);

    public async Task<Patient> SaveAsync(Guid clinicId, Patient patient, string? userName = null)
    {
        Patient? previous = null;
        if (patient.Id != Guid.Empty)
        {
            previous = await _db.Patients.AsNoTracking()
                .FirstOrDefaultAsync(p => p.ClinicId == clinicId && p.Id == patient.Id);
        }

        patient.ClinicId = clinicId;
        patient.UpdatedAt = DateTime.UtcNow;
        patient.UpdatedBy = userName;

        var patientNo = string.IsNullOrWhiteSpace(patient.PatientNo)
            ? await GeneratePatientNoAsync(clinicId)
            : patient.PatientNo.Trim();

        if (await _db.Patients.AnyAsync(p => p.ClinicId == clinicId && p.PatientNo == patientNo && p.Id != patient.Id))
            throw new InvalidOperationException($"Barcode/Patient No '{patientNo}' is already assigned to another patient.");

        patient.PatientNo = patientNo;

        if (patient.Id == Guid.Empty)
        {
            patient.Id = Guid.NewGuid();
            patient.CreatedAt = DateTime.UtcNow;
            patient.CreatedBy = userName;
            await _visitStatus.OnPatientRegisteredAsync(clinicId, patient);
            _db.Patients.Add(patient);
        }
        else
        {
            _db.Patients.Update(patient);
        }

        await _db.SaveChangesAsync();

        if (previous is null)
        {
            await _webhooks.DispatchAsync(clinicId, WebhookEvents.PatientCreated, new
            {
                patient.Id,
                patient.PatientNo,
                patient.FirstName,
                patient.LastName
            });
        }
        else
        {
            await _webhooks.DispatchAsync(clinicId, WebhookEvents.PatientUpdated, new
            {
                patient.Id,
                patient.PatientNo,
                patient.FirstName,
                patient.LastName
            });
        }

        if (previous is not null)
            await _propagation.PropagatePatientAsync(clinicId, previous, patient);

        return patient;
    }

    public Task<string> NextPatientNoAsync(Guid clinicId) => GeneratePatientNoAsync(clinicId);

    public Task<string> NextAppointmentIdAsync(Guid clinicId) => GenerateAppointmentIdAsync(clinicId);

    public Task<int> CountTotalAsync(Guid clinicId) =>
        _db.Patients.CountAsync(p => p.ClinicId == clinicId);

    public Task<int> CountTodayAsync(Guid clinicId)
    {
        var today = DateTime.UtcNow.Date;
        return _db.Patients.CountAsync(p => p.ClinicId == clinicId && p.CreatedAt.Date >= today);
    }

    public Task<int> CountActiveAsync(Guid clinicId) =>
        _db.Patients.CountAsync(p => p.ClinicId == clinicId &&
            p.Status != PatientVisitStatuses.Cancelled &&
            p.Status != "Inactive");

    public Task<int> CountInactiveAsync(Guid clinicId) =>
        _db.Patients.CountAsync(p => p.ClinicId == clinicId &&
            (p.Status == PatientVisitStatuses.Cancelled || p.Status == "Inactive"));

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
        var query = _db.Patients.Where(p => p.ClinicId == clinicId);
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
        _db.Patients
            .Where(p => p.ClinicId == clinicId)
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

    private async Task<string> GeneratePatientNoAsync(Guid clinicId)
    {
        var count = await _db.Patients.CountAsync(p => p.ClinicId == clinicId);
        string no;
        do
        {
            count++;
            no = $"PAT-{count:D5}";
        } while (await _db.Patients.AnyAsync(p => p.ClinicId == clinicId && p.PatientNo == no));

        return no;
    }

    private async Task<string> GenerateAppointmentIdAsync(Guid clinicId)
    {
        var count = await _db.Patients.CountAsync(p => p.ClinicId == clinicId);
        string id;
        do
        {
            count++;
            id = $"APT-{count:D6}";
        } while (await _db.Patients.AnyAsync(p => p.ClinicId == clinicId && p.AppointmentId == id));

        return id;
    }
}
