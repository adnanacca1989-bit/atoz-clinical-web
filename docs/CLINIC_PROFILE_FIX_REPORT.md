# Clinic Profile Fix Report

**Date:** 2026-06-30  
**Build:** `2026.06.30-r178`  
**Page:** `/Settings/ClinicProfile`  
**Production trace (user report):** `00-814fd181851ac404b3dd2d66fb0d9c1d-d3cc5abb41b2992f-00`

---

## Executive Summary

The Clinic Profile & Branding page crashed with the generic **"Something went wrong"** error page. The root cause was an **unhandled `NullReferenceException` during Razor view rendering** when building Time Zone / Language / Form Style dropdowns via `SelectList` over primitive string arrays.

Production was still exhibiting this failure because the fix in **r177** had not been merged/deployed to `main` at the time of the report, and the dropdown implementation remained fragile. **r178** replaces `SelectList` entirely with explicit `<option>` elements, adds defensive null handling, structured logging, and a dedicated PostgreSQL branding schema probe.

---

## 1. Reproduction

### Local reproduction (confirmed)

| Step | Result |
|------|--------|
| Authenticated GET `/Settings/ClinicProfile` with **old** `new SelectList(string[])` markup | **HTTP 500** |
| Exception | `System.NullReferenceException` |
| Stack (top frames) | `MultiSelectList.GetListItemsWithValueField()` → `SelectTagHelper.Process()` → `ClinicProfile.cshtml` |

### Reproduction command

```powershell
dotnet test -c Release --filter "FullyQualifiedName~ClinicProfileTests"
```

Before fix: `Authenticated_clinic_admin_can_open_workflow_page` for `/Settings/ClinicProfile` failed with HTTP 500.

After fix: **4 dedicated ClinicProfile tests + 150 total tests pass**.

---

## 2. Render Production Logs

Direct access to Render logs is **not available from this environment** (no Render API token / dashboard access).

### How to locate the exception in Render

1. Open **Render Dashboard** → service `atoz-clinical` → **Logs**
2. Search for trace ID: `00-814fd181851ac404b3dd2d66fb0d9c1d-d3cc5abb41b2992f-00`
3. Or search: `ClinicProfile`, `NullReferenceException`, `SelectTagHelper`, `/Settings/ClinicProfile`

### Expected log signature (pre-r178)

```
System.NullReferenceException: Object reference not set to an instance of an object.
   at Microsoft.AspNetCore.Mvc.Rendering.MultiSelectList.GetListItemsWithValueField()
   at Microsoft.AspNetCore.Mvc.TagHelpers.SelectTagHelper.Process(...)
   at AtoZClinical.Web.Pages.Settings.Pages_Settings_ClinicProfile.ExecuteAsync()
```

### Secondary production risk (mitigated in r178)

If PostgreSQL is missing branding columns (`LogoBase64`, `PrimaryColor`, etc.), EF queries can throw `PostgresException`. Startup now runs `EnsureClinicBrandingSchemaAsync` with per-column `ALTER TABLE ... IF NOT EXISTS` and a probe query.

---

## 3. Root Cause

| Item | Detail |
|------|--------|
| **Exception type** | `System.NullReferenceException` |
| **Failure phase** | **View rendering** (after page handler), not database load |
| **Trigger** | `asp-items="@(new SelectList(ClinicProfileModel.TimeZones))"` (and similar for Language / Form Style) |
| **Why handler try/catch did not help** | `OnGetAsync` completed successfully; exception thrown when Razor executed `SelectTagHelper` |
| **Why production still failed** | Branch `feature/performance-security-hardening` (r177) was pushed but **not merged to `main`**; Render deploys from `main` |

---

## 4. Fix Applied (r178)

### View layer (primary fix)

- Removed all `SelectList` / `asp-items` usage on Clinic Profile
- Render dropdowns with explicit `@foreach` + `<option>` elements (cannot NRE on value-field reflection)

### Defensive coding

- `ClinicBrandingHelper` — normalizes primary color, time zone, language, form style
- `ClinicProfileModel` — safe fallback input on load failure; try/catch on save/upload/clear; structured logging
- `ClinicProfileService` — null-safe clinic name, logging on load/save/errors
- `ClinicBrandingPageFilter` — logs warnings, falls back to default color
- `_Layout.cshtml` — normalizes CSS primary color variable

### Schema hardening

- New `EnsureClinicBrandingSchemaAsync` in `DatabaseInitializer` (individual `ALTER` statements + EF probe)

### Tests added

| Test | Verifies |
|------|----------|
| `ClinicProfileTests.Get_clinic_profile_returns_ok_with_expected_content` | Page loads, no error page |
| `ClinicProfileTests.Post_save_profile_persists_primary_color` | Save + DB persistence |
| `ClinicProfileTests.Post_upload_logo_saves_and_page_shows_success` | Logo upload |
| `ClinicProfileTests.Branding_filter_applies_primary_color_on_next_request` | Immediate branding in layout |
| `ClinicBrandingHelperTests` | Color normalization |
| `MigrationValidationTests.Test_host_schema_supports_clinic_branding_columns` | EF can query branding columns |

---

## 5. Files Modified

| File | Change |
|------|--------|
| `AtoZClinical.Web/Pages/Settings/ClinicProfile.cshtml` | Explicit `<option>` dropdowns; null-safe preview; antiforgery on logo forms |
| `AtoZClinical.Web/Pages/Settings/ClinicProfile.cshtml.cs` | Logging, safe fallbacks, refactored load/save/upload handlers |
| `AtoZClinical.Infrastructure/Services/ClinicProfileService.cs` | Logging, null guards, normalization |
| `AtoZClinical.Infrastructure/Services/ClinicBrandingHelper.cs` | **New** — shared branding normalization |
| `AtoZClinical.Web/Filters/ClinicBrandingPageFilter.cs` | Logging + safe color fallback |
| `AtoZClinical.Web/Pages/Shared/_Layout.cshtml` | Normalize `--clinic-primary` CSS variable |
| `AtoZClinical.Infrastructure/Data/DatabaseInitializer.cs` | `EnsureClinicBrandingSchemaAsync` |
| `AtoZClinical.Web/AppBuildInfo.cs` | `2026.06.30-r178` |
| `AtoZClinical.Tests/ClinicProfileTests.cs` | **New** — E2E coverage |
| `AtoZClinical.Tests/ClinicBrandingHelperTests.cs` | **New** — unit tests |
| `AtoZClinical.Tests/MigrationValidationTests.cs` | Branding column probe |
| `AtoZClinical.Tests/Helpers/ClinicalAuthTestHelper.cs` | `GetClinicAdminClinicIdAsync` helper |

---

## 6. Verification Results

| Check | Result |
|-------|--------|
| Release build | **0 errors** |
| Full test suite | **150 passed**, 6 skipped, **0 failed** |
| Clinic Profile GET | **OK** — no "Something went wrong" |
| Save primary color `#ff5500` | **OK** — persisted to DB |
| Logo upload (1×1 PNG) | **OK** — `data:image/png;base64,...` stored |
| Branding on next request | **OK** — `--clinic-primary: #00aa66` in dashboard HTML |
| Logo remove | Covered by service + handler try/catch |
| Exceptions during tests | **None** |

---

## 7. Deploy Checklist

1. Merge `feature/performance-security-hardening` → `main`
2. Wait for Render auto-deploy (~1–2 min)
3. Confirm build label shows **`2026.06.30-r178`** in navbar
4. Open `/Settings/ClinicProfile` — should load form
5. Save a color + upload a small logo — refresh dashboard to confirm branding

---

*Generated after full Clinic Profile investigation and fix verification.*
