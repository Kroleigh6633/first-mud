# Progression Sim — Pass 2 — `balanced`

- Hours: **40**  Seed: **42**  Archetype: **Aether**  Start zone: **aeldran-3-portmere**
- Action weights: combat 40 / harvest 25 / craft 25 / salvage 10

## Hour-40 snapshot

| Lvl | Craft | Salv | AvgCompLayer | EnchMat | TopGear | ReliableDanger |
|-----|-------|------|--------------|---------|---------|----------------|
| 5   | 34    | 47   | 4.00         | 171     | 2       | d10            |

## Curve

| Hour | Lvl | Danger |
|------|-----|--------|
| 1    | 1   | d4     |
| 5    | 2   | d6     |
| 10   | 3   | d6     |
| 15   | 3   | d8     |
| 20   | 4   | d8     |
| 25   | 4   | d8     |
| 30   | 4   | d9     |
| 35   | 4   | d9     |
| 40   | 5   | d10    |

## Bottleneck

- **workmanship-5-gear** — first choke at hour 40. No equipped item reached
  workmanship 5 in 40h (final=2). Sim formula: `avgIn(=1) + craftingSkill/20`
  → at craft=34, output is 2, clamped up by recipe `baseWorkmanshipMin`.
  Current low-tier recipes have `min: 1–2`, so equipped gear floor stays at 2.

## Observation vs Pass 1

Identical output to Pass 1 (deterministic — same seed, same data, no tool
changes in between). Confirms Pass 1's diagnosis is stable, not a seed
artefact.

## Note on reliable-danger d10

The probe reports d10 "reliable" because aggregate win rate across 12 rolls
is ≥60%. See cross-check in `progression-tune-proposal-v1.md` §5: at d7+
wins are pyrrhic (player HP% avg 19%). This is the gap between the sim's
win/loss metric and the user's play-test felt difficulty.
