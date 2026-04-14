# Playbook Pass 1 — Creative Pass #7 (2026-04-13)

First creative cycle under the playbook-driven mandate. Goal: rerun the 5 seed
playbooks on the post-combat-rebalance base, diagnose divergences, and
reconcile each divergence by either tuning game data or updating the spec.

Base: `07c1f3c` (merged `worktree-agent-a9f8a24b`: playbook harness) on top of
the combat-rebalance cycle (`6360749`).

## Baseline (pre-tune) sweep

All 5 playbooks ran trivial-dominant after the combat rebalance. The rebalance
halved monster scaling and added a 12%-per-tier party-scaling multiplier; the
rebalance landed as `hpPerDanger=0.20, powerPerDanger=0.15, scalingPerTier=0.12`.

| playbook | cells | diverged | character |
|---|---|---|---|
| `full-party`           | 11 | 7  | d4-d10 all trivial in 1-2 rounds; mid-game sweep |
| `nude-character`       | 25 | 15 | mid-level (pl5-12) too strong; low-level (pl1 d0) over-easy |
| `gear-only`            | 25 | 18 | gear proxy (+2 pl/tier) swamps the danger curve |
| `imbue-only`           | 20 | 16 | pl5 + gt3 baseline already clears most cells |
| `companion-contribution` | 36 | 7  | 3-companion party trivialises d9; solo-no-companion closer to expectation |

Core diagnosis: party-scaling `1 + 0.12 * (playerLevel + avgCompanionLayer)`
gave a full-party mid-game loadout a ~3.3x multiplier, which combined with
the halved monster scaling made the player far stronger than the old specs
anticipated. Two levers existed — tune data, or accept-and-update specs.

## Tunes applied (`content/combat-curves.json`)

Converged on:

| field | before | after | rationale |
|---|---|---|---|
| `monsterScaling.hpPerDanger`    | 0.20 | 0.40 | restore monster HP slope at high danger without punishing low-level |
| `monsterScaling.powerPerDanger` | 0.15 | 0.28 | restore monster damage slope; matches HP adjustment |
| `partyScaling.scalingPerTier`   | 0.12 | 0.03 | party multiplier was calibrated assuming `playerLevel` meant literal character level, but gear/imbue proxies stack into it and runaway; 0.03 keeps the party bonus meaningful (pl 8 + layer 3 = 1.33x) without the old explosion |

Other files (`content/monsters.json`, `content/progression-curves.json`) were
inspected; no data tunes there were needed once the scaling curves came back
in line.

## Spec updates applied (per playbook)

Each playbook's `expectedViability` cells were rewritten to match the tuned
reality. Descriptions were expanded to capture the post-rebalance design
intent so future cycles have canonical justification.

### `full-party.json` — spec-update

**Change:** every danger cell now expects `trivial`.
**Why:** the spec previously claimed a mid-game loadout should find d9-d10
"hard" at 30-60% win rate. That never matched user-stated intent — mid-game
stack IS supposed to carry overworld monster cells up to the zone ceiling.
Sim rounds climb 1.0 → 2.4 from d0 to d10, which is where the design tension
actually lives (turn-efficiency, not survival). Boss/pack playbooks will own
the "hard content" curve.

### `nude-character.json` — spec-update + content-tune

**Change:** low-danger low-level cells flipped `balanced/hard → trivial`;
high-level sweep cells flipped `easy/balanced/hard → trivial/balanced`;
pl5 d6 dropped `hard → punishing`.
**Why:** a bare pl-1 vs unscaled timber-wolf (hp 28) is a trivial sweep in
2 rounds — old spec was unrealistically pessimistic. High-level bare
characters clear 1v1 because the sim is always 1 monster. The real viability
cliff (pl5 d4 balanced → d6 punishing) IS captured after tunes.

### `gear-only.json` — spec-update + content-tune

**Change:** trivial-floor extended further across gear tiers 3-5; danger-9
cells at gear 3-4 land in `balanced/trivial` rather than `hard/balanced`.
**Why:** gear-tier +2-level proxy stacks with base pl 5 to effective pl 11+
at gearTier 3+, which out-paces 1v1 danger-level scaling. The observable
curve is: gear >= danger+2 → trivial, gear ~ danger → balanced/hard, gear
<< danger → punishing.

### `imbue-only.json` — spec-update + content-tune

**Change:** most cells flipped to trivial because baseline gt=3 already
clears most content; imbueLevel 0-1 at d9 are the only non-trivial cells
(`balanced/easy`).
**Why:** imbues in the proxy model (+1 pl each) are a marginal buff on top
of already-over-scaled gear. This playbook is now primarily a **regression
detector** — it should stay trivial; if it goes non-trivial, gear or monsters
have drifted. The description flags that a proxy-free variant is needed for
the playbook to test "imbues carry where gear doesn't" in the future.

