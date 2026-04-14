# first-mud Design Tools

Simulation / validation tools for the creative-design agent. **Not game code.**
These tools live outside `src/` and do not ship with the game server.

## Layout

```
tools/design/
  DesignTools.slnx                 # standalone solution
  FirstMud.DesignTools/            # single console app with subcommands
    Program.cs                     # dispatcher
    Shared/                        # ConsolePretty, SimLog, FixtureLoader, Args, RepoRoot
    Tools/
      ScenarioPlayer/              # FULL
      DialogueLint/                # FULL
      FactionState/                # scaffold + README
      EncounterSim/                # scaffold + README
      EconomySim/                  # scaffold + README
      PlaybookRunner/              # FULL — balance harness
  FirstMud.DesignTools.Tests/
  playbooks/                       # canonical balance scenarios (JSON)
  schemas/                         # playbook.schema.json
  fixtures/
    quest-sample.json
    dialogue-sample.json
```

## playbook-runner — balance harness

The harness the creative agent runs every cycle. Replaces one-off rebalance
agents with a library of canonical playbooks. Each playbook fixes some
variables (holdouts) and sweeps others (axes), running `CombatSimulationService`
for every cell in the grid and classifying each into a viability band. Cells
whose actual band differs from the playbook's expected band are flagged as
DIVERGENT — those are the cells the agent investigates.

```bash
dotnet run --project FirstMud.DesignTools -- playbook-runner --playbook full-party
dotnet run --project FirstMud.DesignTools -- playbook-runner --playbook gear-only --seed 99
dotnet run --project FirstMud.DesignTools -- playbook-runner --playbook full-party \
    --compare-to ../../docs/design/sim-logs/playbook-runner-20260413-120000.json
```

Seed playbooks under `tools/design/playbooks/`:

| id                        | expected curve (one-liner) |
|---------------------------|----------------------------|
| `nude-character`          | Viable at low danger only; falls off fast above danger 3. |
| `gear-only`               | Tier ≈ danger keeps fights balanced; +2 eff. levels / tier. |
| `imbue-only`              | Imbues add ~1–2 effective tiers on top of a tier-3 gear baseline. |
| `companion-contribution`  | Extra party slots rescue HP budgets that gear alone can't. |
| `full-party`              | Regression baseline: mid-game loadout vs dangerLevel 0–10. |

Exit codes: `0`=all on-band, `1`=arg error, `2`=load error, `3`=one or more cells diverged.

> **Proxy note.** Gear and imbue systems don't yet exist in
> `CombatSimulationService`, so `gearTier` and `imbueLevel` axes are implemented
> as player-level boosts (+2 / tier, +1 / imbue). Swap to real stats once the
> gear/imbue systems land; the playbooks themselves won't change.

## Build & run

```bash
cd tools/design
dotnet build DesignTools.slnx
dotnet test  DesignTools.slnx

# From the FirstMud.DesignTools folder, or via `dotnet run --project ...`:
dotnet run --project FirstMud.DesignTools -- scenario-player
dotnet run --project FirstMud.DesignTools -- scenario-player --choose accept,hunt-poach,honest
dotnet run --project FirstMud.DesignTools -- dialogue-lint
dotnet run --project FirstMud.DesignTools -- faction-state --at Q3.after-harken
dotnet run --project FirstMud.DesignTools -- encounter-sim --monster timber-wolf --party 2:Fire,3:Water --rolls 1000 --player-level 5 --player-element Fire --seed 42
dotnet run --project FirstMud.DesignTools -- economy-sim --hours 48 --scenario farming
dotnet run --project FirstMud.DesignTools -- playbook-runner --playbook full-party
```

## Commands

| command           | status   | key flags                                          |
|-------------------|----------|----------------------------------------------------|
| `scenario-player` | full     | `--fixture <path>` `--choose id,id,id` `--allow-underflow` |
| `dialogue-lint`   | full     | `--fixture <path>`                                 |
| `faction-state`   | scaffold | `--at <marker>`                                    |
| `encounter-sim`   | full     | `--monster <id>` (repeatable) `--party <L:elem,...>` `--rolls <N>` `--player-level <L>` `--player-element <E>` `--seed <S>` `--danger-level <0..10>` |
| `economy-sim`     | scaffold | `--hours <N>` `--scenario <name>`                  |
| `progression-sim` | full     | `--hours <N>` `--seed <S>` `--playstyle <name>` `--player-archetype fire\|water\|earth\|balanced` `--starting-zone <id>` `--all-playstyles` `--report-pass <N>` |

## Input formats

### Quest-chain spec (v1, JSON)

