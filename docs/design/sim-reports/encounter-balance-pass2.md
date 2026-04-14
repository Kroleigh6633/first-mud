# Encounter Balance — Pass 2 (Creative Pass #4)

**Run date.** 2026-04-13. Worktree `agent-ac7f00bc`.

**Tool.** `tools/design/FirstMud.DesignTools` → `encounter-sim`. Seed `42`,
500 rolls per matchup. Solo player, element `Aether` (neutral pivot — no
elemental advantage either direction) at the player level closest to each
zone's narrative tier.

**Caveat — `--danger-level` is NOT in this worktree.** The Pass-3 spec
called for re-running balance with the live game's danger-level scaling.
The flag is not present in this branch's `EncounterSimCommand` (verified by
grep). All sims below use **raw `monsters.json` stats** as the README
explicitly warns: in live combat the `MonsterFactory` applies HP ×1.4–5×
and power ×1.3–4× per zone danger level. **Every "trivial" result below
should be re-read as: "trivial at base stats; live danger-scaling needed
to confirm."** When the flag merges, re-run this report with
`--danger-level 1, 3, 5, 8, 10` per zone tier and overwrite §Results.

## Method

For each of the five Pass-1 zones, the entire palette monster set was
simulated at the recommended introductory player level for that zone:

| Zone | Biome | Player level | Party |
|---|---|---|---|
| 1 Eastmile Stretch | plains | 1 | solo |
| 2 Eastmile Village | plains | 1 | solo |
| 3 Thornwood Verge | forest | 2 | solo |
| 4 Marchkeep | mountain | 3 | solo |
| 5 Gravenmarsh | swamp | 3 | solo |

Solo runs are the strict-floor calibration: a party of L:Fire,L:Earth
bond-2 companions trivializes everything except plains-tier-3, so solo is
the only gradient that surfaces useful spread.

## Results

### Zone 1–2 (plains, solo pl=1)

| Monster | Tier | Win % | Avg rounds | HP min | Band |
|---|---|---|---|---|---|
| cave-rat       | t0 | 100.0 | 1.6 | 73 | trivial |
| stray-dog      | t0 | 100.0 | 1.8 | 58 | trivial |
| highway-bandit | t1 | 100.0 | 2.3 | 33 | trivial |
| wild-horse     | t1 | 100.0 | 2.3 | 24 | trivial |
| rogue-knight   | t2 |  97.0 | 3.5 |  1 | trivial |
| pack-alpha-wolf| t2 |  97.6 | 3.4 |  1 | trivial |
| **wandering-ogre** | t3 |  70.6 | 4.6 |  1 | **balanced** |
| **mounted-raider** | t3 |  77.0 | 4.4 |  1 | **balanced** |

Zone 1–2 has 2/8 monsters in `balanced`. Both t3. Survives the
"every-zone-needs-one-balanced" check.

### Zone 3 (forest, solo pl=2)

| Monster | Tier | Win % | Avg rounds | HP min | Band |
|---|---|---|---|---|---|
| timber-wolf        | t0 | 100.0 | 1.6 | 70 | trivial |
| wild-boar          | t0 | 100.0 | 1.8 | 66 | trivial |
| thornweaver-spider | t1 | 100.0 | 2.0 | 59 | trivial |
| forest-bandit      | t1 | 100.0 | 1.8 | 49 | trivial |
| dire-bear          | t2 | 100.0 | 3.1 | 10 | trivial |
| treant             | t2 | 100.0 | 3.3 | 14 | trivial |
| elder-stag         | t3 |  95.2 | 3.5 |  1 | trivial |
| thornwood-guardian | t3 |  96.4 | 4.3 |  1 | trivial |

**FLAG: zero `balanced` palette monsters at narrative pl.** Calibration
sub-run at pl=1 puts elder-stag (66.4%) and thornwood-guardian (68.0%)
into `balanced`. Conclusion: either the zone's recommended pl should
**drop to 1** (contradicts narrative — Verge is post-Eastmile) **or**
forest tier-3 needs an HP/power bump in the +30–40% range to compensate
for the missing danger-level scaler.

### Zone 4 (mountain, solo pl=3)

