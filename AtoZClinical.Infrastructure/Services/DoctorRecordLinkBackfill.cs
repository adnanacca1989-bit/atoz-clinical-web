using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public static class DoctorRecordLinkBackfill
{
    public static async Task BackfillAsync(ClinicalDbContext db)
    {
        var clinicIds = await db.Clinics.AsNoTracking().Select(c => c.Id).ToListAsync();
        foreach (var clinicId in clinicIds)
            await BackfillClinicAsync(db, clinicId);
    }

    public static async Task BackfillClinicAsync(ClinicalDbContext db, Guid clinicId)
    {
        var doctors = await db.Doctors.ForClinic(clinicId).AsNoTracking().ToListAsync();
        if (doctors.Count == 0)
            return;

        var changed = false;

        foreach (var patient in await db.Patients.Where(p => p.ClinicId == clinicId && p.DoctorRecordId == null).ToListAsync())
        {
            var doctor = DoctorNameMatcher.ResolveSingleDoctor(doctors, null, patient.DoctorName);
            if (doctor is null) continue;
            patient.DoctorRecordId = doctor.Id;
            changed = true;
        }

        changed |= await LinkRowsAsync(await db.LabRequests.Where(r => r.ClinicId == clinicId && r.DoctorRecordId == null).ToListAsync(), doctors, r => r.DoctorName, (r, d) => r.DoctorRecordId = d.Id);
        changed |= await LinkRowsAsync(await db.LabResults.Where(r => r.ClinicId == clinicId && r.DoctorRecordId == null).ToListAsync(), doctors, r => r.DoctorName, (r, d) => r.DoctorRecordId = d.Id);
        changed |= await LinkRowsAsync(await db.RadiologyRequests.Where(r => r.ClinicId == clinicId && r.DoctorRecordId == null).ToListAsync(), doctors, r => r.DoctorName, (r, d) => r.DoctorRecordId = d.Id);
        changed |= await LinkRowsAsync(await db.RadiologyResults.Where(r => r.ClinicId == clinicId && r.DoctorRecordId == null).ToListAsync(), doctors, r => r.DoctorName, (r, d) => r.DoctorRecordId = d.Id);
        changed |= await LinkRowsAsync(await db.PharmacyRequests.Where(r => r.ClinicId == clinicId && r.DoctorRecordId == null).ToListAsync(), doctors, r => r.DoctorName, (r, d) => r.DoctorRecordId = d.Id);
        changed |= await LinkRowsAsync(await db.PharmacyBills.Where(b => b.ClinicId == clinicId && b.DoctorRecordId == null).ToListAsync(), doctors, b => b.DoctorName, (b, d) => b.DoctorRecordId = d.Id);
        changed |= await LinkRowsAsync(await db.CashReceipts.Where(r => r.ClinicId == clinicId && r.DoctorRecordId == null).ToListAsync(), doctors, r => r.DoctorName, (r, d) => r.DoctorRecordId = d.Id);
        changed |= await LinkRowsAsync(await db.CashPayments.Where(p => p.ClinicId == clinicId && p.DoctorRecordId == null).ToListAsync(), doctors, p => p.DoctorName, (p, d) => p.DoctorRecordId = d.Id);
        changed |= await LinkRowsAsync(await db.Prescriptions.Where(p => p.ClinicId == clinicId && p.DoctorRecordId == null).ToListAsync(), doctors, p => p.DoctorName, (p, d) => p.DoctorRecordId = d.Id);
        changed |= await LinkRowsAsync(await db.ServiceIncomeRequests.Where(r => r.ClinicId == clinicId && r.DoctorRecordId == null).ToListAsync(), doctors, r => r.DoctorName, (r, d) => r.DoctorRecordId = d.Id);
        changed |= await LinkRowsAsync(await db.Appointments.Where(a => a.ClinicId == clinicId && a.DoctorRecordId == null).ToListAsync(), doctors, a => a.DoctorName, (a, d) => a.DoctorRecordId = d.Id);

        foreach (var invoice in await db.Invoices.Where(i => i.ClinicId == clinicId && i.DoctorRecordId == null).ToListAsync())
        {
            var doctor = DoctorNameMatcher.ResolveSingleDoctor(doctors, null, invoice.DoctorName);
            if (doctor is null) continue;
            invoice.DoctorRecordId = doctor.Id;
            changed = true;
        }

        if (changed)
            await db.SaveChangesAsync();
    }

    private static Task<bool> LinkRowsAsync<T>(
        List<T> rows,
        IReadOnlyList<Doctor> doctors,
        Func<T, string?> getName,
        Action<T, Doctor> apply)
    {
        var changed = false;
        foreach (var row in rows)
        {
            var doctor = DoctorNameMatcher.ResolveSingleDoctor(doctors, null, getName(row));
            if (doctor is null) continue;
            apply(row, doctor);
            changed = true;
        }

        return Task.FromResult(changed);
    }
}
