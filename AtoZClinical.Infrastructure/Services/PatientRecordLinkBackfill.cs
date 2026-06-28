using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public static class PatientRecordLinkBackfill
{
    public static async Task BackfillAsync(ClinicalDbContext db)
    {
        var clinicIds = await db.Clinics.AsNoTracking().Select(c => c.Id).ToListAsync();
        foreach (var clinicId in clinicIds)
            await BackfillClinicAsync(db, clinicId);
    }

    public static async Task BackfillClinicAsync(ClinicalDbContext db, Guid clinicId)
    {
        var patients = await db.Patients.ForClinic(clinicId).AsNoTracking().ToListAsync();
        if (patients.Count == 0)
            return;

        var changed = false;

        changed |= LinkRows(
            await db.LabRequests.Where(r => r.ClinicId == clinicId && r.PatientRecordId == null).ToListAsync(),
            patients,
            r => r.PatientBarcode, r => r.PatientName,
            (r, p) => r.PatientRecordId = p.Id);

        changed |= LinkRows(
            await db.LabResults.Where(r => r.ClinicId == clinicId && r.PatientRecordId == null).ToListAsync(),
            patients,
            _ => null, r => r.PatientName,
            (r, p) => r.PatientRecordId = p.Id);

        changed |= LinkRows(
            await db.RadiologyRequests.Where(r => r.ClinicId == clinicId && r.PatientRecordId == null).ToListAsync(),
            patients,
            r => r.PatientBarcode, r => r.PatientName,
            (r, p) => r.PatientRecordId = p.Id);

        changed |= LinkRows(
            await db.RadiologyResults.Where(r => r.ClinicId == clinicId && r.PatientRecordId == null).ToListAsync(),
            patients,
            _ => null, r => r.PatientName,
            (r, p) => r.PatientRecordId = p.Id);

        changed |= LinkRows(
            await db.PharmacyRequests.Where(r => r.ClinicId == clinicId && r.PatientRecordId == null).ToListAsync(),
            patients,
            r => r.PatientId, r => r.PatientName,
            (r, p) => r.PatientRecordId = p.Id);

        changed |= LinkRows(
            await db.PharmacyBills.Where(b => b.ClinicId == clinicId && b.PatientRecordId == null).ToListAsync(),
            patients,
            b => b.PatientId, b => b.PatientName,
            (b, p) => b.PatientRecordId = p.Id);

        changed |= LinkRows(
            await db.ServiceIncomeRequests.Where(r => r.ClinicId == clinicId && r.PatientRecordId == null).ToListAsync(),
            patients,
            r => r.PatientBarcode, r => r.PatientName,
            (r, p) => r.PatientRecordId = p.Id);

        changed |= LinkRows(
            await db.CashReceipts.Where(r => r.ClinicId == clinicId && r.PatientRecordId == null).ToListAsync(),
            patients,
            r => r.PatientId, r => r.PatientName,
            (r, p) => r.PatientRecordId = p.Id);

        changed |= LinkRows(
            await db.CashPayments.Where(p => p.ClinicId == clinicId && p.PatientRecordId == null).ToListAsync(),
            patients,
            p => p.PatientId, p => p.PayeeName,
            (p, patient) => p.PatientRecordId = patient.Id);

        changed |= LinkRows(
            await db.Prescriptions.Where(p => p.ClinicId == clinicId && p.PatientRecordId == null).ToListAsync(),
            patients,
            _ => null, p => p.PatientName,
            (p, patient) => p.PatientRecordId = patient.Id);

        foreach (var invoice in await db.Invoices.Where(i => i.ClinicId == clinicId && i.PatientRecordId == null).ToListAsync())
        {
            var patient = PatientNameMatcher.ResolveSinglePatient(patients, null, invoice.PatientId, invoice.PatientName);
            if (patient is null) continue;
            invoice.PatientRecordId = patient.Id;
            changed = true;
        }

        if (changed)
            await db.SaveChangesAsync();
    }

    private static bool LinkRows<T>(
        List<T> rows,
        IReadOnlyList<Patient> patients,
        Func<T, string?> getBarcode,
        Func<T, string?> getName,
        Action<T, Patient> apply)
    {
        var changed = false;
        foreach (var row in rows)
        {
            var patient = PatientNameMatcher.ResolveSinglePatient(patients, null, getBarcode(row), getName(row));
            if (patient is null) continue;
            apply(row, patient);
            changed = true;
        }

        return changed;
    }
}
