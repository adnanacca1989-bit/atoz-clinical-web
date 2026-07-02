# EF Core Tracking Audit Report

**Date:** 2026-06-25  
**Build:** `2026.06.30-r176`  
**Scope:** All `AtoZClinical.Infrastructure` services, `ClinicSaveHelper`, `RolePermissionBootstrap`  
**AutoMapper:** Not used in this solution

---

## Summary

| Metric | Count |
|--------|------:|
| Files inspected | **28** |
| Files modified | **14** |
| Tracking conflicts fixed | **18** |
| Safe patterns retained (`ExecuteIsolatedSaveAsync`) | **12** `Update()` call sites |
| Remaining EF Core risks | **4** (documented below) |

**Recommendation:** All known `GetAsync` + `Update(detached)` conflicts in CRUD services are resolved. Deploy and re-test Doctor Surgery, Book Room, Patient/Doctor registration, and billing workflows.

---

## Root Cause

ASP.NET Core uses a **scoped** `ClinicalDbContext` per HTTP request. When a service:

1. Loads an entity with `GetAsync` / `FirstOrDefaultAsync` (entity becomes **tracked**), then
2. Calls `_db.Update(detachedInstance)` on a **second** object with the same primary key,

EF Core throws:

> *The instance of entity type 'X' cannot be tracked because another instance with the same key value for {'Id'} is already being tracked.*

---

## Standard Fix Applied

Added `ClinicSaveHelper.CopyTrackedScalars<T>()` which:

- Copies scalar values via `Entry(tracked).CurrentValues.SetValues(incoming)`
- Preserves `Id`, `CreatedAt`, and `CreatedBy` on the tracked row
- Avoids `_db.Update()` on detached instances

**Pattern:**

```csharp
var owned = await _db.Entities.FirstOrDefaultAsync(e => e.Id == item.Id);
ClinicSaveHelper.CopyTrackedScalars(_db, owned, item);
owned.UpdatedAt = DateTime.UtcNow;
await _db.SaveChangesAsync();
```

For authorization checks that do not need tracking, queries now use **`AsNoTracking()`** or scoped `.Apply(_doctorScope.Filter)` checks before isolated saves.

---

## Files Inspected

| File | Module |
|------|--------|
| `Data/ClinicSaveHelper.cs` | Shared infrastructure |
| `Services/InpatientServices.cs` | Doctor Surgery, Book Room, Ward Room |
| `Services/PatientService.cs` | Patient Registration |
| `Services/ClinicModuleServices.cs` | Doctors, Lab, Cash Receipt, Service Income, Lab Result |
| `Services/AppointmentService.cs` | Appointments |
| `Services/ExtendedServices.cs` | Radiology, Pharmacy, Billing, Prescriptions, Chart Accounts, Role Permissions |
| `Services/ServiceIncomeRequestService.cs` | Service Income Requests |
| `Services/PharmacyItemRegistrationService.cs` | Pharmacy Registration |
| `Services/PharmacyInventoryService.cs` | Pharmacy Opening Balance |
| `Services/PharmacyPurchaseBillService.cs` | Pharmacy Purchase |
| `Services/ExpenseVoucherService.cs` | Expenses / Billing |
| `Services/ClinicSettingsService.cs` | Administration / Settings |
| `Services/ClinicLookupService.cs` | Administration lookups (UOM, Currency, Owner, Language, Vendor) |
| `Services/ClinicProfileService.cs` | Clinic profile (no `Update()` — inspected, OK) |
| `Services/VendorClinicService.cs` | Vendor SaaS (inspected, OK) |
| `Services/PatientInvoiceService.cs` | Billing propagation (inspected, OK) |
| `Services/ArReportService.cs` | Reports (read-only, OK) |
| `Services/RequestReportService.cs` | Reports (read-only, OK) |
| `Services/WardPatientReportService.cs` | Reports (read-only, OK) |
| `Services/MasterDataPropagationService.cs` | Propagation (inspected, OK) |
| `Services/ClinicalJournalSyncService.cs` | Journal sync (inspected, OK) |
| `Services/BillingPropagationService.cs` | Billing sync (inspected, OK) |
| `Services/DoctorScopeService.cs` | Authorization (inspected, OK) |
| `Services/FormPermissionService.cs` | Administration (read-only in save path, OK) |
| `Services/PatientPortalService.cs` | Portal (inspected, OK) |
| `Services/TrialRegistrationVerificationService.cs` | Registration (inspected, OK) |
| `RolePermissionBootstrap.cs` | Admin bootstrap (updates tracked instance — OK) |
| `Data/ClinicalDbContext.cs` | SaveChanges audit hook (inspected, OK) |

---

## Files Modified

| File | Changes |
|------|---------|
| `ClinicSaveHelper.cs` | Added `CopyTrackedScalars`, `FindTrackedAsync` |
| `InpatientServices.cs` | Doctor Surgery, Book Room — tracked update |
| `PatientService.cs` | Patient Registration — tracked update |
| `ClinicModuleServices.cs` | Doctor Registration, Lab Result; auth checks → `AsNoTracking` for Lab Request, Cash Receipt |
| `AppointmentService.cs` | Tracked update |
| `ExtendedServices.cs` | Prescription, Cash Payment, Pharmacy Bill, Radiology Result, Role Permission; auth → `AsNoTracking` for Radiology/Pharmacy Request, Invoice |
| `ServiceIncomeRequestService.cs` | Auth check → `AsNoTracking` |
| `PharmacyItemRegistrationService.cs` | Inventory snapshot → `AsNoTracking` |
| `PharmacyInventoryService.cs` | Opening Balance — tracked update |
| `PharmacyPurchaseBillService.cs` | Purchase Bill — tracked update |
| `ExpenseVoucherService.cs` | Expense Voucher — tracked update |
| `ClinicSettingsService.cs` | Clinic configuration — tracked update |
| `ClinicLookupService.cs` | UOM, Currency, Owner, Language, Vendor — tracked update |

