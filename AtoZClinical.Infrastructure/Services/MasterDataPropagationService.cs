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

        var labRequestNos = await _db.LabRequests
            .ForClinic(clinicId)
            .Where(r => r.PatientBarcode == patientNo || r.PatientName == oldName || r.PatientName == newName)
            .Select(r => r.RequestNo)
            .ToListAsync();
        if (labRequestNos.Count > 0)
        {
            await _db.LabResults
                .ForClinic(clinicId)
                .Where(r => r.RequestNo != null && labRequestNos.Contains(r.RequestNo.Value))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.PatientName, newName)
                    .SetProperty(r => r.DoctorName, doctorName)
                    .SetProperty(r => r.Specialty, specialty)
                    .SetProperty(r => r.UpdatedAt, now));
        }

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

        var radiologyRequestNos = await _db.RadiologyRequests
            .ForClinic(clinicId)
            .Where(r => r.PatientBarcode == patientNo || r.PatientName == oldName || r.PatientName == newName)
            .Select(r => r.RequestNo)
            .ToListAsync();
        if (radiologyRequestNos.Count > 0)
        {
            await _db.RadiologyResults
                .ForClinic(clinicId)
                .Where(r => r.RequestNo != null && radiologyRequestNos.Contains(r.RequestNo.Value))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.PatientName, newName)
                    .SetProperty(r => r.DoctorName, doctorName)
                    .SetProperty(r => r.Specialty, specialty)
                    .SetProperty(r => r.UpdatedAt, now));
        }

        await _db.ServiceIncomeRequests
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

        await _db.ExpenseVouchers
            .Where(e => e.ClinicId == clinicId && e.PayeeName == oldName)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.PayeeName, newName)
                .SetProperty(e => e.UpdatedAt, now));

        await PropagateJournalPatientDoctorNamesAsync(clinicId, oldName, newName, null, null);
    }

    public async Task PropagateDoctorAsync(Guid clinicId, Doctor previous, Doctor current)
    {
        var oldName = previous.Name.Trim();
        var newName = current.Name.Trim();
        var oldNorm = oldName.ToLowerInvariant();
        var specialty = current.Specialty;
        var now = DateTime.UtcNow;

        if (oldName == newName && previous.Specialty == current.Specialty && previous.ConsultationFee == current.ConsultationFee)
            return;

        await _db.Patients
            .Where(p => p.ClinicId == clinicId && p.DoctorName != null && p.DoctorName.Trim().ToLower() == oldNorm)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.DoctorName, newName)
                .SetProperty(p => p.Specialty, specialty)
                .SetProperty(p => p.UpdatedAt, now));

        await _db.Invoices
            .Where(i => i.ClinicId == clinicId && i.DoctorName != null && i.DoctorName.Trim().ToLower() == oldNorm)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.DoctorName, newName)
                .SetProperty(i => i.Specialty, specialty)
                .SetProperty(i => i.UpdatedAt, now));

        await _db.LabRequests
            .Where(r => r.ClinicId == clinicId && r.DoctorName != null && r.DoctorName.Trim().ToLower() == oldNorm)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.DoctorName, newName)
                .SetProperty(r => r.Specialty, specialty)
                .SetProperty(r => r.UpdatedAt, now));

        await _db.LabResults
            .Where(r => r.ClinicId == clinicId && r.DoctorName != null && r.DoctorName.Trim().ToLower() == oldNorm)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.DoctorName, newName)
                .SetProperty(r => r.Specialty, specialty)
                .SetProperty(r => r.UpdatedAt, now));

        await _db.RadiologyRequests
            .Where(r => r.ClinicId == clinicId && r.DoctorName != null && r.DoctorName.Trim().ToLower() == oldNorm)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.DoctorName, newName)
                .SetProperty(r => r.Specialty, specialty)
                .SetProperty(r => r.UpdatedAt, now));

        await _db.RadiologyResults
            .Where(r => r.ClinicId == clinicId && r.DoctorName != null && r.DoctorName.Trim().ToLower() == oldNorm)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.DoctorName, newName)
                .SetProperty(r => r.Specialty, specialty)
                .SetProperty(r => r.UpdatedAt, now));

        await _db.PharmacyRequests
            .Where(r => r.ClinicId == clinicId && r.DoctorName != null && r.DoctorName.Trim().ToLower() == oldNorm)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.DoctorName, newName)
                .SetProperty(r => r.Specialty, specialty)
                .SetProperty(r => r.UpdatedAt, now));

        await _db.PharmacyBills
            .Where(b => b.ClinicId == clinicId && b.DoctorName != null && b.DoctorName.Trim().ToLower() == oldNorm)
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.DoctorName, newName)
                .SetProperty(b => b.Specialty, specialty)
                .SetProperty(b => b.UpdatedAt, now));

        await _db.CashReceipts
            .Where(r => r.ClinicId == clinicId && r.DoctorName != null && r.DoctorName.Trim().ToLower() == oldNorm)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.DoctorName, newName)
                .SetProperty(r => r.Specialty, specialty)
                .SetProperty(r => r.UpdatedAt, now));

        await _db.CashPayments
            .Where(p => p.ClinicId == clinicId && p.DoctorName != null && p.DoctorName.Trim().ToLower() == oldNorm)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.DoctorName, newName)
                .SetProperty(p => p.UpdatedAt, now));

        await _db.Prescriptions
            .Where(p => p.ClinicId == clinicId && p.DoctorName != null && p.DoctorName.Trim().ToLower() == oldNorm)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.DoctorName, newName)
                .SetProperty(p => p.Specialty, specialty)
                .SetProperty(p => p.UpdatedAt, now));

        await _db.ServiceIncomeRequests
            .Where(r => r.ClinicId == clinicId && r.DoctorName != null && r.DoctorName.Trim().ToLower() == oldNorm)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.DoctorName, newName)
                .SetProperty(r => r.Specialty, specialty)
                .SetProperty(r => r.UpdatedAt, now));

        await _db.Appointments
            .Where(a => a.ClinicId == clinicId && a.DoctorName != null && a.DoctorName.Trim().ToLower() == oldNorm)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.DoctorName, newName));

        if (previous.ConsultationFee != current.ConsultationFee)
            await PropagateConsultationFeeAsync(clinicId, current);

        await PropagateJournalPatientDoctorNamesAsync(clinicId, null, null, oldName, newName);
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

        await _db.SaveChangesAsync();
        foreach (var invoice in invoices)
        {
            try { await _journalSync.SyncInvoiceAsync(clinicId, invoice, invoice.Lines.ToList()); }
            catch { }
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
