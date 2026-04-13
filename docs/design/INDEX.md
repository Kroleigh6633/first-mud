# Design Ledger — first-mud

Running index of design artifacts in `docs/design/`. Every run appends.
Canon is `lore/*.md` and is read-only from here.

## Status Key

- **draft** — first pass, expect to change
- **ready** — reviewed, stable enough to hand to a migration/content agent
- **locked** — frozen; changes require explicit user approval

## Ledger

| Title | Status | Canon Files Touched (read-only) | Outstanding Questions |
|---|---|---|---|
| `aeldran-zones.md` | draft (Pass #1) | world-aeldran, factions, reputation, economy-and-crafting | Does the "Starting Road" have a canonical name? Where is the Rider Post HQ physically? Are the Compact Cities three discrete zones or one metro zone? |
| `quest-chains.md` | draft (Pass #1) | factions, reputation, magic-system, worlds-and-portals | Covered in `canon-deliberations.md` Q1. Other: Does Aldric know Weaveborn exists by name yet? Are AI players eligible to claim these chains? |
| `faction-threads.md` | draft (Pass #1) | factions, reputation, world-aeldran | Covered in `canon-deliberations.md` Q2, Q3. Other: Fairgean inland sighting — fixed date or float? |
| `npc-voices.md` | draft (Pass #1) | factions, reputation, companions | Do NPCs carry literacy/dialect markers? Regional naming convention? |
| `encounter-palette.md` | draft (Pass #1) | combat, world-aeldran, magic-system, worlds-and-portals | Wildfolk default hostility? Ardweld automata capture-vs-destroy? |
| `canon-deliberations.md` | draft (Pass #2) | — (references factions, reputation, companions) | Three rulings awaiting user overrule; see that file |
| `world-events.md` | draft (Pass #2) | factions, reputation, world-aeldran, economy-and-crafting | Event scheduler priorities correct? Tick cadence (daily at dawn) correct for the game loop? Season length? |
| `sim-reports/sealed-letter-playthrough.md` | draft (Pass #2) | factions, reputation, magic-system, worlds-and-portals | Should `success` terminal grant a mechanical item, not just rep? |
| `tools/design/fixtures/quest-sealed-letter.json` | draft (Pass #2) | — | Fixture needs a real scenario-player run to validate once toolbox merges |
| `tools/design/fixtures/dialogue-harken-vos.json` | draft (Pass #2, fix Pass #4) | — | `roots[]` extended; lint orphans cleared. `requiresReputationMax` still tool-unsupported. |
| `sim-reports/encounter-balance-pass2.md` | draft (Pass #4) | combat, world-aeldran | `--danger-level` flag missing in this worktree; report uses raw stats. Re-run when flag merges. |
| `world-events-draft-schema.md` | draft (Pass #4) | — | Strawman v1 spec for `content/events.json`. Three open Qs in §"Open questions." |
| `tools/design/fixtures/event-red-market.json` | draft (Pass #4) | — | Sample fixture for the strawman schema. |
| `tools/design/fixtures/event-dream-tick.json` | draft (Pass #4) | factions | Sample fixture; uses `rotationState` block (schema extension proposal). |
| `tools/design/fixtures/event-seed-conversion.json` | draft (Pass #4) | factions, reputation | Sample fixture; couples to Chain 4 quest. Q7 (rep ownership) pending. |
| `quest-chains-batch3.md` | draft (Pass #4) | factions, reputation, companions | Chain 4 hooks event-window soft-failure. Schema-gap Q5/Q7 deferred. |
| `tools/design/fixtures/quest-chain4-thinking-girl.json` | draft (Pass #4) | — | 4 paths verified via scenario-player. |

## Run log

- **2026-04-13** — Pass #1 (sibling worktree agent-ae87bb87). Initial six
  deliverables authored at draft. No canon files modified. Hand-off
  candidate: `encounter-palette.md` — flavor+intent blocks are shaped to
  migrate 1:1 into `content/monsters.json` entries once stat schema lands.
- **2026-04-13** — Pass #2 (worktree agent-a2a43ce2). Delivered:
  - `canon-deliberations.md` — conservative rulings on the three Pass #1
    canon-ambiguous items. All three overturnable by user.
  - `tools/design/fixtures/quest-sealed-letter.json` — Chain 1 translated
    to scenario-player spec. 12 nodes, 9 terminal outcomes, 0 dead branches
    (verified by hand-simulation; real tool run pending — see note below).
  - `docs/design/sim-logs/2026-04-13.md` + two JSON sidecar files —
    placeholder sim-logs, hand-derived. **ACTION REQUIRED:** when
    `tools/design/DesignTools.slnx` merges into this worktree, re-run
    scenario-player on the fixture and dialogue-lint on the Harken Vos
    fixture to replace the placeholders.
  - `docs/design/sim-reports/sealed-letter-playthrough.md` — narrative
    findings: 5 design holes identified (commission-token scoping, route-
    vs-time predicate on soft-failure, Drest intent-read realism,
    orphaned Thornwood callback, Ashen-mole player-visibility).
  - `tools/design/fixtures/dialogue-harken-vos.json` — dialogue tree for
    Harken Vos covering all 7 required states. Schema gap surfaced:
    `requiresReputationMax` needed for low-rep voice variants.
  - `docs/design/world-events.md` — 12 world-tick events cataloguing idle-
    play content. Raw material for `content/events.json`.
  - **Hand-off candidate:** `tools/design/fixtures/quest-sealed-letter.json`
    is shaped 1:1 for the scenario-player v1 schema; promote to
    `content/quests/` on the day the content pipeline lands.

### Pass #2 open questions for next cycle
1. Dialogue-tree v1 schema needs `requiresReputationMax` (or general
   predicate syntax) before NPC low-rep variants can be fixture-linted.
   **RESOLVED in Pass #5** — `requiresReputationMax` is wired through
   `DialogueLinter` and accepted on fixture nodes.
2. Commission token as arc-scoped state: where does that live in world-state?
3. Season / day-count calendar: what is the canonical year length?
   `world-events.md` assumes ~365 days with day 200 as first frost; canon
   has not defined this.

- **2026-04-13** — Pass #4 (worktree agent-ac7f00bc). Delivered:
  - `docs/design/sim-reports/encounter-balance-pass2.md` — full 5-zone
    palette balance sweep at narrative pl. **Three of five zones
    (forest, mountain, swamp) produce ZERO `balanced` palette monsters
    at base stats** — `--danger-level` flag is NOT in this worktree, so
    raw-stat reads dominate. Designer attention: bump tier-3 stats
    +30–40% HP / +20–25% power, OR land `--danger-level` and re-run.
  - `docs/design/world-events-draft-schema.md` — strawman v1 schema for
    `content/events.json`. 5 trigger types, 5 effect families,
    cleanup contract, scheduler discipline.
  - `tools/design/fixtures/event-red-market.json`,
    `event-dream-tick.json`, `event-seed-conversion.json` — three
    sample fixtures matching the strawman schema.
  - `docs/design/quest-chains-batch3.md` + `tools/design/fixtures/
    quest-chain4-thinking-girl.json` — Chain 4 "The Thinking Girl,"
    5 terminals, hooks the Seed-Conversion world-event window with a
    soft-failure terminal on window expiry. All 4 traced paths run
    clean through scenario-player.
  - `canon-deliberations.md` extended with Pass #3 follow-up: Q4
    (`removeItem` underflow), Q5 (`absentFlags` schema add), Q6
    (sub-region rep — rejected, use opinion+flags), and a deferred Q7
    (event vs. chain rep ownership).
  - **Fixture fix:** `dialogue-harken-vos.json` `roots` extended to
    `["greet", "greet-low-rep", "check-in", "turn-in"]` — closes 8
    orphan-node lint warnings caught this pass.
  - **Hand-off candidates:**
    - `quest-chain4-thinking-girl.json` — promote to `content/quests/`
      after Q5/Q7 rulings (depends on `absentFlags` and rep ownership).
    - `event-*.json` trio — promote to `content/events.json` once a
      content pipeline + `event-lint` tool exists.

### Pass #3 / #4 open questions for next cycle
1. `--danger-level` encounter-sim flag is documented as expected but
   absent in this worktree. Until merged, balance reads default to
   trivial across zones 3–5.
2. Q7: world-event fixtures' rep-delta ownership vs. quest chain rep
   ownership (see canon-deliberations.md Pass #3 follow-up).
3. NPC `opinion` integer (−200..+200) is proposed in Q6 ruling but no
   fixture or schema currently expresses it. Needs a future spec doc
   before npc-voices.md grows opinion-drift annotations.
4. Dialogue-lint tool does not parse `requiresReputationMax` or
   `requiresFlag` node-level predicates (verified by source read);
   schema-tool parity gap. Real warnings (8 orphans) only surface when
   re-entry nodes are explicitly listed in `roots[]` — until the lint
   tool grows multi-root inference from `requires*` fields.
## Pass #5 additions

| Title | Status | Canon Files Touched (read-only) | Outstanding Questions |
|---|---|---|---|
| `sim-reports/encounter-balance-pass3.md` | ready | combat, world-aeldran | Verify with a 3-companion party (not solo lvl-5); repeat against gravenmarsh/swamp pool. |
| `encounter-stat-tweaks-proposal.md` | draft | combat, world-aeldran | Needs curve-designer review before content edits. |
| `tapers-content-proposal.md` | ready | magic-system, economy-and-crafting | 7th "Sealed" taper for Compact? Qualities inline vs split file? |
| `npc-voices-batch3.md` | draft | factions, reputation | Q4-Q6 in canon-deliberations pending user ruling. |
| `canon-deliberations.md` (Q4–Q7 added) | draft | — | Four new rulings awaiting user overrule. |

### Pass #5 run log (2026-04-13, agent-a31a7ab9)

- Re-ran encounter-balance Pass #3 for real against `--danger-level`.
  Report `sim-reports/encounter-balance-pass3.md` supersedes the Pass #4
  provisional.
- Drafted `encounter-stat-tweaks-proposal.md` — concrete monster-level
  tweaks for portmere + starting-road (missing `balanced`), and the
  ashen-reach tier-2+ cliff.
- Verified `dialogue-lint` accepts `requiresReputationMax`. Fixed the
  Harken Vos fixture `roots` to surface all 17 nodes; lint returns
  clean (only intended high-rep warning).
- Verified `scenario-player --allow-underflow` is **not wired**
  (worktree predates that merge). Sealed-letter bribe-scribe branch
  remains unreachable; recorded as gap.
- Drafted `content/tapers.json` shape — 6-entry migration, inline
  qualities, no existing recipe edits needed.
- Drafted voice samples for Kesh, Varn, Brother Velm (batch 3); all
  three were referenced in `quest-sealed-letter.json` without a voice.
- Added canon Q4 (catalogue-as-tell), Q5 (npc-id convention),
  Q6 (Chapel↔Thornwood channel), Q7 (tool-gap etiquette) with
  conservative rulings.

### Pass #5 open questions for next cycle
1. Missing `--allow-underflow` in `scenario-player` blocks the
   bribe-scribe exploration on Sealed Letter. Wire it or document that
   the `removeItem` op soft-fails on underflow.
2. `content/npcs.json` still not in tree. Voice batches 1+2+3 are all
   drafted and waiting for the catalogue to land so they can be
   cross-linked.
3. Solo-lvl-5 Aether baseline for encounter-sim may over-estimate zone
   difficulty. Re-run pass #3 with a canonical 3-companion party once
   such a party is specced.
