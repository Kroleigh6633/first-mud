# Active branches ledger

Every agent updates this file. One row per in-flight branch. Remove rows when merged into main.

## Format

| branch | status | base | summary | supersedes | files | updated |
|---|---|---|---|---|---|---|

Status vocabulary:
- `in-progress` — agent actively working
- `ready-to-merge` — commits pushed, awaits merge agent
- `superseded by <branch>` — work was subsumed by another branch; safe to drop
- `merging` — a merge agent has claimed this row
- `blocked: <reason>` — needs human or upstream work

Columns:
- **branch** — git branch name (e.g. `worktree-agent-acaf56f7`)
- **status** — from vocabulary above
- **base** — commit sha or branch the work started from
- **summary** — one line, what this change does
- **supersedes** — comma-separated branch names this replaces, or `-`
- **files** — comma-separated top-level paths touched (truncate if many)
- **updated** — ISO date (YYYY-MM-DD) of last row edit

## Rows

| branch | status | base | summary | supersedes | files | updated |
|---|---|---|---|---|---|---|

| worktree-agent-a9c4d378 | ready-to-merge | main | Danger-6 TPK fix: small-party pack cap, pre-combat win-rate gate, fix double-scaled survivability check | - | src/FirstMud.GameServer/Services/MonsterFactory.cs, src/FirstMud.GameServer/Handlers/MoveCommandHandler.cs, tests/FirstMud.Tests/MechanicsSweepTests.cs | 2026-04-13 |

<!-- agents add rows above this line -->
