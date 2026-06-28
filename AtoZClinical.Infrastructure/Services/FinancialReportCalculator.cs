using AtoZClinical.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public sealed class FinancialReportCalculator
{
    private readonly ClinicalDbContext _db;
    private readonly PharmacyCogsService _cogs;

    public FinancialReportCalculator(ClinicalDbContext db, PharmacyCogsService cogs)
    {
        _db = db;
        _cogs = cogs;
    }

    public async Task<decimal> ComputeNetIncomeAsync(
        Guid clinicId,
        DateTime fromDate,
        DateTime toDate,
        string? doctorName = null,
        string? patientName = null)
    {
        var from = fromDate.Date;
        var to = toDate.Date;

        var invoices = await _db.Invoices
            .Include(i => i.Lines)
            .ForClinic(clinicId)
            .Where(i => i.InvoiceDate >= from && i.InvoiceDate <= to)
            .ToListAsync();

        if (!string.IsNullOrWhiteSpace(doctorName))
            invoices = invoices.Where(i => i.DoctorName?.Contains(doctorName, StringComparison.OrdinalIgnoreCase) == true).ToList();
        if (!string.IsNullOrWhiteSpace(patientName))
            invoices = invoices.Where(i => i.PatientName?.Contains(patientName, StringComparison.OrdinalIgnoreCase) == true).ToList();

        decimal revenue = 0;
        foreach (var inv in invoices)
            revenue += inv.Lines.Sum(l => l.LineTotal);

        var cogs = await _cogs.GetTotalCogsAsync(clinicId, from, to, doctorName, patientName);

        var chartAccounts = await _db.ChartAccounts
            .AsNoTracking()
            .ForClinic(clinicId)
            .ToListAsync();
        var expenseAccounts = new HashSet<string>(
            chartAccounts
                .Where(a => string.Equals(a.CategoryType, "Expense", StringComparison.OrdinalIgnoreCase))
                .Select(a => a.Name),
            StringComparer.OrdinalIgnoreCase);

        var payments = await _db.CashPayments
            .ForClinic(clinicId)
            .Where(p => p.PaymentDate >= from && p.PaymentDate <= to)
            .ToListAsync();

        var operatingExpenses = payments
            .Where(p => string.IsNullOrWhiteSpace(p.PatientId))
            .Where(p => !string.IsNullOrWhiteSpace(p.ChartAccountName) && expenseAccounts.Contains(p.ChartAccountName))
            .Sum(p => p.Amount);

        var voucherExpenses = await _db.ExpenseVouchers
            .Include(v => v.Lines)
            .ForClinic(clinicId)
            .Where(v => v.ExpenseDate >= from && v.ExpenseDate <= to)
            .ToListAsync();

        operatingExpenses += voucherExpenses.SelectMany(v => v.Lines).Sum(l => l.Amount);

        return revenue - cogs - operatingExpenses;
    }

    public async Task<decimal> ComputePatientCreditLiabilityAsync(Guid clinicId, DateTime asOf)
    {
        var invoices = await _db.Invoices
            .AsNoTracking()
            .ForClinic(clinicId)
            .Where(i => i.InvoiceDate.Date <= asOf)
            .ToListAsync();

        var receipts = await _db.CashReceipts
            .AsNoTracking()
            .ForClinic(clinicId)
            .Where(r => r.ReceiptDate.Date <= asOf)
            .ToListAsync();

        var payments = await _db.CashPayments
            .AsNoTracking()
            .ForClinic(clinicId)
            .Where(p => p.PaymentDate.Date <= asOf)
            .ToListAsync();

        return InvoiceArCalculator.ComputeTotalPatientCredit(invoices, receipts, payments);
    }
}
