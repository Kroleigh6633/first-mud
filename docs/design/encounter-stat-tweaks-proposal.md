# Encounter Stat Tweaks — Proposal

**Source data:** `docs/design/sim-reports/encounter-balance-pass3.md`
**Status:** draft — not applied. Content agent should reconcile with curve
designer before editing `content/monsters.json`.

Design target: each zone has at least one `balanced` (40–60% win rate vs a
solo level-5 Aether baseline) encounter, and no zone is dominated by
`punishing` (<10% win rate) cells the player cannot progress through at
expected level.

All proposed deltas are **on the monster stat**, not the scaling curve. The
curve is doing roughly right work on tier-2+ creatures — the problem is
tier-0/tier-1 lows at low danger and tier-3 cliffs at high danger.

## portmere (plains, d=1) — add a balanced rung

Zone is a commercial hub; pure-trivial is defensible. But the tutorial
"ambient threat" is weak. Minimal tweak:

- **`wandering-ogre` (t3, hp 88):** Bump hp to **110** and add a t2.5
  marker so it's not penalized by the t3-curve shaping. Expected effect:
  slide from 99.8% → ~75% (balanced). This is the one "rare big threat"
  of Portmere; keeping the rest trivial is correct.

Alternative: leave portmere trivial, and rely on starting-road for the
first balanced encounter.

## starting-road (plains, d=2) — mandatory balanced rung

This is the new-player first-combat zone. Current best is `mounted-raider`
at 96.6%. Proposals (pick one, not all):

- **`mounted-raider` (t3, hp 75, spd 9):** Bump hp to **95**, drop spd to
  8. Expected: 96.6% → ~70%. Most canon-coherent (a mounted raider
  *should* be a real fight for a lvl-5 solo).
- **`rogue-knight` (t2, hp 60):** Bump hp to **80** and swap `brutal-
  smash` for a t3 ability (`cavalry-charge`?). Expected: 100% → ~65%.
- **Add a new `plains-outlaw-captain` t2.5 entry** — hp 70, spd 8, level
  3, abilities `[trick-slash, swift-blow, shield-bash]`. Drops into the
  d=2 spawn table as the "named" threat.

Recommended: **mounted-raider hp bump**, plus add `plains-outlaw-captain`
as a named-spawn for worldbuilding richness. Two targets beats one.

## thornwood (forest, d=3) — already balanced; address tier-1 softness

`elder-stag` and `thornwood-guardian` land balanced. However the
tier-0/tier-1 pool (timber-wolf through treant) remains trivial at d=3,
which makes the zone feel uneventful unless the player *happens* to spawn
against the two named creatures.

- **`dire-bear` (t2, hp 65, spd 5):** Bump hp to **80**. Expected:
  99.0% → ~85% (easy but not trivial).
- **`treant` (t2, hp 75, spd 3):** Add `thorn-barrage` (AoE) to move it
  from 98.6% → ~80%.
- **`thornweaver-spider` (t1, hp 35):** No change at d=3, but earmark for
  buff if the zone's companion-party results show it melts too fast.

## drowned-coast (water, d=5) — balanced rung present; soften tier-3 cliff

`sea-serpent` 68.6% balanced is good. `kraken-spawn` 37.2% hard is
acceptable. But `deep-horror` (14%) and `drowned-revenant` (24%) are cliff
encounters that a d=5 player has no business meeting until a d=6+ zone.

Two options:

- **Move deep-horror and drowned-revenant out of d=5 spawn pool** — tag
  them for d=7+ (Maw Borderlands) only. No stat change. Cleanest fix.
- **Or: reduce their element-Aether advantage against the player's Aether
  attacks** — add a weakness tag. This is a systems-level tweak.

Recommended: **move to d=7+**. Drowned-coast is a mid-game zone; saving
the worst horrors for later is a coherent threat-escalation beat.

## ashen-reach (desert, d=8) — rebalance the whole tier-2+ pool

This is the worst-behaved zone. Five of eight monsters land punishing at
d=8. A level-5 solo baseline is *not* the target here (d=8 content assumes
a later-game player), but the gap between "balanced" (fire-lizard, 73.6%)
and "0% win" (ember-drake) is too wide.

- **`sand-wurm` (t2, hp 65):** 15% win. Reduce hp to **55** (scaling does
  the heavy lifting). Expected: 15% → ~35% (hard).
- **`ash-golem` (t2, hp 70):** 11.6%. Same treatment — drop hp to **55**.
- **`phoenix-hatchling` (t3, hp 70):** 0.2%. The `revive-flame` self-heal
  is the killer. Cap self-heal to once per combat. Expected: 0.2% → ~15%.
- **`ember-drake` (t3, hp 85):** 0.0%. Boss-tier at d=8. Move to `isBoss:
  true` spawn tag so it only appears in named encounters, not the general
  pool.

Alt proposal: **add a t2.5 `ashen-revenant`** — hp 65, spd 6, level 4,
abilities `[ash-surge, phantom-strike, null-field]`, element Aether. Sits
between fire-lizard (balanced) and sand-wurm (hard) to give the zone a
smoother difficulty curve.

## Test matrix for verification

After any tweak, re-run:

```bash
bash tools/design/run-pass3-sweep.sh docs/design/sim-reports/pass3-rerun-<date>.txt
```

Acceptance criteria:

- Every zone has ≥1 `balanced` cell.
- No zone has >50% of its pool rated `punishing` at its scaled danger.
- Delta between zone d=0 baseline and d=N scaled stays monotonic in zone
  danger — i.e. higher-danger zones remain harder at baseline.

## Coordination note

These proposals touch `content/monsters.json`. A single migration pass
can apply all of them; the shape is already 1:1 with the current schema
(add fields where marked, otherwise numeric edits). No code changes
required.
