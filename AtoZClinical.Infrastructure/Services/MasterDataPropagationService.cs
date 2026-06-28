using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

/// <summary>
/// Keeps denormalized patient/doctor/catalog fields in sync across transactions and reports
/// when master records are updated in registration screens.
/// </summary>
public sealed class MasterDataPropagationService
{
    private readonly ClinicalDbContext _db;
    private readonly BillingPropagationService _billing;
    private readonly ClinicalJournalSyncService _journalSync;

    public MasterDataPropagationService(
        ClinicalDbContext db,
        BillingPropagationService billing,
        ClinicalJournalSyncService journalSync)
    {
        _db = db;
        _billing = billing;
        _journalSync = journalSync;
    }

    public async Task PropagatePatientAsync(Guid clinicId, Patient previous, Patient current)
    {
        var patientNo = current.PatientNo.Trim();
        var oldPatientNo = previous.PatientNo.Trim();
        var oldName = previous.FullName.Trim();
        var newName = current.FullName.Trim();
        var age = current.AgeYears;
        var phone = current.Phone;
        var gender = current.Gender;
        var city = current.City;
        var doctorName = current.DoctorName;
        var specialty = current.Specialty;
        var appointmentDate = current.AppointmentDate;
        var appointmentTime = current.AppointmentTime;
        var now = DateTime.UtcNow;

        var variants = await BuildPatientNameVariantsAsync(clinicId, current.Id, oldName, newName, patientNo, oldPatientNo);
        var anyChanged = false;

        foreach (var invoice in await _db.Invoices.Include(i => i.Lines).ForClinic(clinicId).ToListAsync())
        {
            if (!PatientNameMatcher.ShouldUpdatePatient(
                    invoice.PatientRecordId, invoice.PatientName, invoice.PatientId,
                    current.Id, patientNo, oldPatientNo, variants))
                continue;

            invoice.PatientRecordId = current.Id;
            invoice.PatientId = patientNo;
            invoice.PatientName = newName;
            invoice.Phone = phone;
            invoice.Age = age;
            invoice.Gender = gender;
            invoice.City = city;
            invoice.DoctorName = doctorName;
            invoice.Specialty = specialty;
            invoice.UpdatedAt = now;
            anyChanged = true;
        }

        foreach (var row in await _db.LabRequests.ForClinic(clinicId).ToListAsync())
        {
            if (!PatientNameMatcher.ShouldUpdatePatient(
                    row.PatientRecordId, row.PatientName, row.PatientBarcode,
                    current.Id, patientNo, oldPatientNo, variants))
                continue;

            row.PatientRecordId = current.Id;
            row.PatientBarcode = patientNo;
            row.PatientName = newName;
            row.Phone = phone;
            row.Age = age;
            row.Gender = gender;
            row.City = city;
            row.DoctorName = doctorName;
            row.Specialty = specialty;
            row.UpdatedAt = now;
            anyChanged = true;
        }

        foreach (var row in await _db.RadiologyRequests.ForClinic(clinicId).ToListAsync())
        {
            if (!PatientNameMatcher.ShouldUpdatePatient(
                    row.PatientRecordId, row.PatientName, row.PatientBarcode,
                    current.Id, patientNo, oldPatientNo, variants))
                continue;

            row.PatientRecordId = current.Id;
            row.PatientBarcode = patientNo;
            row.PatientName = newName;
            row.Phone = phone;
            row.Age = age;
            row.Gender = gender;
            row.City = city;
            row.DoctorName = doctorName;
            row.Specialty = specialty;
            row.UpdatedAt = now;
            anyChanged = true;
        }

        foreach (var row in await _db.ServiceIncomeRequests.ForClinic(clinicId).ToListAsync())
        {
            if (!PatientNameMatcher.ShouldUpdatePatient(
                    row.PatientRecordId, row.PatientName, row.PatientBarcode,
                    current.Id, patientNo, oldPatientNo, variants))
                continue;

            row.PatientRecordId = current.Id;
            row.PatientBarcode = patientNo;
            row.PatientName = newName;
            row.Phone = phone;
            row.Age = age;
            row.Gender = gender;
            row.City = city;
            row.DoctorName = doctorName;
            row.Specialty = specialty;
            row.UpdatedAt = now;
            anyChanged = true;
        }

        foreach (var row in await _db.PharmacyRequests.ForClinic(clinicId).ToListAsync())
        {
            if (!PatientNameMatcher.ShouldUpdatePatient(
                    row.PatientRecordId, row.PatientName, row.PatientId,
                    current.Id, patientNo, oldPatientNo, variants))
                continue;

            row.PatientRecordId = current.Id;
            row.PatientId = patientNo;
            row.PatientName = newName;
            row.Phone = phone;
            row.Age = age;
            row.Gender = gender;
            row.City = city;
            row.DoctorName = doctorName;
            row.Specialty = specialty;
            row.UpdatedAt = now;
            anyChanged = true;
        }

        foreach (var row in await _db.PharmacyBills.ForClinic(clinicId).ToListAsync())
        {
            if (!PatientNameMatcher.ShouldUpdatePatient(
                    row.PatientRecordId, row.PatientName, row.PatientId,
                    current.Id, patientNo, oldPatientNo, variants))
                continue;

            row.PatientRecordId = current.Id;
            row.PatientId = patientNo;
            row.PatientName = newName;
            row.DoctorName = doctorName;
            row.Specialty = specialty;
            row.UpdatedAt = now;
            anyChanged = true;
        }

        foreach (var row in await _db.CashReceipts.ForClinic(clinicId).ToListAsync())
        {
            if (!PatientNameMatcher.ShouldUpdatePatient(
                    row.PatientRecordId, row.PatientName, row.PatientId,
                    current.Id, patientNo, oldPatientNo, variants))
                continue;

            row.PatientRecordId = current.Id;
            row.PatientId = patientNo;
            row.PatientName = newName;
            row.PatientSearch = newName;
            row.Phone = phone;
            row.Age = age;
            row.Gender = gender;
            row.City = city;
            row.DoctorName = doctorName;
            row.Specialty = specialty;
            row.AppointmentDate = appointmentDate;
            row.AppointmentTime = appointmentTime;
            row.UpdatedAt = now;
            anyChanged = true;
        }

        foreach (var row in await _db.CashPayments.ForClinic(clinicId).ToListAsync())
        {
            if (!PatientNameMatcher.ShouldUpdatePatient(
                    row.PatientRecordId, row.PayeeName, row.PatientId,
                    current.Id, patientNo, oldPatientNo, variants))
                continue;

            row.PatientRecordId = current.Id;
            row.PatientId = patientNo;
            row.PayeeName = newName;
            row.DoctorName = doctorName;
            row.UpdatedAt = now;
            anyChanged = true;
        }

        foreach (var row in await _db.Prescriptions.ForClinic(clinicId).ToListAsync())
        {
            if (!PatientNameMatcher.ShouldUpdatePatient(
                    row.PatientRecordId, row.PatientName, null,
                    current.Id, patientNo, oldPatientNo, variants))
                continue;

            row.PatientRecordId = current.Id;
            row.PatientName = newName;
            row.Age = age;
            row.Gender = gender;
            row.DoctorName = doctorName;
            row.Specialty = specialty;
            row.UpdatedAt = now;
            anyChanged = true;
        }

        var labRequestNos = (await _db.LabRequests.ForClinic(clinicId).ToListAsync())
            .Where(r => PatientNameMatcher.ShouldUpdatePatient(
                r.PatientRecordId, r.PatientName, r.PatientBarcode, current.Id, patientNo, oldPatientNo, variants))
            .Select(r => r.RequestNo)
            .ToList();

        foreach (var row in await _db.LabResults.ForClinic(clinicId).ToListAsync())
        {
            var matchesPatient = PatientNameMatcher.ShouldUpdatePatient(
                row.PatientRecordId, row.PatientName, null, current.Id, patientNo, oldPatientNo, variants);
            var matchesRequest = row.RequestNo is int requestNo && labRequestNos.Contains(requestNo);

            if (!matchesPatient && !matchesRequest)
                continue;

            row.PatientRecordId = current.Id;
            row.PatientName = newName;
            row.DoctorName = doctorName;
            row.Specialty = specialty;
            row.UpdatedAt = now;
            anyChanged = true;
        }

        var radiologyRequestNos = (await _db.RadiologyRequests.ForClinic(clinicId).ToListAsync())
            .Where(r => PatientNameMatcher.ShouldUpdatePatient(
                r.PatientRecordId, r.PatientName, r.PatientBarcode, current.Id, patientNo, oldPatientNo, variants))
            .Select(r => r.RequestNo)
            .ToList();

        foreach (var row in await _db.RadiologyResults.ForClinic(clinicId).ToListAsync())
        {
            var matchesPatient = PatientNameMatcher.ShouldUpdatePatient(
                row.PatientRecordId, row.PatientName, null, current.Id, patientNo, oldPatientNo, variants);
            var matchesRequest = row.RequestNo is int requestNo && radiologyRequestNos.Contains(requestNo);

            if (!matchesPatient && !matchesRequest)
                continue;

            row.PatientRecordId = current.Id;
            row.PatientName = newName;
            row.DoctorName = doctorName;
            row.Specialty = specialty;
            row.UpdatedAt = now;
            anyChanged = true;
        }

        foreach (var row in await _db.Appointments.ForClinic(clinicId).Where(a => a.PatientId == current.Id).ToListAsync())
        {
            row.DoctorName = doctorName;
            row.Department = specialty;
            anyChanged = true;
        }

        foreach (var row in await _db.ExpenseVouchers.ForClinic(clinicId).ToListAsync())
        {
            if (!variants.Contains(row.PayeeName ?? string.Empty))
                continue;

            row.PayeeName = newName;
            row.UpdatedAt = now;
            anyChanged = true;
        }

        if (anyChanged)
            await _db.SaveChangesAsync();

        await PropagateJournalPatientNamesAsync(clinicId, variants, newName);
    }

