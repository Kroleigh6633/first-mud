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
| worktree-agent-a28b3adf | in-progress | content-layer-pilot | Auto-quest completion flow playbook + flow evaluator + AutoQuestSimulationService + ASHEN_001 startingZoneId tune | - | tools/design, content/quests.json, docs/design/sim-reports | 2026-04-13 |
| worktree-agent-a9a66dad | in-progress | content-layer-pilot | Auto-quest precondition checks | - | src/FirstMud.GameServer | 2026-04-13 |
| worktree-agent-a32711f5 | in-progress | content-layer-pilot | Auto-progression MVP | - | src/FirstMud.GameServer, src/FirstMud.Client | 2026-04-13 |
<!-- agents add rows above this line -->
