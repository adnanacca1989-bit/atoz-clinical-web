using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public sealed class AppointmentService
{
    private readonly ClinicalDbContext _db;

    public AppointmentService(ClinicalDbContext db) => _db = db;

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
        if (appointment.Id == Guid.Empty)
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