    public async Task SyncPatientLinkedRowsAsync(Guid clinicId, Patient patient)
    {
        var now = DateTime.UtcNow;
        var anyChanged = false;
        var newName = patient.FullName.Trim();
        var patientNo = patient.PatientNo.Trim();

        foreach (var row in await _db.Invoices.ForClinic(clinicId).Where(i => i.PatientRecordId == patient.Id).ToListAsync())
        {
            row.PatientId = patientNo;
            row.PatientName = newName;
            row.Phone = patient.Phone;
            row.Age = patient.AgeYears;
            row.Gender = patient.Gender;
            row.City = patient.City;
            row.DoctorName = patient.DoctorName;
            row.Specialty = patient.Specialty;
            row.UpdatedAt = now;
            anyChanged = true;
        }

        foreach (var row in await _db.LabRequests.ForClinic(clinicId).Where(r => r.PatientRecordId == patient.Id).ToListAsync())
        {
            row.PatientBarcode = patientNo;
            row.PatientName = newName;
            row.Phone = patient.Phone;
            row.Age = patient.AgeYears;
            row.Gender = patient.Gender;
            row.City = patient.City;
            row.UpdatedAt = now;
            anyChanged = true;
        }

        foreach (var row in await _db.LabResults.ForClinic(clinicId).Where(r => r.PatientRecordId == patient.Id).ToListAsync())
        {
            row.PatientName = newName;
            row.UpdatedAt = now;
            anyChanged = true;
        }

        foreach (var row in await _db.RadiologyRequests.ForClinic(clinicId).Where(r => r.PatientRecordId == patient.Id).ToListAsync())
        {
            row.PatientBarcode = patientNo;
            row.PatientName = newName;
            row.Phone = patient.Phone;
            row.Age = patient.AgeYears;
            row.Gender = patient.Gender;
            row.City = patient.City;
            row.UpdatedAt = now;
            anyChanged = true;
        }

        foreach (var row in await _db.RadiologyResults.ForClinic(clinicId).Where(r => r.PatientRecordId == patient.Id).ToListAsync())
        {
            row.PatientName = newName;
            row.UpdatedAt = now;
            anyChanged = true;
        }

        foreach (var row in await _db.PharmacyRequests.ForClinic(clinicId).Where(r => r.PatientRecordId == patient.Id).ToListAsync())
        {
            row.PatientId = patientNo;
            row.PatientName = newName;
            row.Phone = patient.Phone;
            row.Age = patient.AgeYears;
            row.Gender = patient.Gender;
            row.City = patient.City;
            row.UpdatedAt = now;
            anyChanged = true;
        }

        foreach (var row in await _db.PharmacyBills.ForClinic(clinicId).Where(b => b.PatientRecordId == patient.Id).ToListAsync())
        {
            row.PatientId = patientNo;
            row.PatientName = newName;
            row.UpdatedAt = now;
            anyChanged = true;
        }

        foreach (var row in await _db.ServiceIncomeRequests.ForClinic(clinicId).Where(r => r.PatientRecordId == patient.Id).ToListAsync())
        {
            row.PatientBarcode = patientNo;
            row.PatientName = newName;
            row.Phone = patient.Phone;
            row.Age = patient.AgeYears;
            row.Gender = patient.Gender;
            row.City = patient.City;
            row.UpdatedAt = now;
            anyChanged = true;
        }

        foreach (var row in await _db.CashReceipts.ForClinic(clinicId).Where(r => r.PatientRecordId == patient.Id).ToListAsync())
        {
            row.PatientId = patientNo;
            row.PatientName = newName;
            row.PatientSearch = newName;
            row.Phone = patient.Phone;
            row.Age = patient.AgeYears;
            row.Gender = patient.Gender;
            row.City = patient.City;
            row.UpdatedAt = now;
            anyChanged = true;
        }

        foreach (var row in await _db.CashPayments.ForClinic(clinicId).Where(p => p.PatientRecordId == patient.Id).ToListAsync())
        {
            row.PatientId = patientNo;
            row.PayeeName = newName;
            row.UpdatedAt = now;
            anyChanged = true;
        }

        foreach (var row in await _db.Prescriptions.ForClinic(clinicId).Where(p => p.PatientRecordId == patient.Id).ToListAsync())
        {
            row.PatientName = newName;
            row.Age = patient.AgeYears;
            row.Gender = patient.Gender;
            row.UpdatedAt = now;
            anyChanged = true;
        }

        if (anyChanged)
            await _db.SaveChangesAsync();
    }

    public async Task SyncAllPatientLinkedRowsAsync(Guid clinicId)
    {
        var patients = await _db.Patients.ForClinic(clinicId).AsNoTracking().ToListAsync();
        foreach (var patient in patients)
            await SyncPatientLinkedRowsAsync(clinicId, patient);
    }

