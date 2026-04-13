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
| worktree-agent-a1fc25ed | in-progress | HEAD | Polish 4 workflow rough edges: merge-ledger cleanup procedure, worktree-health cwd tagging, ledger-is-source-of-truth rule, HMR deploy helper script | - | docs/workflow/agent-briefing-template.md, docs/workflow/README.md, scripts/agent/ledger-lint.ps1, scripts/agent/worktree-health.ps1, scripts/agent/deploy-worktree.ps1, scripts/agent/README.md | 2026-04-13 |

<!-- agents add rows above this line -->