| Monster | Tier | Win % | Avg rounds | HP min | Band |
|---|---|---|---|---|---|
| mountain-goat | t0 | 100.0 | 1.1 | 84 | trivial |
| rock-beetle   | t0 | 100.0 | 1.1 | 85 | trivial |
| stone-troll   | t1 | 100.0 | 2.1 | 53 | trivial |
| mountain-lion | t1 | 100.0 | 2.0 | 54 | trivial |
| wyvern        | t2 | 100.0 | 2.4 | 35 | trivial |
| rock-golem    | t2 | 100.0 | 2.9 | 32 | trivial |
| dragon-whelp  | t3 |  99.6 | 3.3 |  1 | trivial |
| frost-giant   | t3 | 100.0 | 3.3 |  4 | trivial |

**FLAG: zero `balanced`.** Calibration at pl=1 → dragon-whelp 65.0%,
frost-giant 70.0% → both balanced. Marchkeep is supposed to be the
**first hard faction-pressure zone**; raw stats don't deliver. Same
mitigation as Zone 3.

### Zone 5 (swamp, solo pl=3)

| Monster | Tier | Win % | Avg rounds | HP min | Band |
|---|---|---|---|---|---|
| swamp-rat     | t0 | 100.0 | 1.1 | 88 | trivial |
| leech-swarm   | t0 | 100.0 | 1.1 | 89 | trivial |
| bog-wraith    | t1 | 100.0 | 1.5 | 65 | trivial |
| marsh-crawler | t1 | 100.0 | 1.8 | 73 | trivial |
| moor-stalker  | t2 | 100.0 | 2.2 | 51 | trivial |
| poison-toad   | t2 | 100.0 | 2.1 | 58 | trivial |
| swamp-hydra   | t3 | 100.0 | 3.3 | 18 | trivial |
| grave-wight   | t3 | 100.0 | 3.1 | 35 | trivial |

**FLAG: zero `balanced`.** Calibration at pl=1 → swamp-hydra 76.2%
balanced; grave-wight 94.0% easy. The swamp-hydra is the only true mini-
boss raw-stat-balanced encounter; grave-wight needs a buff to deserve
its tier-3 slot if danger-level scaling does not land.

## Zones requiring designer attention

**Three of five zones produce zero `balanced` palette monsters at the
narratively-correct player level: Zone 3 (forest), Zone 4 (mountain),
Zone 5 (swamp).** Recommended interventions, in priority order:

1. **Land `--danger-level` in encounter-sim.** This is the upstream root
   cause; without it the report cannot validate live-combat balance.
   Until then, all zones except 1–2 read trivial regardless of stats.
2. **If live-game danger-scaling is the answer**, the encounter-sim caveat
   should be re-printed as a designer-aid table (per-tier multiplier
   reference) so future creative passes can hand-derive expected bands.
3. **If raw stats must hold their own** (no danger-scaling), bump tier-3
   HP +30–40% and power +20–25% for forest, mountain, swamp palettes.

## Contradictions vs. drafted design

- `aeldran-zones.md` calls Marchkeep the "first hard faction-pressure
  zone." Combat sim says Marchkeep palette is trivial at pl=3. This is a
  contradiction *only* if the player is expected to fight the palette;
  Marchkeep's drafted hook is **social-first** (papers, brooch, refuse
  Hollemar's commission). The combat palette is incidental. Reconcile by
  reading Marchkeep monsters as **encounter color**, not gating combat.
  Update `encounter-palette.md` Marchkeep section: tag entries with
  `pressure: social` to disambiguate.
- `encounter-palette.md` calls thornwood-guardian a Rare-tier encounter.
  Sim agrees raw stats *should* be hard, but at pl=2 it's a 96% win.
  Either the guardian is for pl 5+ players (re-classify the palette
  ladder) or the stats need the bump above.

## JSON sidecars

- `docs/design/sim-logs/encounter-sim-*.json` — written by tool, one per
  matchup (32 files this run, plus 12 from calibration sub-run).

## Hand-off

This report is the authoritative balance read until `--danger-level` is
merged. After merge, **re-run with the flag at each zone's intended
danger band** and overwrite §Results above. Do not edit `monsters.json`
on the basis of this report alone — wait for the danger-scaling read.
