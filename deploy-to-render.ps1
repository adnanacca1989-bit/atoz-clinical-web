# Run after signing into GitHub (gh auth login)
# Creates public repo and pushes code for Render Blueprint deploy.

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$git = "C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe"
$env:Path = [System.Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [System.Environment]::GetEnvironmentVariable('Path','User')

Write-Host "Checking GitHub login..." -ForegroundColor Cyan
gh auth status
if ($LASTEXITCODE -ne 0) {
    Write-Host "Run: gh auth login --hostname github.com --git-protocol https --web" -ForegroundColor Yellow
    exit 1
}

$repoName = "atoz-clinical-web"
Write-Host "Creating GitHub repo: $repoName" -ForegroundColor Cyan
gh repo create $repoName --public --source=. --remote=origin --push --description "A to Z Clinical Management System - SaaS multi-tenant"

Write-Host ""
Write-Host "Done! Next steps on Render:" -ForegroundColor Green
Write-Host "1. Open https://dashboard.render.com/select-repo?type=blueprint"
Write-Host "2. Connect GitHub and select repo: $repoName"
Write-Host "3. Click Apply — Render reads render.yaml automatically"
Write-Host "4. Your public URL will be: https://atoz-clinical.onrender.com"
Write-Host "5. Vendor login: vendor / (see Seed__VendorPassword in Render dashboard)"
