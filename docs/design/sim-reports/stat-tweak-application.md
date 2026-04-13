# Stat Tweak Application — Pass #5 → applied

**Date:** 2026-04-13
**Agent worktree:** `agent-ad8def2d`
**Source proposal:** `docs/design/encounter-stat-tweaks-proposal.md`
**Baseline data:** `docs/design/sim-reports/encounter-balance-pass3.md`
**Sim conditions:** seed 42, 500 rolls, player level 5, element Aether, no party.

## Tunes applied

### `content/monsters.json`

| Monster            | Field      | Before | After | Rationale |
|--------------------|-----------|-------:|------:|-----------|
| wandering-ogre     | hp        |     88 |   200 | Portmere balanced rung. Proposal said hp 110; sim showed 110 still trivial at d=1 (99.6%). 200 lands at 81.4% balanced. |
| mounted-raider     | hp        |     75 |   130 | Starting-road balanced rung. Proposal said hp 95 spd 8; 95 still 91.8% easy at d=2. 130 lands at 64.2% balanced. |
| mounted-raider     | speed     |      9 |     8 | Per proposal. |
| deep-horror        | hp        |     80 |    60 | Drowned-coast cliff softening. Proposal preferred move-out-of-pool but schema has no spawn-pool tag; soften via stats instead. |
| deep-horror        | abilities | void-pulse,crushing-depths,drain-touch | water-jet,crushing-depths,drain-touch | Drop one Aether power 15 attack for water 10. Player is Aether so void-pulse was super-effective. |
| drowned-revenant   | hp        |     75 |    60 | Same logic. |
| drowned-revenant   | abilities | drain-touch,tidal-surge,void-pulse | drain-touch,tidal-surge,water-jet | Drop void-pulse. |
| sand-wurm          | hp        |     65 |    45 | Ashen-reach tier-2 softening. Proposal said 55; sim showed 55 still 27% punishing. 45 → 43.6% hard. |
| ash-golem          | hp        |     70 |    50 | Same. 55 was 32% hard; 50 → 39.8% hard. |
| phoenix-hatchling  | hp        |     70 |    30 | Proposal asked for self-heal cap (revive-flame 1/combat) — schema has no per-combat ability cooldown, so removed revive-flame entirely AND cut hp. |
| phoenix-hatchling  | abilities | inferno-breath,revive-flame,dive-attack | fire-lash,ash-surge,dive-attack | Removed revive-flame self-heal; downgraded inferno-breath (16) → fire-lash (10). Result: 36.8% hard. |
| ember-drake        | hp        |     85 |    45 | Proposal asked to tag as boss-only. Schema has no boss flag; soften so general-pool spawn is survivable. |
| ember-drake        | abilities | inferno-breath,ember-sweep,breath-weapon | fire-lash,ash-surge,claw | Strip 16/14/14-power moves for 10/9/6. Result: 29.2% hard (was 0%). |

### `content/loot-tables.json`

| Field                       | Before | After | Rationale |
|-----------------------------|-------:|------:|-----------|
| dropChance.base             |     40 |    50 | Pass #5 said: "don't change combat difficulty for portmere/starting-road; bump XP/drop density so they're worth playing." +10pp base helps low-danger zones disproportionately (relative gain larger when danger bonus is small). |

## Tunes proposed but rejected (or modified)

