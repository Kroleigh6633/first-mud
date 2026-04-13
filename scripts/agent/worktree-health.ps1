# worktree-health.ps1
# Reports health of all git worktrees: orphaned dirs, uncommitted changes, divergence from main.
# Usage: scripts/agent/worktree-health.ps1 [-DivergenceThreshold 20]

param(
    [int]$DivergenceThreshold = 20,
    [string]$MainRef = 'main'
)

$git = Get-Command git -ErrorAction SilentlyContinue
if (-not $git) { Write-Error 'git not on PATH'; exit 2 }

# Normalize main ref — first-mud's default may differ
$mainExists = git rev-parse --verify --quiet "$MainRef" 2>$null
if (-not $mainExists) {
    # fall back to content-layer-pilot (current default per repo state) if main missing
    $fallback = 'content-layer-pilot'
    if (git rev-parse --verify --quiet $fallback 2>$null) {
        Write-Host "Note: '$MainRef' not found, using '$fallback' as base"
        $MainRef = $fallback
    }
}

$raw = git worktree list --porcelain
$worktrees = @()
$current = @{}
foreach ($line in $raw) {
    if ($line -eq '') {
        if ($current.Count -gt 0) { $worktrees += $current; $current = @{} }
        continue
    }
    if ($line -match '^worktree (.+)$') { $current.path = $matches[1] }
    elseif ($line -match '^HEAD (.+)$')  { $current.head = $matches[1] }
    elseif ($line -match '^branch (.+)$') { $current.branch = ($matches[1] -replace '^refs/heads/', '') }
    elseif ($line -eq 'detached') { $current.branch = '(detached)' }
}
if ($current.Count -gt 0) { $worktrees += $current }

Write-Host "Git worktrees: $($worktrees.Count)"
Write-Host ""

foreach ($wt in $worktrees) {
    $path = $wt.path
    $branch = if ($wt.branch) { $wt.branch } else { '(detached)' }
    Write-Host "-- $branch"
    Write-Host "   path: $path"

    if (-not (Test-Path $path)) {
        Write-Host "   STATUS: ORPHANED (path missing on disk)"
        continue
    }

    Push-Location $path
    try {
        $dirty = git status --porcelain 2>$null
        if ($dirty) {
            $count = ($dirty -split "`n" | Where-Object { $_ -ne '' }).Count
            Write-Host "   uncommitted: $count file(s)"
        }
        else {
            Write-Host "   uncommitted: clean"
        }

        if ($wt.branch -and $wt.branch -ne $MainRef) {
            $ahead = (git rev-list --count "$MainRef..HEAD" 2>$null)
            $behind = (git rev-list --count "HEAD..$MainRef" 2>$null)
            if ($null -ne $ahead) {
                $marker = if ([int]$ahead -gt $DivergenceThreshold) { ' (HIGH)' } else { '' }
                Write-Host "   vs ${MainRef}: +$ahead / -$behind$marker"
            }
        }
    }
    finally {
        Pop-Location
    }
}

# Detect orphaned dirs: folders under .claude/worktrees/ not in git's worktree list.
# Distinguish the current process's cwd (OS-locks the directory, can't be removed
# while we're running inside it) from truly stale orphans left over from dead agents.
$worktreeRoot = Join-Path (git rev-parse --show-toplevel) '.claude/worktrees'
if (Test-Path $worktreeRoot) {
    $gitPaths = $worktrees | ForEach-Object { (Resolve-Path $_.path -ErrorAction SilentlyContinue).Path } | Where-Object { $_ }
    $gitPathsNorm = $gitPaths | ForEach-Object { $_.ToLower().TrimEnd('\', '/') }

    # Build the set of "expected-locked" paths: our own cwd and each ancestor that
    # sits under .claude/worktrees/. This covers the common case where the agent's
    # cwd IS one of the worktree dirs.
    $myCwd = (Get-Location).Path.ToLower().TrimEnd('\', '/')
    $lockedPaths = @()
    $p = $myCwd
    while ($p) {
        $lockedPaths += $p
        $parent = Split-Path $p -Parent
        if (-not $parent -or $parent -eq $p) { break }
        $p = $parent.ToLower().TrimEnd('\', '/')
    }

    $onDisk = Get-ChildItem $worktreeRoot -Directory -ErrorAction SilentlyContinue
    foreach ($d in $onDisk) {
        $norm = $d.FullName.ToLower().TrimEnd('\', '/')
        if ($gitPathsNorm -notcontains $norm) {
            if ($lockedPaths -contains $norm) {
                Write-Host "-- LOCKED-CWD (expected): $($d.FullName) (agent's own cwd, OS-locked — cannot be removed while running)"
            }
            else {
                Write-Host "-- ORPHAN (stale): $($d.FullName) (on disk, not in git worktree list)"
            }
        }
    }
}

exit 0
