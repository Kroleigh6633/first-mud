# Progression Sim — Pass 1

- Hours: **40**  Seed: **42**  Archetype: **Aether**  Start zone: **aeldran-3-portmere**
- Playstyles: `balanced`, `combat-heavy`, `craft-heavy`, `enchanting-focused`

## Summary (final hour)

| Playstyle | Lvl | Craft | Salv | AvgCompLayer | EnchMat | TopGear | ReliableDanger |
|-----------|-----|-------|------|--------------|---------|---------|----------------|
| balanced | 5 | 34 | 47 | 4.00 | 171 | 2 | d10 |
| combat-heavy | 5 | 11 | 19 | 5.00 | 62 | 2 | d8 |
| craft-heavy | 4 | 88 | 52 | 3.00 | 467 | 8 | d8 |
| enchanting-focused | 5 | 31 | 40 | 4.00 | 197 | 4 | d10 |

## Reliable-Danger curve (by hour, baseline playstyle)

Playstyle: `balanced`

| Hour | Lvl | Danger |
|------|-----|--------|
| 1 | 1 | d4 |
| 5 | 2 | d6 |
| 10 | 3 | d6 |
| 15 | 3 | d8 |
| 20 | 4 | d8 |
| 25 | 4 | d8 |
| 30 | 4 | d9 |
| 35 | 4 | d9 |
| 40 | 5 | d10 |

## Bottleneck detection

### `balanced`

- **workmanship-5-gear** — first choke at hour 40: no equipped item reached workmanship 5 in 40h (final=2). CraftingSkill/20 bonus too slow.

### `combat-heavy`

- **workmanship-5-gear** — first choke at hour 40: no equipped item reached workmanship 5 in 40h (final=2). CraftingSkill/20 bonus too slow.

### `craft-heavy`

- (none detected within sim horizon)

### `enchanting-focused`

- **workmanship-5-gear** — first choke at hour 40: no equipped item reached workmanship 5 in 40h (final=4). CraftingSkill/20 bonus too slow.

## Proposed data tunes

- `CraftingService.CalculateWorkmanship`: divisor `craftingSkill / 20` → `craftingSkill / 10` (doubles skill contribution to workmanship).
- `recipes.json`: raise `baseWorkmanshipMax` for tier-2+ recipes (Iron Helm/Greaves/Vambraces) from `6` to `8` so mid-skill crafts can hit workmanship 5-6.

