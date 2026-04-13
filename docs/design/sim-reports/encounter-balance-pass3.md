# Encounter Balance — Pass #3 (Real Scaled Run)

**Date:** 2026-04-13
**Agent worktree:** `agent-a31a7ab9`
**Tool:** `encounter-sim` (seed 42, 500 rolls/cell, player level 5, element Aether, no party)
**Scope:** 5 zones × biome-matched monster pool (8 each) × 2 danger levels (unscaled d=0 vs scaled d=zoneDanger)
**Total sims:** 80 (40 scaled, 40 unscaled baseline)

Pass #2 produced a provisional report because `--danger-level` was not wired
through to `MonsterScaling.Apply`. That flag is now present in this worktree
(`tools/design/FirstMud.DesignTools/Tools/EncounterSim/EncounterSimCommand.cs`
lines 33, 74) and the tool injects scaling identically to `MonsterFactory`.

Pass #4 canon notes: the prompt referenced zone labels (plains/village/
thornwood/marchkeep/gravenmarsh) that do not match `content/zones.json`. I
adjusted to the canonical zone set. Five zones cover danger 1, 2, 3, 5, 8 —
a full ladder.

## Zone danger ladder (from `content/zones.json`)

| Zone (internal id)              | biome   | dangerLevel | Pool size |
|---------------------------------|---------|-------------|-----------|
| `aeldran-3-portmere`            | plains  | 1           | 8         |
| `aeldran-7-starting-road`       | plains  | 2           | 8 (same pool as portmere) |
| `aeldran-2-thornwood`           | forest  | 3           | 8         |
| `aeldran-5-drowned-coast`       | water   | 5           | 8         |
| `aeldran-6-ashen-reach`         | desert  | 8           | 8         |

Note: the Ashen Reach is `desert` in content; `aeldran-4-gravenmarsh` is
tagged `swamp` with `dangerLevel: 3` (tied with Thornwood). I picked
thornwood for the d=3 slot because its monster tier ladder is more canon-
load-bearing (Quest Chain 2 targets it). Swamp pool can be re-run next pass.

## Summary table — unscaled baseline vs scaled (win rate)

A single-cell value is a 500-roll win-rate. "Diff" is
`WinRate(unscaled) - WinRate(scaled)`. Higher diff = scaling bit harder.

### portmere (plains, d=1)

| Monster          | d=0 (unscaled) | d=1 (scaled) | Diff   | Scaled difficulty |
|------------------|---------------:|-------------:|-------:|-------------------|
| cave-rat         | 100.0%         | 100.0%       |  0.0pp | trivial           |
| stray-dog        | 100.0%         | 100.0%       |  0.0pp | trivial           |
| highway-bandit   | 100.0%         | 100.0%       |  0.0pp | trivial           |
| wild-horse       | 100.0%         | 100.0%       |  0.0pp | trivial           |
| rogue-knight     | 100.0%         | 100.0%       |  0.0pp | trivial           |
| pack-alpha-wolf  | 100.0%         | 100.0%       |  0.0pp | trivial           |
| wandering-ogre   | 100.0%         |  99.8%       |  0.2pp | trivial           |
| mounted-raider   | 100.0%         | 100.0%       |  0.0pp | trivial           |

### starting-road (plains, d=2)

| Monster          | d=0    | d=2    | Diff    | Scaled difficulty |
|------------------|-------:|-------:|--------:|-------------------|
| cave-rat         | 100.0% | 100.0% |   0.0pp | trivial           |
| stray-dog        | 100.0% | 100.0% |   0.0pp | trivial           |
| highway-bandit   | 100.0% | 100.0% |   0.0pp | trivial           |
| wild-horse       | 100.0% | 100.0% |   0.0pp | trivial           |
| rogue-knight     | 100.0% | 100.0% |   0.0pp | trivial           |
| pack-alpha-wolf  | 100.0% | 100.0% |   0.0pp | trivial           |
| wandering-ogre   | 100.0% |  98.0% |   2.0pp | trivial           |
| mounted-raider   | 100.0% |  96.6% |   3.4pp | trivial           |

### thornwood (forest, d=3) — FIRST BALANCED RESULTS APPEAR

| Monster             | d=0    | d=3    | Diff    | Scaled difficulty |
|---------------------|-------:|-------:|--------:|-------------------|
| timber-wolf         | 100.0% | 100.0% |   0.0pp | trivial           |
| wild-boar           | 100.0% | 100.0% |   0.0pp | trivial           |
| thornweaver-spider  | 100.0% | 100.0% |   0.0pp | trivial           |
| forest-bandit       | 100.0% | 100.0% |   0.0pp | trivial           |
| dire-bear           | 100.0% |  99.0% |   1.0pp | trivial           |
| treant              | 100.0% |  98.6% |   1.4pp | trivial           |
| **elder-stag**      | 100.0% |  **65.6%** |  **34.4pp** | **balanced**  |
| **thornwood-guardian** | 100.0% | **71.6%** |  **28.4pp** | **balanced** |

