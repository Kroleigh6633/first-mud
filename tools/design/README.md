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
dotnet run --project FirstMud.DesignTools -- encounter-sim --monster wolf --party sera,varn --rolls 1000
dotnet run --project FirstMud.DesignTools -- economy-sim --hours 48 --scenario farming
```

## Commands

| command           | status   | key flags                                          |
|-------------------|----------|----------------------------------------------------|
| `scenario-player` | full     | `--fixture <path>` `--choose id,id,id`             |
| `dialogue-lint`   | full     | `--fixture <path>`                                 |
| `faction-state`   | scaffold | `--at <marker>`                                    |
| `encounter-sim`   | scaffold | `--monster <id>` `--party <csv>` `--rolls <N>`     |
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

## Isolation

This project has its **own solution** (`DesignTools.slnx`) and is **not** referenced by
`FirstMud.slnx`. It imports the game's `FirstMud.Application` and `FirstMud.Domain`
projects read-only so content shapes stay in sync with the live game.
