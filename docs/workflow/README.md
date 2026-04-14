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

   **Expect a conflict on `docs/workflow/active-branches.md`** — this is normal. The incoming branch self-added a row; HEAD has its own rows; auto-merge can't resolve. Resolution: delete the incoming branch's row from both sides of the conflict, keep HEAD's other rows, `git add`, then `git commit`. Still one commit; rule #3 holds. If the merge has zero other file conflicts, the Edit+add+commit flow above collapses the conflict resolution and the row removal into the same commit.
4. **Client-side fixes verify HMR.** Before reporting a fix to `src/FirstMud.Client/**`, run `scripts/agent/check-hmr.ps1 <changed-file>`. Note: the dev container mounts main's working tree by default — if your change is in a worktree, either (a) merge first then verify, or (b) run `scripts/agent/deploy-worktree.ps1 <worktree> <file>` to copy the file into main's mount (dirties main's tree; `git restore` after if still iterating), or (c) rebind the Docker mount to your worktree. Report which you did.
5. **Supersession protocol.** If branch B is a strict superset of branch A's changes:
   - B's commit message includes a `Supersedes: <branch A>` trailer
   - B's ledger row lists A under `supersedes`
   - A's ledger row flips to `status: superseded by <branch B>`
   - The merge agent deletes both rows when B lands (A is implicitly dropped)
6. **Pre-flight check.** Before starting, scan the ledger for rows touching the files you plan to edit. If you find overlap, either coordinate (supersede) or pick a narrower scope.
7. **Ledger is the source of truth — evaluated against HEAD at merge-agent start.** Briefings must quote ledger state, not override it. The merge agent evaluates the incoming branch's row **as it appears in HEAD's ledger** (i.e. the base branch it is merging into) at the moment the agent starts — not as the row appears on the incoming branch's tip. This resolves the otherwise-circular requirement that a branch mark itself `ready-to-merge`: it can, and typically does, but that self-edit only becomes authoritative once merged.

   Rules:
   - If HEAD's ledger has the row with status `ready-to-merge`, proceed.
   - If HEAD's ledger has the row with status `in-progress` (or any other non-ready state), stop and reconcile — the author hasn't flipped it yet, or a prior merge agent left it mid-flight.
   - If HEAD's ledger has **no row** for the incoming branch (common: the branch never pushed its own ledger edit to HEAD before landing), the briefing is authoritative. The merge agent may proceed on the briefing alone, and the post-merge state will simply have no row to delete for that branch.

   Example: orchestrator spawns agent-X, which edits `foo.cs` and updates its own ledger row on its branch to `ready-to-merge`, then commits. A merge agent is spawned. From the merge agent's perspective on the base branch, the ledger row still shows `in-progress` (or is absent), because agent-X's ledger edit lives on agent-X's branch, not on base. The merge agent treats the briefing as authoritative (row absent) or reconciles (row present and not-ready). After `git merge`, agent-X's `ready-to-merge` edit is what conflicts — the merge agent deletes that row entirely per rule #3.

   An orchestrator writing a merge briefing must run `scripts/agent/ledger-lint.ps1` against base first; it may list branches whose HEAD-ledger row is `in-progress` only if the briefing explicitly states the branch has self-flipped on its tip, in which case the merge agent proceeds on the briefing and deletes the row on merge.

8. **No empty ready-to-merge.** A row may only be marked `ready-to-merge` when `git log content-layer-pilot..<branch-name>` returns at least one commit. Merge agents MUST run `scripts/agent/ledger-lint.ps1 -VerifyCommits` as their first pre-flight step and refuse to process rows that fail the check. The `-VerifyCommits` flag also errors on rows whose branch is missing from `git branch -a`, and warns on `in-progress` rows older than 24h.

## Scripts

See `scripts/agent/README.md` for `check-hmr.ps1`, `ledger-lint.ps1`, `worktree-health.ps1`.

## Why not a git hook / structured DB

A hook would block merges and punish the common case. A structured DB would force agents to learn an API. A markdown table is diffable, human-readable, and Edit-tool-friendly — the lowest-friction coordination surface that still catches the problems we actually hit.
