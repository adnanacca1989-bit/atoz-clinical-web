using AtoZClinical.Core.Entities;
using AtoZClinical.Core.Webhooks;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public sealed class AppointmentService
{
    private readonly ClinicalDbContext _db;
    private readonly IWebhookDispatchService _webhooks;

    public AppointmentService(ClinicalDbContext db, IWebhookDispatchService webhooks)
    {
        _db = db;
        _webhooks = webhooks;
    }

    public Task<List<Appointment>> ListAsync(Guid clinicId, DateTime? from = null, DateTime? to = null)
    {
        var query = _db.Appointments
            .Include(a => a.Patient)
            .Where(a => a.ClinicId == clinicId);

        if (from.HasValue)
        {
            query = query.Where(a => a.AppointmentDate >= from.Value.Date);
        }

        if (to.HasValue)
        {
            query = query.Where(a => a.AppointmentDate <= to.Value.Date);
        }

        return query.OrderBy(a => a.AppointmentDate).ThenBy(a => a.StartTime).ToListAsync();
    }

    public Task<Appointment?> GetAsync(Guid clinicId, Guid id) =>
        _db.Appointments.Include(a => a.Patient)
            .FirstOrDefaultAsync(a => a.ClinicId == clinicId && a.Id == id);

    public async Task<Appointment> SaveAsync(Guid clinicId, Appointment appointment)
    {
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
