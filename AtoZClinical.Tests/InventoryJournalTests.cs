using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AtoZClinical.Tests;

public class InventoryJournalTests
{
    [Fact]
    public async Task Opening_balance_and_purchase_post_inventory_to_trial_balance()
    {
        var clinicId = Guid.NewGuid();
        await using var db = await SqliteTestDatabase.CreateAsync(TestClinicProvider.ForClinic(clinicId));
        db.Db.Clinics.Add(new Clinic { Id = clinicId, ClinicCode = "TST", Name = "Test Clinic" });
        await DatabaseInitializer.EnsureStandardChartAccountsAsync(db.Db, clinicId);

        var item = new PharmacyItem
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            ItemNo = 1,
            Barcode = "MED-001",
            MedicineCode = "MED-001",
            MedicineName = "Paracetamol",
            BaseUom = "Pcs",
            DefaultUnitPrice = 10m,
            MovingAverageCost = 5m,
            QuantityOnHand = 0
        };
        db.Db.PharmacyItems.Add(item);

        var opening = new PharmacyOpeningBalance
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            BalanceNo = 1,
            BalanceDate = new DateTime(2026, 6, 1),
            UpdatedAt = DateTime.UtcNow
        };
        opening.Lines.Add(new PharmacyOpeningBalanceLine
        {
            Id = Guid.NewGuid(),
            PharmacyOpeningBalanceId = opening.Id,
            LineNo = 1,
            Barcode = item.Barcode,
            MedicineCode = item.MedicineCode,
            MedicineName = item.MedicineName,
            Qty = 100,
            UnitCost = 10m,
            Total = 1000m
        });
        db.Db.PharmacyOpeningBalances.Add(opening);
        await db.Db.SaveChangesAsync();

        var inventory = new PharmacyInventoryService(db.Db);
        var journalSync = new ClinicalJournalSyncService(db.Db, NullLogger<ClinicalJournalSyncService>.Instance);
        var openingService = new PharmacyOpeningBalanceService(
            db.Db, new AuditService(db.Db), inventory, journalSync);

        await openingService.SaveAsync(clinicId, opening, opening.Lines.ToList());

        var purchase = new PharmacyPurchaseBill
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            PurchaseNo = 1,
            PurchaseDate = new DateTime(2026, 6, 5),
            SupplierName = "Vendor A",
            NetAmount = 500m,
            AmountPaid = 500m,
            BalanceDue = 0m,
            PaymentMethod = "Cash",
            UpdatedAt = DateTime.UtcNow
        };
        purchase.Lines.Add(new PharmacyPurchaseBillLine
        {
            Id = Guid.NewGuid(),
            PharmacyPurchaseBillId = purchase.Id,
            LineNo = 1,
            Barcode = item.Barcode,
            MedicineCode = item.MedicineCode,
            MedicineName = item.MedicineName,
            Qty = 50,
            UnitCost = 10m,
            LineTotal = 500m
        });
        db.Db.PharmacyPurchaseBills.Add(purchase);
        await db.Db.SaveChangesAsync();

        var purchaseService = new PharmacyPurchaseBillService(
            db.Db, new AuditService(db.Db), inventory, journalSync);
        await purchaseService.SaveAsync(clinicId, purchase, purchase.Lines.ToList());

        await journalSync.EnsureClinicalJournalsAsync(clinicId);

        var journal = new JournalReportService(db.Db, journalSync);
        var tb = await journal.GetTrialBalanceAsync(clinicId, new DateTime(2026, 6, 30));
        var inventoryRow = tb.Single(r => r.AccountName == "Inventory");

        Assert.Equal(1500m, inventoryRow.Balance);
    }

    [Fact]
    public async Task Pharmacy_bill_posts_cogs_to_profit_and_loss()
    {
        var clinicId = Guid.NewGuid();
        await using var db = await SqliteTestDatabase.CreateAsync(TestClinicProvider.ForClinic(clinicId));
        db.Db.Clinics.Add(new Clinic { Id = clinicId, ClinicCode = "TST", Name = "Test Clinic" });
        await DatabaseInitializer.EnsureStandardChartAccountsAsync(db.Db, clinicId);

        var item = new PharmacyItem
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            ItemNo = 1,
            Barcode = "MED-002",
            MedicineCode = "MED-002",
            MedicineName = "Ibuprofen",
            BaseUom = "Pcs",
            DefaultUnitPrice = 20m,
            MovingAverageCost = 8m,
            QuantityOnHand = 10
        };
        db.Db.PharmacyItems.Add(item);

        db.Db.PharmacyInventoryMovements.Add(new PharmacyInventoryMovement
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            PharmacyItemId = item.Id,
            MovementDate = new DateTime(2026, 6, 1),
            MovementType = PharmacyInventoryTypes.OpeningBalance,
            ReferenceType = PharmacyInventoryTypes.ReferenceOpeningBalance,
            ReferenceId = Guid.NewGuid(),
            ReferenceNo = 1,
            LineNo = 1,
            Barcode = item.Barcode,
            MedicineCode = item.MedicineCode,
            MedicineName = item.MedicineName,
            QtyIn = 10,
            QtyOut = 0,
            UnitCost = 8m,
            TotalValue = 80m,
            BalanceQtyAfter = 10,
            MovingAvgCostAfter = 8m
        });

        var bill = new PharmacyBill
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            BillNo = 1,
            BillDate = new DateTime(2026, 6, 10),
            PatientName = "Patient",
            TotalAmount = 40m,
            UpdatedAt = DateTime.UtcNow
        };
        bill.Lines.Add(new PharmacyBillLine
        {
            Id = Guid.NewGuid(),
            PharmacyBillId = bill.Id,
            LineNo = 1,
            Barcode = item.Barcode,
            MedicineCode = item.MedicineCode,
            MedicineName = item.MedicineName,
            Qty = 2,
            UnitPrice = 20m,
            LineTotal = 40m
        });
        db.Db.PharmacyBills.Add(bill);
        await db.Db.SaveChangesAsync();

        var inventory = new PharmacyInventoryService(db.Db);
        await inventory.SyncBillOutAsync(clinicId, bill, bill.Lines.ToList());

        var journalSync = new ClinicalJournalSyncService(db.Db, NullLogger<ClinicalJournalSyncService>.Instance);
        await journalSync.SyncPharmacyBillAsync(clinicId, bill, bill.Lines.ToList());

        var journal = new JournalReportService(db.Db, journalSync);
        var pl = FinancialStatementBuilder.BuildProfitAndLoss(
            await journal.GetPeriodActivityAsync(clinicId, new DateTime(2026, 6, 1), new DateTime(2026, 6, 30)));

        Assert.Equal(16m, pl.TotalCostOfGoodsSold);
        Assert.Contains(pl.CostOfGoodsSold, l => l.Account == "Cost of Goods Sold" && l.Amount == 16m);
    }
}
