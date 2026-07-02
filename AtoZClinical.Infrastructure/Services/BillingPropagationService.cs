using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

/// <summary>
/// Keeps patient/doctor fields and billed line amounts in sync across clinical transactions
/// and invoice billing when records are edited after creation.
/// </summary>
public sealed class BillingPropagationService
{
    private readonly ClinicalDbContext _db;
    private readonly PatientInvoiceService _invoices;
    private readonly ClinicalJournalSyncService _journalSync;

    public BillingPropagationService(
        ClinicalDbContext db,
        PatientInvoiceService invoices,
        ClinicalJournalSyncService journalSync)
    {
        _db = db;
        _invoices = invoices;
        _journalSync = journalSync;
    }

    public sealed record PatientDoctorContext(
        string? PatientId,
        string? PatientName,
        string? DoctorName,
        int? Age = null,
        string? Phone = null,
        string? Gender = null,
        string? City = null,
        string? Specialty = null);

    public Task PropagatePatientDoctorAsync(Guid clinicId, PatientDoctorContext before, PatientDoctorContext after) =>
        PropagatePatientDoctorInternalAsync(clinicId, before, after, recalcPayments: true);

    public async Task SyncLabRequestAsync(
        Guid clinicId,
        LabRequest current,
        IReadOnlyList<LabRequestLine> lines,
        LabRequest? previous,
        IReadOnlyList<LabRequestLine>? previousLines)
    {
        if (previous is not null)
            await PropagatePatientDoctorInternalAsync(
                clinicId,
                ToContext(previous),
                ToContext(current),
                recalcPayments: false);

        await SyncPrefixLinesToInvoicesAsync(
            clinicId,
            $"Lab #{current.RequestNo}:",
            current.PatientBarcode,
            current.PatientName,
            lines.Select(l => new BillingLine(l.TestName ?? l.TestCode ?? "Test", l.Qty, l.Fee)).ToList(),
            previousLines?.Select(l => l.TestName ?? l.TestCode ?? "Test").ToList());

        if (current.RequestNo > 0)
        {
            await _db.LabResults
                .ForClinic(clinicId)
                .Where(r => r.RequestNo == current.RequestNo)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.PatientName, current.PatientName)
                    .SetProperty(r => r.DoctorName, current.DoctorName)
                    .SetProperty(r => r.Specialty, current.Specialty)
                    .SetProperty(r => r.UpdatedAt, DateTime.UtcNow));
        }
    }

    public async Task SyncLabResultAsync(Guid clinicId, LabResult current, LabResult? previous)
    {
        if (previous is not null)
            await PropagatePatientDoctorInternalAsync(
                clinicId,
                ToContext(previous),
                ToContext(current),
                recalcPayments: false);
    }

    public async Task SyncRadiologyRequestAsync(
        Guid clinicId,
        RadiologyRequest current,
        IReadOnlyList<RadiologyRequestLine> lines,
        RadiologyRequest? previous,
        IReadOnlyList<RadiologyRequestLine>? previousLines)
    {
        if (previous is not null)
            await PropagatePatientDoctorInternalAsync(
                clinicId,
                ToContext(previous),
                ToContext(current),
                recalcPayments: false);

        await SyncPrefixLinesToInvoicesAsync(
            clinicId,
            $"Radiology #{current.RequestNo}:",
            current.PatientBarcode,
            current.PatientName,
            lines.Select(l => new BillingLine(l.TestName ?? l.TestCode ?? "Test", l.Qty, l.Fee)).ToList(),
            previousLines?.Select(l => l.TestName ?? l.TestCode ?? "Test").ToList());

        if (current.RequestNo > 0)
        {
            await _db.RadiologyResults
                .ForClinic(clinicId)
                .Where(r => r.RequestNo == current.RequestNo)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.PatientName, current.PatientName)
                    .SetProperty(r => r.DoctorName, current.DoctorName)
                    .SetProperty(r => r.Specialty, current.Specialty)
                    .SetProperty(r => r.UpdatedAt, DateTime.UtcNow));
        }
    }

    public Task SyncRadiologyResultAsync(Guid clinicId, RadiologyResult current, RadiologyResult? previous)
    {
        if (previous is not null)
            return PropagatePatientDoctorInternalAsync(
                clinicId,
                ToContext(previous),
                ToContext(current),
                recalcPayments: false);
        return Task.CompletedTask;
    }

    public async Task SyncPharmacyRequestAsync(
        Guid clinicId,
        PharmacyRequest current,
        IReadOnlyList<PharmacyRequestLine> lines,
        PharmacyRequest? previous,
        IReadOnlyList<PharmacyRequestLine>? previousLines)
    {
        if (previous is not null)
            await PropagatePatientDoctorInternalAsync(
                clinicId,
                ToContext(previous),
                ToContext(current),
                recalcPayments: false);

        var billingLines = lines.Select(l => new BillingLine(l.MedicineName ?? l.MedicineCode ?? "Medicine", l.Qty, l.UnitPrice)).ToList();
        var previousNames = previousLines?.Select(l => l.MedicineName ?? l.MedicineCode ?? "Medicine").ToList();
        await SyncPrefixLinesToInvoicesAsync(
            clinicId,
            $"Pharmacy Req #{current.RequestNo}:",
            current.PatientId,
            current.PatientName,
            billingLines,
            previousNames);
    }

    public async Task SyncPharmacyBillAsync(
        Guid clinicId,
        PharmacyBill current,
        IReadOnlyList<PharmacyBillLine> lines,
        PharmacyBill? previous,
        IReadOnlyList<PharmacyBillLine>? previousLines)
    {
        if (previous is not null)
            await PropagatePatientDoctorInternalAsync(
                clinicId,
                ToContext(previous),
                ToContext(current),
                recalcPayments: false);

        var billingLines = lines.Select(l => new BillingLine(l.MedicineName ?? l.MedicineCode ?? "Medicine", l.Qty, l.UnitPrice)).ToList();
        var previousNames = previousLines?.Select(l => l.MedicineName ?? l.MedicineCode ?? "Medicine").ToList();
        await SyncPrefixLinesToInvoicesAsync(
            clinicId,
            $"Pharmacy Bill #{current.BillNo}:",
            current.PatientId,
            current.PatientName,
            billingLines,
            previousNames);

        if (current.RequestNo is > 0)
        {
            await SyncPrefixLinesToInvoicesAsync(
                clinicId,
                $"Pharmacy Req #{current.RequestNo}:",
                current.PatientId,
                current.PatientName,
                [],
                null);
        }
    }

    public async Task SyncCashReceiptAsync(Guid clinicId, CashReceipt current, CashReceipt? previous)
    {
        if (previous is not null)
            await PropagatePatientDoctorInternalAsync(
                clinicId,
                ToContext(previous),
                ToContext(current),
                recalcPayments: false);

        await _invoices.RecalculateInvoicePaymentsAsync(
            clinicId, current.PatientName, current.PatientId, current.DoctorName);
    }

    public async Task SyncCashPaymentAsync(Guid clinicId, CashPayment current, CashPayment? previous)
    {
        if (previous is not null)
            await PropagatePatientDoctorInternalAsync(
                clinicId,
                ToContext(previous),
                ToContext(current),
                recalcPayments: false);

        await _invoices.RecalculateInvoicePaymentsAsync(
            clinicId, current.PayeeName, current.PatientId, current.DoctorName);
    }

    public Task SyncPrescriptionAsync(Guid clinicId, Prescription current, Prescription? previous)
    {
        if (previous is null) return Task.CompletedTask;
        return PropagatePatientDoctorInternalAsync(
            clinicId,
            ToContext(previous),
            ToContext(current),
            recalcPayments: false);
    }

    public async Task SyncServiceIncomeRequestAsync(
        Guid clinicId,
        ServiceIncomeRequest current,
        IReadOnlyList<ServiceIncomeRequestLine> lines,
        ServiceIncomeRequest? previous,
        IReadOnlyList<ServiceIncomeRequestLine>? previousLines)
    {
        if (previous is not null)
            await PropagatePatientDoctorInternalAsync(
                clinicId,
                ToContext(previous),
                ToContext(current),
                recalcPayments: false);

        await SyncPrefixLinesToInvoicesAsync(
            clinicId,
            $"Service #{current.RequestNo}:",
            current.PatientBarcode,
            current.PatientName,
            lines.Select(l => new BillingLine(l.ServiceName ?? "Service", l.Qty, l.Fee)).ToList(),
            previousLines?.Select(l => l.ServiceName ?? "Service").ToList());
    }

    private async Task PropagatePatientDoctorInternalAsync(
        Guid clinicId,
        PatientDoctorContext before,
        PatientDoctorContext after,
        bool recalcPayments)
    {
        if (ContextsEqual(before, after)) return;

        await ClinicSaveHelper.ExecuteInTransactionAsync(_db, async () =>
        {
            await PropagatePatientDoctorCoreAsync(clinicId, before, after, recalcPayments);
        });
    }

    private async Task PropagatePatientDoctorCoreAsync(
        Guid clinicId,
        PatientDoctorContext before,
        PatientDoctorContext after,
        bool recalcPayments)
    {
        var oldId = before.PatientId?.Trim();
        var oldName = before.PatientName?.Trim();
        var oldDoctor = before.DoctorName?.Trim();
        if (string.IsNullOrWhiteSpace(oldId) && string.IsNullOrWhiteSpace(oldName)) return;

        var newId = after.PatientId?.Trim();
        var newName = after.PatientName?.Trim();
        var newDoctor = after.DoctorName?.Trim();
        var now = DateTime.UtcNow;

        await _db.Invoices
            .ForClinic(clinicId)
            .Where(i => MatchesPatientDoctor(i.PatientId, i.PatientName, i.DoctorName, oldId, oldName, oldDoctor))
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.PatientId, newId)
                .SetProperty(i => i.PatientName, newName)
                .SetProperty(i => i.Phone, after.Phone)
                .SetProperty(i => i.Age, after.Age)
                .SetProperty(i => i.Gender, after.Gender)
                .SetProperty(i => i.City, after.City)
                .SetProperty(i => i.DoctorName, newDoctor)
                .SetProperty(i => i.Specialty, after.Specialty)
                .SetProperty(i => i.UpdatedAt, now));

        await _db.LabRequests
            .ForClinic(clinicId)
            .Where(r => MatchesPatientDoctor(r.PatientBarcode, r.PatientName, r.DoctorName, oldId, oldName, oldDoctor))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.PatientBarcode, newId)
                .SetProperty(r => r.PatientName, newName)
                .SetProperty(r => r.Phone, after.Phone)
                .SetProperty(r => r.Age, after.Age)
                .SetProperty(r => r.Gender, after.Gender)
                .SetProperty(r => r.City, after.City)
                .SetProperty(r => r.DoctorName, newDoctor)
                .SetProperty(r => r.Specialty, after.Specialty)
                .SetProperty(r => r.UpdatedAt, now));

        await _db.ServiceIncomeRequests
            .ForClinic(clinicId)
            .Where(r => MatchesPatientDoctor(r.PatientBarcode, r.PatientName, r.DoctorName, oldId, oldName, oldDoctor))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.PatientBarcode, newId)
                .SetProperty(r => r.PatientName, newName)
                .SetProperty(r => r.Phone, after.Phone)
                .SetProperty(r => r.Age, after.Age)
                .SetProperty(r => r.Gender, after.Gender)
                .SetProperty(r => r.City, after.City)
                .SetProperty(r => r.DoctorName, newDoctor)
                .SetProperty(r => r.Specialty, after.Specialty)
                .SetProperty(r => r.UpdatedAt, now));

        await _db.LabResults
            .ForClinic(clinicId)
            .Where(r => MatchesPatientDoctor(null, r.PatientName, r.DoctorName, oldId, oldName, oldDoctor))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.PatientName, newName)
                .SetProperty(r => r.DoctorName, newDoctor)
                .SetProperty(r => r.Specialty, after.Specialty)
                .SetProperty(r => r.UpdatedAt, now));

        await _db.RadiologyRequests
            .ForClinic(clinicId)
            .Where(r => MatchesPatientDoctor(r.PatientBarcode, r.PatientName, r.DoctorName, oldId, oldName, oldDoctor))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.PatientBarcode, newId)
                .SetProperty(r => r.PatientName, newName)
                .SetProperty(r => r.Phone, after.Phone)
                .SetProperty(r => r.Age, after.Age)
                .SetProperty(r => r.Gender, after.Gender)
                .SetProperty(r => r.City, after.City)
                .SetProperty(r => r.DoctorName, newDoctor)
                .SetProperty(r => r.Specialty, after.Specialty)
                .SetProperty(r => r.UpdatedAt, now));

        await _db.RadiologyResults
            .ForClinic(clinicId)
            .Where(r => MatchesPatientDoctor(null, r.PatientName, r.DoctorName, oldId, oldName, oldDoctor))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.PatientName, newName)
                .SetProperty(r => r.DoctorName, newDoctor)
                .SetProperty(r => r.Specialty, after.Specialty)
                .SetProperty(r => r.UpdatedAt, now));

        await _db.PharmacyRequests
            .ForClinic(clinicId)
            .Where(r => MatchesPatientDoctor(r.PatientId, r.PatientName, r.DoctorName, oldId, oldName, oldDoctor))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.PatientId, newId)
                .SetProperty(r => r.PatientName, newName)
                .SetProperty(r => r.Phone, after.Phone)
                .SetProperty(r => r.Age, after.Age)
                .SetProperty(r => r.Gender, after.Gender)
                .SetProperty(r => r.City, after.City)
                .SetProperty(r => r.DoctorName, newDoctor)
                .SetProperty(r => r.Specialty, after.Specialty)
                .SetProperty(r => r.UpdatedAt, now));

        await _db.PharmacyBills
            .ForClinic(clinicId)
            .Where(b => MatchesPatientDoctor(b.PatientId, b.PatientName, b.DoctorName, oldId, oldName, oldDoctor))
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.PatientId, newId)
                .SetProperty(b => b.PatientName, newName)
                .SetProperty(b => b.DoctorName, newDoctor)
                .SetProperty(b => b.Specialty, after.Specialty)
                .SetProperty(b => b.UpdatedAt, now));

        await _db.CashReceipts
            .ForClinic(clinicId)
            .Where(r => MatchesPatientDoctor(r.PatientId, r.PatientName, r.DoctorName, oldId, oldName, oldDoctor))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.PatientId, newId)
                .SetProperty(r => r.PatientName, newName)
                .SetProperty(r => r.PatientSearch, newName)
                .SetProperty(r => r.Phone, after.Phone)
                .SetProperty(r => r.Age, after.Age)
                .SetProperty(r => r.Gender, after.Gender)
                .SetProperty(r => r.City, after.City)
                .SetProperty(r => r.DoctorName, newDoctor)
                .SetProperty(r => r.Specialty, after.Specialty)
                .SetProperty(r => r.UpdatedAt, now));

        await _db.CashPayments
            .ForClinic(clinicId)
            .Where(p => MatchesPatientDoctor(p.PatientId, p.PayeeName, p.DoctorName, oldId, oldName, oldDoctor))
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.PatientId, newId)
                .SetProperty(p => p.PayeeName, newName)
                .SetProperty(p => p.DoctorName, newDoctor)
                .SetProperty(p => p.UpdatedAt, now));

        await _db.Prescriptions
            .ForClinic(clinicId)
            .Where(p => MatchesPatientDoctor(null, p.PatientName, p.DoctorName, oldId, oldName, oldDoctor))
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.PatientName, newName)
                .SetProperty(p => p.Age, after.Age)
                .SetProperty(p => p.Gender, after.Gender)
                .SetProperty(p => p.DoctorName, newDoctor)
                .SetProperty(p => p.Specialty, after.Specialty)
                .SetProperty(p => p.UpdatedAt, now));

        if (recalcPayments)
            await _invoices.RecalculateInvoicePaymentsAsync(clinicId, newName, newId, newDoctor);
    }

    private async Task SyncPrefixLinesToInvoicesAsync(
        Guid clinicId,
        string prefix,
        string? patientId,
        string? patientName,
        IReadOnlyList<BillingLine> lines,
        IReadOnlyList<string>? previousItemNames)
    {
        await ClinicSaveHelper.ExecuteInTransactionAsync(_db, async () =>
        {
            await SyncPrefixLinesToInvoicesCoreAsync(clinicId, prefix, patientId, patientName, lines, previousItemNames);
        });
    }

    private async Task SyncPrefixLinesToInvoicesCoreAsync(
        Guid clinicId,
        string prefix,
        string? patientId,
        string? patientName,
        IReadOnlyList<BillingLine> lines,
        IReadOnlyList<string>? previousItemNames)
    {
        var invoiceQuery = _db.Invoices
            .Include(i => i.Lines)
            .ForClinic(clinicId)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(patientId))
            invoiceQuery = invoiceQuery.Where(i => i.PatientId == patientId.Trim());
        if (!string.IsNullOrWhiteSpace(patientName))
        {
            var name = patientName.Trim();
            invoiceQuery = invoiceQuery.Where(i => i.PatientName != null && i.PatientName.Contains(name));
        }

        var invoices = await invoiceQuery.ToListAsync();

        if (invoices.Count == 0) return;

        var changedInvoices = new List<Invoice>();
        foreach (var invoice in invoices)
        {
            var existing = invoice.Lines
                .Where(l => l.ServiceName != null &&
                            l.ServiceName.Contains(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(l => l.LineNo)
                .ToList();

            if (lines.Count == 0)
            {
                if (existing.Count == 0) continue;
                foreach (var orphan in existing)
                {
                    invoice.Lines.Remove(orphan);
                    _db.InvoiceLines.Remove(orphan);
                }
            }
            else
            {
                for (var i = 0; i < lines.Count; i++)
                {
                    var current = lines[i];
                    var serviceName = $"{prefix} {current.ItemName}";
                    InvoiceLine? target = null;

                    if (previousItemNames is not null && i < previousItemNames.Count)
                    {
                        var oldItem = previousItemNames[i];
                        target = existing.FirstOrDefault(l =>
                            l.ServiceName != null &&
                            l.ServiceName.Contains($"{prefix} {oldItem}", StringComparison.OrdinalIgnoreCase));
                    }

                    target ??= i < existing.Count ? existing[i] : null;

                    if (target is not null)
                    {
                        target.ServiceName = serviceName;
                        target.Qty = current.Qty;
                        target.UnitFee = current.UnitFee;
                        target.LineTotal = current.Qty * current.UnitFee;
                    }
                    else
                    {
                        var nextLineNo = invoice.Lines.Count == 0 ? 1 : invoice.Lines.Max(l => l.LineNo) + 1;
                        var added = new InvoiceLine
                        {
                            Id = Guid.NewGuid(),
                            InvoiceId = invoice.Id,
                            LineNo = nextLineNo,
                            ServiceName = serviceName,
                            Qty = current.Qty,
                            UnitFee = current.UnitFee,
                            LineTotal = current.Qty * current.UnitFee
                        };
                        _db.InvoiceLines.Add(added);
                        invoice.Lines.Add(added);
                    }
                }

                for (var i = lines.Count; i < existing.Count; i++)
                {
                    var orphan = existing[i];
                    invoice.Lines.Remove(orphan);
                    _db.InvoiceLines.Remove(orphan);
                }
            }

            invoice.SubTotal = invoice.Lines.Sum(l => l.LineTotal);
            invoice.TotalAmount = invoice.SubTotal - invoice.Discount + invoice.TaxAmount;
            invoice.BalanceDue = invoice.TotalAmount - invoice.AmountPaid;
            invoice.UpdatedAt = DateTime.UtcNow;
            changedInvoices.Add(invoice);
        }

        if (changedInvoices.Count == 0) return;

        await _db.SaveChangesAsync();

        foreach (var invoice in changedInvoices)
        {
            try
            {
                await _journalSync.SyncInvoiceAsync(clinicId, invoice, invoice.Lines.ToList());
            }
            catch { }

            try
            {
                await _invoices.RecalculateInvoicePaymentsAsync(
                    clinicId, invoice.PatientName, invoice.PatientId, invoice.DoctorName);
            }
            catch { }
        }
    }

    private static PatientDoctorContext ToContext(LabRequest r) =>
        new(r.PatientBarcode, r.PatientName, r.DoctorName, r.Age, r.Phone, r.Gender, r.City, r.Specialty);

    private static PatientDoctorContext ToContext(ServiceIncomeRequest r) =>
        new(r.PatientBarcode, r.PatientName, r.DoctorName, r.Age, r.Phone, r.Gender, r.City, r.Specialty);

    private static PatientDoctorContext ToContext(LabResult r) =>
        new(null, r.PatientName, r.DoctorName, null, null, null, null, r.Specialty);

    private static PatientDoctorContext ToContext(RadiologyRequest r) =>
        new(r.PatientBarcode, r.PatientName, r.DoctorName, r.Age, r.Phone, r.Gender, r.City, r.Specialty);

    private static PatientDoctorContext ToContext(RadiologyResult r) =>
        new(null, r.PatientName, r.DoctorName, null, null, null, null, r.Specialty);

    private static PatientDoctorContext ToContext(PharmacyRequest r) =>
        new(r.PatientId, r.PatientName, r.DoctorName, r.Age, r.Phone, r.Gender, r.City, r.Specialty);

    private static PatientDoctorContext ToContext(PharmacyBill b) =>
        new(b.PatientId, b.PatientName, b.DoctorName, null, null, null, null, b.Specialty);

    private static PatientDoctorContext ToContext(CashReceipt r) =>
        new(r.PatientId, r.PatientName, r.DoctorName, r.Age, r.Phone, r.Gender, r.City, r.Specialty);

    private static PatientDoctorContext ToContext(CashPayment p) =>
        new(p.PatientId, p.PayeeName, p.DoctorName);

    private static PatientDoctorContext ToContext(Prescription p) =>
        new(null, p.PatientName, p.DoctorName, p.Age, p.Gender, null, null, p.Specialty);

    private static bool ContextsEqual(PatientDoctorContext a, PatientDoctorContext b) =>
        string.Equals(a.PatientId?.Trim(), b.PatientId?.Trim(), StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.PatientName?.Trim(), b.PatientName?.Trim(), StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.DoctorName?.Trim(), b.DoctorName?.Trim(), StringComparison.OrdinalIgnoreCase) &&
        a.Age == b.Age &&
        string.Equals(a.Phone?.Trim(), b.Phone?.Trim(), StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.Gender?.Trim(), b.Gender?.Trim(), StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.City?.Trim(), b.City?.Trim(), StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.Specialty?.Trim(), b.Specialty?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool MatchesPatientDoctor(
        string? patientId,
        string? patientName,
        string? doctorName,
        string? oldId,
        string? oldName,
        string? oldDoctor)
    {
        var idMatch = !string.IsNullOrWhiteSpace(oldId) &&
                      string.Equals(patientId?.Trim(), oldId, StringComparison.OrdinalIgnoreCase);
        var nameMatch = !string.IsNullOrWhiteSpace(oldName) &&
                        string.Equals(patientName?.Trim(), oldName, StringComparison.OrdinalIgnoreCase);
        if (!idMatch && !nameMatch) return false;

        if (string.IsNullOrWhiteSpace(oldDoctor)) return true;
        return string.Equals(doctorName?.Trim(), oldDoctor, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record BillingLine(string ItemName, int Qty, decimal UnitFee);
}
