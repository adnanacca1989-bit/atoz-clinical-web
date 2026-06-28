using AtoZClinical.Core.Entities;
using AtoZClinical.Core.Webhooks;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public sealed class AppointmentService
{
    private readonly ClinicalDbContext _db;
    private readonly IWebhookDispatchService _webhooks;
    private readonly DoctorScopeContext _doctorScope;

    public AppointmentService(
        ClinicalDbContext db,
        IWebhookDispatchService webhooks,
        DoctorScopeContext doctorScope)
    {
        _db = db;
        _webhooks = webhooks;
        _doctorScope = doctorScope;
    }

    public Task<List<Appointment>> ListAsync(Guid clinicId, DateTime? from = null, DateTime? to = null)
    {
        var query = _db.Appointments
            .Include(a => a.Patient)
            .Where(a => a.ClinicId == clinicId)
            .Apply(_doctorScope.Filter);

        if (from.HasValue)
            query = query.Where(a => a.AppointmentDate >= from.Value.Date);

        if (to.HasValue)
            query = query.Where(a => a.AppointmentDate <= to.Value.Date);

        return query.OrderBy(a => a.AppointmentDate).ThenBy(a => a.StartTime).ToListAsync();
    }

    public async Task<Appointment?> GetAsync(Guid clinicId, Guid id)
    {
        var item = await _db.Appointments.Include(a => a.Patient)
            .FirstOrDefaultAsync(a => a.ClinicId == clinicId && a.Id == id);
        if (item is null || !DoctorScopeQuery.Matches(_doctorScope.Filter, item.DoctorRecordId, item.DoctorName))
            return null;
        return item;
    }

    public async Task<Appointment> SaveAsync(Guid clinicId, Appointment appointment)
    {
        if (appointment.Id != Guid.Empty)
        {
            var existing = await GetAsync(clinicId, appointment.Id);
            if (existing is null)
                throw new UnauthorizedAccessException("You do not have access to this record.");
        }

        appointment.ClinicId = clinicId;
        var isNew = appointment.Id == Guid.Empty;
        if (isNew)
        {
            appointment.Id = Guid.NewGuid();
            appointment.CreatedAt = DateTime.UtcNow;
            _db.Appointments.Add(appointment);
        }
        else
        {
            _db.Appointments.Update(appointment);
        }

        await _db.SaveChangesAsync();

        if (isNew)
        {
            await _webhooks.DispatchAsync(clinicId, WebhookEvents.AppointmentCreated, new
            {
                appointment.Id,
                appointment.PatientId,
                appointment.AppointmentDate,
                appointment.StartTime,
                appointment.DoctorName
            });
        }

        return appointment;
    }

    public async Task DeleteAsync(Guid clinicId, Guid id)
    {
        var item = await GetAsync(clinicId, id);
        if (item is null) return;
        _db.Appointments.Remove(item);
        await _db.SaveChangesAsync();
    }
}