### `companion-contribution.json` — spec-update + content-tune

**Change:** 0-companion cells at d6+ flipped `hard/punishing → punishing`;
3-companion cells at d6 flipped `balanced/easy → trivial`; 3-companion
layer-1 d9 flipped `hard` (stayed, it's genuinely tight at 46-53% win).
**Why:** party-scaling still multiplies the base character when even a
single layer-1 companion is present, so "3 companions" buffs the player
noticeably even at low companion layer.

## Final state

After tunes + spec updates, expected-vs-actual is convergent for 4 of 5
playbooks across 3 seeded runs:

| playbook | stable status |
|---|---|
| `full-party`           | all 11 cells on-band every run |
| `nude-character`       | 24/25 stable; pl1-d2 occasionally 99.5% (easy band edge) |
| `gear-only`            | 23-25/25 stable; gt3-d7, gt4-d9, gt5-d9 flap near 95% boundary |
| `imbue-only`           | 19-20/20 stable; imbue0-d7 flaps 96-97% |
| `companion-contribution` | all 36 cells on-band |

The residual flap is NOT a data-tune issue — it is the playbook engine
using `string.GetHashCode()` for cell-salt (non-deterministic across
process invocations in .NET Core by default). Cells land on the correct
band on average but occasionally cross a 95%/85% boundary on the wrong
side. Captured as an open question for the next engineering cycle.

See `docs/design/sim-logs/playbook-pass-7-full.txt` for the canonical run
output captured during this cycle (also individual `playbook-runner-*.json`
files under that directory per-playbook).

## Files touched

### `content/`
- `content/combat-curves.json` — three-field tune (hp, power, scalingPerTier).

### `tools/design/playbooks/`
- `full-party.json` — expected curve rewritten; description expanded.
- `nude-character.json` — expected curve rewritten; description expanded.
- `gear-only.json` — expected curve rewritten; description expanded.
- `imbue-only.json` — expected curve rewritten; description expanded.
- `companion-contribution.json` — expected curve rewritten; description expanded.

No source code changes.

## Unresolved structural issues

1. **Playbook cell-salt non-determinism.** `PlaybookEngine.RunCell` uses
   `kv.Key.GetHashCode()` to salt the seed. .NET Core randomises
   `string.GetHashCode()` per process, so the same playbook on the same
   seed produces slightly different per-cell RNG across invocations. Does
   not affect correctness of the harness (distributions converge) but does
   flap borderline cells between bands. **Engineering fix:** replace with
   a deterministic hash (e.g. FNV-1a over axis key-value strings) or
   fold axis values into the Random seed directly. Flag for the next
   engineering cycle.

2. **Proxy model under-specification.** `gearTier * 2` levels and
   `imbueLevel * 1` level treat gear and imbues as raw character-level
   additives. Real gear grants damage/hp slots independently; real imbues
   grant elemental effects. Until those land in the combat sim, the
   `gear-only` and `imbue-only` playbooks can only test coarse scaling,
   not the axis they're named for. Not actionable this cycle.

3. **Companion-leveling asymmetry (from briefing).** Nothing surfaced in
   the playbook sweep that isolated a companion-layer regression; the
   `companion-contribution` playbook only varies layer at fixed
   companionCount=3. A future playbook `companion-layer-curve` (fix count
   = 3, sweep layer 1-10, sweep danger) would stress this. Not built
   this cycle; flagged below.

## Three open questions for next cycle

1. **Is the playbook cell-salt non-determinism worth an engineering fix now?**
   The flap happens only on cells at the 85%/95% band boundary. If the answer
   is yes, swap `GetHashCode()` for FNV-1a and tighten all cells that flipped.

2. **Should `imbue-only` and `gear-only` drop the proxy baseline?** With gt=3
   baked into `imbue-only`, imbues are a marginal term on an already-strong
   player. A cleaner variant uses gt=0, imbueLevel axis, and tests "imbues
   alone carry pl=5 through d-X" — answers a different question but is more
   informative about imbue contribution.

3. **Should we add a `pack-viability` playbook?** Every current playbook runs
   1v1 vs a single scaled monster. Real overworld encounters are packs of
   2-5. The playbook specs assume this gap (nude-character + full-party both
   reference `pack` in their updated descriptions), but the playbook itself
   doesn't exist. Candidate axes: `partySize × packSize × dangerLevel`.
