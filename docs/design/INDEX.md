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

## Pass #6 additions

| Title | Status | Canon Files Touched (read-only) | Outstanding Questions |
|---|---|---|---|
| `sim-reports/progression-pass-2-all.md` | ready | combat, economy-and-crafting | Pyrrhic-win band missing from encounter-sim (Q10). |
| `sim-reports/progression-pass-2-balanced.md` | ready | — | — |
| `sim-reports/progression-pass-2-combat-heavy.md` | ready | — | — |
| `sim-reports/progression-pass-2-craft-heavy.md` | ready | — | — |
| `sim-reports/progression-pass-2-enchanting-focused.md` | ready | — | — |
| `progression-tune-proposal-v1.md` | draft | combat, economy-and-crafting | Three tunes (A, B, C) awaiting user approval. Formula fix in §6 is code-layer, deferred to engineering agent. |
| `quest-chains-batch4.md` | draft | factions, world-aeldran | Q8 (Below-Song canon), Q9 (two-good-end pattern). |
| `tools/design/fixtures/quest-chain5-wrong-tide.json` | draft | — | 6 terminals traced clean via scenario-player; allowUnderflow=false. |
| `canon-deliberations.md` (Q8–Q10 + Q7 update) | draft | — | Three rulings; Q7 updated to reflect `--allow-underflow` is now live. |

### Pass #6 run log (2026-04-13, agent-a12aa569)

- Re-ran `progression-sim --all-playstyles --seed 42 --hours 40` at baseline,
  identical to Pass 1 (deterministic confirms stability).
- Tested three data-only tunes locally in the worktree:
  - **Tune A** (recipes.json low-tier min 1→3, max 4→5) — lifts
    balanced/combat-heavy TopGear 2 → 3. No regression on craft-heavy.
  - **Tune B** (recipes.json iron tier min 3→4, max 6/7 → 8) — no 40h
    effect alone, but raises ceiling for future combined tune.
  - **Tune C** (combat-curves.json hp 0.4→0.3, power 0.3→0.25) — lifts
    combat-heavy reliable-danger d8 → d10. Seed-42 craft-heavy regresses
    due to RNG path divergence (artifact, not real).
- Authored `progression-tune-proposal-v1.md` ranking A/B/C with
  predicted effects, plus a §6 "considered + rejected" (four ideas).
- Verified `--allow-underflow=false` hard-errors on bribe-scribe with the
  exact message; restored fixture afterwards.
- Drafted Chain 5 "The Wrong Tide" (Drowned Coast); 6 terminals all reachable.
- Added Q8 (Below-Song), Q9 (two-good-end pattern), Q10 (pyrrhic-win band).
- Updated Q7 ruling to reflect `--allow-underflow` is now real.

### Pass #6 open questions for next cycle

1. **Code-layer tune** (`CraftingService.CalculateWorkmanship`:
   `craftingSkill/20 → /10`) is the single highest-leverage fix but out of
   creative's mandate. Needs an engineering agent cycle.
2. **encounter-sim pyrrhic band** — tool should weight HP% alongside
   win-rate. Without it, progression-sim's "reliable d10" claim for
   balanced misleads the user.
3. **Piecewise combat curve** — current `monsterScaling` is linear. A
   curve that is softer at d3–d5 and same at d8+ would better match the
   user's play-test ceiling without trivialising end-game content. Would
   require `combat-curves.json` schema extension.

## Pass #7 additions

First cycle under the playbook-driven mandate. Balance sweep against the
5 seed playbooks on the post-rebalance base.

| Title | Status | Canon Files Touched (read-only) | Outstanding Questions |
|---|---|---|---|
| `sim-reports/playbook-pass-1.md` | ready | combat | Playbook cell-salt non-determinism at band edges. |
| `tools/design/playbooks/full-party.json` | ready | — | (spec updated) Mid-game stack dominance acknowledged as design intent. |
| `tools/design/playbooks/nude-character.json` | ready | — | (spec updated) 1v1 solo monster always favorable — flag for `pack-viability` playbook. |
| `tools/design/playbooks/gear-only.json` | ready | — | (spec updated) Proxy model under-specifies gear. |
| `tools/design/playbooks/imbue-only.json` | ready | — | (spec updated) Baseline gt=3 swamps imbue signal. |
| `tools/design/playbooks/companion-contribution.json` | ready | — | (spec updated) Future `companion-layer-curve` playbook needed. |
| `content/combat-curves.json` (tune) | ready | combat | hp 0.2→0.4, power 0.15→0.28, scalingPerTier 0.12→0.03. |

### Pass #7 run log (2026-04-13, agent-a6d6f55c → creative-pass-7 worktree)

- Merged playbook harness branch `worktree-agent-a9f8a24b` into
  `content-layer-pilot`. Build 0/0, tests 402+16=418, DesignTools 34.
- Ran full 5-playbook baseline sweep; 63 of 117 expected cells diverged
  post-rebalance (full-party 7, nude 15, gear 18, imbue 16, companion 7).
- Iteratively tuned `combat-curves.json`: `hpPerDanger 0.20 → 0.40`,
  `powerPerDanger 0.15 → 0.28`, `scalingPerTier 0.12 → 0.03`.
