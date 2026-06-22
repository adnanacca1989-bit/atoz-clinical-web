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

    public MasterDataPropagationService(ClinicalDbContext db) => _db = db;

    public async Task PropagatePatientAsync(Guid clinicId, Patient previous, Patient current)
    {
        var patientNo = current.PatientNo.Trim();
        var oldName = previous.FullName.Trim();
        var newName = current.FullName.Trim();
        var age = current.AgeYears;
        var phone = current.Phone;
        var gender = current.Gender;
        var city = current.City;
        var doctorName = current.DoctorName;
        var specialty = current.Specialty;
        var now = DateTime.UtcNow;

        bool matchesIdOrName(string? id, string? barcode, string? name) =>
            (!string.IsNullOrWhiteSpace(id) && id.Trim() == patientNo) ||
            (!string.IsNullOrWhiteSpace(barcode) && barcode.Trim() == patientNo) ||
            (!string.IsNullOrWhiteSpace(name) && name.Trim() == oldName);

        var invoices = await _db.Invoices
            .Where(i => i.ClinicId == clinicId)
            .ToListAsync();
        foreach (var row in invoices.Where(i => matchesIdOrName(i.PatientId, null, i.PatientName)))
        {
            row.PatientId = patientNo;
            row.PatientName = newName;
            row.Phone = phone;
            row.Age = age;
            row.Gender = gender;
            row.City = city;
            row.DoctorName = doctorName;
            row.Specialty = specialty;
            row.UpdatedAt = now;
        }

        var labRequests = await _db.LabRequests.Where(r => r.ClinicId == clinicId).ToListAsync();
        foreach (var row in labRequests.Where(r => matchesIdOrName(null, r.PatientBarcode, r.PatientName)))
        {
            row.PatientBarcode = patientNo;
            row.PatientName = newName;
            row.Phone = phone;
            row.Age = age;
            row.Gender = gender;
            row.City = city;
            row.DoctorName = doctorName;
            row.Specialty = specialty;
            row.UpdatedAt = now;
        }

        var labResults = await _db.LabResults.Where(r => r.ClinicId == clinicId).ToListAsync();
        foreach (var row in labResults.Where(r => !string.IsNullOrWhiteSpace(r.PatientName) && r.PatientName.Trim() == oldName))
        {
            row.PatientName = newName;
            row.DoctorName = doctorName;
            row.Specialty = specialty;
            row.UpdatedAt = now;
        }

        var radioRequests = await _db.RadiologyRequests.Where(r => r.ClinicId == clinicId).ToListAsync();
        foreach (var row in radioRequests.Where(r => matchesIdOrName(null, r.PatientBarcode, r.PatientName)))
        {
            row.PatientBarcode = patientNo;
            row.PatientName = newName;
            row.Phone = phone;
            row.Age = age;
            row.Gender = gender;
            row.City = city;
            row.DoctorName = doctorName;
            row.Specialty = specialty;
            row.UpdatedAt = now;
        }

        var radioResults = await _db.RadiologyResults.Where(r => r.ClinicId == clinicId).ToListAsync();
        foreach (var row in radioResults.Where(r => !string.IsNullOrWhiteSpace(r.PatientName) && r.PatientName.Trim() == oldName))
        {
            row.PatientName = newName;
            row.DoctorName = doctorName;
            row.Specialty = specialty;
            row.UpdatedAt = now;
        }

        var pharmacyRequests = await _db.PharmacyRequests.Where(r => r.ClinicId == clinicId).ToListAsync();
        foreach (var row in pharmacyRequests.Where(r => matchesIdOrName(r.PatientId, null, r.PatientName)))
        {
            row.PatientId = patientNo;
            row.PatientName = newName;
            row.Phone = phone;
            row.Age = age;
            row.Gender = gender;
            row.City = city;
            row.DoctorName = doctorName;
            row.Specialty = specialty;
            row.UpdatedAt = now;
        }

        var pharmacyBills = await _db.PharmacyBills.Where(b => b.ClinicId == clinicId).ToListAsync();
        foreach (var row in pharmacyBills.Where(b => matchesIdOrName(b.PatientId, null, b.PatientName)))
        {
            row.PatientId = patientNo;
            row.PatientName = newName;
            row.DoctorName = doctorName;
            row.Specialty = specialty;
            row.UpdatedAt = now;
        }

        var receipts = await _db.CashReceipts.Where(r => r.ClinicId == clinicId).ToListAsync();
        foreach (var row in receipts.Where(r => matchesIdOrName(r.PatientId, null, r.PatientName)))
        {
            row.PatientId = patientNo;
            row.PatientName = newName;
            row.PatientSearch = newName;
            row.Phone = phone;
            row.Age = age;
            row.Gender = gender;
            row.City = city;
            row.DoctorName = doctorName;
            row.Specialty = specialty;
            row.UpdatedAt = now;
        }

        var prescriptions = await _db.Prescriptions.Where(p => p.ClinicId == clinicId).ToListAsync();
        foreach (var row in prescriptions.Where(p => !string.IsNullOrWhiteSpace(p.PatientName) && p.PatientName.Trim() == oldName))
        {
            row.PatientName = newName;
            row.Age = age;
            row.Gender = gender;
            row.DoctorName = doctorName;
            row.Specialty = specialty;
            row.UpdatedAt = now;
        }

        var appointments = await _db.Appointments
            .Where(a => a.ClinicId == clinicId && a.PatientId == current.Id)
            .ToListAsync();
        foreach (var row in appointments)
        {
            row.DoctorName = doctorName;
            row.Department = specialty;
        }

        await _db.SaveChangesAsync();
    }

    public async Task PropagateDoctorAsync(Guid clinicId, Doctor previous, Doctor current)
    {
        var oldName = previous.Name.Trim();
        var newName = current.Name.Trim();
        var specialty = current.Specialty;
        var now = DateTime.UtcNow;

        if (oldName == newName && previous.Specialty == current.Specialty && previous.ConsultationFee == current.ConsultationFee)
            return;

        bool matchesDoctor(string? name) =>
            !string.IsNullOrWhiteSpace(name) && name.Trim() == oldName;

        var patients = await _db.Patients.Where(p => p.ClinicId == clinicId).ToListAsync();
        foreach (var row in patients.Where(p => matchesDoctor(p.DoctorName)))
        {
            row.DoctorName = newName;
            row.Specialty = specialty;
            row.UpdatedAt = now;
        }

        foreach (var row in (await _db.Invoices.Where(i => i.ClinicId == clinicId).ToListAsync()).Where(i => matchesDoctor(i.DoctorName)))
        {
            row.DoctorName = newName;
            row.Specialty = specialty;
            row.UpdatedAt = now;
        }

        foreach (var row in (await _db.LabRequests.Where(r => r.ClinicId == clinicId).ToListAsync()).Where(r => matchesDoctor(r.DoctorName)))
        {
            row.DoctorName = newName;
            row.Specialty = specialty;
            row.UpdatedAt = now;
        }

        foreach (var row in (await _db.LabResults.Where(r => r.ClinicId == clinicId).ToListAsync()).Where(r => matchesDoctor(r.DoctorName)))
        {
            row.DoctorName = newName;
            row.Specialty = specialty;
            row.UpdatedAt = now;
        }

        foreach (var row in (await _db.RadiologyRequests.Where(r => r.ClinicId == clinicId).ToListAsync()).Where(r => matchesDoctor(r.DoctorName)))
        {
            row.DoctorName = newName;
            row.Specialty = specialty;
            row.UpdatedAt = now;
        }

        foreach (var row in (await _db.RadiologyResults.Where(r => r.ClinicId == clinicId).ToListAsync()).Where(r => matchesDoctor(r.DoctorName)))
        {
            row.DoctorName = newName;
            row.Specialty = specialty;
            row.UpdatedAt = now;
        }

        foreach (var row in (await _db.PharmacyRequests.Where(r => r.ClinicId == clinicId).ToListAsync()).Where(r => matchesDoctor(r.DoctorName)))
        {
            row.DoctorName = newName;
            row.Specialty = specialty;
            row.UpdatedAt = now;
        }

        foreach (var row in (await _db.PharmacyBills.Where(b => b.ClinicId == clinicId).ToListAsync()).Where(b => matchesDoctor(b.DoctorName)))
        {
            row.DoctorName = newName;
            row.Specialty = specialty;
            row.UpdatedAt = now;
        }

        foreach (var row in (await _db.CashReceipts.Where(r => r.ClinicId == clinicId).ToListAsync()).Where(r => matchesDoctor(r.DoctorName)))
        {
            row.DoctorName = newName;
            row.Specialty = specialty;
            row.UpdatedAt = now;
        }

        foreach (var row in (await _db.Prescriptions.Where(p => p.ClinicId == clinicId).ToListAsync()).Where(p => matchesDoctor(p.DoctorName)))
        {
            row.DoctorName = newName;
            row.Specialty = specialty;
            row.UpdatedAt = now;
        }

        foreach (var row in (await _db.Appointments.Where(a => a.ClinicId == clinicId).ToListAsync()).Where(a => matchesDoctor(a.DoctorName)))
            row.DoctorName = newName;

        if (previous.ConsultationFee != current.ConsultationFee)
            await PropagateConsultationFeeAsync(clinicId, current);

        await _db.SaveChangesAsync();
    }

    private async Task PropagateConsultationFeeAsync(Guid clinicId, Doctor doctor)
    {
        var invoices = await _db.Invoices
            .Include(i => i.Lines)
            .Where(i => i.ClinicId == clinicId && i.DoctorName == doctor.Name)
            .ToListAsync();

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
    }

    private static bool IsConsultationLine(string? serviceName, string doctorName) =>
        !string.IsNullOrWhiteSpace(serviceName) &&
        (serviceName.Contains("Consultation", StringComparison.OrdinalIgnoreCase) ||
         serviceName.Contains(doctorName, StringComparison.OrdinalIgnoreCase));

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
            .Where(l => l.PharmacyRequest.ClinicId == clinicId)
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
            .Where(l => l.PharmacyBill.ClinicId == clinicId)
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

        await RecalcPharmacyRequestTotalsAsync(clinicId, requestIds);
        await RecalcPharmacyBillTotalsAsync(clinicId, billIds);
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

        await RecalcInvoiceTotalsAsync(clinicId, invoiceIds);
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
    }
}
