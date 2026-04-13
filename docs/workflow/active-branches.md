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
| worktree-agent-a8f77995 | ready-to-merge | 9c5e5b9 | Migrate progression scaling (workmanship divisor + companion layer thresholds) into content/progression-curves.json + apply Pass-1 tunes + bump enchanting drop weights | - | content/progression-curves.json, content/schemas/progression-curves.schema.json, content/loot-tables.json, src/FirstMud.Domain/Configuration/, src/FirstMud.Domain/Entities/Companion.cs, src/FirstMud.Domain/ValueObjects/Workmanship.cs, src/FirstMud.Application/Content/, src/FirstMud.Application/Services/CraftingService.cs, tools/design/FirstMud.DesignTools/Tools/ProgressionSim/ProgressionSimulator.cs, tests/, docs/design/sim-reports/progression-pass-tuned.md | 2026-04-13 |
| worktree-agent-ae337e6a | ready-to-merge | main | Apply Tune A (6 low-tier recipes: min 1→3, max 4→5) + Tune B (3 iron recipes: min 3→4, max→8) from progression-tune-proposal-v1; sim-verified no regressions, combat-heavy+enchanting gear floor 2→3 | - | content/recipes.json, docs/design/sim-reports/progression-pass-final.md | 2026-04-13 |

<!-- agents add rows above this line -->
