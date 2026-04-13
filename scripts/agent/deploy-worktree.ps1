# deploy-worktree.ps1
# Copies a file from a worktree into main's working tree so that Docker's HMR
# (which mounts src/FirstMud.Client from main, not from worktrees) can pick it up.
#
# WARNING: this dirties main's working tree. If you are still iterating on the
# fix in the worktree, run `git restore <file>` in main before the next merge
# so main doesn't accumulate ad-hoc copies.
#
# Usage:
#   scripts/agent/deploy-worktree.ps1 <worktree-path-or-id> <file-relative-to-repo-root>
#
# Examples:
#   scripts/agent/deploy-worktree.ps1 agent-a1fc25ed src/FirstMud.Client/src/components/WorldMap.tsx
#   scripts/agent/deploy-worktree.ps1 .claude/worktrees/agent-a1fc25ed src/FirstMud.Client/src/App.tsx

param(
    [Parameter(Mandatory = $true)][string]$Worktree,
    [Parameter(Mandatory = $true)][string]$File
)

$ErrorActionPreference = 'Stop'

$repoRoot = (git rev-parse --show-toplevel) 2>$null
if (-not $repoRoot) { Write-Error 'not in a git repo'; exit 2 }

# The "main" checkout is the primary worktree (where HMR mounts from).
# `git worktree list --porcelain` first entry is the main checkout.
$mainCheckout = $null
$raw = git worktree list --porcelain
foreach ($line in $raw) {
    if ($line -match '^worktree (.+)$') { $mainCheckout = $matches[1]; break }
}
if (-not $mainCheckout) { Write-Error 'could not determine main checkout'; exit 2 }

# Resolve worktree path: accept either "agent-xxxx", a repo-relative path, or absolute
$wtPath = $null
if (Test-Path $Worktree) {
    $wtPath = (Resolve-Path $Worktree).Path
}
else {
    $candidate = Join-Path $mainCheckout ".claude/worktrees/$Worktree"
    if (Test-Path $candidate) { $wtPath = (Resolve-Path $candidate).Path }
}
if (-not $wtPath) { Write-Error "worktree not found: $Worktree"; exit 2 }

$src = Join-Path $wtPath $File
if (-not (Test-Path $src)) { Write-Error "source file not found: $src"; exit 2 }

$dst = Join-Path $mainCheckout $File
$dstDir = Split-Path $dst -Parent
if (-not (Test-Path $dstDir)) { Write-Error "destination dir missing in main: $dstDir"; exit 2 }

Write-Host "deploy-worktree: copying"
Write-Host "  from: $src"
Write-Host "  to:   $dst"
Copy-Item -Force -Path $src -Destination $dst

Write-Host ""
Write-Host "Deployed. Dirtied main's working tree — run 'git restore $File' in main"
Write-Host "when you are done iterating so main doesn't accumulate ad-hoc copies."
Write-Host ""

# Trigger / surface HMR: delegate to check-hmr.ps1 if present
$checkHmr = Join-Path $mainCheckout 'scripts/agent/check-hmr.ps1'
if (Test-Path $checkHmr) {
    Write-Host "Invoking check-hmr.ps1 to surface the reload log line..."
    & pwsh -NoProfile -File $checkHmr $File
}
else {
    Write-Host "(check-hmr.ps1 not found; skipping HMR log capture)"
}

exit 0
