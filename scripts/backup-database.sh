#!/bin/sh
# Daily PostgreSQL backup to S3 (Render cron job).
# Required: DATABASE_URL, BACKUP_S3_BUCKET, AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY
# Optional: AWS_DEFAULT_REGION (default us-west-2)
# Configure S3 lifecycle rules on the bucket to expire objects after 30 days.

set -eu

if [ -z "${DATABASE_URL:-}" ]; then
  echo "ERROR: DATABASE_URL is not set." >&2
  exit 1
fi

if [ -z "${BACKUP_S3_BUCKET:-}" ]; then
  echo "ERROR: BACKUP_S3_BUCKET is not set." >&2
  exit 1
fi

if [ -z "${AWS_ACCESS_KEY_ID:-}" ] || [ -z "${AWS_SECRET_ACCESS_KEY:-}" ]; then
  echo "ERROR: AWS credentials are not set." >&2
  exit 1
fi

export AWS_DEFAULT_REGION="${AWS_DEFAULT_REGION:-us-west-2}"
TIMESTAMP="$(date -u +%Y%m%d_%H%M%S)"
FILENAME="atoz_clinical_${TIMESTAMP}.sql.gz"
TMPFILE="/tmp/${FILENAME}"

echo "Starting backup at ${TIMESTAMP} UTC..."

pg_dump "${DATABASE_URL}" | gzip -9 > "${TMPFILE}"
BYTES="$(wc -c < "${TMPFILE}" | tr -d ' ')"
echo "Dump size: ${BYTES} bytes"

S3_KEY="postgres/${FILENAME}"
aws s3 cp "${TMPFILE}" "s3://${BACKUP_S3_BUCKET}/${S3_KEY}" --only-show-errors
rm -f "${TMPFILE}"

echo "Backup uploaded to s3://${BACKUP_S3_BUCKET}/${S3_KEY}"
echo "Done. Use S3 lifecycle policy to prune backups older than 30 days."
