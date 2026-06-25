# Basic load test for AtoZ Clinical Web (health + login page).
# Usage: .\scripts\load-test.ps1 -BaseUrl http://localhost:5000 -Requests 100

param(
    [string]$BaseUrl = "http://localhost:5000",
    [int]$Requests = 50,
    [int]$Concurrency = 5
)

$ErrorActionPreference = "Stop"
$BaseUrl = $BaseUrl.TrimEnd("/")
$endpoints = @("/health", "/Account/Login")

$scriptBlock = {
    param($BaseUrl, $Path)
    $url = "$BaseUrl$Path"
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 30
        $sw.Stop()
        [pscustomobject]@{ Ok = ($response.StatusCode -lt 400); Ms = $sw.ElapsedMilliseconds; Path = $Path }
    }
    catch {
        $sw.Stop()
        [pscustomobject]@{ Ok = $false; Ms = $sw.ElapsedMilliseconds; Path = $Path; Error = $_.Exception.Message }
    }
}

Write-Host "Load test: $Requests requests, concurrency $Concurrency, base $BaseUrl"

$paths = 1..$Requests | ForEach-Object { $endpoints[($_ - 1) % $endpoints.Count] }
$results = $paths | ForEach-Object -Parallel {
    param($item, $base, $block)
    & $block $base $item
} -ThrottleLimit $Concurrency -ArgumentList $BaseUrl, $scriptBlock

$success = @($results | Where-Object Ok)
$failures = @($results | Where-Object { -not $_.Ok })
$avg = if ($success.Count -gt 0) { [math]::Round(($success | Measure-Object -Property Ms -Average).Average, 1) } else { 0 }

Write-Host ""
Write-Host "Completed: $($results.Count) | Success: $($success.Count) | Failures: $($failures.Count) | Avg latency: ${avg}ms"
foreach ($f in $failures) { Write-Warning "FAIL $($f.Path): $($f.Error)" }

if ($failures.Count -gt 0) { exit 1 }
