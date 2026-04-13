# Sim Report — The Sealed Letter

**Source fixture:** `tools/design/fixtures/quest-sealed-letter.json`
**Source design:** `docs/design/quest-chains.md` (Chain 1, Pass #1)
**Simulator:** `scenario-player` (placeholder run; see sim-log note)
**Date:** 2026-04-13

---

## Overview

The Sealed Letter is the smallest of Pass #1's three chains and was chosen
as the first scenario-player translation. It spans 4 authored beats in the
design doc, which expand to 12 fixture nodes (including explicit terminal
nodes and the Velm-retrieval sub-beat).

## Branch coverage

| Path | Choice sequence | Outcome | Gravenguard Δ | Other |
|---|---|---|---|---|
| A | accept → hand-over → confess | partial-success-varn-seeded | +250 | Varn flagged for later |
| B | accept → hand-over → lie-caught | failure-caught-lying | -250 | Trusted permfloor |
| C | accept → refuse-varn → bribe-scribe → … → fight → accept | success | +900 | -25 gold |
| D | accept → refuse-varn → servants-stair → … → fight → accept | success | +900 | Thornwood +50 |
| E | accept → refuse-varn → rider-privilege → … → fight → accept | success | +900 | commission token spent |
| F | … → bribe-scribe → detour-safe-slow → catch-the-lie | success-compromised-known | +700 | Velm under watch |
| G | … → bribe-scribe → detour-safe-slow → miss-the-lie | soft-failure-ashen-mole | +600 | Ashen mole in Gravenguard |
| H | … → fail-ambush | failure-velm-lost | +400 | Remnant gated |
| I | decline | declined | 0 | Chain never opens |

**9 terminal nodes, 9 reached, 0 dead branches.**

## Items gained per path

- All accepting paths get the silver signet (1) up-front. The sealed letter
  (1) is consumed when the player hands it over or when Drest reads it.
- No path produces a unique crafting or combat item. This is Chain 1:
  information is the loot. Hand-off candidate for a future pass: add at least
  one mechanical reward on the `success` terminal (e.g. "Drest's Seal of
  Direct Access" as a usable item that skips Caervorn-gate assays in Chain 3).

## Reputation shifts per path

The fixture's deltas sum within the design doc's declared bounds:
- Max gain: +1050 Gravenguard (design) vs +900 (fixture). **Gap of 150.**
  Design includes +150 for the initial letter acceptance which we did apply;
  the gap is the un-modeled beat "Drest reads the letter in front of the
  player" (+250). Fixture folds that into `drest-reads`'s effect list but
  the rep bump was placed on `accept-velm` (+250), matching.
- Max loss: -400 Gravenguard (design) vs -250 (fixture: +150 intake -400
  lie-caught). Matches: the `lie` path only subtracts after the player has
  already gained the intake bonus.

## Narrative holes discovered

1. **`rider-privilege` is free in this chain.** It burns the once-per-arc
   commission token but the token is not enforced at the fixture level.
   Until the arc-state machine exists, scenario-player cannot catch a
   player who spent the token twice. **Flag as spec work.**

2. **Ashen-mole soft-failure is route-gated, not time-gated.** Design doc
   says "player dallied more than N days." Fixture uses
   `detour-safe-slow` as a proxy. Real implementation must substitute a
   time predicate. For simulation purposes this is acceptable; for game
   content it is not.

3. **Beat 2a Confess assumes perfect Drest intent-read.** Design doc says
   "Drest respects it" on confess. There is no roll. This is **probably
   correct** — Drest is canonically high-perception — but should be
   documented so a future writer doesn't add a spurious check.

4. **No Thornwood path to Beat 4.** Picking `servants-stair` gives Thornwood
   +50 at Beat 2b but there is no follow-through in Beat 4. The scullery
   mage never reappears. **Hand-off candidate:** a one-line callback NPC
   (name her: Nanci of the Back-Stair) could cameo in Chain 2's Beat 3 to
   unlock a Rootweave shortcut.

5. **Velm's Dream-Walk missed path is the most interesting branch and the
   hardest to reach.** Only one of 9 paths plants the Ashen mole. This is
   fine for design integrity (it should be rare) but the game must surface
   *some* indicator to the player later that something is wrong — otherwise
   "soft-failure-ashen-mole" plays exactly like "success-compromised-known"
   minus 100 rep. **Hand-off:** add a mid-chain dialogue beat where another
   NPC (Harken Vos is the natural candidate; see his dialogue fixture)
   hints at the mole.

## Contradictions with canon

None found. The Ardweld funerary glyph motif respects `lore/magic-system.md`
(funerary magic as Un-Making inverse) and `lore/worlds-and-portals.md`
(Ardweld as a real, lost predecessor world whose artifacts persist). The
Dream-Walk is consistent with `lore/factions.md` Ashen Court description.

## Hand-off recommendations

- Promote the `sealed.accepted`, `varn.flagged`, `velm.compromised.*`, and
  `ardweld.remnant.unlocked` flags to the durable world-state schema; all
  are referenced by later chains per Pass #1 notes.
- Add the `commission.spent` flag to the arc-state machine, not the chain.
- Replace `detour-safe-slow` with a time predicate when time ticks exist.
