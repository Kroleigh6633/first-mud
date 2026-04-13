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
| `aeldran-zones.md` | draft | world-aeldran, factions, reputation, economy-and-crafting | Does the "Starting Road" have a canonical name? Where is the Rider Post HQ physically? Are the Compact Cities three discrete zones or one metro zone? |
| `quest-chains.md` | draft | factions, reputation, magic-system, worlds-and-portals | Can a Rider formally refuse a King's commission without termination? Does Aldric Caervorn know the Weaveborn exists by name yet? Are AI players eligible to "claim" these chains or only human? |
| `faction-threads.md` | draft | factions, reputation, world-aeldran | Is the Ashen Court's silence a trigger event (auto-fires at story beat X) or strictly reputation-gated? Does the Fairgean inland sighting have a fixed in-world date or float? |
| `npc-voices.md` | draft | factions, reputation, companions | Do NPCs have literacy/dialect markers or is that left to per-region style guides? Is there a canonical naming convention per region (Welsh-derived, etc.)? |
| `encounter-palette.md` | draft | combat, world-aeldran, magic-system, worlds-and-portals | Are Wildfolk hostile-by-default in some zones, or only ever neutral/allied? Can Ardweld automata be captured (Summoners War layer) or only destroyed? |

## Run log

- **2026-04-13** — Initial pass. All six deliverables authored at draft. No canon files modified. Hand-off candidate: `encounter-palette.md` — flavor+intent blocks are shaped to migrate 1:1 into `content/monsters.json` entries once stat schema lands.
