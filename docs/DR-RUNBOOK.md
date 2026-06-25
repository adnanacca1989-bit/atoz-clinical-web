# Disaster Recovery Runbook — AtoZ Clinical Web

Last updated: Phase 4 implementation.

## Objectives

- **RPO** (Recovery Point Objective): 24 hours (daily S3 backups)
- **RTO** (Recovery Time Objective): 4 hours for full database restore on Render

## Backup sources

| Source | Schedule | Location |
|--------|----------|----------|
| Automated PostgreSQL dump | Daily 03:00 UTC | S3 `postgres/atoz_clinical_YYYYMMDD_HHMMSS.sql.gz` |
| Clinic admin export | On demand | Admin → Data Backup (ZIP/Excel) |

Configure on Render cron job `atoz-clinical-db-backup` with `BACKUP_S3_BUCKET` and AWS credentials.

## Quarterly restore drill (required)

1. Pick the latest backup key from S3: `postgres/atoz_clinical_*.sql.gz`
2. Run `scripts/dr-restore-drill.ps1` against a **non-production** database
3. Verify row counts for `Clinics`, `Patients`, `Invoices`
4. Log drill date, backup file used, and pass/fail in your ops journal

## Full database restore (production incident)

### Prerequisites

- AWS CLI with read access to backup bucket
- Render PostgreSQL connection string for target database
- Maintenance window announced to customers

### Steps

1. **Stop web traffic** — suspend Render web service or enable maintenance page
2. **Download backup**
   ```bash
   aws s3 cp s3://YOUR_BUCKET/postgres/atoz_clinical_YYYYMMDD_HHMMSS.sql.gz ./restore.sql.gz
   gunzip restore.sql.gz
   ```
3. **Restore** (empty target DB recommended)
   ```bash
   psql "$DATABASE_URL" -c "DROP SCHEMA public CASCADE; CREATE SCHEMA public;"
   psql "$DATABASE_URL" -f restore.sql
   ```
4. **Run migrations** — deploy latest app; `DatabaseInitializer` runs `MigrateAsync()` on startup
5. **Smoke test** — `/health`, vendor login, one clinic login, patient list
6. **Resume** web service

## Application failure (no data loss)

1. Check Render logs (Serilog console output)
2. Hit `/health` with `X-Health-Token` header for DB + error metrics
3. Roll back to previous deploy in Render dashboard
4. If migration failed, restore DB from latest backup then redeploy known-good build

## Alerting

- Configure `Operations__AlertWebhookUrl` (Slack incoming webhook) on production
- Alerts fire when server error rate exceeds threshold (default 5% after 20+ requests)
- Render email notifications for service health check failures

## Contacts

Document your on-call vendor admin and Render account owner here.
