# Progression Sim — Pass Final (Tunes A + B applied)

- Hours: **40**  Seed: **42**  Archetype: **Aether**  Start zone: **aeldran-3-portmere**
- Playstyles: `balanced`, `combat-heavy`, `craft-heavy`, `enchanting-focused`
- Baseline: post-`progression-tunes` merge (`progression-pass-tuned.md`).
- This pass adds: Tune A (6 low-tier recipe bumps) + Tune B (3 iron-gear
  recipe bumps) from `docs/design/progression-tune-proposal-v1.md`.
- Tune C (combat-curves softening) intentionally NOT applied this pass —
  out of scope for the recipe-only brief.

## Recipes touched (`content/recipes.json`)

### Tune A — low-tier floor bump (`min 1 → 3, max 4 → 5`)

| Recipe              | Before (min/max) | After (min/max) |
|---------------------|------------------|-----------------|
| LEATHER_CAP_001     | 1 / 4            | 3 / 5           |
| LEATHER_BOOTS_001   | 1 / 4            | 3 / 5           |
| STONE_AXE_001       | 1 / 4            | 3 / 5           |
| LEATHER_LEGGINGS_001| 1 / 4            | 3 / 5           |
| LEATHER_GLOVES_001  | 1 / 4            | 3 / 5           |
| BONE_RING_001       | 1 / 4            | 3 / 5           |

### Tune B — iron-gear ceiling raise (`min 3 → 4, max → 8`)

| Recipe              | Before (min/max) | After (min/max) |
|---------------------|------------------|-----------------|
| IRON_HELM_001       | 3 / 6            | 4 / 8           |
| IRON_GREAVES_001    | 3 / 7            | 4 / 8           |
| IRON_VAMBRACES_001  | 3 / 6            | 4 / 8           |

## Post-tune hour-40 snapshot (seed 42) vs `progression-pass-tuned` baseline

| Playstyle          | ReliableDanger (base → now) | TopGear (base → now) | AvgCompLayer | EnchMat |
|--------------------|----------------------------|----------------------|--------------|---------|
| balanced           | d8 → d8                    | 3 → **3**            | 5.00         | 155     |
| combat-heavy       | d8 → d8                    | 2 → **3**            | 5.00         | 76      |
| craft-heavy        | d8 → d8                    | 8 → **8**            | 3.00         | 507     |
| enchanting-focused | d8 → d8                    | 2 → **3**            | 5.00         | 138     |

- **combat-heavy gear floor lifts 2 → 3.** Tune A's targeted effect: the
  playstyle with lowest crafting skill now equips workmanship-3 leather,
  not 2.
- **enchanting-focused gear floor lifts 2 → 3.** Same mechanism — low
  craft skill benefits from recipe-min bump.
- **balanced stays at 3** — already at Tune A's new floor in the baseline
  seed; no regression, still at the lifted floor.
- **craft-heavy preserved at 8** — Tune B's Iron Helm/Greaves/Vambraces
  max raise to 8 is in range; craft-heavy was already pulling max-8 from
  its skill ceiling via other recipes (the divisor fix from the merged
  progression-tunes branch). No overshoot past 8.
- **No playstyle regressed** on gear or reliable-danger.

## Encounter-sim spot-checks

Party 3:Fire,3:Water, player level 5, 500 rolls, seed 42.

| Monster      | Danger | Wins  | HP% avg | HP% min | Damage med | Label    |
|--------------|--------|-------|---------|---------|------------|----------|
| timber-wolf  | 3      | 100%  | 100%    | 78%     | 0          | trivial  |
| frost-giant  | 5      | 100%  | 66%     | 4%      | 48         | trivial  |

Pre-tune baseline (from proposal §5): d3 trivial/100% HP, d5 trivial/47% HP.

- **d3 stays trivial with 100% HP** — gear bumps did NOT push previously
  `balanced`-labelled d3 content into trivial (d3 was already trivial
  pre-tune). No over-trivialisation.
- **d5 HP% lifts 47% → 66%** — meaningful quality-of-life improvement
  (matches proposal's predicted ~65% post-A+B survivability). The gap
  between sim's "reliable" and user's "pyrrhic" narrows.

## Tunes backed off

**None.** No playstyle regressed on reliable-danger or gear; encounter-sim
d3 did not over-trivialise. Both Tune A and Tune B land as proposed.

## Build/test gate

- `dotnet build FirstMud.slnx` — **0 errors / 0 warnings** ✓
- `dotnet test FirstMud.slnx` — **417 passed** (401 unit + 16 integration) ✓
- `dotnet test tools/design/DesignTools.slnx` — **26 passed** ✓

## Follow-up (deferred to next pass)

- Tune C (`combat-curves.json` `hpPerDanger` 0.4→0.3, `powerPerDanger`
  0.3→0.25) — the proposal's highest-leverage item for the user's
  "can't clear d5" play-test feeling. Left for a dedicated curves pass so
  its encounter-sim impact can be isolated from recipe changes.
- `CraftingService.CalculateWorkmanship` is already at `/10` from the
  merged progression-tunes branch.
