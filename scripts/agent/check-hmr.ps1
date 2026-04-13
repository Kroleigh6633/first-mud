# check-hmr.ps1
# Greps the firstmud-client container log for a recent Vite HMR update matching a filename.
# Usage: scripts/agent/check-hmr.ps1 WorldMap.tsx
# Exit 0 if a matching HMR update is present in the last 40 log lines; exit 1 otherwise.

param(
    [Parameter(Mandatory = $true)]
    [string]$File
)

$container = 'firstmud-client'

# Ensure docker is available
$docker = Get-Command docker -ErrorAction SilentlyContinue
if (-not $docker) {
    Write-Error "docker not found on PATH"
    exit 2
}

# Ensure container is running
$running = docker ps --filter "name=$container" --format '{{.Names}}' 2>$null
if (-not $running) {
    Write-Error "container '$container' is not running"
    exit 2
}

$logs = docker logs $container --tail 40 2>&1
$pattern = [regex]::Escape($File)
$hits = $logs | Select-String -Pattern "\[vite\] \(client\) hmr update" | Select-String -Pattern $pattern

if ($hits) {
    Write-Host "HMR update found for $File :"
    $hits | ForEach-Object { Write-Host "  $_" }
    exit 0
}
else {
    Write-Host "No recent HMR update for $File in last 40 log lines of $container."
    Write-Host "Full tail:"
    $logs | ForEach-Object { Write-Host "  $_" }
    exit 1
}
