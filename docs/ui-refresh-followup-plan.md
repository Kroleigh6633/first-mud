# UI Refresh Follow-Up Plan (MEDIUM / MINOR disconnects)

Companion to the event-bus refactor that landed in `FirstMud.Application/Events/` and
`FirstMud.GameServer/Services/EventOrchestrators/`. The four MAJOR disconnects (craft,
harvester duty, consume consumable, activate/deactivate) now fan out through the bus and
update every affected panel automatically.

The remaining issues all ride on the same infrastructure — each one is a small edit to an
existing handler/service plus (in a few cases) one new orchestrator subscription. Nothing
below requires new events except where noted.

## Ordering rationale

Do them in this order: reuse-first (MEDIUM #5, #6), then the companion/quest holes
(#7), then the silent background ticks (#8, #9). #10 is optional until lock state
actually surfaces in another panel.

---

## MEDIUM #5 — Equip/Unequip → Crafting Panel ingredient counts

**Problem.** `EquipCommandHandler` / `UnequipCommandHandler` send `EquipmentChanged` but
nothing tells the crafting panel that available ingredient counts shifted (an equipped
weapon is no longer craftable-as-component).

**Approach.** Equipping does not delete an item — it moves ownership/slot. The simplest
fix is to publish `ItemAddedToInventoryEvent` / `ItemConsumedEvent` semantics are wrong
here; instead add a new lightweight event:

```csharp
public record EquipmentChangedEvent(Guid PlayerId, Guid? EquippedItemId, Guid? UnequippedItemId) : IIntegrationEvent;
```

Then either:

- **Option A (small):** `InventoryRefreshOrchestrator` also subscribes to
  `EquipmentChangedEvent` and re-broadcasts the Inventory snapshot. Crafting panel derives
  ingredient availability from inventory, so a fresh inventory push is enough.
- **Option B (cleaner):** A dedicated `RecipeListOrchestrator` that re-runs
  `ViewRecipesCommandHandler`'s payload build. Preferred if we later want per-recipe
  deltas.

**Files.** `EquipCommandHandler.cs:96`, `UnequipCommandHandler.cs:194`, new orchestrator.
**Effort.** ~1 hour (Option A) / ~2 hours (Option B).

---

## MEDIUM #6 — Lock/Unlock → orchestrator, drop the duplicated snapshot

**Problem.** `LockItemCommandHandler.cs:102-162` duplicates the full Inventory payload
build from `OpenInventoryCommandHandler` — a pure DRY violation that already existed
before the bus landed.

**Approach.**

1. Replace the inline payload build with a call to `InventorySnapshotService.BroadcastAsync`.
2. Publish a trivial event like `InventoryItemMetadataChangedEvent(ItemId)` or just reuse
   `ItemAddedToInventoryEvent` semantics (the orchestrator already re-pushes inventory).

Either way the 60-line handler becomes ~15.

**Files.** `InventoryCommandHandler.cs:102-162`.
**Effort.** ~30 minutes.

---

## MEDIUM #7 — Quest completion / AutoFarm → Companion + City refresh

**Problem.** `QuestCommandHandler` and `AutoFarmCommandHandler` can trigger
`BuildingService.AutoAssignIdleCompanionsAsync` (directly or via side effects). Neither
publishes `CompanionStateChangedEvent` or `CityStateChangedEvent`, so those panels stay
stale after quest turn-in or auto-farm stop.

**Approach.** Audit every caller of `AutoAssignIdleCompanionsAsync` and
`ClearCompanionFromBuildingsAsync`; ensure each publishes both events afterward. Better:
move the publish into `BuildingService` itself so callers can't forget.

**Files.** `BuildingService.cs` (add publisher dependency), callers in
`QuestCommandHandler.cs`, `AutoFarmCommandHandler.cs`, `CompanionCommandHandler.cs`
(already done for activate/deactivate — can delete the explicit publish once
`BuildingService` does it).
**Effort.** ~1-2 hours including test coverage.

---

## MINOR #8 — Resource node regeneration → zone tile refresh

**Problem.** `GameLoopService.ProcessResourceRegenAsync` silently updates DB; clients
never learn until they re-enter the zone.

**Approach.** New event `ResourceNodeRegeneratedEvent(ZoneId, IReadOnlyList<NodeDelta>)`
published per affected zone per tick. Orchestrator re-broadcasts the `ZoneView` payload
(or a new `ZoneTilesUpdated` delta) to all players currently in that zone.

Players-in-zone lookup requires a SignalR group per zone (not per player). Worth
considering as a broader refactor — currently every group is keyed by player id.

**Files.** `GameLoopService.cs:500-525`, new orchestrator, possibly `GameHub.cs` for zone
groups.
**Effort.** ~3-4 hours (largest of the batch; scope the group-plumbing decision first).

---

## MINOR #9 — Building construction progress → live progress bar

**Problem.** `GameLoopService.ProcessBuildingConstructionAsync` only broadcasts when a
building completes. The progress bar in the City panel never animates.

**Approach.** After each tick, publish `CityStateChangedEvent("ConstructionTick")` for
each player with at least one under-construction building. `CityRefreshOrchestrator`
already handles this event — no new subscriber needed.

Optionally throttle: only publish when `ConstructionProgress` crosses a 10% boundary to
avoid one broadcast per second per building per player.

**Files.** `GameLoopService.cs:452-498`.
**Effort.** ~30 minutes.

---

## MINOR #10 — Lock state surface in other panels

**Problem.** The audit called this out speculatively — if Crafting ever filters out
locked items, the panel won't re-refresh when lock toggles.

**Approach.** Solved for free once MEDIUM #6 lands (orchestrator re-pushes inventory,
Crafting panel reads from it).

**Effort.** 0 (subsumed by #6).

---

## Orchestrator reuse cheatsheet

| Mutation type | Event to publish | Subscribed orchestrator(s) |
|---|---|---|
| Item removed from inventory | `ItemConsumedEvent(..., FromStorage: false)` | Inventory |
| Item removed from storage | `ItemConsumedEvent(..., FromStorage: true, HomesteadId)` | Storage |
| Item added to inventory | `ItemAddedToInventoryEvent` | Inventory |
| Storage contents changed (other) | `StorageChangedEvent(HomesteadId)` | Storage |
| Companion activate/duty/usage/layer | `CompanionStateChangedEvent` | CompanionList, City |
| Building placement/progress/assignment | `CityStateChangedEvent` | City |

When you add a new mutation, the test is: *"which panel(s) might now be wrong?"* Pick the
matching event(s), publish, done. No more ad-hoc `hubContext.Clients.Group().SendAsync`.
