using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public sealed class PatientService
{
    private readonly ClinicalDbContext _db;
    private readonly MasterDataPropagationService _propagation;
    private readonly InvoiceDeleteGuardService _invoiceGuard;
    private readonly PatientVisitStatusService _visitStatus;

    public PatientService(
        ClinicalDbContext db,
        MasterDataPropagationService propagation,
        InvoiceDeleteGuardService invoiceGuard,
        PatientVisitStatusService visitStatus)
    {
        _db = db;
        _propagation = propagation;
        _invoiceGuard = invoiceGuard;
        _visitStatus = visitStatus;
    }

    public Task<List<Patient>> ListAsync(Guid clinicId, string? search = null)
    {
        var query = _db.Patients.Where(p => p.ClinicId == clinicId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            if (_db.Database.IsNpgsql())
            {
                var pattern = $"%{term}%";
                query = query.Where(p =>
                    EF.Functions.ILike(p.PatientNo, pattern) ||
                    EF.Functions.ILike(p.FirstName, pattern) ||
                    EF.Functions.ILike(p.LastName, pattern) ||
                    (p.Phone != null && EF.Functions.ILike(p.Phone, pattern)));
            }
            else
            {
                query = query.Where(p =>
                    p.PatientNo.Contains(term) ||
                    p.FirstName.Contains(term) ||
                    p.LastName.Contains(term) ||
                    (p.Phone != null && p.Phone.Contains(term)));
            }
        }

        return query.OrderByDescending(p => p.CreatedAt).ToListAsync();
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
        if (patient.Id == Guid.Empty)
        {
            patient.Id = Guid.NewGuid();
            patient.PatientNo = string.IsNullOrWhiteSpace(patient.PatientNo)
                ? await GeneratePatientNoAsync(clinicId)
                : patient.PatientNo.Trim();
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

    public async Task<int> GetNextVisitNumberAsync(Guid clinicId, string? nationalId, string? phone, Guid? excludePatientId = null)
    {
        if (string.IsNullOrWhiteSpace(nationalId) && string.IsNullOrWhiteSpace(phone))
            return 1;

        var query = _db.Patients.Where(p => p.ClinicId == clinicId);
        if (excludePatientId.HasValue)
            query = query.Where(p => p.Id != excludePatientId.Value);

        if (!string.IsNullOrWhiteSpace(nationalId))
        {
            var nid = nationalId.Trim();
            query = query.Where(p => p.NationalId != null && p.NationalId == nid);
        }
        else
        {
            var ph = phone!.Trim();
            query = query.Where(p => p.Phone != null && p.Phone == ph);
        }

        var count = await query.CountAsync();
        var maxVisit = await query
            .Select(p => p.VisitNumber)
            .ToListAsync();

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
