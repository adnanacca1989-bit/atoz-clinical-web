# AtoZ Clinical — SaaS Readiness Assessment

**Assessment date:** June 2026  
**Architecture:** Shared-database multi-tenant SaaS  
**Target scale:** 100+ clinics, 10,000+ patients per clinic

---

## Executive Summary

AtoZ Clinical is **already a multi-tenant SaaS application**. This release hardens tenant isolation, expands subscription management, adds a vendor SaaS dashboard, clinic branding, security audit logging, backup history, and performance indexes.

**Commercial readiness:** Suitable for **pilot and early commercial launch** with vendor-managed onboarding. Full enterprise/compliance sale requires items in the "Before commercial sale" section below.

---

## 1. Multi-Tenant Architecture

### Implemented

| Component | Status | Location |
|-----------|--------|----------|
| `ClinicId` on all clinical entities | ✅ | `AtoZClinical.Core/Entities/` |
| EF Core global query filters (`IClinicScoped`) | ✅ | `ClinicalDbContext.cs` |
| Tenant middleware | ✅ | `TenantContextMiddleware.cs` |
| API key tenant binding | ✅ | `ApiKeyAuthenticationMiddleware.cs` |
| Subdomain tenant resolution | ✅ | `ClinicSubdomainMiddleware.cs` |
| Explicit `clinicId` in service APIs | ✅ | All `*Service.cs` |
| `TenantQueryExtensions.ForClinic()` | ✅ NEW | `TenantQueryExtensions.cs` |
| `IgnoreQueryFilters` + explicit clinic filter | ✅ | `DoctorService`, backup history |

### Isolation model

```
Vendor (bypass filter) → sees all clinics
Clinic user → TenantClinicId set → EF filter hides other clinics
API key → bound to one clinic
```

### Remaining risks

| Risk | Severity | Recommendation |
|------|----------|----------------|
| Child line tables (InvoiceLine, etc.) have no `ClinicId` | Medium | Isolated via parent FK; add `ClinicId` + filter for defense-in-depth |
| `ApplicationUser` not EF-filtered | Medium | Enforce via `user.ClinicId` + auth (current) |
| No PostgreSQL Row-Level Security | Medium | Add RLS for regulated deployments |
| `DedicatedConnectionName` not wired | Low | Enterprise manual provisioning only |
| Vendor role bypasses all filters | Expected | Ensure `/Vendor` stays role-protected |

---

## 2. SaaS Subscription Management

### Plans

| Plan | Price/mo | Max users |
|------|----------|-----------|
| Trial | Free (30 days) | 10 |
| Basic | $49 | 10 |
| Standard | $99 | 25 |
| Professional | $199 | 100 |

### Implemented

- `SubscriptionType`, `SubscriptionStartDate`, `SubscriptionExpiryDate` on `Clinic`
- `SaasSubscriptionService` — renew, expire, sync dates
- Auto-expire job every 6 hours (`ClinicLicenseMaintenanceService`)
- Stripe Checkout + webhooks (`StripeBillingService`)
- Vendor renewal via `/Vendor/Details`
- Subscription report: `/Vendor/Subscriptions`
- Access blocked when expired (`ClinicAccessService` + `ClinicTenantMiddleware`)

### Configure Stripe (production)

```env
Billing__Enabled=true
Stripe__SecretKey=sk_live_...
Stripe__WebhookSecret=whsec_...
Stripe__PriceIdBasic=price_...
Stripe__PriceIdStandard=price_...
Stripe__PriceIdProfessional=price_...
```

---

## 3. Vendor Administration Portal

### Pages

| URL | Purpose |
|-----|---------|
| `/Vendor/Dashboard` | **SaaS dashboard** — clinics, users, patients, MRR, ARR, growth |
| `/Vendor/Clients` | Manage clinics (activate, suspend, renew) |
| `/Vendor/Subscriptions` | Subscription report (start/expiry/status) |
| `/Vendor/Analytics` | MRR, trials, churn |
| `/Vendor/Create` | Provision new clinic |
| `/Vendor/Details` | Per-clinic management + renewal |

### Dashboard metrics

- Total / Active / Trial / Expired clinics
- Total users, total patients (platform-wide)
- Monthly & annual revenue (estimated from plan catalog)
- Monthly clinic growth (6 months)
- Plan distribution

---

## 4. Security Review

### Implemented

- Role-based authorization (`Vendor`, `ClinicAdmin`, `ClinicStaff`)
- Form-level permissions per clinic (`FormPermissionService`)
- MFA (TOTP), SSO (Google/Microsoft)
- PHI access audit middleware
- **NEW:** `SecurityAuditService` — login, logout, failed login
- Immutable audit log (`AuditLogEntry` — append-only)
- Patient save audit logging
- Rate limiting on auth and registration

### Gaps

| Gap | Priority |
|-----|----------|
| API / portal PHI audit expansion | High |
| Centralized security audit UI for vendor | Medium |
| Penetration test before enterprise sale | High |
| HIPAA BAA / compliance documentation | High (US healthcare) |

---

