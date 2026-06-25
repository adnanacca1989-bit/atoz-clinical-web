#Requires -Version 5.1
<#
.SYNOPSIS
  Creates a GitHub Project board from .github/roadmap/items.json

.PARAMETER Repo
  GitHub repository owner/name. Defaults to GITHUB_REPO or git remote origin.

.PARAMETER Owner
  Project owner (user or org). Defaults to repo owner.

.EXAMPLE
  $env:GITHUB_REPO = "myorg/AtoZClinicalWeb"
  gh auth login
  .\scripts\setup-github-roadmap-project.ps1
#>
[CmdletBinding()]
param(
    [string] $Repo = $env:GITHUB_REPO,
    [string] $Owner,
    [switch] $DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Require-Gh {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw "Install GitHub CLI from https://cli.github.com/ then run: gh auth login"
    }
    if (-not $DryRun) {
        gh auth status 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Run: gh auth login" }
    }
}

function Resolve-Repo([string] $Value) {
    if ($Value) { return $Value }
    if (Get-Command git -ErrorAction SilentlyContinue) {
        $remote = git remote get-url origin 2>$null
        if ($remote -match 'github\.com[:/](?<owner>[^/]+)/(?<name>[^/.]+)') {
            return "$($Matches.owner)/$($Matches.name)"
        }
    }
    throw "Set -Repo owner/name or GITHUB_REPO."
}

function Run-Gh([string[]] $Args) {
    if ($DryRun) {
        Write-Host "[dry-run] gh $($Args -join ' ')"
        return $null
    }
    $out = gh @Args 2>&1
    if ($LASTEXITCODE -ne 0) { throw $out }
    return $out
}

function Label-Color([string] $Name) {
    switch -Regex ($Name) {
        '^critical$' { 'd73a4a' }
        '^important$' { 'fbca04' }
        '^future$' { '0e8a16' }
        '^security$' { 'b60205' }
        '^compliance$' { '5319e7' }
        default { 'ededed' }
    }
}

function Map-Effort([string] $Effort) {
    if ($Effort -match 'day') { return 'XS' }
    if ($Effort -match '^1') { return 'S' }
    if ($Effort -match '^2') { return 'M' }
    if ($Effort -match '4|5|6|8') { return 'L' }
    if ($Effort -match '12|Very') { return 'XL' }
    if ($Effort -match 'Ongoing') { return 'Ongoing' }
    return 'M'
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$itemsPath = Join-Path (Split-Path -Parent $scriptRoot) ".github\roadmap\items.json"
if (-not (Test-Path $itemsPath)) { throw "Missing $itemsPath" }

Require-Gh
$Repo = Resolve-Repo $Repo
$repoOwner = ($Repo -split '/', 2)[0]
if (-not $Owner) { $Owner = $repoOwner }

$roadmap = Get-Content $itemsPath -Raw | ConvertFrom-Json
Write-Host "Roadmap setup for $Repo"

foreach ($label in $roadmap.labels) {
    $color = Label-Color $label
    Run-Gh @('label', 'create', $label, '--repo', $Repo, '--color', $color, '--force') | Out-Null
}

$milestoneNumbers = @{}
foreach ($ms in $roadmap.milestones) {
    if ($DryRun) {
        $milestoneNumbers[$ms.key] = 0
        continue
    }
    $existing = gh api "repos/$Repo/milestones" --jq ".[] | select(.title==`"$($ms.title)`") | .number" 2>$null | Select-Object -First 1
    if ($existing) {
        $milestoneNumbers[$ms.key] = [int]$existing
    } else {
        $created = gh api "repos/$Repo/milestones" -f "title=$($ms.title)" -f "description=$($ms.description)" | ConvertFrom-Json
        $milestoneNumbers[$ms.key] = [int]$created.number
    }
    Write-Host "Milestone: $($ms.title) (#$($milestoneNumbers[$ms.key]))"
}

if ($DryRun) {
    $projectNumber = 1
    $projectId = 'DRY_RUN'
} else {
    $project = Run-Gh @('project', 'create', '--owner', $Owner, '--title', $roadmap.projectTitle, '--format', 'json') | ConvertFrom-Json
    $projectNumber = [int]$project.number
    $projectId = $project.id
    Write-Host "Created project #$projectNumber"
}

function New-SelectField([string] $Name, [string[]] $Options) {
    if ($DryRun) {
        Write-Host "[dry-run] field $Name`: $($Options -join ',')"
        return
    }
    Run-Gh @(
        'project', 'field-create', "$projectNumber", '--owner', $Owner,
        '--name', $Name, '--data-type', 'SINGLE_SELECT',
        '--single-select-options', ($Options -join ',')
    ) | Out-Null
}