```json
{
  "id": "harken-wolf-pelts",
  "title": "Harken's Wolf Pelt Request",
  "startState": { "flags": [], "inventory": [], "reputation": { "wytchwood": 0 } },
  "root": "intro",
  "beats": [
    { "id": "intro", "description": "...",
      "choices": [ { "id": "accept", "text": "...", "next": "hunt",
                     "effects": [ { "type": "setFlag", "key": "harken.accepted" } ] } ] },
    { "id": "hunt", "description": "...", "requires": { "flags": ["harken.accepted"] },
      "choices": [ ... ] },
    { "id": "end-good", "description": "...", "terminal": true, "outcome": "success" }
  ]
}
```

Effect types: `setFlag`, `clearFlag`, `addItem`, `removeItem`, `addReputation`.
Requires: `flags[]`, `items[{key,amount}]`, `reputation{faction: min}`. Both
beats and choices may carry a `requires` block; a choice whose `requires`
fails is **not traversable** (the runner skips it; forcing into it errors).

See [docs/scenario-spec.md](docs/scenario-spec.md) for the full schema and
the underflow / gating semantics.

#### removeItem underflow

By default `removeItem` is a **hard error** when the requested amount exceeds
the current stock (an absent key counts as stock 0). The runner exits non-zero
and prints e.g. `ERROR at beat 'past-varn' choice 'bribe-scribe': removeItem
'gold' x25 exceeds stock (0 available)`.

To suppress the hard error and clamp the subtract to zero (useful when
exploring fixtures with intentionally-incomplete startState):

- pass `--allow-underflow` on the CLI, **or**
- set `"allowUnderflow": true` at the top level of the fixture.

In both cases the underflow is still reported as a per-step warning.

### Common footguns

- **removeItem underflow.** A choice removes `gold x25` but the player has
  none. Default = hard error. Use `--allow-underflow` only when intentionally
  sketching. Real fix is usually to add the stock to `startState.inventory`
  via an earlier reward chain.
- **Missing prerequisite flags.** A beat's `requires.flags` lists a flag the
  default-first-choice path never sets. Currently a *warning* (so visited
  beats still report). Watch for `warn: missing flag '...'` in the markdown.
- **Terminal unreachable due to rep gating.** A choice gated on
  `reputation.<faction> >= N` will be silently skipped during default
  traversal if the start rep is too low. The successful terminal becomes
  unreachable. Use `--choose` to force into the gated branch and confirm the
  rep wall is intentional, not an authoring slip.

### Dialogue tree (v1, JSON)

```json
{
  "npcId": "harken",
  "roots": ["greet"],
  "startingReputation": { "wytchwood": 0 },
  "nodes": [
    { "id": "greet", "text": "...", "options": [
        { "text": "...", "next": "work" },
        { "text": "...", "next": "insider", "requiresReputation":    { "faction": "wytchwood", "min": 25 } },
        { "text": "...", "next": "stranger", "requiresReputationMax": { "faction": "wytchwood", "max": 10 } } ] },
    { "id": "work", "text": "...", "terminal": true, "effects": { "wytchwood": 5 } }
  ]
}
```

See `tools/design/docs/dialogue-spec.md` for the full field reference and lint
rules (including `requiresReputationMax`, node `effects`, and reachability
analysis for low-rep branches).

## Outputs

Every run writes:
- **stdout** — human-readable summary (color-coded).
- **JSON** — `docs/design/sim-logs/<tool>-<timestamp>.json` (structured).
- **markdown** — appended to `docs/design/sim-logs/YYYY-MM-DD.md` (one day per file).

## How the creative agent should use these tools

1. Author a quest-chain spec or dialogue tree under `content/` or `docs/design/drafts/`.
2. Run `scenario-player --fixture <path>` with `--choose` to walk each branch.
3. Run `dialogue-lint --fixture <path>` to catch orphans, unreachable gates.
4. Read the generated markdown in `docs/design/sim-logs/` — that's the daily design journal.
5. When the spec stabilises, promote it into the game-content pipeline.

## encounter-sim

Deterministic per-round combat simulator. Reuses the live game's damage, hit,
dodge, crit, and element formulas via a parallel `CombatSimulationService` in
`FirstMud.Application` that takes an injected `Random` for reproducibility.

```bash
# one monster, party of two, 1000 rolls, seeded (reproducible)
dotnet run --project FirstMud.DesignTools -- encounter-sim \
    --monster timber-wolf --party 2:Fire,3:Water \
    --rolls 1000 --player-level 5 --player-element Fire --seed 42

# multi-monster pack (repeat --monster)
dotnet run --project FirstMud.DesignTools -- encounter-sim \
    --monster frost-giant --monster dragon-whelp --monster thornwood-guardian \
    --party 5:Fire,5:Water,5:Earth \
    --rolls 2000 --player-level 12 --player-element Air --seed 7
```