    private async Task<HashSet<string>> BuildPatientNameVariantsAsync(
        Guid clinicId,
        Guid patientId,
        string oldName,
        string newName,
        string patientNo,
        string oldPatientNo)
    {
        var patients = await _db.Patients.ForClinic(clinicId).AsNoTracking().ToListAsync();
        var variants = PatientNameMatcher.BuildNameVariants(oldName, newName, Array.Empty<string?>());

        PatientNameMatcher.CollectUnambiguousVariants(patients, patientId, oldName, variants);
        PatientNameMatcher.CollectUnambiguousVariants(patients, patientId, newName, variants);

        void AddLinkedNames(IEnumerable<string?> names)
        {
            foreach (var name in names)
            {
                if (!string.IsNullOrWhiteSpace(name))
                    variants.Add(name.Trim());
            }
        }

        AddLinkedNames(await _db.Invoices.Where(i => i.ClinicId == clinicId && i.PatientRecordId == patientId).Select(i => i.PatientName).ToListAsync());
        AddLinkedNames(await _db.LabRequests.Where(r => r.ClinicId == clinicId && r.PatientRecordId == patientId).Select(r => r.PatientName).ToListAsync());
        AddLinkedNames(await _db.LabResults.Where(r => r.ClinicId == clinicId && r.PatientRecordId == patientId).Select(r => r.PatientName).ToListAsync());
        AddLinkedNames(await _db.RadiologyRequests.Where(r => r.ClinicId == clinicId && r.PatientRecordId == patientId).Select(r => r.PatientName).ToListAsync());
        AddLinkedNames(await _db.RadiologyResults.Where(r => r.ClinicId == clinicId && r.PatientRecordId == patientId).Select(r => r.PatientName).ToListAsync());
        AddLinkedNames(await _db.PharmacyRequests.Where(r => r.ClinicId == clinicId && r.PatientRecordId == patientId).Select(r => r.PatientName).ToListAsync());
        AddLinkedNames(await _db.PharmacyBills.Where(b => b.ClinicId == clinicId && b.PatientRecordId == patientId).Select(b => b.PatientName).ToListAsync());
        AddLinkedNames(await _db.ServiceIncomeRequests.Where(r => r.ClinicId == clinicId && r.PatientRecordId == patientId).Select(r => r.PatientName).ToListAsync());
        AddLinkedNames(await _db.CashReceipts.Where(r => r.ClinicId == clinicId && r.PatientRecordId == patientId).Select(r => r.PatientName).ToListAsync());
        AddLinkedNames(await _db.CashPayments.Where(p => p.ClinicId == clinicId && p.PatientRecordId == patientId).Select(p => p.PayeeName).ToListAsync());
        AddLinkedNames(await _db.Prescriptions.Where(p => p.ClinicId == clinicId && p.PatientRecordId == patientId).Select(p => p.PatientName).ToListAsync());

        foreach (var storedName in await CollectDistinctStoredPatientNamesAsync(clinicId))
        {
            if (!PatientNameMatcher.NamesReferToSamePatient(oldName, storedName))
                continue;

            var matches = patients.Where(p => PatientNameMatcher.NamesReferToSamePatient(p.FullName, storedName)).ToList();
            if (matches.Count == 1 && matches[0].Id == patientId)
                variants.Add(storedName);
        }

        variants.Add(patientNo);
        variants.Add(oldPatientNo);

        return variants;
    }

    private async Task<List<string>> CollectDistinctStoredPatientNamesAsync(Guid clinicId)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(IEnumerable<string?> source)
        {
            foreach (var name in source)
            {
                if (!string.IsNullOrWhiteSpace(name))
                    names.Add(name.Trim());
            }
        }

        Add(await _db.Invoices.Where(i => i.ClinicId == clinicId).Select(i => i.PatientName).ToListAsync());
        Add(await _db.LabRequests.Where(r => r.ClinicId == clinicId).Select(r => r.PatientName).ToListAsync());
        Add(await _db.LabResults.Where(r => r.ClinicId == clinicId).Select(r => r.PatientName).ToListAsync());
        Add(await _db.RadiologyRequests.Where(r => r.ClinicId == clinicId).Select(r => r.PatientName).ToListAsync());
        Add(await _db.RadiologyResults.Where(r => r.ClinicId == clinicId).Select(r => r.PatientName).ToListAsync());
        Add(await _db.PharmacyRequests.Where(r => r.ClinicId == clinicId).Select(r => r.PatientName).ToListAsync());
        Add(await _db.PharmacyBills.Where(b => b.ClinicId == clinicId).Select(b => b.PatientName).ToListAsync());
        Add(await _db.ServiceIncomeRequests.Where(r => r.ClinicId == clinicId).Select(r => r.PatientName).ToListAsync());
        Add(await _db.CashReceipts.Where(r => r.ClinicId == clinicId).Select(r => r.PatientName).ToListAsync());
        Add(await _db.CashPayments.Where(p => p.ClinicId == clinicId).Select(p => p.PayeeName).ToListAsync());
        Add(await _db.Prescriptions.Where(p => p.ClinicId == clinicId).Select(p => p.PatientName).ToListAsync());
        Add(await _db.JournalEntries.Where(j => j.ClinicId == clinicId).Select(j => j.PatientName).ToListAsync());

