param(
    [string]$BaseUrl = "https://atoz-clinical.onrender.com",
    [int]$ConcurrentUsers = 100,
    [int]$DurationSeconds = 60
)

$endpoints = @("/health", "/Account/Login", "/Portal/Login")
$results = New-Object System.Collections.Generic.List[object]
$endTime = (Get-Date).AddSeconds($DurationSeconds)
$swTotal = [System.Diagnostics.Stopwatch]::StartNew()

Write-Host "Load test: $ConcurrentUsers workers for ${DurationSeconds}s -> $BaseUrl"

$block = {
    param($BaseUrl, $Endpoints, $EndTime, $WorkerId)
    $rng = [System.Random]::new($WorkerId)
    $local = [System.Collections.Generic.List[object]]::new()
    while ((Get-Date) -lt $EndTime) {
        $path = $Endpoints[$rng.Next($Endpoints.Length)]
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        try {
            $resp = Invoke-WebRequest -Uri "$BaseUrl$path" -UseBasicParsing -TimeoutSec 30
            $sw.Stop()
            $local.Add([pscustomobject]@{ Path = $path; Status = [int]$resp.StatusCode; Ms = $sw.ElapsedMilliseconds })
        }
        catch {
            $sw.Stop()
            $status = 0
            if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
            $local.Add([pscustomobject]@{ Path = $path; Status = $status; Ms = $sw.ElapsedMilliseconds })
        }
        Start-Sleep -Milliseconds (50 + $rng.Next(150))
    }
    return $local
}

$jobs = for ($i = 1; $i -le $ConcurrentUsers; $i++) {
    Start-Job -ScriptBlock $block -ArgumentList $BaseUrl, $endpoints, $endTime, $i
}

$jobs | Wait-Job -Timeout ($DurationSeconds + 60) | Out-Null
foreach ($job in $jobs) {
    if ($job.State -eq 'Completed') {
        $results.AddRange([object[]](Receive-Job $job))
    }
    Remove-Job $job -Force -ErrorAction SilentlyContinue
}

$flat = $results
$total = $flat.Count
$errors = @($flat | Where-Object { $_.Status -ge 500 -or $_.Status -eq 0 })
$durations = @($flat | ForEach-Object { [double]$_.Ms } | Sort-Object)
$p95Index = if ($durations.Count -gt 0) { [Math]::Min($durations.Count - 1, [Math]::Ceiling($durations.Count * 0.95) - 1) } else { 0 }

[pscustomobject]@{
    Scenario = "powershell-load-$ConcurrentUsers-vus"
    BaseUrl = $BaseUrl
    DurationSeconds = $DurationSeconds
    TotalRequests = $total
    SuccessCount = $total - $errors.Count
    ErrorCount = $errors.Count
    ErrorRate = if ($total -gt 0) { [Math]::Round($errors.Count / $total, 4) } else { 0 }
    AvgMs = if ($durations.Count -gt 0) { [Math]::Round(($durations | Measure-Object -Average).Average, 1) } else { 0 }
    P95Ms = if ($durations.Count -gt 0) { $durations[$p95Index] } else { 0 }
    MaxMs = if ($durations.Count -gt 0) { ($durations | Measure-Object -Maximum).Maximum } else { 0 }
    ElapsedSeconds = [Math]::Round($swTotal.Elapsed.TotalSeconds, 1)
} | ConvertTo-Json -Depth 4