New-SelectField 'Priority' @('Critical', 'Important', 'Future')
New-SelectField 'Status' @('Todo', 'In Progress', 'Done')
New-SelectField 'Effort' @('XS', 'S', 'M', 'L', 'XL', 'Ongoing')

function Get-FieldId([string] $FieldName) {
    if ($DryRun) { return "FIELD_$FieldName" }
    $json = Run-Gh @('project', 'field-list', "$projectNumber", '--owner', $Owner, '--format', 'json') | ConvertFrom-Json
    return ($json.fields | Where-Object { $_.name -eq $FieldName } | Select-Object -First 1).id
}

function Get-OptionId([string] $FieldName, [string] $OptionName) {
    if ($DryRun) { return "OPT_${FieldName}_$OptionName" }
    $json = Run-Gh @('project', 'field-list', "$projectNumber", '--owner', $Owner, '--format', 'json') | ConvertFrom-Json
    $field = $json.fields | Where-Object { $_.name -eq $FieldName } | Select-Object -First 1
    return ($field.options | Where-Object { $_.name -eq $OptionName } | Select-Object -First 1).id
}

$priorityFieldId = Get-FieldId 'Priority'
$statusFieldId = Get-FieldId 'Status'
$effortFieldId = Get-FieldId 'Effort'

$issueCount = 0
foreach ($item in $roadmap.items) {
    $body = @"
## Roadmap item $($item.id)

| Field | Value |
|-------|-------|
| **Priority** | $($item.priority) |
| **Business value** | $($item.businessValue) |
| **Complexity** | $($item.complexity) |
| **Effort** | $($item.effort) |

---
_Auto-generated from `.github/roadmap/items.json`._
"@

    if ($DryRun) {
        Write-Host "[dry-run] issue: [$($item.id)] $($item.title)"
        continue
    }

    $labelArgs = @()
    foreach ($l in $item.labels) { $labelArgs += '--label', $l }

    $issueUrl = Run-Gh @(
        'issue', 'create', '--repo', $Repo,
        '--title', "[$($item.id)] $($item.title)",
        '--body', $body,
        '--milestone', "$($milestoneNumbers[$item.milestone])"
    ) + $labelArgs

    $projectItem = Run-Gh @('project', 'item-add', "$projectNumber", '--owner', $Owner, '--url', $issueUrl.Trim(), '--format', 'json') | ConvertFrom-Json

    $updates = @(
        @{ field = $priorityFieldId; option = (Get-OptionId 'Priority' $item.priority) },
        @{ field = $statusFieldId; option = (Get-OptionId 'Status' 'Todo') },
        @{ field = $effortFieldId; option = (Get-OptionId 'Effort' (Map-Effort $item.effort)) }
    )

    foreach ($u in $updates) {
        if (-not $u.option) { continue }
        Run-Gh @(
            'project', 'item-edit', '--id', $projectItem.id, '--project-id', $projectId,
            '--field-id', $u.field, '--single-select-option-id', $u.option
        ) | Out-Null
    }

    Write-Host "Issue: $($issueUrl.Trim())"
    $issueCount++
}

Write-Host ""
Write-Host "Created $issueCount issues."
Write-Host "Open board: gh project view $projectNumber --owner $Owner --web"
Write-Host "Tip: Add a Board view grouped by Priority in the project settings."
