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
| `scenario-player` | full     | `--fixture <path>` `--choose id,id,id` `--allow-underflow` |
| `dialogue-lint`   | full     | `--fixture <path>`                                 |
| `faction-state`   | scaffold | `--at <marker>`                                    |
| `encounter-sim`   | full     | `--monster <id>` (repeatable) `--party <L:elem,...>` `--rolls <N>` `--player-level <L>` `--player-element <E>` `--seed <S>` |
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
        { "text": "...", "next": "insider", "requiresReputation": { "faction": "wytchwood", "min": 25 } } ] },
    { "id": "work", "text": "...", "terminal": true }
  ]
}
```

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

**Known caveat**: `encounter-sim` uses raw `monsters.json` stats. The live
game's `MonsterFactory` applies danger-level scaling (HP ×1.4–5×, power
×1.3–4×, boss bonuses) on top of these base stats. Tune encounters by
`--player-level` / party layer to explore design space, but remember a t3 boss
in live combat at danger 10 will be ~2× the raw stats you see here.

## Isolation

This project has its **own solution** (`DesignTools.slnx`) and is **not** referenced by
`FirstMud.slnx`. It imports the game's `FirstMud.Application` and `FirstMud.Domain`
projects read-only so content shapes stay in sync with the live game.