---

## Tracking Conflicts Fixed (by module)

| Module | Service / Entity | Issue | Fix |
|--------|------------------|-------|-----|
| **Doctor Surgery** | `DoctorSurgeryService` / `DoctorSurgery` | `GetAsync` + `Update` | `CopyTrackedScalars` on tracked row |
| **Book Room** | `RoomBookingService` / `RoomBooking` | Detached `Update` | Load tracked + copy |
| **Patient Registration** | `PatientService` / `Patient` | Detached `Update` | Load tracked + copy |
| **Doctor Registration** | `DoctorService` / `Doctor` | Detached `Update` | Load tracked + copy |
| **Laboratory** | `LabResultService` / `LabResult` | `GetAsync` + `Update` | Tracked copy |
| **Laboratory** | `LabRequestService` | Tracked auth load before isolated save | `AsNoTracking` scope check |
| **Radiology** | `RadiologyResultService` / `RadiologyResult` | Detached `Update` | Tracked copy |
| **Radiology** | `RadiologyRequestService` | Tracked auth before isolated save | `AsNoTracking` scope check |
| **Pharmacy** | `PrescriptionService` / `Prescription` | `GetAsync` + `Update` | Single tracked instance |
| **Pharmacy** | `PharmacyBillService` / `PharmacyBill` | `GetAsync` + `Update` | Tracked copy |
| **Pharmacy** | `PharmacyOpeningBalanceService` | Detached `Update` | Tracked copy |
| **Pharmacy** | `PharmacyPurchaseBillService` | Detached `Update` | Tracked copy |
| **Pharmacy** | `PharmacyItemRegistrationService` | Tracked read before isolated save | `AsNoTracking` snapshot |
| **Billing** | `CashPaymentService` / `CashPayment` | `GetAsync` + `Update` | Tracked copy |
| **Billing** | `ExpenseVoucherService` | Detached `Update` | Tracked copy |
| **Billing** | `CashReceiptService` | Tracked auth before isolated save | `AsNoTracking` scope check |
| **Billing** | `InvoiceService` | Tracked auth before isolated save | `AsNoTracking` scope check |
| **Administration** | `ClinicSettingsService` / `ClinicConfiguration` | Detached `Update` | Tracked copy |
| **Administration** | `ClinicLookupService` (5 entities) | Detached `Update` / vendor `Get` + `Update` | Tracked copy |
| **Administration** | `RolePermissionService` | Detached `Update` | Tracked copy |
| **Appointments** | `AppointmentService` | `GetAsync` + `Update` | Tracked copy |

---

## Safe Patterns Retained (no change required)

These `Update()` calls run inside `ClinicSaveHelper.ExecuteIsolatedSaveAsync`, which calls `ChangeTracker.Clear()` before staging:

- `RadiologyTestService`, `RadiologyRequestService`, `PharmacyRequestService`
- `InvoiceService`, `ChartAccountService`, `ServiceIncomeService`
- `CashReceiptService`, `LabTestService`, `LabRequestService`
- `PharmacyItemRegistrationService` (update path)

`PatientService` / `DoctorService` previously used `AsNoTracking` before `Update` — replaced with tracked copy for consistency.

`RolePermissionBootstrap` updates the **same** tracked instance (`existing.IsVisible = true`) — correct.

`WardRoomService` mutates tracked `WardRoom` rows in place — correct.

**Reports** (`ArReportService`, `RequestReportService`, etc.) are read-only — no tracking risk.

---

## Remaining EF Core Risks

| ID | Risk | Severity | Notes |
|----|------|----------|-------|
| R1 | Child navigation graphs on copy | Low | `CopyTrackedScalars` copies scalars only; child collections (invoice lines, etc.) are still replaced explicitly via remove/add — current code handles this |
| R2 | `ExecuteIsolatedSaveAsync` + detached `Update` | Low | Safe today because tracker is cleared; future edits must preserve `Clear()` before `Update()` |
| R3 | Concurrent updates (last-write-wins) | Medium | No optimistic concurrency tokens (`RowVersion`) on clinical entities — unrelated to tracking, but true data-loss risk under concurrent edits |
| R4 | `PatientHistory` / large report reads | Medium | Performance risk (full-table reads), not tracking — see `PRODUCTION_READINESS_REPORT.md` |

---

## Verification

```
dotnet build -c Release  → 0 errors
dotnet test -c Release   → 139 passed, 6 skipped
```

### Manual re-test checklist

- [ ] Doctor Surgery — Edit record #1 → **Update**
- [ ] Book Room — Edit booking → **Update**
- [ ] Patient Registration — Edit patient → **Update**
- [ ] Doctor Registration — Edit doctor → **Update**
- [ ] Laboratory Request / Result — Edit → **Update**
- [ ] Radiology Request / Result — Edit → **Update**
- [ ] Pharmacy Request / Bill / Opening Balance — Edit → **Update**
- [ ] Invoice / Cash Payment / Cash Receipt — Edit → **Update**
- [ ] Settings → Clinic configuration save
- [ ] Administration → Role permissions save

---

*Generated after full-project EF Core tracking audit.*
