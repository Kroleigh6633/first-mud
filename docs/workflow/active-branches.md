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
| worktree-agent-a12aa569 | ready-to-merge | 9c5e5b9 | cycle 6: progression audit pass 2 + tune proposal v1 + batch4 quest chain | - | docs/design/sim-reports/progression-pass-2-*.md, docs/design/progression-tune-proposal-v1.md, docs/design/quest-chains-batch4.md, docs/design/canon-deliberations.md, docs/design/INDEX.md, tools/design/fixtures/quest-chain5-wrong-tide.json | 2026-04-13 |

<!-- agents add rows above this line -->