**Party format**: `layer:element,...` — each entry spawns a Wildfolk companion
at that bond layer with that element affinity. Omit `--party` for a solo run.

**Outputs** (per run):
- stdout pretty summary (win/loss, avg rounds, HP% left, damage-taken
  percentiles, MVP companion, difficulty band)
- `docs/design/sim-logs/encounter-sim-<ts>.json` — full structured summary
- Markdown append to `docs/design/sim-logs/<today>.md` — one-line per run

### Difficulty bands (by win-rate)

The simulator categorises encounters by aggregate win rate across all rolls:

| band        | win rate    | designer intent                                  |
|-------------|-------------|--------------------------------------------------|
| `trivial`   | `> 95%`     | chore content; xp grind only                     |
| `easy`      | `85–95%`    | intro/zone-1 content; low tension                |
| `balanced`  | `60–85%`    | sweet spot; resource-drain without frequent TPK  |
| `hard`      | `30–60%`    | gear-check; expect consumables + retries         |
| `punishing` | `< 30%`     | boss / faction-event / progression wall          |

Use `--rolls` to tighten confidence on the band — 1000 is usually enough,
2000+ for borderline tunes.

## Known rough edges

- `SimLog.WriteJson` stamps filenames as `yyyyMMdd-HHmmss` (local-ish compact)
  rather than ISO-8601 (`yyyy-MM-ddTHH-mm-ssZ`). The timestamp is UTC but not
  self-describing in the filename.
- `SimLog.AppendMarkdown` appends `title` and `body` verbatim — a caller that
  passes a `\n` in `title` will break the `## ...` header line, and a `body`
  containing triple-backticks can escape an embedded fence. Consumers currently
  pass fixed strings, so this hasn't bitten us, but it's worth sanitizing.
- JSON payloads written by `WriteJson` use `WriteIndented = true` with default
  escaping; string fields with newlines serialize as `\n` escapes correctly,
  but the Markdown sibling writes raw newlines which can interact oddly with
  daily-log collation tools.

None of these are fixed in this change — just documented.

### Danger-level scaling (`--danger-level N`)

Pass `--danger-level N` (0–10) to apply the same multipliers `MonsterFactory`
uses at runtime, so sim results match what the live game spawns at that
danger. Without the flag, the tool runs against raw `monsters.json` stats and
prints a stderr warning automatically:

```
Warning: running unscaled monster stats. Live game scales HP+power by zone
danger. Use --danger-level N to match live.
```

The applied danger level is recorded in both the JSON sim-log
(`dangerLevel`, `scaled`) and the markdown daily journal so reviewers can
tell at a glance whether a row was scaled.

The scaling math is shared with `MonsterFactory.ScaleMonster` via the
`MonsterScaling.Apply` helper in `FirstMud.Application`, with coefficients
authored in [`content/combat-curves.json`](../../content/combat-curves.json):

| coefficient        | default | what it does                                 |
|--------------------|---------|----------------------------------------------|
| `hpPerDanger`      | `0.4`   | HP = base × (1 + danger × 0.4) → 5× at d10   |
| `powerPerDanger`   | `0.3`   | Ability BasePower × (1 + danger × 0.3)       |
| `speedPerDanger`   | `1.0`   | Speed += danger × 1                          |
| `bossHpMultiplier` | `2.0`   | Boss HP doubled on top of normal scaling     |
| `bossSpeedBonus`   | `5`     | Boss speed +5 on top of normal scaling       |

Tweak the JSON to retune; both the live game and `encounter-sim` pick up the
new curves on next load. (Variance, per-monster-level overrides, pack-size
selection, and the boss-flag itself stay in `MonsterFactory` — only clean
numeric coefficients live in the JSON.)

A typical scaled-vs-unscaled compare at danger 5 against a t3 monster
(level-6 solo Fire player vs `frost-giant`, seed 42, 500 rolls) shows the
flag is doing real work:

```
unscaled: Wins 99.8%   Avg rounds 4.2   Damage taken med 65   Difficulty trivial
danger=5: Wins  0.4%   Avg rounds 4.6   Damage taken med 171  Difficulty punishing
```

## progression-sim

Full progression curve simulator. Builds on `encounter-sim`'s combat math but
models a whole N-hour play session: fresh character, starter companions,
playstyle-weighted action loop (combat / harvest / craft / salvage), per-hour
snapshots, and an end-of-run bottleneck report.

