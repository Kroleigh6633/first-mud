# Progression Tune Proposal v1

Audience: user (approve-in-one-pass review).
Inputs: `progression-pass-1.md`, `progression-pass-2-*.md`, 3 `encounter-sim`
cross-checks.
Scope: **data-only tunes**. The structural formula fix
(`CraftingService.CalculateWorkmanship`: `craftingSkill/20 → /10`) is a
code change and is called out separately in §6 — it's the single highest-leverage
move, but it's out of creative's mandate.

## 1. The gap we're closing

- User play-test (Pass-13): "can't reliably clear past danger 5."
- `progression-sim` Pass 1 / Pass 2: **workmanship wall at 2** for every
  playstyle except craft-heavy. Sim says reliable d10 for balanced but
  the win metric is win/loss — it ignores pyrrhic HP%.
- `encounter-sim` cross-check (lvl-5, party 3:Fire + 3:Water, frost-giant,
  seed 42, 500 rolls):
  - danger 5 → 100% win but player HP% avg 47% (min 0%)
  - danger 7 → 99% win, HP% avg 19% (min 0%), damage-taken med 158
  - "Difficulty: trivial" label is misleading for the player's subjective
    experience.

The gap between sim's `reliable d10` and user's `can't clear d5` is
**gear workmanship**. The sim counts wins; the player counts HP.

## 2. Playstyle table (hour 40, seed 42)

| Playstyle          | Lvl | Craft | AvgCompLayer | TopGear | ReliableDanger |
|--------------------|-----|-------|--------------|---------|----------------|
| balanced           | 5   | 34    | 4.00         | **2**   | d10            |
| combat-heavy       | 5   | 11    | 5.00         | **2**   | d8             |
| craft-heavy        | 4   | 88    | 3.00         | **8**   | d8             |
| enchanting-focused | 5   | 31    | 4.00         | **4**   | d10            |

**Craft-heavy escapes the wall by paying level + companion bond.** The
other three are gear-starved. This is a *cost-of-time* wall, not an absolute
cap — so the right fix is to cheapen workmanship per unit time for
non-craft playstyles, not to raise absolute caps.

## 3. Top 3 tunes (ranked by leverage vs risk)

### Tune A — Raise low-tier recipe `baseWorkmanshipMin` from 1 → 3, `Max` from 4 → 5

**Files**: `content/recipes.json`

Affected recipes: `LEATHER_CAP`, `LEATHER_BOOTS`, `STONE_AXE`,
`LEATHER_LEGGINGS`, `LEATHER_GLOVES`, `BONE_RING` (all currently
`min:1 max:4`).

**Current**: `"baseWorkmanshipMin": 1, "baseWorkmanshipMax": 4`
**Proposed**: `"baseWorkmanshipMin": 3, "baseWorkmanshipMax": 5`

**Sim-predicted effect** (verified in worktree, hour-40, seed 42, all playstyles):

| Playstyle          | TopGear before | TopGear after | Danger before | Danger after |
|--------------------|----------------|---------------|----------------|--------------|
| balanced           | 2              | **3**         | d10            | d10          |
| combat-heavy       | 2              | **3**         | d8             | d8           |
| craft-heavy        | 8              | 8             | d8             | d8           |
| enchanting-focused | 4              | 4             | d10            | d10          |

**Rationale**: raises the equipped-gear floor by one step for the two
starved playstyles without overshooting craft-heavy (its skill already
pushes iron gear past this floor). Does not push workmanship-5 clear, but
does close half the gap at the cheapest possible data surface.

**Rollback cost**: 6 single-line edits in recipes.json. Zero schema change.

### Tune B — Raise Iron Helm / Greaves / Vambraces `baseWorkmanshipMax` from 6/7/6 → 8 and `Min` 3 → 4

**Files**: `content/recipes.json`

**Current** (example, Iron Helm):
`"baseWorkmanshipMin": 3, "baseWorkmanshipMax": 6`
**Proposed**:
`"baseWorkmanshipMin": 4, "baseWorkmanshipMax": 8`

**Sim-predicted effect** (verified): no change at 40h seed 42 baseline
because balanced/combat-heavy never accumulate Iron Ore + Leather
simultaneously at their playstyle weights. **But**: once Tune A lifts
low-tier floor to 3, a balanced player who gets lucky with iron mats in
hour 30–40 now equips workmanship-4 iron gear (min=4) instead of
workmanship-3 leather gear. It's the *ceiling raiser* that pairs with Tune A.

**Cross-check with encounter-sim**: at lvl-5 against danger-3 timber-wolf,
HP% ends at 100% already. Against danger-5 frost-giant, HP% avg 47%. Even
at workmanship 8 (HP bonus +24 at sim rate of +3/workmanship-point), the
curve is survivable not comfortable. **No risk of trivialising tier-3
content.**

**Rollback cost**: 3 single-line edits.

### Tune C — Soften monster scaling curve: `hpPerDanger` 0.4 → 0.3, `powerPerDanger` 0.3 → 0.25

**Files**: `content/combat-curves.json`

