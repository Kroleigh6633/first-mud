# Crafting baseline playbook — pass 1

**Date:** 2026-04-13  
**Playbook:** `tools/design/playbooks/crafting-success.json`  
**Runner:** `CraftingEvaluator` (kind=crafting) via `playbook-runner` CLI  
**Seed:** 42, rolls/cell: 500, seedSpread: 32

## Motivation

User playtest feedback at Crafting Skill 2: *"crafting constantly fails."* This pass
establishes the real outcome distribution of `CraftingService.AttemptCraftAsync`
across a skill × difficulty grid, using `CraftingService.RollOutcome` as the
single source of truth shared between game and sim.

## Baseline (pre-tune) outcome distribution

All cells, 500 rolls each, skill-check satisfied (we simulate the post-gate
math — below-required skill is an instant NearMiss with zero component loss
and isn't a balance concern).

```
craftingSkill | recipeDifficulty | Success | NearMiss | UnexpRes | CompLoss | Discov
         1    |         1        |  47.9%  |   30.8%  |   14.2%  |    4.6%  |   2.5%
         2    |         1        |  41.9%  |   33.8%  |   16.0%  |    4.8%  |   3.5%  ← user's cell
         2    |         3        |  53.8%  |   26.7%  |   13.3%  |    4.6%  |   1.7%
         8    |         5        |  48.5%  |   30.0%  |   13.8%  |    5.6%  |   2.1%
        40    |         8        |  52.3%  |   27.3%  |   13.5%  |    5.2%  |   1.7%
```

(Full 32-cell table in the run log at `tools/design/simulations/playbook-runner/playbook-runner-*.json`.)

## Findings

1. **Crafting skill has no effect on outcome distribution.** Every cell sits in
   the same ~42-58% Success band regardless of craftingSkill 1→40. This is by
   design in the current code (`CraftingService.RollOutcome` branches only on
   `quantitiesMatch`), but it contradicts player intuition. Flagged for a
   future pass: craftingSkill should improve effective tolerance or success-band.

2. **At skill=2, difficulty=1 the user's perception is accurate but nuanced.**
   Success = 41.9%, meaning **58% of attempts produce something other than the
   named recipe**. But unpack the "failures":
   - **33.8% NearMiss** — no components lost, player retries freely.
   - **16.0% UnexpectedResult** — an *alternate* item (flavoured as a surprise bonus).
   - **4.8% ComponentLoss** — genuine destruction of materials.
   - **3.5% Discovery** — a rare variant.
   
   True "bad" outcomes (ComponentLoss) are ~5%. The remaining 53% of
   non-Success outcomes either cost nothing (NearMiss) or give a consolation
   item (UnexpectedResult, Discovery). The user is reading all non-Success as
   failure — a perception/labeling problem compounding a genuine low
   Success-rate floor.

3. **Root cause of low Success rate: UI rounding.** Displayed ingredient
   quantities are rounded to the nearest 5 (see `GetSeededQuantitiesAsync`),
   while the seeded per-player quantity varies in ±20%. The match check has
   only ±5% tolerance. For a base-10 ingredient, seeded can be 8-12 while the
   display says "10" or "5" — a naive player submitting the displayed value
   fails the tolerance on roughly half the seeds.

## Verdict on "constantly fails"

**The user is mostly right — but the data says "frequent" (≈50-60% non-Success),
not "constant" (≥70%).** Combined with the perception issue — NearMiss reads
like failure to a player who only sees the recipe name disappear — the
subjective experience of "constantly failing" is believable.

## Tune applied (pass 1)

`CraftingService.RollOutcome` adjusted:

| Branch | Outcome | Before | After |
|---|---|---|---|
| quantitiesMatch=true  | Success          | 85% | **90%** |
| quantitiesMatch=true  | UnexpectedResult | 10% |   7%   |
| quantitiesMatch=true  | ComponentLoss    |  3% | **1%** |
| quantitiesMatch=false | NearMiss         | 70% | **55%** |
| quantitiesMatch=false | UnexpectedResult | 20% | **40%** |
| quantitiesMatch=false | ComponentLoss    |  8% | **3%** |

Design rationale: the current punishment for missing tolerance is too harsh
given the UI-rounding problem is mostly the UI's fault, not the player's. Shift
bulk of the mismatch branch into UnexpectedResult (player gets SOMETHING) and
keep NearMiss as a smaller free-retry slice. Hard failures (ComponentLoss)
halved because they were disproportionate to the actual skill of the attempt.

### Post-tune distribution at skill=2, difficulty=1

```
Success 48.3%  NearMiss 24.6%  UnexpectedResult 22.9%  ComponentLoss 2.3%  Discovery 1.9%
```

- Productive outcomes (Success + UnexpectedResult + Discovery): **73.1%** (was 61.4%).
- Hard failures (ComponentLoss): **2.3%** (was 4.8%, roughly halved).
- NearMiss free-retries: **24.6%** (was 33.8%).

## Follow-ups for next cycle

1. **Skill-scaled tolerance.** Currently `QuantityMatches` uses a flat ±5%. Make
   it scale with `craftingSkill / recipe.RequiredCraftingSkill` so skilled
   players effectively "see through" the UI rounding. That gives skill mechanical
   meaning and targets the root cause.
2. **UX labeling pass.** Rename NearMiss → "Wasted effort (no loss)" or similar
   in the player-facing text so the "free retry" nature reads clearly.
3. **Reduce UI rounding granularity** from 5 → 2 for low-base recipes, or make
   it skill-scaled (skilled crafter sees finer hint).

## Engineering notes

- `CraftingService.RollOutcome(Random, bool)` is now public — single source of
  truth. Sim uses it directly; game uses it via `AttemptCraftAsync`.
- `CraftingService.SeededQuantity(...)` and `QuantityMatches(...)` also exposed
  for the sim harness.
- `PlaybookEngine` dispatches on the new `kind` field. `kind:"combat"` (default)
  is unchanged; `kind:"crafting"` routes to `CraftingEvaluator`.
- Test count: DesignTools.Tests 34 → 36 (two new evaluator determinism/total
  tests). Main suite unchanged at 402 + 16.
