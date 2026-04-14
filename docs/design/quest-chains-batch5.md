# Quest Chains — Batch 5

Two new chains covering zones previously untouched by the chain fixtures
(Caervorn Highlands and Ashen Reach). Both exercise the `requires.items`
gate and typed `addItem`/`removeItem` effects — the same pattern
`DungeonMasterService` uses to attach `targetItemNames` to procgen
quests for deterministic auto-complete.

- Chain 6 fixture: `tools/design/fixtures/quest-chain6-ash-and-salt.json`
- Chain 7 fixture: `tools/design/fixtures/quest-chain7-highland-post.json`

Zone coverage after this batch: Caervorn Highlands (chain 7, d4),
Thornwood (chain 4, d3), Drowned Coast (chain 5, d5), Ashen Reach
(chain 6, d8). Still unhooked: Portmere (d1, starter — intentionally
un-chained), Gravenmarsh (d3), Gravenhold (d2), Starting Road (d2),
Maw Borderlands (d6, wyrd).

---

## Chain 6 — *Ash and Salt*

**Zone:** `aeldran-6-ashen-reach` (desert, danger 8).
**Beats:** 6 non-terminal + 4 terminal. **Root:** `intro`.
**Faction impact:** Ashen primary (+200 / +75 / -150 depending on
terminal), Rider-Compact and Caervorn secondary.

### Why this chain
Ashen Reach had a drafted chain-ashen-reach.md but no shipped fixture.
This fills the gap with a **short, materials-gated** chain that does
not require the player-as-Ashen-agent canon question (Q4) to be ruled.
Hazir is an ashen **smith**, not a court agent — the moral stakes are
"skim the materials or don't," not "feed the artifact."

### Beats
- `intro` — Accept Hazir's commission (4× Ash-Salt + 1× Obsidian Shard).
- `hunt` — Branch: peaceful range-hunt (gives 4 Ash-Salt) vs aggressive
  vent-first approach (gives 3 Ash-Salt + 1 Obsidian Shard up-front,
  bypasses the gather beat).
- `gather` — Mine the Obsidian Shard at the black vent. Two good paths
  and one skim-path that unlocks the lesser terminal.
- `deliver` — Item-gated (`requires.items` 3× Ash-Salt + 1× Obsidian
  Shard). Clean exchange.
- `deliver-cheated` — Skim-branch only. Flawed blade, lower rep.
- Terminals: `end-clean` (Wyrdcut Blade + 200 rep), `end-skimmed`
  (Flawed Wyrdcut Blade + 75 rep), `end-walked` (-150 rep, keep shards),
  `end-refused` (-25 rep, chain closes).

### Canon hooks
- New unique: **Wyrdcut Blade** / **Flawed Wyrdcut Blade** — see
  canon-deliberations Q5.
- Obsidian Shard is the same salvage-suppressed reagent — see
  canon-deliberations Q7.

### Schema validation
Fixture uses `allowUnderflow=false`; every `removeItem` is gated by an
upstream `addItem` path. Python graph-check confirms zero dangling
`next` references.

---

## Chain 7 — *The Highland Post*

**Zone:** `aeldran-1-caervorn-highlands` (mountain, danger 4).
**Beats:** 5 non-terminal + 4 terminal. **Root:** `intro`.
**Faction impact:** Caervorn primary, Rider-Compact and Thornwood
secondary.

### Why this chain
Caervorn Highlands hosts the RIDER_001/002 starter chain (`content/
quests.json`) but had no long-form narrative chain. This adds a
**short diplomatic courier chain** that showcases the three-way
faction-tension pattern: Caervorn dispatch → Thornwood envoy, with
the Compact waiting to buy the letter if the player defects.

### Beats
- `intro` — Accept dispatch (adds `Caervorn Sealed Dispatch`).
- `route` — Branch: scree path (safe), road (ambush), or sell to
  Gravenhold (traitor terminal).
- `road-ambush` — Fight / parley / surrender. Surrender is
  item-gated (`requires.items` on the dispatch) and removes it.
- `arrive` — Item-gated. Deliver to Thornwood envoy. Removes dispatch,
  gifts Thornwood Tally Token, big rep swing.
- Terminals: `end-delivered` (+200 Caervorn, +100 Thornwood),
  `end-surrendered` (-150 Caervorn), `end-sold` (-300 Caervorn,
  +100 Compact), `end-refused` (-25 Caervorn).

### Canon hooks
- Caervorn rep stacking with RIDER_001/002 — see canon-deliberations Q6.
- New items: **Caervorn Sealed Dispatch** (chain-scoped; removed on
  every terminal path). **Thornwood Tally Token** (chain reward,
  hook for future Thornwood chain).

### Schema validation
`allowUnderflow=false`. Every `removeItem` for the dispatch is gated on
`requires.items` for the same name/qty. Chain-graph check passes.

---

## Design pattern: typed item effects as the `targetItemNames` analog

Both chains exploit the same idea `DungeonMasterService` uses for
procgen quest auto-complete: **never parse English at runtime**. Every
material interaction goes through the typed `addItem`/`removeItem`
effect with a case-matched `key` that already exists in
`content/items.json` (Ash-Salt, Obsidian Shard) or is introduced as a
chain-scoped unique (Wyrdcut Blade, Caervorn Sealed Dispatch,
Thornwood Tally Token).

A future extension — attaching `targetItemNames` directly to a chain
fixture so the chain runner can auto-complete beats against the live
inventory — would be a one-line schema addition in
`QuestSpec.cs::Effect`. Both of these chains are already structured
so that such an extension would require zero beat-logic changes.
