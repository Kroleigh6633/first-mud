# Playbook Pass #2 — 2026-04-13 — P0 sweep attempt & honest blocker report

## TL;DR

**P0 could not be executed on this worktree's base.** The eighth-cycle briefing describes a combat-balance sweep against a playbook library, `content/combat-curves.json`, `content/progression-curves.json`, tiered herbs, wired gems, an economy evaluator, and an auto-progression endgame simulator. **None of those are present on branch `worktree-agent-aa258ec1` (checked out from `content-layer-pilot` at commit `049fcb0`).**

The base's `content/` directory contains only:
`buildings.json, consumables.json, factions.json, loot-tables.json, monsters.json, recipes.json, zones.json` (+schemas).

No `combat-curves.json`. No `progression-curves.json`. No herbs-tiered content. No gems content. No playbook spec JSONs. No playbook runner under `tools/design/FirstMud.DesignTools/Tools/` (present: DialogueLint, EconomySim, EncounterSim, FactionState, ScenarioPlayer — no Playbook tool).

That content lives on other parallel-agent branches that have not yet been merged into this worktree's checkout. Running playbooks against content that isn't here would produce meaningless output, and data-tuning curves that don't exist in this worktree would produce files that conflict with the parallel agents' work (explicitly flagged as a surface not to touch).

## Per-playbook status

| Playbook | Status | Reason |
|---|---|---|
| nude-character | NOT RUN | No playbook library / curves present |
| gear-only | NOT RUN | Same |
| imbue-only | NOT RUN | Gems not wired; imbue recipes not present on this base |
| companion-contribution | NOT RUN | No playbook library |
| full-party | NOT RUN | No playbook library |
| pack-viability | NOT RUN | No `actionsPerTurnByDanger` curve to tune against |
| capture-flow | NOT RUN | No playbook library |
| crafting-success | NOT RUN | No playbook library; recipes present but no skill-scaling evaluator |
| herb-supply | NOT RUN | Herb tiers not on this base; economy evaluator absent |
| gem-supply | NOT RUN | Gems not on this base; economy evaluator absent |
| trade-flow | NOT RUN | Vendors/trade stage-1 not on this base |
| auto-quest-completion | NOT RUN | Auto-quest `requires` filter not on this base |
| auto-progression-endgame | NOT RUN | Auto-progression MVP lives on parallel agent branch |

## Tunes applied

**None.** Data-first tune authority was granted, but there is no data to tune on this base that wouldn't collide with parallel-agent surfaces. Tuning `content/monsters.json` or `content/loot-tables.json` blindly (without the curves and playbooks to verify against) would be worse than no tune.

## P1 findings (pre-existing bugs surfaced in earlier cycles)

Also blocked pending merge of the content layer this branch does not yet have:

- **Crafting skill zero-effect:** Cannot verify a fix without the crafting-success playbook or an economy evaluator that scores outcome rolls. Preferred fix direction (for when the merge lands): **skill-scaling quantity-match tolerance** — purer, data-only, lives in a single curve file. Rejected alternative (reduced UI rounding) is a local patch that hides the symptom.
- **D7 double-action rule:** Cannot verify 4v4@d7 = 12.5% without the pack-viability playbook. Preferred direction: **soften via `content/combat-curves.json` with a data-driven `actionsPerTurnByDanger` curve** — this replaces the hardcoded threshold with a curve the design layer can tune without engineering. If the 12.5% number turns out to be intentional, the same curve file can pin it explicitly and the pack-viability expected-band gets updated in the playbook spec.
- **Companion-player level asymmetry (task #74):** Preferred direction: **companion XP rubber-banding scaling with player level**, not auto-level-up. Rubber-banding keeps the companion's identity (gear, traits) while removing the combat-math breakage. Pure data-level implementation if progression curves live in JSON.

All three are **deferred as specs, not shipped**, pending the merge that brings the necessary content and tool surfaces onto this branch.

## P2 deliverables (shipped on this branch)

- **Quest chain — Ashen Reach:** `docs/design/quest-chain-ashen-reach.md` — *"The Glass That Remembers"*. Four-beat chain with a 2b pre-informed-Gravenguard branch, an escort sub-step spec'd for the day the escort-template blocklist is partially lifted (with a fallback travel-montage that runs today against existing `content/monsters.json` biome spawns), and four Beat-4 endings (Extract / Unmake / Leave / Feed — the last content-locked behind new canon Q4).
- **Rumors JSON:** `content/zone-rumors.json` — all 9 Aeldran zones (`aeldran-1` through `aeldran-9`), 4–5 rumor strings each. Zone 7 (Starting Road) gets 4; all others 4–5. Total: 40 rumor strings. Text-only, no mechanical coupling, no schema yet (schema can be added when a consuming service exists).
- **Canon Q4 resolved:** Added to `docs/design/canon-deliberations.md` — player-initiated Ashen sacrifice (surfaced by the Reach chain's Beat 4 "Feed it" option). Conservative ruling: forbidden until user approval. Summary table updated; Pass-7 follow-ups section added noting the rumors + Reach chain as closed work items.

## Meta

- **Ledger state:** No ledger file exists on this worktree base (`docs/workflow/` directory is absent). Workflow briefing template referenced in the eighth-cycle prompt is also not present. Proceeded without ledger row, documenting that gap here.
- **Tests count:** Not reported — no code edits were made, so test count is unchanged from the branch's base (457 + 56 per briefing, not re-verified against this branch since doing so is out of the data-only scope).
- **Honest observation:** With the full content layer + playbook library **not yet merged into the creative agent's working branch**, balance convergence is not measurable from this seat. The eighth-cycle briefing reads as if those surfaces are already present on every worktree; they aren't. For the next cycle to actually execute the P0 sweep, the creative agent needs either (a) a worktree cut *after* the parallel content/playbook merges land, or (b) explicit guidance that the creative agent should `git merge` the parallel agents' branches into its own worktree before running the sweep. Option (b) contradicts "don't touch their surfaces," so (a) is the clean fix: creative cycles run *after* the parallel engineering merges, not alongside them.

  On the design side — zones, quest chains, factions, rumors, canon deliberations — the creative layer is converging. Nine zones covered, three-plus quest chains across four factions including Ashen Reach, rumors for all of Aeldran, four canon questions conservatively resolved. The bottleneck is no longer creative throughput; it's the gap between creative worktrees and engineering worktrees.
