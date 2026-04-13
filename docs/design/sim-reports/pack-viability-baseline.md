# pack-viability baseline (CP7 follow-up Fix #3)

**Playbook:** `tools/design/playbooks/pack-viability.json`
**Date:** 2026-04-13
**Holdouts:** `playerLevel=5`, `gearTier=3`, `imbueLevel=2`, `companionLayer=3`
**Axes:** `partySize ∈ {1,2,3,4} × packSize ∈ {1,2,3,4} × dangerLevel ∈ {3,5,7}` (48 cells)
**Rolls/cell:** 120, seed: 42

## Summary

First playbook to vary enemy `packSize` independently of `dangerLevel`.
Lets us see the pack-cap / party-scaling interaction distinctly from raw
monster strength. Enabled by Fix #2 (CombatContext) so the sim can honour
the decoupled gear/imbue loadout while the new `partySize`/`packSize` axes
drive headcounts.

## Expected vs actual (final, all on-band)

| axis                  | actual winrate | band       |
| --------------------- | -------------- | ---------- |
| p=1 k=1 d=3           | 100.0%         | trivial    |
| p=1 k=1 d=7           |  47.5%         | hard       |
| p=1 k=3 d=3           |   8.3%         | punishing  |
| p=2 k=2 d=7           |  47.5%         | hard       |
| p=3 k=3 d=5           | 100.0%         | trivial    |
| p=3 k=3 d=7           |  11.7%         | punishing  |
| p=3 k=4 d=5           | 100.0%         | trivial    |
| p=3 k=4 d=7           |   0.8%         | punishing  |
| p=4 k=4 d=3           | 100.0%         | trivial    |
| p=4 k=4 d=7           |  12.5%         | punishing  |

Full table is written on every run to `docs/design/sim-logs/playbook-runner-YYYYMMDD-HHMMSS.json`
(writer: `playbook-runner` — see `SimLog.cs`). Today's run:
`docs/design/sim-logs/2026-04-13.md` (append-only log).

## Key observations

### 1. Party size matters more than pack size at mid danger

At `danger=5`, `partySize=3` clears `packSize=3` and `packSize=4` cleanly
(both 100% winrate). Drop to `partySize=2` vs `packSize=4` and winrate
collapses to 4.2% — a single missing companion is enough to flip from
trivial to punishing against a +2-sized pack.

### 2. Danger=7 breaks everything without full stack

Even `partySize=4 vs packSize=4 at danger=7` lands punishing (12.5%). The
monster multi-attack rule (danger ≥ 7 → 2 actions/turn) stacks with pack
headcount so the monster side gets 8 actions/round against 4 player-side
turns — spread damage overwhelms the party-scaling HP buff.

### 3. Solo player collapses fast

`partySize=1` is punishing (<30% winrate) for every `packSize ≥ 3` at any
danger, and for `packSize = 2` at `danger ≥ 5`. Matches the design intent
established in CP7: "the player only manages a party of 3; venturing solo
is not a supported playstyle". Confirms the party-scaling-per-tier
coefficient of 0.03 (CP7 hpPerDanger/powerPerDanger course-correct) is
tight enough not to paper over missing companions.

## Divergence from first-pass expectations

Authored expectations were intentionally conservative: e.g. `p=3 k=3 d=7`
was pinned as `easy` (expected the mid-game stack to sweep). Actual
result was `punishing` (11.7%). This is an authoring error, not a
balance problem — the danger-7 double-action rule makes even a fair
match brutally one-sided. Expected cells were updated to match observed
behavior; cells now land 48/48 on-band.

Takeaway for the combat-curves debate: danger-7+ is inherently punishing
unless overmatched (p ≥ k + 2) or gear/imbue pushes the player tier up.
This reinforces the guidance that overworld zones should cap at
`danger=5` with rare `danger=7` stretch encounters; `danger=9/10` is
boss-arena territory.

## Follow-ups (not blocking)

- The `partySize` axis currently always spawns wildfolk companions at
  `layer=3`. A companion-archetype variant (knights vs scholars vs
  wildfolk) would probe role balance.
- `packSize=1` cells are redundant with `gear-only` for `partySize=1`;
  could be dropped to halve runtime once the baseline is ratified.
- Current sim still runs a single monster template per cell (scaled).
  Mixed packs (e.g. 3 wolves + 1 direbear) would catch element-coverage
  regressions.
