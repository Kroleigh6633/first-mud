# ledger-lint.ps1
# Parses docs/workflow/active-branches.md and reports structural problems.
# Usage: scripts/agent/ledger-lint.ps1
# Exit 0 if clean; exit 1 if any errors found.

param(
    [string]$Path = 'docs/workflow/active-branches.md',
    [int]$StaleDays = 1,
    [switch]$ExpectEmpty
)

if (-not (Test-Path $Path)) {
    Write-Error "Ledger not found at $Path"
    exit 2
}

$requiredCols = @('branch', 'status', 'base', 'summary', 'supersedes', 'files', 'updated')
$lines = Get-Content $Path
$today = Get-Date

$rows = @()
$inRowsSection = $false
foreach ($line in $lines) {
    if ($line -match '^\s*## Rows') { $inRowsSection = $true; continue }
    if (-not $inRowsSection) { continue }
    if ($line -notmatch '^\s*\|') { continue }
    # skip header + separator
    if ($line -match '\|\s*branch\s*\|') { continue }
    if ($line -match '^\s*\|\s*-+\s*\|') { continue }

    $cells = ($line -split '\|') | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' }
    if ($cells.Count -eq 0) { continue }
    $rows += , $cells
}

$errors = @()
$warnings = @()
$validCount = 0
$branchesSeen = @{}

for ($i = 0; $i -lt $rows.Count; $i++) {
    $r = $rows[$i]
    if ($r.Count -ne $requiredCols.Count) {
        $errors += "row $($i+1): expected $($requiredCols.Count) columns, got $($r.Count): $($r -join ' | ')"
        continue
    }
    $branch = $r[0]; $status = $r[1]; $updated = $r[6]

    if ($branchesSeen.ContainsKey($branch)) {
        $errors += "duplicate branch: $branch"
    }
    $branchesSeen[$branch] = $true

    $parsed = $null
    try {
        $parsed = [DateTime]::Parse($updated)
    } catch {
        $parsed = $null
    }
    if (-not $parsed) {
        $errors += "row $branch : invalid date '$updated'"
    }
    else {
        $ageHours = ($today - $parsed).TotalHours
        if ($ageHours -gt ($StaleDays * 24) -and $status -eq 'in-progress') {
            $warnings += "row $branch : stale in-progress ($([int]$ageHours)h old)"
        }
    }

    if ($status -match '^superseded by ') {
        $warnings += "row $branch : superseded but not yet cleaned up (merge agent should drop it)"
    }
    elseif ($status -notmatch '^(in-progress|ready-to-merge|merging|blocked:|superseded)') {
        $errors += "row $branch : unknown status '$status'"
    }

    $validCount++
}

Write-Host "Ledger: $Path"
Write-Host "Rows: $validCount"
Write-Host "Warnings: $($warnings.Count)"
foreach ($w in $warnings) { Write-Host "  WARN  $w" }
Write-Host "Errors: $($errors.Count)"
foreach ($e in $errors) { Write-Host "  ERR   $e" }

if ($ExpectEmpty) {
    if ($validCount -gt 0) {
        Write-Host ""
        Write-Host "ExpectEmpty: FAIL — $validCount row(s) still present (expected 0)."
        foreach ($b in $branchesSeen.Keys) { Write-Host "  present: $b" }
        exit 1
    }
    else {
        Write-Host ""
        Write-Host "ExpectEmpty: OK — ledger has 0 rows."
    }
}

if ($errors.Count -gt 0) { exit 1 } else { exit 0 }
