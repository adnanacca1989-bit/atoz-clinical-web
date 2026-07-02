# A to Z Clinical — Full Solution Audit Report

**Build:** `2026.06.30-r175`  
**Date:** 2026-06-25  
**Scope:** All projects (`Core`, `Infrastructure`, `Web`, `Tests`)  
**Build status:** 0 errors, 29 warnings (nullable + MailKit advisory)

---

## Executive Summary

The solution builds cleanly and follows a layered architecture (Core entities → Infrastructure services/EF → Razor Pages Web host). Security controls are generally strong (Identity lockout, API key auth, security headers, PHI audit middleware). The largest risks are **performance hotspots on dashboard/reports**, **patient portal weak authentication**, and **operational endpoints that previously leaked SMTP metadata**.

**Fixes applied in r175:** XSS in notifications UI, health/debug endpoint hardening, OTP API rate limiting, OTP log redaction, missing `AuthorizeFolder` entries, async fix in pharmacy item numbering, integration/unit tests, k6 load/stress scripts.

---

## Critical Issues

### C1 — Patient visit status sync causes query storm on dashboard

| Field | Value |
|-------|-------|
| **File** | `AtoZClinical.Infrastructure/Services/PatientVisitStatusService.cs` |
| **Method** | `SyncAllPatientStatusesForClinicAsync` → `DeriveStatusFromActivityAsync` |
| **Risk** | **Critical** — performance / availability |
| **Explanation** | Called from `DashboardService.GetSummaryAsync` on every dashboard load. For each patient, runs many `AnyAsync` queries; name-matching fallbacks load entire clinic name columns repeatedly. |
| **Recommended fix** | Run sync on a schedule or after writes; preload activity flags once per clinic; never block dashboard reads. |
| **Example** | ```csharp\n// DashboardService.GetSummaryAsync — remove:\nawait _visitStatus.SyncAllPatientStatusesForClinicAsync(clinicId);\n// Replace with cached counts or background reconciliation\n``` |

### C2 — Pharmacy inventory N+1 per line

| Field | Value |
|-------|-------|
| **File** | `AtoZClinical.Infrastructure/Services/PharmacyInventoryService.cs` |
| **Methods** | `GetOrCreateItemAsync`, `RecalculateItemAsync` |
| **Risk** | **Critical** — DB saturation under pharmacy load |
| **Explanation** | Each inventory line reloads all active items; recalculation calls `SaveChangesAsync` per item. |
| **Recommended fix** | Cache catalog per operation; batch recalculation in one transaction. |

---

## High Priority Issues

### H1 — OTP brute-force surface (partially fixed in r175)

| Field | Value |
|-------|-------|
| **File** | `AtoZClinical.Web/Api/AuthOtpApiEndpoints.cs` |
| **Risk** | **High** |
| **Explanation** | 4-digit OTP with anonymous verify endpoint. r175 adds rate limiting (`auth-otp`, 12/min) and auth POST middleware for `/api/auth/*`. Still vulnerable if attacker rotates IPs. |
| **Fix** | Increase OTP length to 6+ digits; add per-IP + per-user composite limits; enable `AccountVerification:Required` only when SMTP/SMS configured. |

### H2 — Account verification disabled by default

| Field | Value |
|-------|-------|
| **File** | `TrialRegistrationVerificationService.cs` → `AccountVerificationPolicy` |
| **Risk** | **High** (when verification is a business requirement) |
| **Explanation** | r175 wires `IsRequired` to `AccountVerification:Required` (default `false`). Combined with `RequireConfirmedEmail = false`, new users sign in without OTP. |
| **Fix** | Set `AccountVerification:Required=true` in production when SMTP/Twilio are configured. |

### H3 — Unbounded report queries

| Field | Value |
|-------|-------|
| **Files** | `ArReportService.cs`, `RequestReportService.cs`, `PatientHistory.cshtml.cs`, `PatientPrintBundleService.cs` |
| **Risk** | **High** |
| **Explanation** | Full-clinic `ToListAsync` then filter in memory. Grows linearly with clinic size. |
| **Fix** | Push date/patient/doctor filters to SQL; paginate; use `AsNoTracking()`. |

### H4 — Development vendor password fallback

| Field | Value |
|-------|-------|
| **File** | `DatabaseInitializer.cs` (lines 63–74) |
| **Risk** | **High** if staging is internet-exposed |
| **Explanation** | Uses `ChangeMe@Local2026!` when `Seed:VendorPassword` unset outside Production. |
| **Fix** | Require explicit seed password in all non-local environments. |

### H5 — Patient portal weak knowledge factors