### drowned-coast (water, d=5)

| Monster             | d=0    | d=5    | Diff    | Scaled difficulty |
|---------------------|-------:|-------:|--------:|-------------------|
| giant-crab          | 100.0% | 100.0% |   0.0pp | trivial           |
| mud-skipper         | 100.0% | 100.0% |   0.0pp | trivial           |
| tide-lurker         | 100.0% | 100.0% |   0.0pp | trivial           |
| reef-shark          | 100.0% |  99.2% |   0.8pp | trivial           |
| **sea-serpent**     | 100.0% |  **68.6%** |  **31.4pp** | **balanced**  |
| **kraken-spawn**    | 100.0% |  37.2% |  62.8pp | hard              |
| deep-horror         | 100.0% |  14.0% |  86.0pp | punishing         |
| drowned-revenant    | 100.0% |  24.2% |  75.8pp | punishing         |

### ashen-reach (desert, d=8)

| Monster             | d=0    | d=8    | Diff    | Scaled difficulty |
|---------------------|-------:|-------:|--------:|-------------------|
| sand-scorpion       | 100.0% |  99.0% |   1.0pp | trivial           |
| dust-viper          | 100.0% |  98.8% |   1.2pp | trivial           |
| **fire-lizard**     | 100.0% |  **73.6%** |  **26.4pp** | **balanced** |
| giant-centipede     | 100.0% |  94.8% |   5.2pp | easy              |
| sand-wurm           | 100.0% |  15.0% |  85.0pp | punishing         |
| ash-golem           | 100.0% |  11.6% |  88.4pp | punishing         |
| phoenix-hatchling   | 100.0% |   0.2% |  99.8pp | punishing         |
| ember-drake         | 100.0% |   0.0% | 100.0pp | punishing         |

## Deltas — one-line per zone

| Zone            | Avg Δ WinRate (unscaled − scaled) | # monsters moving out of "trivial" |
|-----------------|-----------------------------------:|------------------------------------:|
| portmere        |  0.03pp                            | 0 / 8 |
| starting-road   |  0.67pp                            | 0 / 8 |
| thornwood       |  8.15pp                            | 2 / 8 |
| drowned-coast   | 32.1pp                             | 6 / 8 |
| ashen-reach     | 50.9pp                             | 6 / 8 |

Scaling has essentially zero bite at d≤2 (as expected — scaling curve is
near-flat at low danger). It ramps sharply from d=3 onward and becomes
overwhelming at d=8 for tier-2+ creatures. The shape is roughly correct —
danger is doing real work — but it **over-corrects** at high danger and
**under-corrects** at low danger relative to the spec "every zone has at
least one 'balanced' encounter."

## Zones lacking a `balanced` monster after scaling

- **portmere (d=1):** No balanced result. Best candidate `wandering-ogre` at
  99.8%. Zone is a safe commercial hub so this may be intentional, but the
  *tutorial* road next door is also trivial (see below).
- **starting-road (d=2):** No balanced result. Best candidate `mounted-raider`
  at 96.6%. This is a player's first real combat zone — at least one
  "balanced" encounter is strongly desirable for pacing.
- **thornwood (d=3):** OK — `elder-stag` (65.6%) and `thornwood-guardian`
  (71.6%) both land balanced.
- **drowned-coast (d=5):** OK — `sea-serpent` (68.6%) lands balanced. Tier-3
  (deep-horror, drowned-revenant) are punishing — acceptable as skip-if-
  unlevelled content.
- **ashen-reach (d=8):** OK — `fire-lizard` (73.6%) balanced; but the *entire
  tier-2+ half of the pool* is punishing/near-certain-loss. A level-5 party
  has no realistic advancement path through this zone.

Stat-tweak proposals are captured in
[`encounter-stat-tweaks-proposal.md`](../encounter-stat-tweaks-proposal.md).

## Methodology notes

- Sim runner: `tools/design/run-pass3-sweep.sh` (committed, idempotent).
- Player model: level 5 / Aether / no companions. Using a real 3-companion
  party would shift results; this baseline is intentionally soloed so that
  "balanced" reflects the monster's inherent difficulty, not the party
  comp. A future pass should repeat with a canonical 3-companion party.
- 500 rolls/cell with seed 42 gives stable win-rate to ≈±1pp.
- `--danger-level 0` path still emits the stderr warning and JSON
  `"scaled": false`; scaled runs correctly stamp `"scaled": true`. Behavior
  matches spec.

## Hand-off

- Full JSON per-cell outputs are **not** persisted from this sweep (the
  script captured summary lines only, to keep sim-logs from exploding). If
  the next cycle wants per-cell JSON, re-run with a per-call redirect.
- One-line per-zone delta table is the migration-ready artifact for a
  combat-curve tuner.
