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

    public MasterDataPropagationService(ClinicalDbContext db, BillingPropagationService billing)
    {
        _db = db;
        _billing = billing;
    }

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

        await _db.Invoices
            .Where(i => i.ClinicId == clinicId && (i.PatientId == patientNo || i.PatientName == oldName))
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.PatientId, patientNo)
                .SetProperty(i => i.PatientName, newName)
                .SetProperty(i => i.Phone, phone)
                .SetProperty(i => i.Age, age)
                .SetProperty(i => i.Gender, gender)
                .SetProperty(i => i.City, city)
                .SetProperty(i => i.DoctorName, doctorName)
                .SetProperty(i => i.Specialty, specialty)
                .SetProperty(i => i.UpdatedAt, now));

        await _db.LabRequests
            .Where(r => r.ClinicId == clinicId && (r.PatientBarcode == patientNo || r.PatientName == oldName))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.PatientBarcode, patientNo)
                .SetProperty(r => r.PatientName, newName)
                .SetProperty(r => r.Phone, phone)
                .SetProperty(r => r.Age, age)
                .SetProperty(r => r.Gender, gender)
                .SetProperty(r => r.City, city)
                .SetProperty(r => r.DoctorName, doctorName)
                .SetProperty(r => r.Specialty, specialty)
                .SetProperty(r => r.UpdatedAt, now));

        await _db.LabResults
            .Where(r => r.ClinicId == clinicId && r.PatientName == oldName)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.PatientName, newName)
                .SetProperty(r => r.DoctorName, doctorName)
                .SetProperty(r => r.Specialty, specialty)
                .SetProperty(r => r.UpdatedAt, now));

        await _db.RadiologyRequests
            .Where(r => r.ClinicId == clinicId && (r.PatientBarcode == patientNo || r.PatientName == oldName))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.PatientBarcode, patientNo)
                .SetProperty(r => r.PatientName, newName)
                .SetProperty(r => r.Phone, phone)
                .SetProperty(r => r.Age, age)
                .SetProperty(r => r.Gender, gender)
                .SetProperty(r => r.City, city)
                .SetProperty(r => r.DoctorName, doctorName)
                .SetProperty(r => r.Specialty, specialty)
                .SetProperty(r => r.UpdatedAt, now));

        await _db.RadiologyResults
            .Where(r => r.ClinicId == clinicId && r.PatientName == oldName)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.PatientName, newName)
                .SetProperty(r => r.DoctorName, doctorName)
                .SetProperty(r => r.Specialty, specialty)
                .SetProperty(r => r.UpdatedAt, now));

        await _db.PharmacyRequests
            .Where(r => r.ClinicId == clinicId && (r.PatientId == patientNo || r.PatientName == oldName))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.PatientId, patientNo)
                .SetProperty(r => r.PatientName, newName)
                .SetProperty(r => r.Phone, phone)
                .SetProperty(r => r.Age, age)
                .SetProperty(r => r.Gender, gender)
                .SetProperty(r => r.City, city)
                .SetProperty(r => r.DoctorName, doctorName)
                .SetProperty(r => r.Specialty, specialty)
                .SetProperty(r => r.UpdatedAt, now));

        await _db.PharmacyBills
            .Where(b => b.ClinicId == clinicId && (b.PatientId == patientNo || b.PatientName == oldName))
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.PatientId, patientNo)
                .SetProperty(b => b.PatientName, newName)
                .SetProperty(b => b.DoctorName, doctorName)
                .SetProperty(b => b.Specialty, specialty)
                .SetProperty(b => b.UpdatedAt, now));

        await _db.CashReceipts
            .Where(r => r.ClinicId == clinicId && (r.PatientId == patientNo || r.PatientName == oldName))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.PatientId, patientNo)
                .SetProperty(r => r.PatientName, newName)
                .SetProperty(r => r.PatientSearch, newName)
                .SetProperty(r => r.Phone, phone)
                .SetProperty(r => r.Age, age)
                .SetProperty(r => r.Gender, gender)
                .SetProperty(r => r.City, city)
                .SetProperty(r => r.DoctorName, doctorName)
                .SetProperty(r => r.Specialty, specialty)
                .SetProperty(r => r.UpdatedAt, now));

        await _db.CashPayments
            .Where(p => p.ClinicId == clinicId && (p.PatientId == patientNo || p.PayeeName == oldName))
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.PatientId, patientNo)
                .SetProperty(p => p.PayeeName, newName)
                .SetProperty(p => p.DoctorName, doctorName)
                .SetProperty(p => p.UpdatedAt, now));

        await _db.Prescriptions
            .Where(p => p.ClinicId == clinicId && p.PatientName == oldName)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.PatientName, newName)
                .SetProperty(p => p.Age, age)
                .SetProperty(p => p.Gender, gender)
                .SetProperty(p => p.DoctorName, doctorName)
                .SetProperty(p => p.Specialty, specialty)
                .SetProperty(p => p.UpdatedAt, now));

        await _db.Appointments
            .Where(a => a.ClinicId == clinicId && a.PatientId == current.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.DoctorName, doctorName)
                .SetProperty(a => a.Department, specialty));
    }

    public async Task PropagateDoctorAsync(Guid clinicId, Doctor previous, Doctor current)
    {
        var oldName = previous.Name.Trim();
        var newName = current.Name.Trim();
        var specialty = current.Specialty;
        var now = DateTime.UtcNow;

        if (oldName == newName && previous.Specialty == current.Specialty && previous.ConsultationFee == current.ConsultationFee)
            return;

        await _db.Patients
            .Where(p => p.ClinicId == clinicId && p.DoctorName == oldName)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.DoctorName, newName)
                .SetProperty(p => p.Specialty, specialty)
                .SetProperty(p => p.UpdatedAt, now));

        await _db.Invoices
            .Where(i => i.ClinicId == clinicId && i.DoctorName == oldName)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.DoctorName, newName)
                .SetProperty(i => i.Specialty, specialty)
                .SetProperty(i => i.UpdatedAt, now));

        await _db.LabRequests
            .Where(r => r.ClinicId == clinicId && r.DoctorName == oldName)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.DoctorName, newName)
                .SetProperty(r => r.Specialty, specialty)
                .SetProperty(r => r.UpdatedAt, now));

        await _db.LabResults
            .Where(r => r.ClinicId == clinicId && r.DoctorName == oldName)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.DoctorName, newName)
                .SetProperty(r => r.Specialty, specialty)
                .SetProperty(r => r.UpdatedAt, now));

        await _db.RadiologyRequests
            .Where(r => r.ClinicId == clinicId && r.DoctorName == oldName)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.DoctorName, newName)
                .SetProperty(r => r.Specialty, specialty)
                .SetProperty(r => r.UpdatedAt, now));

        await _db.RadiologyResults
            .Where(r => r.ClinicId == clinicId && r.DoctorName == oldName)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.DoctorName, newName)
                .SetProperty(r => r.Specialty, specialty)
                .SetProperty(r => r.UpdatedAt, now));

        await _db.PharmacyRequests
            .Where(r => r.ClinicId == clinicId && r.DoctorName == oldName)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.DoctorName, newName)
                .SetProperty(r => r.Specialty, specialty)
                .SetProperty(r => r.UpdatedAt, now));

        await _db.PharmacyBills
            .Where(b => b.ClinicId == clinicId && b.DoctorName == oldName)
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.DoctorName, newName)
                .SetProperty(b => b.Specialty, specialty)
                .SetProperty(b => b.UpdatedAt, now));

        await _db.CashReceipts
            .Where(r => r.ClinicId == clinicId && r.DoctorName == oldName)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.DoctorName, newName)
                .SetProperty(r => r.Specialty, specialty)
                .SetProperty(r => r.UpdatedAt, now));

        await _db.CashPayments
            .Where(p => p.ClinicId == clinicId && p.DoctorName == oldName)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.DoctorName, newName)
                .SetProperty(p => p.UpdatedAt, now));

        await _db.Prescriptions
            .Where(p => p.ClinicId == clinicId && p.DoctorName == oldName)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.DoctorName, newName)
                .SetProperty(p => p.Specialty, specialty)
                .SetProperty(p => p.UpdatedAt, now));

        await _db.Appointments
            .Where(a => a.ClinicId == clinicId && a.DoctorName == oldName)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.DoctorName, newName));

        if (previous.ConsultationFee != current.ConsultationFee)
            await PropagateConsultationFeeAsync(clinicId, current);
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
