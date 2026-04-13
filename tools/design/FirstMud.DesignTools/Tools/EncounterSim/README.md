# encounter-sim (scaffold)

Forecasts encounter difficulty via Monte-Carlo combat simulation.

## Planned CLI
```
encounter-sim --monster <id> --party <companion-id,...> --rolls <N>
```

## Planned outputs
- Win rate, mean rounds, mean HP remaining, death rate per party member
- Sensitivity table (remove each companion, re-run)

## Status
Scaffold. Probes for `content/monsters.json`, handles absence gracefully.
