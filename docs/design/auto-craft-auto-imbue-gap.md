# auto-craft + auto-imbue — gap analysis

Companion doc to `auto-progression-design.md`. These two sub-modes don't exist
yet; auto-progression dispatches to them but stubs the actual action until
they ship.

## auto-craft

### Current state
- `CraftingService.AttemptCraftAsync(playerId, recipeId, componentItemIds, taperId)`
  executes one craft given an explicit recipe + component Guids.
- `CraftCommandHandler` wires this into the hub.
- No automation; the caller must pick the recipe, pick the components, and
  decide whether to salvage the result.

### What's missing
1. **Recipe selection** — scan `IRecipeRepository`, score each by "expected
   effective-level gain" given the player's current gear tier. Pick the best
   upgradeable slot.
2. **Ingredient audit** — check inventory + storage for required components.
   If short, emit a **farming plan** (`{ material: "iron-ore", qty: 4,
   zoneHint: "..." }`) the meta-mode can execute via auto-farm.
3. **Auto-equip after craft** — if the crafted item beats the equipped item
   in the same slot on a tier / stat basis, auto-equip and queue the old for
   salvage.
4. **Failure handling** — `CraftingOutcome.NearMiss` / component-partial-loss
   is already modelled; auto-craft must not spiral (cap retries, escalate to
   auto-progression with `reason: "craft stalled"`).

### Integration touchpoints
- Calls `CraftingService.AttemptCraftAsync` with its picked recipe.
- Needs a new `AutoCraftService` (scoped, analogous to `AutoFarmService`
  session state).
- Needs a new `AutoCraftCommandHandler` + `AutoCraftCommand` + parser.
- Tick handler or inline orchestrator (the existing `PracticeEnchantingCommandHandler`
  is a good template — it loops once per invocation rather than on a tick).
- Emits `AutoFarmCommand` downstream when ingredients are short.

### Scope estimate: **medium**

Recipe-scoring is the hard part (requires a view of all slots + materials +
expected-vs-current stats). Everything else is wiring.

### Recommended playbook when it ships
- **`craft-upgrade-progress`** — axis `playerLevel [1,3,5,8]`, measures
  hours-to-gear-tier-3 with auto-craft active (starting from zero inventory).
  Expected band initially `punishing` at L1, `trivial` at L8.

---

## auto-imbue

### Current state
- `ImbueService.ImbueAsync(playerId, itemId, taperId)` executes one imbue.
- `ImbueCommandHandler` wires it to the hub.
- `PracticeEnchantingCommandHandler` already does a crude "imbue every
  eligible item + salvage fully-imbued" loop — this is 80% of the logic
  auto-imbue needs.

### What's missing
1. **Taper prioritisation** — given a pool of tapers and equipped items,
   decide which taper goes to which item. Current `practiceEnchanting`
   does first-fit; auto-imbue should pick the imbue-type that best
   closes a deficit (e.g. prefer offensive element tapers on the weapon
   slot).
2. **Taper farming plan** — if no tapers are available, emit a farming
   plan: "farm monsters that drop `spark-taper` in <zone>". Requires
   exposing taper drop-tables from content.
3. **Coverage tracking** — progress metric "% of equipped slots fully
   imbued to ≥ level 2" that the binding-constraint formula reads.
4. **Integration with auto-craft** — newly crafted items should be
   automatically handed to auto-imbue for enchanting before being equipped.

### Integration touchpoints
- Extends `PracticeEnchantingCommandHandler` into a standing service
  (`AutoImbueService` + command + parser + handler).
- Consumes `ImbueService.ImbueAsync`.
- Exposes `GetImbueCoverage(playerId)` for the progression scoring.
- Emits `AutoFarmCommand` when tapers are short.

### Scope estimate: **small**

Much of the logic already exists in `PracticeEnchantingCommandHandler`. The
real work is the coverage metric + taper-farming integration.

### Recommended playbook when it ships
- **`imbue-coverage`** — axes `imbueLevel [0,1,2,3]` × `gearTier [1,2,3]`,
  measures time-to-full-coverage given a fixed material budget. Expected
  `trivial` at high imbue levels, `punishing` at imbue 0 (nothing to imbue
  with).