## 5. Scalability Review

### Indexes added (migration `AddSaasPlatformFeatures`)

- `Clinics`: Status, PlanName, SubscriptionExpiryDate
- `Invoices`: (ClinicId, InvoiceDate)
- `Appointments`: (ClinicId, AppointmentDate)
- `AuditLogEntries`: (ClinicId, DateTime)
- `SecurityAuditEntries`: ClinicId, CreatedAt, EventType

### Existing optimizations

- Patient paging (`PagedResult`)
- Dashboard SQL aggregates
- Optional read replica (`ConnectionStrings__ReportingDatabase`)
- Connection pooling (PostgreSQL)

### Recommendations for 100+ clinics

| Item | Effort |
|------|--------|
| Redis distributed cache | Medium |
| Paginate all list screens | Medium |
| Audit log DB-side date filtering | Low |
| Horizontal scaling (2+ web instances) | Medium |
| Upgrade Render DB tier at ~50 clinics | Low |

---

## 6. Clinic Management & Branding

### NEW: `/Settings/ClinicProfile`

- Clinic name, contact, address, website, tagline
- Logo upload (base64, max 500 KB)
- Primary color (applied to navbar via CSS variable)
- Time zone (IANA)
- Language selection
- Form style

### Also available

- Currency, UOM, users, maintenance mode
- Enterprise: subdomain, patient portal (`/Admin/Enterprise`)
- Self-service billing (`/Billing`)

---

## 7. Backup & Recovery

### Platform (Render)

- Daily `pg_dump` → S3 (`scripts/backup-database.sh`, `render.yaml`)
- DR runbook: `docs/DR-RUNBOOK.md`

### Per-clinic

- Export ZIP / workbook (`/Admin/Backup`)
- **NEW:** Backup history table (`ClinicBackupHistory`)
- Partial restore (patients, doctors, chart of accounts)

### Gap

- Full clinic restore (invoices, pharmacy, lab) not implemented — export-only for those modules

---

## 8. Audit Trail

| Event | Logged |
|-------|--------|
| User login / logout / failed login | ✅ `SecurityAuditEntry` |
| Patient create/update | ✅ `AuditLogEntry` |
| Invoice, prescription, pharmacy, radiology | ✅ |
| Role permissions | ✅ |
| Backup export/restore | ✅ |
| PHI page access | ✅ middleware |
| Appointment changes | ⚠️ Partial (webhooks only) |
| User administration | ⚠️ Add to Settings/Users |

View: `/Admin/AuditLog` (clinic-scoped)

---

## 9. Reporting & Analytics

| Report | URL |
|--------|-----|
| SaaS dashboard | `/Vendor/Dashboard` |
| Subscription report | `/Vendor/Subscriptions` |
| SaaS analytics | `/Vendor/Analytics` |
| Clinic reports | `/Reports/*` |

---

## 10. Missing Components (Before Commercial Sale)

### Must-have

1. **Stripe live configuration** with Basic/Standard/Professional price IDs
2. **Terms of service + privacy policy** (pages exist; legal review needed)
3. **Email deliverability** (SPF/DKIM for trial/onboarding emails)
4. **Production secrets** — rotate vendor password, enforce MFA for admins

### Should-have

5. Full clinic restore from backup
6. Vendor security audit viewer
7. Stripe actual revenue vs estimated MRR reconciliation
8. Onboarding wizard for new clinics
9. Annual billing option in Stripe
10. PostgreSQL RLS for regulated customers

### Nice-to-have

11. Dedicated database per enterprise tenant (schema reserved)
12. Redis cache layer
13. SMS appointment reminders
14. Insurance / EMR integrations

---

## Database Migration

Apply on deploy:

```bash
dotnet ef database update --project AtoZClinical.Infrastructure --startup-project AtoZClinical.Web
```

Migration: `AddSaasPlatformFeatures`  
Fallback: `DatabaseInitializer.EnsureSaasPlatformSchemaAsync` (PostgreSQL column patches)

---

## Quick Start (Vendor)

1. Log in as vendor → redirected to `/Vendor/Dashboard`
2. Create clinic or approve trial registration
3. Assign plan + expiry on **Details → Renew**
4. Clinic admin configures profile at **Settings → Clinic Profile & Branding**
5. Clinic subscribes at **Billing** (when Stripe enabled)

---

## Verdict

| Area | Score | Notes |
|------|-------|-------|
| Multi-tenancy | 8/10 | Solid app-layer isolation |
| Subscriptions | 8/10 | Plans + Stripe + auto-expire |
| Vendor portal | 8/10 | Full dashboard added |
| Security | 7/10 | Good base; expand API audit |
| Scalability | 7/10 | OK for 100 clinics with DB tier upgrade |
| Branding | 7/10 | Logo + colors live |
| Backup | 6/10 | Platform DR good; partial clinic restore |
| Commercial readiness | 7/10 | Ready for pilot SaaS sales |

**Recommendation:** Proceed with controlled commercial launch (Iraq/MENA clinics) while completing Stripe live setup and legal/compliance documentation for healthcare data handling.
