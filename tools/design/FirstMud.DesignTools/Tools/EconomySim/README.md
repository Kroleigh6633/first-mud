# economy-sim (scaffold)

Forecasts resource flows over time for different playstyles.

## Planned CLI
```
economy-sim --hours <N> --scenario <balanced|farming|combat|market>
```

## Planned outputs
- Per-hour production / consumption / net for food, herbs, coin
- Surplus or shortage events
- Morale projection (ties into food economy design note)

## Status
Scaffold. Loads `IContentProvider` and probes for recipes / buildings files.
