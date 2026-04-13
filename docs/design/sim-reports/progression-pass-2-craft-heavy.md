# Progression Sim — Pass 2 — `craft-heavy`

- Hours: **40**  Seed: **42**  Archetype: **Aether**  Start zone: **aeldran-3-portmere**
- Action weights: combat 10 / harvest 45 / craft 35 / salvage 10

## Hour-40 snapshot

| Lvl | Craft | Salv | AvgCompLayer | EnchMat | TopGear | ReliableDanger |
|-----|-------|------|--------------|---------|---------|----------------|
| 4   | 88    | 52   | 3.00         | 467     | 8       | d8             |

## Bottleneck

- **(none detected within sim horizon.)** Craft-heavy is the only playstyle
  that clears the workmanship-5 wall — at craft=88, bonus is +4, iron gear
  clamps up to `baseWorkmanshipMax=7` (live) / 8 (with Tune B).

## Playstyle-specific notes

- **Character level caps at 4** — not 5. Craft-heavy trades XP for
  workmanship. This is the **tradeoff that answers the "does a playstyle
  escape the wall?" question**: craft-heavy escapes the workmanship wall but
  pays with a level / companion / reliable-danger cap.
- Danger ceiling d8 — same as combat-heavy. So the two polar playstyles
  both cap at d8 by different routes.
- Enchanting-mat surplus (467) is unused — the sim has no imbue loop, but a
  live game with taper recipes would have an enormous over-supply from this
  playstyle. Future tune candidate: trim enchanting-mat drop weights so
  craft-heavy hoarding does not trivialise a later imbue economy.

## Implication for the audit

Craft-heavy *does* escape the workmanship wall, so the wall is not an
absolute floor — it is **a cost-of-time wall**. The right fix is therefore
to **cheapen workmanship per unit time** for non-craft playstyles, not to
raise absolute caps (which would overshoot craft-heavy).
