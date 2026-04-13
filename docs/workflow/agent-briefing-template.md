# Agent briefing template

Paste this skeleton into every spawned worktree agent prompt. Fill the `{{…}}` slots.

---

## Worktree agent briefing

You are running inside a git worktree at:
`.claude/worktrees/agent-{{AGENT_ID}}/`
on branch `worktree-agent-{{AGENT_ID}}`.

**Path rule:** all file writes must use paths inside the worktree (relative, or absolute paths under `.claude/worktrees/agent-{{AGENT_ID}}/`). Never write via the main-repo path `C:\claude\matt\personal\first-mud\first-mud\src\...` — that bypasses your worktree and corrupts the main checkout.

### Pre-flight (do this first)

1. Read `docs/workflow/active-branches.md`.
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
- **Client-side changes:** run `scripts/agent/check-hmr.ps1 <primary-file>` and paste its output in your report. If HMR cannot verify from the worktree (Docker mounts main), say so explicitly; do not claim verified.
- **Build/test:** `dotnet build FirstMud.slnx` must stay at 0 errors / 0 warnings. `dotnet test FirstMud.slnx` must not lose tests.

### Report (under 200 words)

- What you changed (files + one-line rationale each)
- Commit sha(s)
- Ledger row state
- HMR verification outcome (if applicable)
- Any overlapping branches you found during pre-flight and how you handled them
