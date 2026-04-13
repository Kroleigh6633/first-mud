# Client engine/content split

The monolithic `useGameConnection` hook has been split into two collaborating hooks.
The old hook name remains as a thin delegator so no consumer needs to change.

## useHubConnection (engine / transport)

File: `src/FirstMud.Client/src/hooks/useHubConnection.ts`

Scope:
- Builds the SignalR `HubConnection` against `/gamehub`.
- Handles start, stop, authenticate-on-connect and authenticate-on-reconnect.
- Wires `onreconnecting`, `onreconnected`, `onclose`, and catches `start()` errors
  (including the benign StrictMode double-mount abort noise).
- Exposes a stable `sendCommand(command, payload)` that invokes the server's
  universal `SendCommand` hub method.
- Accepts a caller-supplied `registerHandlers` callback that it replays against
  every fresh `HubConnection` it builds, so domain subscribers don't need to
  re-subscribe on reconnect.
- Emits coarse lifecycle notifications (`connected` / `reconnecting` / `reconnected`
  / `disconnected` / `error`) as a tagged union through an `onLifecycle` callback.

MUST NOT know about:
- Server-specific event names (`WorldState`, `Inventory`, `CombatUpdate`, ...).
- Game domain payload shapes.
- React state slices or message formatting.
- Anything from `types/game.ts` other than `ConnectionState`.

## useGameState (content / domain)

File: `src/FirstMud.Client/src/hooks/useGameState.ts`

Scope:
- Consumes `useHubConnection` internally.
- Owns every `connection.on(...)` subscription the server can emit (~40 events).
- Owns every React state slice components render from (world state, inventory,
  combat, storage, city, companions, quests, recipes, crafting, equipment).
- Preserves all legacy quirks:
  - `weavePercent` 0-1 fraction is normalised to 0-100.
  - `QuestAccepted`, `NewQuestAvailable`, `NewZoneDiscovered`, `CompanionLayerUp`
    auto-refetch state via follow-up `sendCommand` calls.
  - `EquipmentChanged` and `CraftingComplete` trigger an immediate inventory
    refresh via `connection.invoke` (see "Straddlers" below).
  - Combat state self-clears 5s after `Victory` / `Defeat` / `Fled`.
- Reads `firstmud_player` from `localStorage` to decide `needsPlayerCreation`
  and drives the `enabled` flag on the transport hook accordingly.
- Translates transport lifecycle events into user-visible `system` messages
  via the lifecycle callback (`Connected to server.`, `Reconnecting...`, etc.).

MUST NOT know about:
- SignalR APIs beyond the `registerHandlers(hub => hub.on(...))` factory.
- Connection/reconnect/stop lifecycle.
- Hub URL, logger configuration, or the `SendCommand` method name.

## useGameConnection (compat shim)

File: `src/FirstMud.Client/src/hooks/useGameConnection.ts`

15-line delegator: `return useGameState()`. Re-exports `GameConnectionResult` as
an alias of `GameStateResult` so `App.tsx` needs no change. New code should
import `useGameState` directly.

## Straddlers: handlers that bridge transport and domain

A small number of handlers could not be cleanly isolated from the transport:

1. **`EquipmentChanged` and `CraftingComplete`** call `connection.invoke('SendCommand', 'openinventory', null)`
   directly instead of going through the hook's `sendCommand`. The original code
   did this and the semantics matter: `sendCommand` is memoised with an empty
   dep array and references `connectionRef.current`, which is fine, but these
   refetches fire from inside the same event tick as the state update and the
   original author chose to skip the state-check wrapper. Preserving that
   behaviour verbatim means the domain hook keeps a direct handle to the
   `connection` object passed into the registrar factory. This is fine — it's
   already the domain code running; it just happens to know the hub's one
   calling convention.

2. **Reconnect re-authentication** lives in `useHubConnection` because it's a
   transport concern (the server identifies clients by playerId on the
   connection), but conceptually it also affects domain state because the
   server then pushes a fresh `WorldState` snapshot. This is handled cleanly by
   the registrar-replay mechanism — domain handlers are re-subscribed against
   the same connection so they pick up the post-reauth push without any
   explicit coordination.

Neither of these leaks the *other* direction (domain handling inside the
transport hook, or connection lifecycle inside the domain hook), so the split
is clean in the important direction.

## What would belong in a third hook in the future

A natural next split, if this file grows again:

- **`useKeybinds`** — the keyboard-handling code currently inside
  `GameTerminal.tsx` (movement, panel toggles, quest waypoint nav, etc.).
  Today that lives entirely in the component; pulling it into a hook would
  let it consume `useGameState` without the component owning the dispatch
  table.
- **`useRenderPipeline`** — any canvas/rot.js rendering state. Currently the
  map canvas is driven directly from `zoneTiles` in the component; if we add
  FX, particle systems, or animated sprites, isolating that as a
  `useRenderPipeline(tiles, combat)` hook would keep rendering concerns out
  of the component body.
- **`useGameCommands`** — typed wrappers around `sendCommand` (`enterZone(id)`,
  `openInventory()`, `assignBuilder(companionId, buildingId)`). Today
  components pass `sendCommand` and stringly-typed command names. A command
  facade would make refactors safer and is the logical place for any retry,
  optimistic-update, or rate-limit behaviour.

None of these are needed today; the goal of this pass was purely to separate
transport from content without changing behaviour.
