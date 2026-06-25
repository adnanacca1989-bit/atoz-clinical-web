# Quarterly DR restore drill — downloads latest S3 backup and restores to a target PostgreSQL database.
# Usage:
#   .\scripts\dr-restore-drill.ps1 -DatabaseUrl "postgresql://..." -S3Bucket "my-backups"
#
# NEVER point this at production without a maintenance window.

param(
    [Parameter(Mandatory = $true)]
    [string]$DatabaseUrl,
    [Parameter(Mandatory = $true)]
    [string]$S3Bucket,
    [string]$S3Prefix = "postgres/",
    [switch]$SkipRestore
)

$ErrorActionPreference = "Stop"

Write-Host "Listing latest backup in s3://$S3Bucket/$S3Prefix ..."
$latest = aws s3 ls "s3://$S3Bucket/$S3Prefix" | Sort-Object | Select-Object -Last 1
if (-not $latest) { throw "No backups found in s3://$S3Bucket/$S3Prefix" }

$fileName = ($latest -split '\s+')[-1]
$s3Key = "$S3Prefix$fileName"
$localGz = Join-Path $env:TEMP $fileName
$localSql = $localGz -replace '\.gz$',''

Write-Host "Downloading $s3Key ..."
aws s3 cp "s3://$S3Bucket/$s3Key" $localGz
Write-Host "Decompressing ..."
& gzip -dk $localGz 2>$null
if (-not (Test-Path $localSql)) {
    # Windows may not have gzip - use .NET
    $inStream = [System.IO.File]::OpenRead($localGz)
    $gzip = New-Object System.IO.Compression.GzipStream($inStream, [System.IO.Compression.CompressionMode]::Decompress)
    $outStream = [System.IO.File]::Create($localSql)
    $gzip.CopyTo($outStream)
    $outStream.Close(); $gzip.Close(); $inStream.Close()
}

if ($SkipRestore) {
    Write-Host "SkipRestore set. SQL file at $localSql"
    exit 0
}

if (-not (Get-Command psql -ErrorAction SilentlyContinue)) {
    throw "psql not found. Install PostgreSQL client tools or restore manually with docs/DR-RUNBOOK.md"
}

Write-Host "Restoring to target database (drill) ..."
$env:PGPASSWORD = $null
psql $DatabaseUrl -c "DROP SCHEMA public CASCADE; CREATE SCHEMA public;"
psql $DatabaseUrl -f $localSql

Write-Host "Restore drill complete. Verify clinics/patients counts:"
psql $DatabaseUrl -c "SELECT COUNT(*) AS clinics FROM \"Clinics\"; SELECT COUNT(*) AS patients FROM \"Patients\";"
