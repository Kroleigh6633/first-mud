# Agent briefing template

Paste this skeleton into every spawned worktree agent prompt. Fill the `{{…}}` slots.

---

## Worktree agent briefing

You are running inside a git worktree at:
`.claude/worktrees/agent-{{AGENT_ID}}/`
on branch `worktree-agent-{{AGENT_ID}}`.

**Path rule:** all file writes must use paths inside the worktree (relative, or absolute paths under `.claude/worktrees/agent-{{AGENT_ID}}/`). Never write via the main-repo path `C:\claude\matt\personal\first-mud\first-mud\src\...` — that bypasses your worktree and corrupts the main checkout.

### Pre-flight (do this first)

1. **Consult the ledger first — it is the source of truth.** Read `docs/workflow/active-branches.md`. Anything in your briefing that contradicts the ledger loses; reconcile before proceeding. If your branch is not in the ledger, your first action is to add it (step 3 below). If you are a merge agent and a target branch is not `ready-to-merge` in the ledger, stop and reconcile rather than merging anyway.
2. Scan for rows that touch the files you plan to edit. If any row overlaps and is newer, either:
   - Rebase onto that branch and supersede it (see supersession protocol in `docs/workflow/README.md`), or
   - Narrow your scope so there is no overlap, or
   - Stop and report the conflict.
3. Add your own row to the ledger with `status: in-progress`, `updated: {{TODAY}}`, a one-line summary, and the file paths you expect to touch.

### Task

{{TASK_DESCRIPTION}}

### Completion requirements

- **Commit before returning.** The branch is your commit authorization. Use a descriptive message; if you superseded another branch, include `Supersedes: <branch>` as a trailer.
- **Update your ledger row** to `status: ready-to-merge` (or `blocked: <reason>` / `superseded by <branch>`). Bump the `updated` date.
- **Client-side changes (HMR constraint).** Docker mounts `src/FirstMud.Client/` from **main**, not from worktrees. That means an edit in your worktree is **not visible to the browser until merge**. Options, in order of preference:
  1. Do the edit, commit, return, and let the merge agent's merge-to-main be the HMR trigger. Verification happens post-merge. This is normal and fine.
  2. If you need to visually verify before merge, run `scripts/agent/deploy-worktree.ps1 <worktree-path> <file>` to copy the file into main's mount. This dirties main's working tree — you must `git restore` it in main before the next merge if you are still iterating. Then run `scripts/agent/check-hmr.ps1 <file>`.
  3. Rebind the Docker mount to your worktree via compose override (heavy; usually unnecessary).

  In your report, state which option you used and paste the `check-hmr.ps1` output if applicable. Do not claim "visually verified" if you only ran option 1.
- **Build/test:** `dotnet build FirstMud.slnx` must stay at 0 errors / 0 warnings. `dotnet test FirstMud.slnx` must not lose tests.

### Merging-agent checklist (only if you are a merge agent)

Use this procedure to land a `ready-to-merge` branch in a **single commit** without a `chore(ledger)` follow-up (the workflow forbids `--amend`):

1. **First action:** run `scripts/agent/ledger-lint.ps1 -VerifyCommits`. If it exits non-zero, list the offending rows and HALT — the orchestrator needs to respawn those authors. Do not attempt any merge until the ledger is clean. Confirm the target branch is listed and its status is `ready-to-merge`. If not, stop and reconcile.
2. Flip its row to `status: merging` and commit that on the base branch.
3. `git merge --no-commit --no-ff <branch>` — this stages the merge without committing. The incoming branch will re-add its own ledger row at this point.
4. Use `Edit` on `docs/workflow/active-branches.md` to delete the re-added row.
5. `git add docs/workflow/active-branches.md`
6. `git commit -m "merge <branch>"` — single commit, row gone, tree clean.
7. `scripts/agent/ledger-lint.ps1 -ExpectEmpty` — post-merge verification. Fails loudly if the row survived.

If step 7 fails, something went wrong with step 4; fix in a follow-up commit and investigate why the row wasn't caught in the merge.

### Final-step checklist (MANDATORY before flipping to `ready-to-merge`)

Before reporting "ready-to-merge":

1. `git add -A && git commit -m "..."` on the worktree's branch.
2. Verify `git log <trunk>..HEAD --oneline` returns YOUR commit (non-empty output).
3. Only then flip the ledger row to `ready-to-merge`.

An empty `ready-to-merge` row is a workflow-level failure (see README rule #8). If your branch has 0 commits ahead of trunk, DO NOT flip the row — report `blocked: no changes` or `superseded` instead.

### Report (under 200 words)

- What you changed (files + one-line rationale each)
- Commit sha(s)
- Ledger row state
- HMR verification outcome (if applicable)
- Any overlapping branches you found during pre-flight and how you handled them
