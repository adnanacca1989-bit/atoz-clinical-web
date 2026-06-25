# Optional local OWASP ZAP baseline against a running instance (http://localhost:5034)
param(
    [string]$TargetUrl = "http://localhost:5034"
)

Write-Host "OWASP ZAP baseline scan against $TargetUrl"
docker run --rm -v "${PWD}:/zap/wrk:rw" `
  -t ghcr.io/zaproxy/zaproxy:stable zap-baseline.py `
  -t $TargetUrl `
  -r zap-report.html

Write-Host "Report written to zap-report.html"
