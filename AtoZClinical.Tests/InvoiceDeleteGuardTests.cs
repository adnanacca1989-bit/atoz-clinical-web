using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Tests;

public class InvoiceDeleteGuardTests
{
    [Fact]
    public async Task EnsureCanDeleteLabRequestAsync_blocks_when_line_on_invoice()
    {
        var clinicId = Guid.NewGuid();
        await using var db = await SqliteTestDatabase.CreateAsync(TestClinicProvider.ForClinic(clinicId));
        db.Db.Clinics.Add(new Clinic { Id = clinicId, ClinicCode = "TST", Name = "Test" });

        var invoice = new Invoice
        {
            ClinicId = clinicId,
            InvoiceNo = 1,
            PatientName = "Adnan",
            TotalAmount = 5_000m
        };
        invoice.Lines.Add(new InvoiceLine
        {
            LineNo = 1,
            ServiceName = "Lab #3: CBC",
            UnitFee = 5_000m,
            LineTotal = 5_000m
        });
        db.Db.Invoices.Add(invoice);
        await db.Db.SaveChangesAsync();

        var guard = new InvoiceDeleteGuardService(db.Db);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => guard.EnsureCanDeleteLabRequestAsync(clinicId, 3));
        Assert.Equal(InvoiceDeleteGuardService.BlockMessage, ex.Message);
    }

    [Fact]
    public async Task EnsureCanDeleteCashPaymentAsync_blocks_when_patient_has_invoice()
    {
        var clinicId = Guid.NewGuid();
        await using var db = await SqliteTestDatabase.CreateAsync(TestClinicProvider.ForClinic(clinicId));
        db.Db.Clinics.Add(new Clinic { Id = clinicId, ClinicCode = "TST", Name = "Test" });
        db.Db.Invoices.Add(new Invoice
        {
            ClinicId = clinicId,
            InvoiceNo = 1,
            PatientId = "PAT-00001",
            PatientName = "Adnan",
            TotalAmount = 25_000m
        });
        await db.Db.SaveChangesAsync();

        var guard = new InvoiceDeleteGuardService(db.Db);
        var payment = new CashPayment { PatientId = "PAT-00001", PayeeName = "Adnan", Amount = 75_000m };
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => guard.EnsureCanDeleteCashPaymentAsync(clinicId, payment));
        Assert.Equal(InvoiceDeleteGuardService.BlockMessage, ex.Message);
    }
}
