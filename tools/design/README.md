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
  FirstMud.DesignTools.Tests/
  fixtures/
    quest-sample.json
    dialogue-sample.json
```

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
```

## Commands

| command           | status   | key flags                                          |
|-------------------|----------|----------------------------------------------------|
| `scenario-player` | full     | `--fixture <path>` `--choose id,id,id`             |
| `dialogue-lint`   | full     | `--fixture <path>`                                 |
| `faction-state`   | scaffold | `--at <marker>`                                    |
| `encounter-sim`   | full     | `--monster <id>` (repeatable) `--party <L:elem,...>` `--rolls <N>` `--player-level <L>` `--player-element <E>` `--seed <S>` `--danger-level <0..10>` |
| `economy-sim`     | scaffold | `--hours <N>` `--scenario <name>`                  |

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
Requires: `flags[]`, `items[{key,amount}]`, `reputation{faction: min}`.

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

## Isolation

This project has its **own solution** (`DesignTools.slnx`) and is **not** referenced by
`FirstMud.slnx`. It imports the game's `FirstMud.Application` and `FirstMud.Domain`
projects read-only so content shapes stay in sync with the live game.