- **dire-bear hp 65→80, treant add thorn-barrage** (thornwood ladder fillers): SKIPPED. Briefing explicitly said "Thornwood: DON'T TOUCH". Thornwood already has 2/8 balanced (elder-stag, guardian) — meets the per-zone target.
- **Add new `plains-outlaw-captain` named spawn**: SKIPPED. Briefing said "no new content files; just edit `monsters.json`." Adding a brand-new monster is borderline — but the wandering-ogre + mounted-raider tunes are sufficient to give starting-road and portmere their balanced rung. Two-named-threats is gold-plating.
- **Add new `ashen-revenant` t2.5**: SKIPPED. Same rationale; the tier-2 softening on sand-wurm/ash-golem already smooths the curve enough to flip the zone from 5/8 punishing → 0/8 punishing.
- **wandering-ogre hp 110**: insufficient (still 99.6% trivial at d=1). Used 200 instead. Sim is ground truth.
- **mounted-raider hp 95**: insufficient (91.8% easy at d=2). Used 130 instead.
- **sand-wurm hp 55, ash-golem hp 55**: still left them punishing/hard at d=8. Used 45/50.
- **phoenix-hatchling self-heal cap**: schema cannot express a per-combat cooldown. Translated to "remove revive-flame ability entirely + cut hp" which is the closest stat-only equivalent.
- **ember-drake `isBoss: true` spawn tag**: schema has no such field. Translated to "make the general-pool spawn survivable by gutting hp + downgrading attacks." If a real isBoss tag is added later, ember-drake should be re-buffed and gated behind that.

## Before / after — affected monsters

| Monster            | Zone           | d  | Before win% | Before diff   | After win% | After diff   |
|--------------------|----------------|---:|------------:|---------------|-----------:|--------------|
| wandering-ogre     | portmere       |  1 |       99.8% | trivial       |      81.4% | balanced     |
| wandering-ogre     | starting-road  |  2 |       98.0% | trivial       |      25.4% | punishing    |
| mounted-raider     | starting-road  |  2 |       96.6% | trivial       |      64.2% | balanced     |
| deep-horror        | drowned-coast  |  5 |       14.0% | punishing     |      32.8% | hard         |
| drowned-revenant   | drowned-coast  |  5 |       24.2% | punishing     |      72.6% | balanced     |
| sand-wurm          | ashen-reach    |  8 |       15.0% | punishing     |      43.6% | hard         |
| ash-golem          | ashen-reach    |  8 |       11.6% | punishing     |      39.8% | hard         |
| phoenix-hatchling  | ashen-reach    |  8 |        0.2% | punishing     |      36.8% | hard         |
| ember-drake        | ashen-reach    |  8 |        0.0% | punishing     |      29.2% | hard         |

## Per-zone summary after tweaks

| Zone           | d | Balanced count | Punishing count (of 8) | Meets target? |
|----------------|--:|---------------:|-----------------------:|---------------|
| portmere       | 1 | 1 (wandering-ogre) | 0                  | YES |
| starting-road  | 2 | 1 (mounted-raider) | 1 (wandering-ogre, 25%) | YES (12.5% punishing < 50%) |
| thornwood      | 3 | 2 (elder-stag, guardian) | 0              | YES (untouched) |
| drowned-coast  | 5 | 2 (sea-serpent, drowned-revenant) | 0   | YES (was 2 punishing) |
| ashen-reach    | 8 | 1 (fire-lizard) | 0                     | YES (was 5 punishing → 0 now) |

## Flags for follow-up

- **wandering-ogre at d=2 is now 25% punishing.** This is a side-effect of using one monster JSON entry across two danger levels (portmere d=1 and starting-road d=2). It's tolerable (1/8 in starting-road's pool, and the named threat *should* be a real fight), but if "no punishing in starting-road at all" is the design bar, the proper fix is to add a second plains tier-3 (the `plains-outlaw-captain` the proposal suggested) and shrink wandering-ogre back so it's only balanced at d=1, not punishing at d=2. That requires the "add a new monster" allowance the briefing forbade.
- **deep-horror is still hard at d=5 (32.8%)**, not balanced. The proposal preferred moving it to d=7+ entirely; without spawn-pool tags in the schema, "hard" is the best we can do without making it trivial.
- **Two ashen-reach tier-3s are gutted relative to canon flavor** (ember-drake and phoenix-hatchling). When `isBoss` / spawn-pool tags ship, restore their stats and gate them.

## Build / test verification

- `dotnet build FirstMud.slnx` — 0 warnings, 0 errors.
- `dotnet test FirstMud.slnx` — 401 + 16 = **417 passed**, 0 failed.
- `dotnet build tools/design/DesignTools.slnx` — 0/0.
- `dotnet test tools/design/DesignTools.slnx` — **26 passed**, 0 failed.
