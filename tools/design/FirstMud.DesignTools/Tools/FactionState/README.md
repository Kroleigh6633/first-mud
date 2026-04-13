# faction-state (scaffold)

Renders the state of all factions at a given timestep or quest-beat marker.

## Planned inputs
- `lore/factions.md` — canonical faction identities and starting relationships.
- `content/faction-timeline.json` (future) — ordered events with deltas:
  - treaties formed / broken
  - leadership changes
  - quest-chain completions that shift relations

## Planned CLI
```
faction-state --at <marker>
faction-state --at Q3.after-harken
```

## Planned outputs
- Relationship matrix (per-faction, integer -100..100)
- Active treaties / conflicts
- Ambient tension level per region

## Status
Scaffold only. Loads faction lore file if present and prints load summary.