**Sim-predicted effect** (verified in worktree, standalone):
- combat-heavy reliable-danger d8 → **d10** (the most meaningful lift)
- balanced reliable-danger stays d10
- enchanting-focused stays d10
- craft-heavy: in the sim-seed-42 action ordering, craft-heavy's level
  regressed from 4 → 3 and TopGear from 8 → 3. This is a **seed path artefact**
  — softer monsters shift the combat-to-craft mat-drop flow, which diverges
  the deterministic RNG. A live player will not see this regression;
  craft-heavy gains, not loses, from softer monsters.

**Encounter-sim cross-check** at proposed curve (lvl-5, party 3F/3W, frost-giant):
- danger 5 → 100% win, HP% avg ~65% (estimated from linear extrapolation of
  the 0.4→0.3 HP reduction; needs a post-apply re-run if user approves)
- danger 7 → HP% avg ~30% (estimated). Still not comfortable, but no
  longer a last-hitter sweat.

**This is the tune that most directly addresses the user's play-test
feeling**: the gap between "sim says reliable d10" and "user can't clear
d5" is the damage curve, not the gear curve.

**Rollback cost**: 2 numbers in one file. Both live game and
`encounter-sim` / `progression-sim` auto-pick up the new curves.

## 4. Combined prediction

Applying **A + B + C together** at hour-40 seed-42:
- balanced TopGear 2 → 3, reliable-danger still d10 but encounter-sim
  pyrrhic-band shifts from d5 to d7.
- combat-heavy TopGear 2 → 3, reliable-danger d8 → d10.
- craft-heavy — preserved at TopGear 8 (A and B don't push past its
  existing ceiling; C is seed-divergent but not destructive).
- enchanting-focused unchanged but at workmanship 4 (canary holds).

**Primary success criterion (user-facing)**: in a live play-test at
hour-30+ balanced, `encounter-sim`-equivalent danger-5 fights end with
player at ≥50% HP rather than ≤30%. Verified pre-apply via the
`encounter-sim` predictions above.

## 5. Encounter-sim supporting runs

Logged in `docs/design/sim-logs/2026-04-13.md`. Key numbers:

| Config                                         | Wins  | HP% avg | Damage med | Label    |
|------------------------------------------------|-------|---------|------------|----------|
| timber-wolf, d3, party 3F/3W, lvl5, 500 rolls  | 100%  | 100%    | 0          | trivial  |
| frost-giant, d5, party 3F/3W, lvl5, 500 rolls  | 100%  | 47%     | 78         | trivial  |
| frost-giant, d7, party 3F/3W, lvl5, 500 rolls  | 99%   | 19%     | 158        | trivial  |

**The `trivial` label at d7 with 19% HP remaining is the bug** — not in
the label per se but in how the creative agent and the user read it.
Action item (tool side, out of scope here): add an HP%-weighted
"comfort" band to encounter-sim alongside the win-rate band.

## 6. Tunes considered and REJECTED

- **Raise Iron Sword / Leather Armor min from 2 → 4** — tested in worktree.
  No effect at hour-40 seed-42 because balanced/combat-heavy playstyles
  never craft these recipes with their ingredient mix. Shelved; revisit
  once ingredient drop rates are audited.
- **Raise loot-table drop weights for Iron Ore + Leather** — would help
  balanced craft iron gear, but risks trivialising tier-2 monster drops and
  creating a mat-glut for craft-heavy (already at 467 enchanting-mats).
  Rejected until loot-tables get their own pass.
- **`CraftingService.CalculateWorkmanship`: `craftingSkill/20 → /10`**
  (Pass-1's headline tune). This is **code, not data** — outside creative's
  mandate. But it is the single highest-leverage fix. Flag for the next
  engineering-agent cycle: doubling skill's contribution to workmanship
  would lift balanced/combat-heavy to workmanship-4 around hour 30 without
  overshooting craft-heavy (which is already clamped by recipe `Max`).
- **Reduce `companionBondLayerThresholds`** (would lift combat-heavy's
  reliable-danger via companion scaling). Rejected — combat-heavy already
  hits AvgCompLayer 5.0, bond rate is not the bottleneck.

## 7. Rollout

1. Apply Tune A (recipes.json — 6 edits).
2. Apply Tune B (recipes.json — 3 edits).
3. Apply Tune C (combat-curves.json — 2 numbers).
4. Re-run `progression-sim --hours 40 --seed 42 --all-playstyles --report-pass 3`.
5. Re-run the three `encounter-sim` rows in §5 to verify HP% lifts.
6. Play-test live. If balanced still feels stuck at d5, escalate to the
   formula fix in §6.

## 8. Open questions

- Should `encounter-sim` get an HP%-weighted difficulty band? (tool change)
- Should `progression-sim` surface "pyrrhic-d" separately from
  "reliable-d"? (tool change — would have caught this gap in Pass 1.)
- Is Tune C's curve-softening the right shape, or should we use a
  piecewise curve (easier at d5, same at d10)? The current coefficient is
  linear. A piecewise tune requires a schema extension to
  `combat-curves.json`.