| Field | Value |
|-------|-------|
| **Files** | `Portal/Login.cshtml.cs`, `PatientPortalService.cs` |
| **Risk** | **High** (PHI exposure) |
| **Explanation** | Auth = patient number + DOB + last 4 phone digits; no rate limit on portal login POST beyond global auth limiter. |
| **Fix** | Issue portal PIN/email magic links; add dedicated rate limiting; optional 2FA for portal. |

---

## Medium Issues

### M1 — Stored XSS in notifications (fixed r175)

| Field | Value |
|-------|-------|
| **File** | `wwwroot/js/clinical-notifications.js` |
| **Risk** | **Medium** |
| **Fix applied** | `escapeHtml()` on title/detail before `innerHTML`. |

### M2 — Missing `[Authorize]` on clinical folders (fixed r175)

| Field | Value |
|-------|-------|
| **File** | `Program.cs` |
| **Folders** | `/Surgery`, `/Ward`, `/Rooms`, `/Expenses`, `/Workflow`, `/Search` |
| **Fix applied** | `AuthorizeFolder` added for defense in depth. |

### M3 — Health/debug endpoint disclosure (fixed r175)

| Field | Value |
|-------|-------|
| **File** | `Program.cs` |
| **Endpoints** | `/health`, `/health/email`, `/debug-email-config`, `/test-email` |
| **Fix applied** | Anonymous `/health` returns minimal JSON; SMTP details require `X-Health-Token`; debug/test-email always require token. |

### M4 — OTP logged in plaintext (fixed r175)

| Field | Value |
|-------|-------|
| **File** | `OtpLogDelivery.cs` |
| **Fix applied** | Plain code only when `Otp:ForceLogDelivery=true`; otherwise logs `REDACTED`. |

### M5 — No explicit EF transactions for multi-step writes

| Field | Value |
|-------|-------|
| **Files** | `BillingPropagationService`, `ClinicalJournalSyncService`, voucher/inventory flows |
| **Risk** | **Medium** — partial consistency on failure |
| **Fix** | `await using var tx = await _db.Database.BeginTransactionAsync()` around invoice+journal+inventory updates. |

### M6 — CSRF disabled on Account/Portal POSTs

| Field | Value |
|-------|-------|
| **File** | `Program.cs`, portal page models |
| **Risk** | **Medium** — login CSRF / portal booking CSRF |
| **Explanation** | Intentional tradeoff for login; portal booking should keep antiforgery. |

### M7 — Password reset token in query string

| Field | Value |
|-------|-------|
| **Files** | `Program.cs` `/reset-password`, `ResetPassword.cshtml.cs` |
| **Risk** | **Medium** — Referer/history leakage |
| **Fix** | POST-only token exchange or fragment-based links (`#token=`). |

### M8 — `FormPermissionPageFilter` skips `/Search`

| Field | Value |
|-------|-------|
| **File** | `FormPermissionPageFilter.cs` |
| **Risk** | **Medium** — permission bypass if handler omits clinic check |
| **Fix** | Remove `/Search` from skip list; enforce form key `Search.Query`. |

### M9 — MailKit NU1902 advisory

| Field | Value |
|-------|-------|
| **Package** | MailKit 4.11.0 |
| **Risk** | **Medium** |
| **Fix** | Upgrade MailKit to patched version when available. |

---

## Low Issues

| ID | Area | File | Issue | Fix |
|----|------|------|-------|-----|
| L1 | Security | `LanguageMiddleware.cs` | `clinical_lang` cookie not HttpOnly | Set HttpOnly unless JS must read it |
| L2 | Security | `SecurityHeadersMiddleware.cs` | CSP `unsafe-inline` | Nonce-based CSP when feasible |
| L3 | Security | `appsettings.json` | Placeholder DB password | Ensure env overrides in all deployments |
| L4 | Performance | `ExtendedServices.cs` | Missing `AsNoTracking` on lists | Add on read-only queries |
| L5 | Performance | `PharmacyItemRegistrationService.cs` | `ContinueWith(.Result)` | **Fixed r175** — proper async |
| L6 | Nullability | `GlobalTransactionSearchService.cs` | CS8604 ILike null args | Null-coalesce before ILike |
| L7 | Architecture | Duplicate modal partials | Layout + page both include patient/doctor modals | Include once in layout only |

---

## Security Audit Summary

| Category | Status |
|----------|--------|
| SQL injection | **Low risk** — EF LINQ; `ExecuteSqlRaw` only static DDL |
| XSS | **Mitigated** notifications; chat already escapes |
| CSRF | **Partial** — auth/portal POSTs ignore antiforgery |
| Authentication | **Strong** Identity policy (12 char, lockout) |
| Authorization | **Improved** r175 folder auth; API keys hashed |
| Secrets in code | **Dev-only** vendor seed password |
| Sensitive logging | **Improved** OTP redaction |
| API endpoints | **Protected** `/api/v1/*` requires API key |

