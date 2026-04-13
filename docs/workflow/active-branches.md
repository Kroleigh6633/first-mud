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
| creative-pass-7 | ready-to-merge | content-layer-pilot | Creative pass #7: playbook balance sweep + combat-curves tune + playbook spec rewrites | - | content/combat-curves.json, tools/design/playbooks, docs/design, tests/FirstMud.Tests | 2026-04-13 |
| worktree-agent-a32711f5 | ready-to-merge | main | Auto-progression mode: design doc, benchmark playbook, scaffolded AutoProgressionService + tick handler + commands/parsers, sub-mode gap analysis, client command surface, 7 new tests (6 unit + 2 playbook smoke) | - | docs/design, src/FirstMud.Application, src/FirstMud.GameServer, src/FirstMud.Client, tools/design/playbooks, tests | 2026-04-13 |
<!-- agents add rows above this line -->