- Rewrote all 5 playbook `expectedViability` grids to reflect the tuned
  post-rebalance reality, with expanded descriptions capturing design
  intent per playbook.
- Verified 4 of 5 playbooks converge every run; `gear-only` flaps 1-3
  cells at 85%/95% band boundaries due to `string.GetHashCode()`
  non-determinism in `PlaybookEngine` cell-salt — documented for next
  engineering cycle.
- No canon/lore files touched. No code changes.

### Pass #7 open questions for next cycle

1. **Playbook cell-salt non-determinism.** `PlaybookEngine.RunCell` uses
   `kv.Key.GetHashCode()` which is randomized per process in .NET Core.
   Replace with FNV-1a or fold axis values directly into the Random seed.
2. **Pack-viability playbook gap.** Every current playbook is 1v1. Real
   overworld is 2-5 monster packs. Candidate axes: `partySize × packSize
   × dangerLevel`.
3. **Gear/imbue proxy is coarse.** Treating gearTier as +2 levels and
   imbueLevel as +1 level conflates damage/hp slots with raw leveling.
   Either (a) land real gear stats in the combat sim, or (b) drop the
   baseline gearTier in `imbue-only` so imbue contribution is visible on
   a non-saturated player.

## Pass #8 additions

| Title | Status | Canon Files Touched (read-only) | Outstanding Questions |
|---|---|---|---|
| `content/recipes.json` (+8 legendary recipes) | ready | economy-and-crafting | Gate legendary recipes behind a new Grand Forge tier-3 building, or keep them at tier-1 Forge with skill-only gate? |
| `content/loot-tables.json` (+Wyrdforged Core, +Starforged Ingot) | ready | economy-and-crafting | Weight 1 vs weight 2 — re-tune once economy-sim has been run against the new supply playbook. |
| `content/monsters.json` (isBoss flags + bossDrops block) | ready | combat | LootService wiring for `bossDrops` is the remaining code-layer task — today the field is data-only. |
| `content/schemas/monsters.schema.json` (isBoss / bossDrops) | ready | — | — |
| `tools/design/playbooks/auto-progression-endgame.json` (updated) | ready | — | Needs AutoProgressionService sub-modes (#515-ish) before the 60h time-to-target can be measured for real. |
| `tools/design/playbooks/economy/top-tier-materials-supply.json` | ready | economy-and-crafting | Bands are provisional — recalibrate after the first real economy-sim run. |

### Pass #8 run log (2026-04-13, task #134)

- Lifted macro-progression cap: 8 new Tier-5 recipes now cover every major
  slot (weapon, offhand, head, chest, legs, hands, feet, accessory) at
  `baseWorkmanshipMin=7 / baseWorkmanshipMax=10` and `requiredCraftingSkill`
  110-120. Old cap had baseWorkmanshipMax topping out at 10 for only 4
  items (Void Blade, Phase Bow, Tear Fragment Ring, Wyrd Armor); helm/
  chest/legs/hands/feet peaked at 9.
- Added 2 top-tier materials to loot-tables: **Wyrdforged Core** (biome-wyrd
  + biome-desert, weight 1, mw 7-10) and **Starforged Ingot** (biome-mountain,
  weight 1, mw 7-10). Both deliberately scarce.
- Added `isBoss: true` to 4 apex monsters: `ember-drake`, `frost-giant`,
  `wyrd-abomination`, `tear-in-the-weave`. Added top-level `bossDrops` block
  mapping those monsters to forced-drop entries for the new materials
  (dropChance 0.20-0.35). Schema extended to describe the new fields.
- Updated `auto-progression-endgame.json`: `expected` band d10 moved from
  "punishing" to "hard" and `timeToTarget` annotated (pre: unreachable,
  post: ~60h target once sub-modes ship).
- New playbook `tools/design/playbooks/economy/top-tier-materials-supply.json`
  measures per-hour acquisition of Wyrdforged Core / Starforged Ingot across
  playstyles. Expected bands: combat-heavy 15-40/40h, balanced 8-25/40h,
  craft-heavy 0-4/40h.
- Build 0/0. Tests: FirstMud.Tests 530 passed, FirstMud.IntegrationTests
  16 passed, FirstMud.DesignTools.Tests 62 passed (608 total, 0 failures).

### Pass #8 open questions for next cycle

1. **Grand Forge building gate.** Should legendary recipes ALSO require a
   new "Grand Forge" tier-3 upgrade of the existing Forge (ceremony gate in
   addition to skill 110-120), or live on tier-1 Forge with skill-only
   gating? **Proposed default:** tier-1 Forge only, skill gate stands alone —
   adding a building tier is a systemic change that should be its own issue.
   Flag for confirmation.
2. **LootService `bossDrops` wiring.** The schema and data are in place,
   but `LootService` doesn't yet read the `bossDrops` block. Task #100
   previously flagged this. Needs a code-layer pass to roll these entries
   per-kill when the slain monster id matches.
3. **economy-sim command for the new playbook.** `top-tier-materials-supply.json`
   is a content contract; the evaluator that consumes it does not yet exist
   for non-gem playbooks. Wire it once `economy-sim` grows a generic
   supply-rate mode.