        return names.ToList();
    }

    private async Task PropagateJournalPatientNamesAsync(Guid clinicId, HashSet<string> variants, string newName)
    {
        var now = DateTime.UtcNow;
        var entries = await _db.JournalEntries
            .ForClinic(clinicId)
            .Where(j => j.PatientName != null)
            .ToListAsync();

        var changed = false;
        foreach (var entry in entries)
        {
            if (!variants.Contains(entry.PatientName!.Trim()))
                continue;

            entry.PatientName = newName;
            entry.UpdatedAt = now;
            changed = true;
        }

        if (changed)
            await _db.SaveChangesAsync();

        try
        {
            var invoices = (await _db.Invoices
                .ForClinic(clinicId)
                .Include(i => i.Lines)
                .ToListAsync())
                .Where(i => variants.Contains(i.PatientName ?? string.Empty))
                .ToList();
            foreach (var invoice in invoices)
            {
                try { await _journalSync.SyncInvoiceAsync(clinicId, invoice, invoice.Lines.ToList()); }
                catch { }
            }
        }
        catch { }
    }

    public async Task PropagateDoctorAsync(Guid clinicId, Doctor previous, Doctor current)
    {
        var oldName = previous.Name.Trim();
        var newName = current.Name.Trim();
        var specialty = current.Specialty;
        var now = DateTime.UtcNow;

        if (oldName == newName && previous.Specialty == current.Specialty && previous.ConsultationFee == current.ConsultationFee)
            return;

        var variants = await BuildDoctorNameVariantsAsync(clinicId, current.Id, oldName, newName);
        var anyChanged = false;

        anyChanged |= UpdateDoctorRows(
            await _db.Patients.Where(p => p.ClinicId == clinicId).ToListAsync(),
            current.Id, variants,
            (row, name, spec, ts) =>
            {
                row.DoctorRecordId = current.Id;
                row.DoctorName = name;
                row.Specialty = spec;
                row.UpdatedAt = ts;
            },
            newName, specialty, now);

        anyChanged |= UpdateDoctorRows(
            await _db.LabRequests.Where(r => r.ClinicId == clinicId).ToListAsync(),
            current.Id, variants,
            (row, name, spec, ts) =>
            {
                row.DoctorRecordId = current.Id;
                row.DoctorName = name;
                row.Specialty = spec;
                row.UpdatedAt = ts;
            },
            newName, specialty, now);

        anyChanged |= UpdateDoctorRows(
            await _db.LabResults.Where(r => r.ClinicId == clinicId).ToListAsync(),
            current.Id, variants,
            (row, name, spec, ts) =>
            {
                row.DoctorRecordId = current.Id;
                row.DoctorName = name;
                row.Specialty = spec;
                row.UpdatedAt = ts;
            },
            newName, specialty, now);

        anyChanged |= UpdateDoctorRows(
            await _db.RadiologyRequests.Where(r => r.ClinicId == clinicId).ToListAsync(),
            current.Id, variants,
            (row, name, spec, ts) =>
            {
                row.DoctorRecordId = current.Id;
                row.DoctorName = name;
                row.Specialty = spec;
                row.UpdatedAt = ts;
            },
            newName, specialty, now);

        anyChanged |= UpdateDoctorRows(
            await _db.RadiologyResults.Where(r => r.ClinicId == clinicId).ToListAsync(),
            current.Id, variants,
            (row, name, spec, ts) =>
            {
                row.DoctorRecordId = current.Id;
                row.DoctorName = name;
                row.Specialty = spec;
                row.UpdatedAt = ts;
            },
            newName, specialty, now);

        anyChanged |= UpdateDoctorRows(
            await _db.PharmacyRequests.Where(r => r.ClinicId == clinicId).ToListAsync(),
            current.Id, variants,
            (row, name, spec, ts) =>
            {
                row.DoctorRecordId = current.Id;
                row.DoctorName = name;
                row.Specialty = spec;
                row.UpdatedAt = ts;
            },
            newName, specialty, now);

        anyChanged |= UpdateDoctorRows(
            await _db.PharmacyBills.Where(b => b.ClinicId == clinicId).ToListAsync(),
            current.Id, variants,
            (row, name, spec, ts) =>
            {
                row.DoctorRecordId = current.Id;
                row.DoctorName = name;
                row.Specialty = spec;
                row.UpdatedAt = ts;
            },
            newName, specialty, now);

        anyChanged |= UpdateDoctorRows(
            await _db.CashReceipts.Where(r => r.ClinicId == clinicId).ToListAsync(),
            current.Id, variants,
            (row, name, spec, ts) =>
            {
                row.DoctorRecordId = current.Id;
                row.DoctorName = name;
                row.Specialty = spec;
                row.UpdatedAt = ts;
            },
            newName, specialty, now);

        anyChanged |= UpdateDoctorRows(
            await _db.CashPayments.Where(p => p.ClinicId == clinicId).ToListAsync(),
            current.Id, variants,
            (row, name, _, ts) =>
            {
                row.DoctorRecordId = current.Id;
                row.DoctorName = name;
                row.UpdatedAt = ts;
            },
            newName, specialty, now);

        anyChanged |= UpdateDoctorRows(
            await _db.Prescriptions.Where(p => p.ClinicId == clinicId).ToListAsync(),
            current.Id, variants,
            (row, name, spec, ts) =>
            {
                row.DoctorRecordId = current.Id;
                row.DoctorName = name;
                row.Specialty = spec;
                row.UpdatedAt = ts;
            },
            newName, specialty, now);

        anyChanged |= UpdateDoctorRows(
            await _db.ServiceIncomeRequests.Where(r => r.ClinicId == clinicId).ToListAsync(),
            current.Id, variants,
            (row, name, spec, ts) =>
            {
                row.DoctorRecordId = current.Id;
                row.DoctorName = name;
                row.Specialty = spec;
                row.UpdatedAt = ts;
            },
            newName, specialty, now);

        anyChanged |= UpdateDoctorRows(
            await _db.Appointments.Where(a => a.ClinicId == clinicId).ToListAsync(),
            current.Id, variants,
            (row, name, spec, _) =>
            {
                row.DoctorRecordId = current.Id;
                row.DoctorName = name;
                row.Department = spec;
            },
            newName, specialty, now);

        var invoices = await _db.Invoices
            .Include(i => i.Lines)
            .Where(i => i.ClinicId == clinicId)
            .ToListAsync();

        foreach (var invoice in invoices)
        {
            if (!DoctorNameMatcher.ShouldUpdateDoctor(invoice.DoctorRecordId, invoice.DoctorName, current.Id, variants))
                continue;

            invoice.DoctorRecordId = current.Id;
            invoice.DoctorName = newName;
            invoice.Specialty = specialty;
            invoice.UpdatedAt = now;

            foreach (var line in invoice.Lines.Where(l => IsConsultationLine(l.ServiceName, oldName) || IsConsultationLine(l.ServiceName, newName)))
                line.ServiceName = $"Consultation Fee - {newName}";

            anyChanged = true;
        }

        if (anyChanged)
            await _db.SaveChangesAsync();

        if (previous.ConsultationFee != current.ConsultationFee || !string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
            await PropagateConsultationFeeAsync(clinicId, current, variants);

        await PropagateJournalDoctorNamesAsync(clinicId, variants, newName);
    }

    public async Task SyncDoctorLinkedRowsAsync(Guid clinicId, Doctor doctor)
    {
        var now = DateTime.UtcNow;
        var anyChanged = false;

        foreach (var row in await _db.Patients.Where(p => p.ClinicId == clinicId && p.DoctorRecordId == doctor.Id).ToListAsync())
        {
            row.DoctorName = doctor.Name;
            row.Specialty = doctor.Specialty;
            row.UpdatedAt = now;
            anyChanged = true;
        }

        foreach (var row in await _db.Invoices.Where(i => i.ClinicId == clinicId && i.DoctorRecordId == doctor.Id).ToListAsync())
        {
            row.DoctorName = doctor.Name;
            row.Specialty = doctor.Specialty;
            row.UpdatedAt = now;
            anyChanged = true;
        }

        foreach (var row in await _db.LabRequests.Where(r => r.ClinicId == clinicId && r.DoctorRecordId == doctor.Id).ToListAsync())
        {
            row.DoctorName = doctor.Name;
            row.Specialty = doctor.Specialty;
            row.UpdatedAt = now;
            anyChanged = true;
        }

        foreach (var row in await _db.LabResults.Where(r => r.ClinicId == clinicId && r.DoctorRecordId == doctor.Id).ToListAsync())
        {
            row.DoctorName = doctor.Name;
            row.Specialty = doctor.Specialty;
            row.UpdatedAt = now;
            anyChanged = true;
        }

        foreach (var row in await _db.RadiologyRequests.Where(r => r.ClinicId == clinicId && r.DoctorRecordId == doctor.Id).ToListAsync())
        {
            row.DoctorName = doctor.Name;
            row.Specialty = doctor.Specialty;
            row.UpdatedAt = now;
            anyChanged = true;
        }

        foreach (var row in await _db.RadiologyResults.Where(r => r.ClinicId == clinicId && r.DoctorRecordId == doctor.Id).ToListAsync())
        {
            row.DoctorName = doctor.Name;
            row.Specialty = doctor.Specialty;
            row.UpdatedAt = now;
            anyChanged = true;
        }

        foreach (var row in await _db.PharmacyRequests.Where(r => r.ClinicId == clinicId && r.DoctorRecordId == doctor.Id).ToListAsync())
        {
            row.DoctorName = doctor.Name;
            row.Specialty = doctor.Specialty;
            row.UpdatedAt = now;
            anyChanged = true;
        }

        foreach (var row in await _db.PharmacyBills.Where(b => b.ClinicId == clinicId && b.DoctorRecordId == doctor.Id).ToListAsync())
        {
            row.DoctorName = doctor.Name;
            row.Specialty = doctor.Specialty;
            row.UpdatedAt = now;
            anyChanged = true;
        }

        foreach (var row in await _db.CashReceipts.Where(r => r.ClinicId == clinicId && r.DoctorRecordId == doctor.Id).ToListAsync())
        {
            row.DoctorName = doctor.Name;
            row.Specialty = doctor.Specialty;
            row.UpdatedAt = now;
            anyChanged = true;
        }

        foreach (var row in await _db.CashPayments.Where(p => p.ClinicId == clinicId && p.DoctorRecordId == doctor.Id).ToListAsync())
        {
            row.DoctorName = doctor.Name;
            row.UpdatedAt = now;
            anyChanged = true;
        }

        foreach (var row in await _db.Prescriptions.Where(p => p.ClinicId == clinicId && p.DoctorRecordId == doctor.Id).ToListAsync())
        {
            row.DoctorName = doctor.Name;
            row.Specialty = doctor.Specialty;
            row.UpdatedAt = now;
            anyChanged = true;
        }

        foreach (var row in await _db.ServiceIncomeRequests.Where(r => r.ClinicId == clinicId && r.DoctorRecordId == doctor.Id).ToListAsync())
        {
            row.DoctorName = doctor.Name;
            row.Specialty = doctor.Specialty;
            row.UpdatedAt = now;
            anyChanged = true;
        }

        foreach (var row in await _db.Appointments.Where(a => a.ClinicId == clinicId && a.DoctorRecordId == doctor.Id).ToListAsync())
        {
            row.DoctorName = doctor.Name;
            row.Department = doctor.Specialty;
            anyChanged = true;
        }

        if (anyChanged)
            await _db.SaveChangesAsync();
    }

    public async Task SyncAllDoctorLinkedRowsAsync(Guid clinicId)
    {
        var doctors = await _db.Doctors.ForClinic(clinicId).AsNoTracking().ToListAsync();
        foreach (var doctor in doctors)
            await SyncDoctorLinkedRowsAsync(clinicId, doctor);
    }

    private async Task<HashSet<string>> BuildDoctorNameVariantsAsync(
        Guid clinicId,
        Guid doctorId,
        string oldName,
        string newName)
    {
        var doctors = await _db.Doctors.ForClinic(clinicId).AsNoTracking().ToListAsync();
        var variants = DoctorNameMatcher.BuildNameVariants(oldName, newName, Array.Empty<string?>());

        DoctorNameMatcher.CollectUnambiguousVariants(doctors, doctorId, oldName, variants);
        DoctorNameMatcher.CollectUnambiguousVariants(doctors, doctorId, newName, variants);

        void AddLinkedNames(IEnumerable<string?> names)
        {
            foreach (var name in names)
            {
                if (!string.IsNullOrWhiteSpace(name))
                    variants.Add(name.Trim());
            }
        }

        AddLinkedNames(await _db.Patients.Where(p => p.ClinicId == clinicId && p.DoctorRecordId == doctorId).Select(p => p.DoctorName).ToListAsync());
        AddLinkedNames(await _db.Invoices.Where(i => i.ClinicId == clinicId && i.DoctorRecordId == doctorId).Select(i => i.DoctorName).ToListAsync());
        AddLinkedNames(await _db.LabRequests.Where(r => r.ClinicId == clinicId && r.DoctorRecordId == doctorId).Select(r => r.DoctorName).ToListAsync());
        AddLinkedNames(await _db.LabResults.Where(r => r.ClinicId == clinicId && r.DoctorRecordId == doctorId).Select(r => r.DoctorName).ToListAsync());
        AddLinkedNames(await _db.RadiologyRequests.Where(r => r.ClinicId == clinicId && r.DoctorRecordId == doctorId).Select(r => r.DoctorName).ToListAsync());
        AddLinkedNames(await _db.RadiologyResults.Where(r => r.ClinicId == clinicId && r.DoctorRecordId == doctorId).Select(r => r.DoctorName).ToListAsync());
        AddLinkedNames(await _db.PharmacyRequests.Where(r => r.ClinicId == clinicId && r.DoctorRecordId == doctorId).Select(r => r.DoctorName).ToListAsync());
        AddLinkedNames(await _db.PharmacyBills.Where(b => b.ClinicId == clinicId && b.DoctorRecordId == doctorId).Select(b => b.DoctorName).ToListAsync());
        AddLinkedNames(await _db.CashReceipts.Where(r => r.ClinicId == clinicId && r.DoctorRecordId == doctorId).Select(r => r.DoctorName).ToListAsync());
        AddLinkedNames(await _db.CashPayments.Where(p => p.ClinicId == clinicId && p.DoctorRecordId == doctorId).Select(p => p.DoctorName).ToListAsync());
        AddLinkedNames(await _db.Prescriptions.Where(p => p.ClinicId == clinicId && p.DoctorRecordId == doctorId).Select(p => p.DoctorName).ToListAsync());
        AddLinkedNames(await _db.ServiceIncomeRequests.Where(r => r.ClinicId == clinicId && r.DoctorRecordId == doctorId).Select(r => r.DoctorName).ToListAsync());
        AddLinkedNames(await _db.Appointments.Where(a => a.ClinicId == clinicId && a.DoctorRecordId == doctorId).Select(a => a.DoctorName).ToListAsync());

        foreach (var storedName in await CollectDistinctStoredDoctorNamesAsync(clinicId))
        {
            if (!DoctorNameMatcher.NamesReferToSameDoctor(oldName, storedName))
                continue;

            var matches = doctors.Where(d => DoctorNameMatcher.NamesReferToSameDoctor(d.Name, storedName)).ToList();
            if (matches.Count == 1 && matches[0].Id == doctorId)
                variants.Add(storedName);
        }

        return variants;
    }

    private async Task<List<string>> CollectDistinctStoredDoctorNamesAsync(Guid clinicId)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(IEnumerable<string?> source)
        {
            foreach (var name in source)
            {
                if (!string.IsNullOrWhiteSpace(name))
                    names.Add(name.Trim());
            }
        }

        Add(await _db.Patients.Where(p => p.ClinicId == clinicId).Select(p => p.DoctorName).ToListAsync());
        Add(await _db.Invoices.Where(i => i.ClinicId == clinicId).Select(i => i.DoctorName).ToListAsync());
        Add(await _db.LabRequests.Where(r => r.ClinicId == clinicId).Select(r => r.DoctorName).ToListAsync());
        Add(await _db.LabResults.Where(r => r.ClinicId == clinicId).Select(r => r.DoctorName).ToListAsync());
        Add(await _db.RadiologyRequests.Where(r => r.ClinicId == clinicId).Select(r => r.DoctorName).ToListAsync());
        Add(await _db.RadiologyResults.Where(r => r.ClinicId == clinicId).Select(r => r.DoctorName).ToListAsync());
        Add(await _db.PharmacyRequests.Where(r => r.ClinicId == clinicId).Select(r => r.DoctorName).ToListAsync());
        Add(await _db.PharmacyBills.Where(b => b.ClinicId == clinicId).Select(b => b.DoctorName).ToListAsync());
        Add(await _db.CashReceipts.Where(r => r.ClinicId == clinicId).Select(r => r.DoctorName).ToListAsync());
        Add(await _db.CashPayments.Where(p => p.ClinicId == clinicId).Select(p => p.DoctorName).ToListAsync());
        Add(await _db.Prescriptions.Where(p => p.ClinicId == clinicId).Select(p => p.DoctorName).ToListAsync());
        Add(await _db.ServiceIncomeRequests.Where(r => r.ClinicId == clinicId).Select(r => r.DoctorName).ToListAsync());
        Add(await _db.Appointments.Where(a => a.ClinicId == clinicId).Select(a => a.DoctorName).ToListAsync());
        Add(await _db.JournalEntries.Where(j => j.ClinicId == clinicId).Select(j => j.DoctorName).ToListAsync());

        return names.ToList();
    }

    private static bool UpdateDoctorRows<T>(
        IList<T> rows,
        Guid doctorId,
        ISet<string> variants,
        Action<T, string, string?, DateTime> apply,
        string newName,
        string? specialty,
        DateTime now)
    {
        var changed = false;
        foreach (var row in rows)
        {
            var recordId = row switch
            {
                Patient p => p.DoctorRecordId,
                LabRequest r => r.DoctorRecordId,
                LabResult r => r.DoctorRecordId,
                RadiologyRequest r => r.DoctorRecordId,
                RadiologyResult r => r.DoctorRecordId,
                PharmacyRequest r => r.DoctorRecordId,
                PharmacyBill b => b.DoctorRecordId,
                CashReceipt r => r.DoctorRecordId,
                CashPayment p => p.DoctorRecordId,
                Prescription p => p.DoctorRecordId,
                ServiceIncomeRequest r => r.DoctorRecordId,
                Appointment a => a.DoctorRecordId,
                _ => null
            };

            var doctorName = row switch
            {
                Patient p => p.DoctorName,
                LabRequest r => r.DoctorName,
                LabResult r => r.DoctorName,
                RadiologyRequest r => r.DoctorName,
                RadiologyResult r => r.DoctorName,
                PharmacyRequest r => r.DoctorName,
                PharmacyBill b => b.DoctorName,
                CashReceipt r => r.DoctorName,
                CashPayment p => p.DoctorName,
                Prescription p => p.DoctorName,
                ServiceIncomeRequest r => r.DoctorName,
                Appointment a => a.DoctorName,
                _ => null
            };

            if (!DoctorNameMatcher.ShouldUpdateDoctor(recordId, doctorName, doctorId, variants))
                continue;

            apply(row, newName, specialty, now);
            changed = true;
        }

        return changed;
    }

    private async Task PropagateJournalDoctorNamesAsync(Guid clinicId, HashSet<string> variants, string newName)
    {
        var now = DateTime.UtcNow;
        var entries = await _db.JournalEntries
            .ForClinic(clinicId)
            .Where(j => j.DoctorName != null)
            .ToListAsync();

        var changed = false;
        foreach (var entry in entries)
        {
            if (!variants.Contains(entry.DoctorName!.Trim()))
                continue;

            entry.DoctorName = newName;
            entry.UpdatedAt = now;
            changed = true;
        }

        if (changed)
            await _db.SaveChangesAsync();
    }

    private async Task PropagateConsultationFeeAsync(Guid clinicId, Doctor doctor, HashSet<string> variants)
    {
        var invoices = await _db.Invoices
            .Include(i => i.Lines)
            .Where(i => i.ClinicId == clinicId)
            .ToListAsync();

        invoices = invoices.Where(i =>
            i.DoctorRecordId == doctor.Id ||
            (i.DoctorName != null && variants.Contains(i.DoctorName.Trim()))).ToList();

        foreach (var invoice in invoices)
        {
            var changed = false;
            foreach (var line in invoice.Lines)
            {
                if (!IsConsultationLine(line.ServiceName, doctor.Name)) continue;
                line.UnitFee = doctor.ConsultationFee;
                line.LineTotal = line.Qty * line.UnitFee;
                changed = true;
            }

            if (!changed) continue;
            invoice.SubTotal = invoice.Lines.Sum(l => l.LineTotal);
            invoice.TotalAmount = invoice.SubTotal - invoice.Discount + invoice.TaxAmount;
            invoice.BalanceDue = invoice.TotalAmount - invoice.AmountPaid;
            invoice.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        foreach (var invoice in invoices)
        {
            try { await _journalSync.SyncInvoiceAsync(clinicId, invoice, invoice.Lines.ToList()); }
            catch { }
        }
    }

    private static bool IsConsultationLine(string? serviceName, string doctorName) =>
        ClinicalDemographicsSyncService.IsConsultationLine(serviceName, doctorName);

    public async Task PropagateLabTestAsync(Guid clinicId, LabTest previous, LabTest current)
    {
        var requestIds = new HashSet<Guid>();
        var lines = await _db.LabRequestLines
            .Include(l => l.LabRequest)
            .Where(l => l.LabRequest.ClinicId == clinicId &&
                        (l.TestCode == previous.TestCode || l.TestCode == current.TestCode ||
                         l.TestName == previous.TestName))
            .ToListAsync();

        foreach (var line in lines)
        {
            line.TestCode = current.TestCode;
            line.TestName = current.TestName;
            line.Category = current.Category;
            line.Fee = current.Fee;
            line.Total = line.Qty * line.Fee;
            requestIds.Add(line.LabRequestId);
        }

        var resultLines = await _db.LabResultLines
            .Include(l => l.LabResult)
            .Where(l => l.LabResult.ClinicId == clinicId &&
                        (l.TestCode == previous.TestCode || l.TestCode == current.TestCode ||
                         l.TestName == previous.TestName))
            .ToListAsync();

        foreach (var line in resultLines)
        {
            line.TestCode = current.TestCode;
            line.TestName = current.TestName;
            line.Category = current.Category;
        }

        await RecalcLabRequestTotalsAsync(clinicId, requestIds);
        foreach (var requestId in requestIds)
        {
            var request = await _db.LabRequests.ForClinic(clinicId).Include(r => r.Lines)
                .FirstOrDefaultAsync(r => r.Id == requestId);
            if (request is null) continue;
            var orderedLines = request.Lines.OrderBy(l => l.LineNo).ToList();
            try
            {
                await _billing.SyncLabRequestAsync(clinicId, request, orderedLines, request, orderedLines);
            }
            catch { }
        }
    }

    public async Task PropagateRadiologyTestAsync(Guid clinicId, RadiologyTest previous, RadiologyTest current)
    {
        var requestIds = new HashSet<Guid>();
        var lines = await _db.RadiologyRequestLines
            .Include(l => l.RadiologyRequest)
            .Where(l => l.RadiologyRequest.ClinicId == clinicId &&
                        (l.TestCode == previous.TestCode || l.TestCode == current.TestCode ||
                         l.TestName == previous.TestName))
            .ToListAsync();

        foreach (var line in lines)
        {
            line.TestCode = current.TestCode;
            line.TestName = current.TestName;
            line.Category = current.Category;
            line.Fee = current.Fee;
            line.Total = line.Qty * line.Fee;
            requestIds.Add(line.RadiologyRequestId);
        }

        var resultLines = await _db.RadiologyResultLines
            .Include(l => l.RadiologyResult)
            .Where(l => l.RadiologyResult.ClinicId == clinicId &&
                        (l.TestCode == previous.TestCode || l.TestCode == current.TestCode ||
                         l.TestName == previous.TestName))
            .ToListAsync();

        foreach (var line in resultLines)
        {
            line.TestCode = current.TestCode;
            line.TestName = current.TestName;
            line.Category = current.Category;
        }

        await RecalcRadiologyRequestTotalsAsync(clinicId, requestIds);
        foreach (var requestId in requestIds)
        {
            var request = await _db.RadiologyRequests.ForClinic(clinicId).Include(r => r.Lines)
                .FirstOrDefaultAsync(r => r.Id == requestId);
            if (request is null) continue;
            var orderedLines = request.Lines.OrderBy(l => l.LineNo).ToList();
            try
            {
                await _billing.SyncRadiologyRequestAsync(clinicId, request, orderedLines, request, orderedLines);
            }
            catch { }
        }
    }

    public async Task PropagatePharmacyItemAsync(Guid clinicId, PharmacyItem previous, PharmacyItem current)
    {
        var requestIds = new HashSet<Guid>();
        var billIds = new HashSet<Guid>();

        bool matchesItem(string? barcode, string? code, string? name) =>
            (!string.IsNullOrWhiteSpace(barcode) && (barcode == previous.Barcode || barcode == current.Barcode)) ||
            (!string.IsNullOrWhiteSpace(code) && (code == previous.MedicineCode || code == current.MedicineCode)) ||
            (!string.IsNullOrWhiteSpace(name) && name == previous.MedicineName);

        var requestLines = await _db.PharmacyRequestLines
            .Include(l => l.PharmacyRequest)
            .Where(l => l.PharmacyRequest.ClinicId == clinicId &&
                        (l.Barcode == previous.Barcode || l.Barcode == current.Barcode ||
                         l.MedicineCode == previous.MedicineCode || l.MedicineCode == current.MedicineCode ||
                         l.MedicineName == previous.MedicineName))
            .ToListAsync();

        foreach (var line in requestLines.Where(l => matchesItem(l.Barcode, l.MedicineCode, l.MedicineName)))
        {
            line.Barcode = current.Barcode;
            line.MedicineCode = current.MedicineCode;
            line.MedicineName = current.MedicineName;
            line.Dosage = current.Dosage;
            line.Uom = current.BaseUom;
            line.UnitPrice = current.DefaultUnitPrice;
            line.Total = line.Qty * line.UnitPrice;
            requestIds.Add(line.PharmacyRequestId);
        }

        var billLines = await _db.PharmacyBillLines
            .Include(l => l.PharmacyBill)
            .Where(l => l.PharmacyBill.ClinicId == clinicId &&
                        (l.Barcode == previous.Barcode || l.Barcode == current.Barcode ||
                         l.MedicineCode == previous.MedicineCode || l.MedicineCode == current.MedicineCode ||
                         l.MedicineName == previous.MedicineName))
            .ToListAsync();

        foreach (var line in billLines.Where(l => matchesItem(l.Barcode, l.MedicineCode, l.MedicineName)))
        {
            line.Barcode = current.Barcode;
            line.MedicineCode = current.MedicineCode;
            line.MedicineName = current.MedicineName;
            line.Dosage = current.Dosage;
            line.Uom = current.BaseUom;
            line.UnitPrice = current.DefaultUnitPrice;
            line.LineTotal = line.Qty * line.UnitPrice;
            billIds.Add(line.PharmacyBillId);
        }

        var prescriptionLines = await _db.PrescriptionLines
            .Include(l => l.Prescription)
            .Where(l => l.Prescription.ClinicId == clinicId &&
                        (l.PharmacyItemId == current.Id ||
                         l.MedicineName == previous.MedicineName ||
                         l.MedicineName == current.MedicineName))
            .ToListAsync();

        foreach (var line in prescriptionLines.Where(l =>
                     l.PharmacyItemId == current.Id || l.MedicineName == previous.MedicineName))
            line.MedicineName = current.MedicineName;

        await _db.SaveChangesAsync();

        await RecalcPharmacyRequestTotalsAsync(clinicId, requestIds);
        await RecalcPharmacyBillTotalsAsync(clinicId, billIds);

        foreach (var requestId in requestIds)
        {
            var request = await _db.PharmacyRequests.ForClinic(clinicId).Include(r => r.Lines)
                .FirstOrDefaultAsync(r => r.Id == requestId);
            if (request is null) continue;
            var orderedLines = request.Lines.OrderBy(l => l.LineNo).ToList();
            try
            {
                await _billing.SyncPharmacyRequestAsync(clinicId, request, orderedLines, request, orderedLines);
            }
            catch { }
        }

        foreach (var billId in billIds)
        {
            var bill = await _db.PharmacyBills.ForClinic(clinicId).Include(b => b.Lines)
                .FirstOrDefaultAsync(b => b.Id == billId);
            if (bill is null) continue;
            var orderedLines = bill.Lines.OrderBy(l => l.LineNo).ToList();
            try
            {
                await _billing.SyncPharmacyBillAsync(clinicId, bill, orderedLines, bill, orderedLines);
            }
            catch { }
        }
    }

    public async Task PropagateServiceIncomeAsync(Guid clinicId, ServiceIncome previous, ServiceIncome current)
    {
        var invoiceIds = new HashSet<Guid>();
        var lines = await _db.InvoiceLines
            .Include(l => l.Invoice)
            .Where(l => l.Invoice.ClinicId == clinicId &&
                        (l.ServiceNo == previous.ServiceNo || l.ServiceNo == current.ServiceNo ||
                         l.ServiceName == previous.Name))
            .ToListAsync();

        foreach (var line in lines)
        {
            line.ServiceNo = current.ServiceNo;
            line.ServiceName = current.Name;
            line.AccountName = current.AccountName;
            line.UnitFee = current.Fee;
            line.LineTotal = line.Qty * line.UnitFee;
            invoiceIds.Add(line.InvoiceId);
        }

        var requestIds = new HashSet<Guid>();
        var requestLines = await _db.ServiceIncomeRequestLines
            .Include(l => l.ServiceIncomeRequest)
            .Where(l => l.ServiceIncomeRequest.ClinicId == clinicId &&
                        (l.ServiceNo == previous.ServiceNo || l.ServiceNo == current.ServiceNo ||
                         l.ServiceName == previous.Name))
            .ToListAsync();

        foreach (var line in requestLines)
        {
            line.ServiceNo = current.ServiceNo;
            line.ServiceName = current.Name;
            line.AccountName = current.AccountName;
            line.Fee = current.Fee;
            line.Total = line.Qty * line.Fee;
            requestIds.Add(line.ServiceIncomeRequestId);
        }

        await RecalcServiceIncomeRequestTotalsAsync(clinicId, requestIds);
        await RecalcInvoiceTotalsAsync(clinicId, invoiceIds);

        foreach (var requestId in requestIds)
        {
            var request = await _db.ServiceIncomeRequests.ForClinic(clinicId).Include(r => r.Lines)
                .FirstOrDefaultAsync(r => r.Id == requestId);
            if (request is null) continue;
            var orderedLines = request.Lines.OrderBy(l => l.LineNo).ToList();
            try
            {
                await _billing.SyncServiceIncomeRequestAsync(clinicId, request, orderedLines, request, orderedLines);
            }
            catch { }
        }
    }

    public async Task PropagateChartAccountAsync(Guid clinicId, ChartAccount previous, ChartAccount current)
    {
        if (string.Equals(previous.Name, current.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(previous.CategoryType, current.CategoryType, StringComparison.OrdinalIgnoreCase))
            return;

        var journalIds = await _db.JournalEntries.ForClinic(clinicId).Select(j => j.Id).ToListAsync();
        if (journalIds.Count > 0)
        {
            var journalLines = await _db.JournalEntryLines
                .Where(l => journalIds.Contains(l.JournalEntryId) && l.AccountName == previous.Name)
                .ToListAsync();
            foreach (var line in journalLines)
            {
                line.AccountName = current.Name;
                line.AccountCategory = current.CategoryType;
            }
        }

        var expenseLines = await _db.ExpenseVoucherLines
            .Include(l => l.ExpenseVoucher)
            .Where(l => l.ExpenseVoucher.ClinicId == clinicId && l.ChartAccountName == previous.Name)
            .ToListAsync();
        foreach (var line in expenseLines)
            line.ChartAccountName = current.Name;

        var clinicInvoiceIds = await _db.Invoices.ForClinic(clinicId).Select(i => i.Id).ToListAsync();
        if (clinicInvoiceIds.Count > 0)
        {
            await _db.InvoiceLines
                .Where(l => clinicInvoiceIds.Contains(l.InvoiceId) && l.AccountName == previous.Name)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.AccountName, current.Name));
        }

        var clinicRequestIds = await _db.ServiceIncomeRequests.ForClinic(clinicId).Select(r => r.Id).ToListAsync();
        if (clinicRequestIds.Count > 0)
        {
            await _db.ServiceIncomeRequestLines
                .Where(l => clinicRequestIds.Contains(l.ServiceIncomeRequestId) && l.AccountName == previous.Name)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.AccountName, current.Name));
        }

        await _db.ServiceIncomes
            .ForClinic(clinicId)
            .Where(s => s.AccountName == previous.Name)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.AccountName, current.Name));

        var pharmacyItems = await _db.PharmacyItems
            .ForClinic(clinicId)
            .Where(p => p.IncomeAccountName == previous.Name || p.CostAccountName == previous.Name || p.InventoryAccountName == previous.Name)
            .ToListAsync();
        foreach (var item in pharmacyItems)
        {
            if (string.Equals(item.IncomeAccountName, previous.Name, StringComparison.OrdinalIgnoreCase))
                item.IncomeAccountName = current.Name;
            if (string.Equals(item.CostAccountName, previous.Name, StringComparison.OrdinalIgnoreCase))
                item.CostAccountName = current.Name;
            if (string.Equals(item.InventoryAccountName, previous.Name, StringComparison.OrdinalIgnoreCase))
                item.InventoryAccountName = current.Name;
        }

        await _db.CashReceipts
            .ForClinic(clinicId)
            .Where(r => r.ChartAccountName == previous.Name)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ChartAccountName, current.Name));

        await _db.SaveChangesAsync();
    }

    private async Task PropagateJournalPatientDoctorNamesAsync(
        Guid clinicId,
        string? oldPatientName,
        string? newPatientName,
        string? oldDoctorName,
        string? newDoctorName)
    {
        var now = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(oldPatientName) && !string.IsNullOrWhiteSpace(newPatientName) &&
            !string.Equals(oldPatientName, newPatientName, StringComparison.OrdinalIgnoreCase))
        {
            await _db.JournalEntries
                .ForClinic(clinicId)
                .Where(j => j.PatientName == oldPatientName)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.PatientName, newPatientName)
                    .SetProperty(j => j.UpdatedAt, now));
        }

        if (!string.IsNullOrWhiteSpace(oldDoctorName) && !string.IsNullOrWhiteSpace(newDoctorName) &&
            !string.Equals(oldDoctorName, newDoctorName, StringComparison.OrdinalIgnoreCase))
        {
            await _db.JournalEntries
                .ForClinic(clinicId)
                .Where(j => j.DoctorName == oldDoctorName)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.DoctorName, newDoctorName)
                    .SetProperty(j => j.UpdatedAt, now));
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(newPatientName) || !string.IsNullOrWhiteSpace(oldPatientName))
            {
                var patientFilter = newPatientName ?? oldPatientName!;
                var invoices = await _db.Invoices
                    .ForClinic(clinicId)
                    .Include(i => i.Lines)
                    .Where(i => i.PatientName == patientFilter || i.PatientName == oldPatientName)
                    .ToListAsync();
                foreach (var invoice in invoices)
                {
                    try { await _journalSync.SyncInvoiceAsync(clinicId, invoice, invoice.Lines.ToList()); }
                    catch { }
                }

                var receipts = await _db.CashReceipts
                    .ForClinic(clinicId)
                    .Where(r => r.PatientName == patientFilter || r.PatientName == oldPatientName)
                    .ToListAsync();
                foreach (var receipt in receipts)
                {
                    try { await _journalSync.SyncCashReceiptAsync(clinicId, receipt); }
                    catch { }
                }

                var bills = await _db.PharmacyBills
                    .ForClinic(clinicId)
                    .Include(b => b.Lines)
                    .Where(b => b.PatientName == patientFilter || b.PatientName == oldPatientName)
                    .ToListAsync();
                foreach (var bill in bills)
                {
                    try { await _journalSync.SyncPharmacyBillAsync(clinicId, bill, bill.Lines.ToList()); }
                    catch { }
                }
            }
            else if (!string.IsNullOrWhiteSpace(newDoctorName) || !string.IsNullOrWhiteSpace(oldDoctorName))
            {
                var doctorFilter = newDoctorName ?? oldDoctorName!;
                var invoices = await _db.Invoices
                    .ForClinic(clinicId)
                    .Include(i => i.Lines)
                    .Where(i => i.DoctorName == doctorFilter || i.DoctorName == oldDoctorName)
                    .ToListAsync();
                foreach (var invoice in invoices)
                {
                    try { await _journalSync.SyncInvoiceAsync(clinicId, invoice, invoice.Lines.ToList()); }
                    catch { }
                }

                var receipts = await _db.CashReceipts
                    .ForClinic(clinicId)
                    .Where(r => r.DoctorName == doctorFilter || r.DoctorName == oldDoctorName)
                    .ToListAsync();
                foreach (var receipt in receipts)
                {
                    try { await _journalSync.SyncCashReceiptAsync(clinicId, receipt); }
                    catch { }
                }
            }
        }
        catch { }
    }

    private async Task RecalcLabRequestTotalsAsync(Guid clinicId, HashSet<Guid> requestIds)
    {
        if (requestIds.Count == 0) return;
        var requests = await _db.LabRequests.Include(r => r.Lines)
            .Where(r => r.ClinicId == clinicId && requestIds.Contains(r.Id))
            .ToListAsync();
        foreach (var r in requests)
        {
            r.TotalAmount = r.Lines.Sum(l => l.Total);
            r.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();
    }

    private async Task RecalcRadiologyRequestTotalsAsync(Guid clinicId, HashSet<Guid> requestIds)
    {
        if (requestIds.Count == 0) return;
        var requests = await _db.RadiologyRequests.Include(r => r.Lines)
            .Where(r => r.ClinicId == clinicId && requestIds.Contains(r.Id))
            .ToListAsync();
        foreach (var r in requests)
        {
            r.TotalAmount = r.Lines.Sum(l => l.Total);
            r.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();
    }

    private async Task RecalcPharmacyRequestTotalsAsync(Guid clinicId, HashSet<Guid> requestIds)
    {
        if (requestIds.Count == 0) return;
        var requests = await _db.PharmacyRequests.Include(r => r.Lines)
            .Where(r => r.ClinicId == clinicId && requestIds.Contains(r.Id))
            .ToListAsync();
        foreach (var r in requests)
        {
            r.TotalAmount = r.Lines.Sum(l => l.Total);
            r.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();
    }

    private async Task RecalcPharmacyBillTotalsAsync(Guid clinicId, HashSet<Guid> billIds)
    {
        if (billIds.Count == 0) return;
        var bills = await _db.PharmacyBills.Include(b => b.Lines)
            .Where(b => b.ClinicId == clinicId && billIds.Contains(b.Id))
            .ToListAsync();
        foreach (var b in bills)
        {
            b.SubTotal = b.Lines.Sum(l => l.LineTotal);
            b.TotalAmount = b.SubTotal - b.Discount;
            b.BalanceDue = b.TotalAmount - b.AmountPaid;
            b.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();
    }

    private async Task RecalcServiceIncomeRequestTotalsAsync(Guid clinicId, HashSet<Guid> requestIds)
    {
        if (requestIds.Count == 0) return;
        var requests = await _db.ServiceIncomeRequests.Include(r => r.Lines)
            .Where(r => r.ClinicId == clinicId && requestIds.Contains(r.Id))
            .ToListAsync();
        foreach (var r in requests)
        {
            r.TotalAmount = r.Lines.Sum(l => l.Total);
            r.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();
    }

    private async Task RecalcInvoiceTotalsAsync(Guid clinicId, HashSet<Guid> invoiceIds)
    {
        if (invoiceIds.Count == 0) return;
        var invoices = await _db.Invoices.Include(i => i.Lines)
            .Where(i => i.ClinicId == clinicId && invoiceIds.Contains(i.Id))
            .ToListAsync();
        foreach (var i in invoices)
        {
            i.SubTotal = i.Lines.Sum(l => l.LineTotal);
            i.TotalAmount = i.SubTotal - i.Discount + i.TaxAmount;
            i.BalanceDue = i.TotalAmount - i.AmountPaid;
            i.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();

        foreach (var invoice in invoices)
        {
            try { await _journalSync.SyncInvoiceAsync(clinicId, invoice, invoice.Lines.ToList()); }
            catch { }
        }
    }
}
