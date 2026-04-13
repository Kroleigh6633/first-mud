# Engine Extraction Notes

First prototype carving a `FirstMud.Engine` project out of `FirstMud.GameServer`.
The goal is a game-agnostic infrastructure library: no references to companions,
weave, crafting, quests, or any domain entity.

## What moved to `FirstMud.Engine`

| New location | Source |
|---|---|
| `FirstMud.Engine.Commands.IGameCommand`     | was `FirstMud.GameServer.Commands.IGameCommand` |
| `FirstMud.Engine.Commands.ICommandHandler<T>` | was `FirstMud.GameServer.Handlers.ICommandHandler<T>` |
| `FirstMud.Engine.Commands.CommandResult`    | was nested in `FirstMud.GameServer.Services.CommandDispatcher` |
| `FirstMud.Engine.Commands.CommandDispatcher`| was `FirstMud.GameServer.Services.CommandDispatcher` |
| `FirstMud.Engine.Tick.GameLoopService`      | was `FirstMud.GameServer.Services.GameLoopService` (tick-scheduling skeleton only) |
| `FirstMud.Engine.Tick.ITickHandler`         | NEW abstraction |
| `FirstMud.Engine.Messaging.IGameNotifier`   | NEW abstraction |

`FirstMud.Engine` only references the `Microsoft.Extensions.*` abstractions packages.
It has no reference to Domain, Application, Infrastructure, or SignalR.

## Abstractions introduced

- **`ITickHandler`** — replaces the hard-coded `if (_tickCount % ticksPerXCycle == 0)`
  branches that used to live inside `GameLoopService.ProcessTickAsync`. Each former
  branch is now a small class in `src/FirstMud.GameServer/TickHandlers/` that declares
  its own cadence (`Interval`). The engine's loop discovers them via DI
  (`GetServices<ITickHandler>()`) and doesn't know what any of them do.
- **`IGameNotifier`** — what `GameLoopService` needs in order to broadcast
  command-dispatch failures back to the player. The concrete SignalR-backed
  implementation (`GameNotificationService`) stays in `GameServer` because it
  depends on the concrete `GameHub`. DI registers it twice: once as itself
  (for game code that uses the richer API) and once as `IGameNotifier`
  (for engine code that only needs the abstraction).

## What did NOT move (and why)

- **`GameHub`** — heavy command-parsing logic that knows about every concrete
  command type (`MoveCommand`, `AttackCommand`, `CraftCommand`, …). Splitting
  it would require a generic command-deserialization seam, which is a
  larger refactor. Left for a follow-up.
- **All `src/FirstMud.GameServer/Handlers/*CommandHandler.cs`** — each handler
  depends on domain repositories, services, and value objects. They correctly
  stay in GameServer and simply implement the engine's `ICommandHandler<T>` now.
- **`Services/Snapshots/*` and `Services/EventOrchestrators/*`** — these call
  domain repositories (`ICompanionRepository`, `IPlayerRepository`, etc.)
  directly. Moving them would require a much larger seam.
- **`WorldStateService`, `BuildingService`, `HomesteadCompanionService`,
  `AiPlayerService`, `DungeonMasterService`, `CombatService`,
  `FarmingOrchestrator`, `InventoryDepositService`, `QuestProgressTracker`,
  `QuestAutoCompleteService`, `MonsterFactory`, `BiomeService`, `StartupSeeder`,
  `ZoneGridLayout`, `ConsumableHelper`** — all game-rules, all stay.
- **`Commands/IGameCommand.cs`** — the *interface* moved; the 50+ concrete
  command records stay in GameServer because many reference domain enums
  (e.g. `WorldId`, `HomesteadDuty`).
- **Domain event publishing (`GameEventPublisher`, `IGameEventBus`)** — the
  extraction brief lists these as engine candidates, but they do not exist
  in the current codebase (only `FirstMud.Domain.Events.DomainEvents` exists,
  and that is a static C# record hierarchy, not a bus). Left for a follow-up
  when an event bus is actually introduced.

## Seams deliberately left intact (follow-up work)

1. **`GameHub.ParseCommand`** — a 200-line switch that hard-codes every
   command. Engine-ify by introducing an `ICommandDeserializer` seam or by
   moving the switch into a per-command strategy registered in DI.
2. **Concrete command records** — most could live in `FirstMud.Engine.Commands`
   if they didn't reference domain enums. A small refactor (pushing
   `WorldId`/`HomesteadDuty`-typed fields behind strings or numeric IDs on
   the wire) would let the records move too. Out of scope for this prototype.
3. **Tick handlers calling `IHubContext<GameHub>` directly** — they should
   call `IGameNotifier` instead. Cheap to fix but not behaviour-preserving
   if any client code depends on SignalR-specific event shapes, so I left
   the direct calls for now.

## Verification

- `dotnet build FirstMud.slnx` → **0 errors, 0 warnings**.
- `dotnet test FirstMud.slnx` → **337 passing, 0 failing** (321 unit + 16 integration).
- Runtime behaviour is identical: same tick cadences, same command queue size,
  same error broadcast, same log lines.
