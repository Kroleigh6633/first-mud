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
| worktree-agent-aea45715 | ready-to-merge | 049fcb0 | Wire content/imbue-recipes.json into IContentProvider + new ImbueWithGemCommand gem-imbue path (taper path preserved) | - | content/imbue-recipes.json, src/FirstMud.Application/Content, src/FirstMud.GameServer (ImbueWithGemCommandHandler + parser + DI), src/FirstMud.Client/src/hooks/useGameCommands.ts, tests/FirstMud.Tests/{Content,Handlers} | 2026-04-13 |
<!-- agents add rows above this line -->
