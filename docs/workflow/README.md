# Agent workflow

This folder holds the process rules for multi-agent work on first-mud. It exists because parallel worktree agents kept stepping on each other: uncommitted branches, silent supersessions, and stale Docker mounts. The ledger + scripts here are the minimum viable coordination layer.

## The ledger

`active-branches.md` is the single source of truth for in-flight work. Every agent reads it on start and writes to it on finish.

## Rules

1. **Commit before returning.** The worktree branch itself is the commit authorization — if you were spawned into `worktree-agent-XXXX`, you commit there. No "awaiting explicit user permission" on worktree branches. The orchestrator has already granted that permission by spawning you.
2. **Write a ledger row on start** with `status: in-progress`. Update on completion to `ready-to-merge` (work done, tests pass) or `superseded` (someone else shipped a strict superset).
3. **Merge agents FIFO** from `ready-to-merge`. They set the row to `merging` while working; on success they delete the row. On conflict, they either resolve or flip to `blocked:` with a reason.

   **Single-commit merge procedure** (avoids the `--no-amend` rule forcing a follow-up `chore(ledger)` commit):
   ```
   git merge --no-commit --no-ff <branch>      # stages merge, does not commit
   # the incoming branch re-adds its own ledger row here; delete it:
   # use Edit on docs/workflow/active-branches.md to remove the merged row
   git add docs/workflow/active-branches.md
   git commit -m "merge <branch>"
   scripts/agent/ledger-lint.ps1 -ExpectEmpty   # post-merge sanity check
   ```
   Result: one commit, incoming row gone, no `chore(ledger)` follow-up.
4. **Client-side fixes verify HMR.** Before reporting a fix to `src/FirstMud.Client/**`, run `scripts/agent/check-hmr.ps1 <changed-file>`. Note: the dev container mounts main's working tree by default — if your change is in a worktree, either (a) merge first then verify, or (b) run `scripts/agent/deploy-worktree.ps1 <worktree> <file>` to copy the file into main's mount (dirties main's tree; `git restore` after if still iterating), or (c) rebind the Docker mount to your worktree. Report which you did.
5. **Supersession protocol.** If branch B is a strict superset of branch A's changes:
   - B's commit message includes a `Supersedes: <branch A>` trailer
   - B's ledger row lists A under `supersedes`
   - A's ledger row flips to `status: superseded by <branch B>`
   - The merge agent deletes both rows when B lands (A is implicitly dropped)
6. **Pre-flight check.** Before starting, scan the ledger for rows touching the files you plan to edit. If you find overlap, either coordinate (supersede) or pick a narrower scope.
7. **Ledger is the source of truth.** Briefings must quote ledger state, not override it. An orchestrator writing a merge briefing must run `scripts/agent/ledger-lint.ps1` first and only list branches currently marked `ready-to-merge`. If a briefing names an agent whose row is not in the ledger, that agent's first action is to add the row; if a briefing lists a merge target whose ledger status is not `ready-to-merge`, the merge agent must stop and reconcile (not silently proceed).

## Scripts

See `scripts/agent/README.md` for `check-hmr.ps1`, `ledger-lint.ps1`, `worktree-health.ps1`.

## Why not a git hook / structured DB

A hook would block merges and punish the common case. A structured DB would force agents to learn an API. A markdown table is diffable, human-readable, and Edit-tool-friendly — the lowest-friction coordination surface that still catches the problems we actually hit.
