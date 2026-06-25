# Enterprise features (Phase 5)

This document covers MFA, SSO, custom subdomains, patient portal, API/webhooks, and optional read-replica reporting.

## Multi-factor authentication (MFA)

- Clinic admins and vendor accounts can enable TOTP at **Settings → Two-factor auth**.
- Set `Security__RequireMfaForAdmins=true` in production to require MFA before using the app.
- Recovery codes are shown once when MFA is first enabled.

## Single sign-on (SSO)

Configure OAuth apps and set environment variables:

| Provider | Variables |
|----------|-----------|
| Google | `Authentication__Google__ClientId`, `Authentication__Google__ClientSecret` |
| Microsoft | `Authentication__Microsoft__ClientId`, `Authentication__Microsoft__ClientSecret` |

SSO only signs in **existing** users matched by email. External login is linked on first use.

Redirect URI: `https://<your-host>/Account/ExternalLogin?handler=Callback`

## Custom subdomain per clinic

1. Set `Security__BaseDomain` (e.g. `atoz-clinical.onrender.com`).
2. In **Admin → Enterprise**, assign a subdomain slug per clinic (e.g. `acme`).
3. Point a wildcard DNS record `*.yourapp.com` to your load balancer / Render service.
4. Patients can use `https://acme.yourapp.com/Portal/Login` without entering a clinic code.

## Patient portal

1. Enable **Patient portal** under **Admin → Enterprise**.
2. Patients sign in at `/Portal/Login` with:
   - Patient / barcode number
   - Date of birth
   - Last 4 digits of phone on file
3. Portal shows upcoming appointments, recent prescriptions, and bills (read-only).

## REST API

Create API keys under **Admin → Integrations**. Send the key as:

- Header `X-Api-Key: atz_...`, or
- `Authorization: Bearer atz_...`

| Endpoint | Description |
|----------|-------------|
| `GET /api/v1/patients?search=` | List patients (max 100) |
| `GET /api/v1/patients/{id}` | Patient detail |
| `POST /api/v1/patients` | Create patient |
| `GET /api/v1/doctors` | List doctors |
| `GET /api/v1/appointments?from=&to=` | Appointments in date range |
| `POST /api/v1/appointments` | Create appointment |
| `GET /api/v1/invoices?from=&to=` | Invoices in date range |
| `GET /api/v1/invoices/{id}` | Invoice detail |
| `GET /api/v1/lab-results?patient=` | Lab results (optional name filter) |

## Patient portal booking

Patients signed in to the portal can use **Book appointment** to request a visit (date, time, optional doctor, reason). Requests create a scheduled appointment and fire the `appointment.created` webhook.

## Outbound webhooks

Add webhook URLs under **Admin → Integrations**. Supported events:

- `patient.created`, `patient.updated`, `appointment.created`

Payloads are JSON POSTs with `X-AtoZ-Event` and `X-AtoZ-Signature` (HMAC-SHA256 of body using the subscription secret).

## Read replica (reporting)

Set `ConnectionStrings__ReportingDatabase` to a PostgreSQL read replica. When configured, these services query the replica (read-only, no tracking):

- Dashboard aggregates
- Doctor report
- Vendor analytics

Primary database remains used for all writes and when no replica is configured.

## Dedicated database (enterprise)

`Clinic.DedicatedConnectionName` is reserved for largest tenants requiring an isolated database. Provisioning is manual — contact your platform operator.