```bash
# Single playstyle, 40h run
dotnet run --project FirstMud.DesignTools -- progression-sim \
    --hours 40 --seed 42 --playstyle balanced

# All playstyles side-by-side + written markdown report
dotnet run --project FirstMud.DesignTools -- progression-sim \
    --hours 40 --seed 42 --all-playstyles --report-pass 1
```

### Playstyles

| name                 | weights (combat / harvest / craft / salvage) | intent                                        |
|----------------------|-----------------------------------------------|-----------------------------------------------|
| `balanced`           | 40 / 25 / 25 / 10                             | mixed play — the default progression yardstick |
| `combat-heavy`       | 80 /  5 / 10 /  5                             | farm XP + loot, minimal crafting               |
| `craft-heavy`        | 10 / 45 / 35 / 10                             | harvest + craft loop, combat only when forced |
| `enchanting-focused` | 45 / 30 / 15 / 10                             | prioritise enchanting-mat drops + tapers       |

### Sample output (balanced, seed 42)

```
Hour | Lvl | Craft | Salv | CompAvg | EnchMat | Gold | TopGear | Danger
-----+-----+-------+------+---------+---------+------+---------+-------
   1 |   1 |     1 |    1 |    1.00 |       0 |    0 |       0 | d4
   5 |   2 |     4 |    6 |    2.00 |       6 |    0 |       1 | d6
  10 |   3 |    12 |   14 |    2.00 |      23 |    0 |       2 | d6
  15 |   3 |    22 |   21 |    3.00 |      42 |    0 |       2 | d8
  20 |   4 |    24 |   25 |    3.00 |      65 |    0 |       2 | d8
  30 |   4 |    30 |   37 |    4.00 |     119 |    0 |       2 | d9
  40 |   5 |    34 |   47 |    4.00 |     171 |    0 |       2 | d10
```

**Columns**: player level, crafting skill, salvage skill, avg companion bond
layer, enchanting-mat pool, gold, top equipped-gear workmanship, and the
highest danger tier the current party can reliably clear (≥60% win rate across
12 probe rolls against a tier-appropriate pack).

### Outputs

- **stdout**: per-hour summary table, one per playstyle.
- **JSON**: `docs/design/sim-logs/progression-sim-<ts>.json` — full action
  log, material ledger, companion snapshot, per-hour decomposition.
- **Markdown**: appended daily journal line in `docs/design/sim-logs/YYYY-MM-DD.md`.
- **Report** (when `--report-pass N`): `docs/design/sim-reports/progression-pass-N.md`
  — summary table, bottleneck detection, proposed data tunes.

### Bottleneck detection

Each run's report flags specific scarcity walls:

- **enchanting-materials** — enchanting-mat pool (Dravenite Dust, Wyrd Shard,
  Moonbloom Petal, Fire Crystal, etc.) never reaches 5 units.
- **workmanship-5-gear** — no equipped item reaches workmanship 5.
- **companion-layer-3** — average active-party bond layer stays below 3.
- **danger-5-ceiling** — reliable danger never reaches 5 (the user's Pass-13
  reported ceiling).

### Proposed data tune format

Every bottleneck produces one or more candidate tunes shaped like content PRs:

```
loot-tables.json: bump Dravenite Dust and Wyrd Shard drop weight
                  from 1 to 3 in biome-forest, biome-wyrd, biome-swamp pools.
CraftingService.CalculateWorkmanship: divisor craftingSkill/20 → craftingSkill/10.
combat-curves.json: reduce monsterScaling.hpPerDanger from 0.4 to 0.3.
```

The creative agent should treat the list as the next iteration's candidate
patches — pick one, apply it, re-run, verify the bottleneck moved or cleared,
and promote the diff.

### Simplifications

The sim is deliberately **not** a full game replay. See the class-level docs
on `ProgressionSimulator` for the full list. The big ones:

- No auto-farm / buildings / food economy / hirelings (Phase-2).
- Travel + downtime are modelled as a constant fudge (real numbers will be
  10–20% slower than sim).
- Salvage yields are simplified to a single-component return.
- Enchanting-mat tracking is bucket-only — no imbue recipe resolution.

These hold for aggregate pacing conclusions but will lie about any specific
number to ±20%. Use the sim for *relative* comparisons across playstyles and
tunes, not absolute balance targets.

## Isolation

This project has its **own solution** (`DesignTools.slnx`) and is **not** referenced by
`FirstMud.slnx`. It imports the game's `FirstMud.Application` and `FirstMud.Domain`
projects read-only so content shapes stay in sync with the live game.
