# Agent diagnostic scripts

PowerShell helpers for multi-agent workflow on first-mud. Run from the repo root (or worktree root).

## Pre-commit discipline (process rule, not a hook)

Every worktree agent commits before returning. The worktree branch *is* the commit authorization — no extra user consent needed. This is enforced by the agent-briefing template (`docs/workflow/agent-briefing-template.md`), not by a git hook. A hook would punish the common case and block merge agents. If you find yourself about to return with uncommitted changes on a `worktree-agent-*` branch, you have violated the rule; commit them.

## `check-hmr.ps1`

Verify a client file actually hot-reloaded in the running Docker dev container.

```powershell
scripts/agent/check-hmr.ps1 WorldMap.tsx
```

Sample output:
```
HMR update found for WorldMap.tsx :
  2026-04-13T14:02:11 [vite] (client) hmr update /src/components/WorldMap.tsx
```
Exit 0 on hit, 1 on miss, 2 on environment error (docker missing, container not running).

**Gotcha:** the dev container mounts main's `src/FirstMud.Client` by default. A worktree change will not appear here until merged, unless the operator rebinds the mount. If the script exits 1, that may be the cause — not a broken fix.

## `ledger-lint.ps1`

Lint `docs/workflow/active-branches.md`.

```powershell
scripts/agent/ledger-lint.ps1
```

Sample output:
```
Ledger: docs/workflow/active-branches.md
Rows: 1
Warnings: 0
Errors: 0
```
Exit 0 if clean, 1 if any errors (duplicate branch, bad status, malformed row). Warns on stale in-progress rows (> 24h) and superseded rows that merge agents forgot to drop.

Pass `-ExpectEmpty` after a merge to assert that the incoming branch's row was cleaned up in the merge commit. Exits 1 if any rows survive.

```powershell
scripts/agent/ledger-lint.ps1 -ExpectEmpty
```

## `worktree-health.ps1`

Audit all git worktrees.

```powershell
scripts/agent/worktree-health.ps1
```

Sample output:
```
Git worktrees: 4
-- content-layer-pilot
   path: C:/claude/matt/personal/first-mud/first-mud
   uncommitted: clean
-- worktree-agent-acaf56f7
   path: .../worktrees/agent-acaf56f7
   uncommitted: clean
   vs content-layer-pilot: +1 / -0
```

Reports: orphan dirs (on disk, not in git), uncommitted working trees, divergence from main. `-DivergenceThreshold N` flags branches more than N commits ahead.

Orphan classification: the script tags the running agent's own cwd (and its parents under `.claude/worktrees/`) as `LOCKED-CWD (expected)` — these directories are OS-locked while the agent is alive and cannot be removed. Everything else under `.claude/worktrees/` that isn't in git's worktree list is tagged `ORPHAN (stale)` and should be cleaned up.

## `deploy-worktree.ps1`

Copy a single file from a worktree into main's working tree so Docker HMR (which mounts main) picks it up. Used when a client-side fix needs visual verification **before** merge.

```powershell
scripts/agent/deploy-worktree.ps1 agent-a1fc25ed src/FirstMud.Client/src/components/WorldMap.tsx
```

Dirties main's working tree. If you are still iterating in the worktree, run `git restore <file>` in main afterward so main doesn't accumulate ad-hoc copies. The script invokes `check-hmr.ps1` automatically to surface the reload log line.