---

## Performance Audit Summary

| Pattern | Severity | Hotspots |
|---------|----------|----------|
| N+1 queries | Critical | `PatientVisitStatusService`, `PharmacyInventoryService` |
| Unbounded ToListAsync | High | Reports, backup, patient history |
| In-memory filtering | High | `ArReportService`, `RequestReportService` |
| Missing AsNoTracking | Medium | `ExtendedServices`, list pages |
| Blocking async | Medium | **Fixed** `NextItemNoAsync` |
| Dashboard coupling | High | Status sync on every load |

**Connection pool:** `Maximum Pool Size=20` in `Program.cs` — monitor under k6 load; increase only with DB capacity.

---

## Architecture Review

| Principle | Assessment |
|-----------|------------|
| Clean Architecture | **Good** — Core has no EF; Infrastructure isolates data access |
| SOLID | **Mixed** — some services are large (`ExtendedServices`, `BillingPropagationService`) |
| Repository pattern | **Partial** — service classes act as repositories (acceptable for this size) |
| DI | **Good** — consistent `AddScoped` registration in `Program.cs` |
| Separation of concerns | **Good** — page models thin; business logic in services |
| Duplication | **Moderate** — patient name matching repeated across services |
| Complexity | **High** in billing/journal/propagation chain |

---

## Logging Review

| Area | Status |
|------|--------|
| Serilog bootstrap + config | **Present** |
| Auth events | **Logged** (login, cookie, audit middleware) |
| Password reset | **Logged** success/failure without secrets |
| Exceptions in page handlers | **Mostly logged** (`ForgotPassword`, API endpoints) |
| Swallowed exceptions | `PatientService.SaveAsync` consultation ensure (`catch { }`) — **silent** |
| Business actions | Audit service used for CRUD; not uniform everywhere |

**Recommendation:** Replace empty `catch { }` with `LogWarning`; ensure all `catch` blocks log except expected control flow.

---

## Unit & Integration Testing

### Existing (26 test files)

Service-level tests for patients, doctors, invoices, dashboard, tenant isolation, SMTP settings, ward reports, etc.

### Added in r175

| File | Coverage |
|------|----------|
| `PasswordResetServiceTests.cs` | Token create, rate limit, mark used |
| `ClinicalIntegrationTests.cs` | Auth redirects, health security, OTP API, patient/doctor CRUD, reports auth |
| `HealthEndpointTests.cs` | Updated for minimal anonymous health |

### Gaps (recommended next)

- End-to-end forgot/reset password with mock email sender
- Appointment service integration
- Billing propagation with journal assertions
- API integration with seeded API key
- Portal login rate limit tests

**Note:** Local test run requires .NET 8 runtime (`Microsoft.NETCore.App 8.0`); build SDK may be 9/10.

---

## Load & Stress Testing (k6)

Scripts: `tests/k6/load-test.js`, `tests/k6/stress-test.js`

| Scenario | Command |
|----------|---------|
| 100 VUs | `k6 run tests/k6/load-test.js` |
| 250 VUs | `k6 run -e VUS=250 tests/k6/load-test.js` |
| 500 VUs | `k6 run -e VUS=500 tests/k6/load-test.js` |
| 1000 VUs | `k6 run -e VUS=1000 tests/k6/load-test.js` |
| Stress | `k6 run tests/k6/stress-test.js` |

**Expected bottlenecks:** PostgreSQL pool (20 connections), `PatientVisitStatusService` on dashboard, unbounded report queries, Render single-instance CPU.

---

## Fixes Applied in r175

1. `clinical-notifications.js` — HTML escape for notification content  
2. `Program.cs` — authorize folders; health endpoint hardening  
3. `AuthOtpApiEndpoints.cs` — rate limiting; reduced user enumeration  
4. `OtpLogDelivery.cs` — redact OTP unless forced log delivery  
5. `AccountVerificationPolicy` — config-driven `AccountVerification:Required`  
6. `PharmacyItemRegistrationService` — async `NextItemNoAsync`  
7. `AuthPostRateLimitMiddleware` — includes `/api/auth` POSTs  
8. Tests + k6 scripts + this report  

---

## Recommended Roadmap

1. **Week 1:** Decouple dashboard from `SyncAllPatientStatusesForClinicAsync`; fix pharmacy inventory batching  
2. **Week 2:** SQL-level filters for AR/request/patient history reports  
3. **Week 3:** Portal auth hardening; enable account verification in production  
4. **Week 4:** EF transactions for billing/journal; MailKit upgrade; expand integration tests  

---

*Report generated as part of solution audit. For questions, reference issue IDs (C1, H1, M1, etc.).*
