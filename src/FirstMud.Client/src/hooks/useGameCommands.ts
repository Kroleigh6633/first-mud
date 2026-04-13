import { useMemo } from 'react';

/**
 * useGameCommands — typed facade over the untyped `sendCommand(name, payload)`
 * transport exposed by `useGameState` / `useHubConnection`.
 *
 * Every method on the returned object delegates to `sendCommand` with the
 * exact command-name string the server's GameHub.ParseCommand switch expects,
 * and a strongly-typed payload where one is required. This lets call-sites
 * stop passing magic strings and instead get IDE autocomplete, rename
 * support, and typo-catching from TypeScript.
 *
 * When adding a new server command:
 *   1. Add the `case "name"` branch in GameHub.ParseCommand.
 *   2. Add a method on `GameCommands` here that calls `sendCommand("name", …)`.
 *
 * Preserved quirks (do not "clean up"):
 *   - `flee()` sends `"flee"` (not `"combat flee"`). This exists as a legacy
 *     call from the auto-quest runner; the combat panel uses the proper
 *     `combatFlee()` method. Kept byte-identical to the previous code.
 *   - Payload `null` vs `undefined`: the underlying sendCommand normalises
 *     `undefined` → `null` before sending, so both are equivalent on the wire.
 */

export type SendCommandFn = (command: string, payload?: unknown) => void;

export type MoveDelta = { deltaX: number; deltaY: number };

export interface AutoFarmPayload {
  targetZone: { x: number; y: number } | null;
  maxDanger: number;
  priority: string;
}

export interface CraftPayload {
  recipeId: string;
  componentIds: string[];
  taperId?: string | null;
}

export interface PlaceBuildingPayload {
  buildingType: string;
  gridX: number;
  gridY: number;
}

export interface CombatUsePayload {
  encounterId: string;
  abilityName: string;
  targetId?: string | null;
}

export interface AutoSalvagePayload {
  category: string;
  maxWorkmanship: number;
}

export interface GameCommands {
  // Movement / world
  move: (delta: MoveDelta) => void;
  enterZone: (payload: { worldId: number; zoneId: string }) => void;
  harvest: () => void;
  portalHome: () => void;
  portalBack: () => void;

  // Panels / views
  openInventory: () => void;
  openStorage: () => void;
  viewCompanions: () => void;
  viewRecipes: () => void;
  viewCity: () => void;

  // Quests
  getQuests: () => void;
  acceptQuest: (payload: { questId: string }) => void;
  completeQuest: (payload: { questId: string; chosenOutcome: string }) => void;
  interactQuest: (payload: { questId: string }) => void;

  // Inventory / equipment
  equip: (payload: { itemId: string; slot?: string }) => void;
  unequip: (payload: { slot: string }) => void;
  useConsumable: (payload: { itemId: string }) => void;
  imbue: (payload: { itemId: string; taperId: string }) => void;
  lockItem: (payload: { itemId: string }) => void;

  // Salvage
  salvage: (payload: { itemId: string }) => void;
  queueSalvage: (payload: { itemId: string }) => void;
  salvageAll: (payload: { category: string }) => void;
  autoSalvage: (payload: AutoSalvagePayload) => void;

  // Storage
  deposit: (payload: { itemId: string }) => void;
  withdraw: (payload: { itemId: string }) => void;
  smelt: (payload: { amount: number }) => void;

  // Crafting / enchanting
  craft: (payload: CraftPayload) => void;
  practiceEnchanting: () => void;

  // Companions
  activateCompanion: (payload: { companionId: string }) => void;
  deactivateCompanion: (payload: { companionId: string }) => void;
  assignCompanionDuty: (payload: { companionId: string; duty: string }) => void;
  recallCompanion: (payload: { companionId: string }) => void;

  // City / buildings
  placeBuilding: (payload: PlaceBuildingPayload) => void;
  assignBuilder: (payload: { companionId: string; buildingId: string }) => void;
  unassignBuilder: (payload: { buildingId: string }) => void;
  buildStaffEverything: () => void;

  // Auto-farm
  autoFarm: (payload?: AutoFarmPayload) => void;

  // Combat
  combatUse: (payload: CombatUsePayload) => void;
  combatFlee: (payload: { encounterId: string }) => void;

  // Trade (stage 1)
  viewVendor: (payload: { npcId: string }) => void;
  buyItem: (payload: { npcId: string; itemName: string; quantity: number }) => void;
  sellItem: (payload: { npcId: string; itemName: string; quantity: number }) => void;

  /**
   * Legacy shorthand kept for the auto-quest runner. Sends the bare `"flee"`
   * command — the server does NOT currently parse this; prefer `combatFlee`
   * from real combat UI.
   */
  flee: () => void;
}

/**
 * Returns a stable, memoised map of typed command methods. The methods
 * themselves are closed over `sendCommand`; since the transport's
 * `sendCommand` is stable across renders, the returned object is stable too.
 */
export function useGameCommands(sendCommand: SendCommandFn): GameCommands {
  return useMemo<GameCommands>(() => ({
    move: (delta) => sendCommand('move', delta),
    enterZone: (p) => sendCommand('enterzone', p),
    harvest: () => sendCommand('harvest', null),
    portalHome: () => sendCommand('portalhome', null),
    portalBack: () => sendCommand('portalback', null),

    openInventory: () => sendCommand('openinventory'),
    openStorage: () => sendCommand('openstorage', null),
    viewCompanions: () => sendCommand('viewcompanions', null),
    viewRecipes: () => sendCommand('viewrecipes', null),
    viewCity: () => sendCommand('viewcity', null),

    getQuests: () => sendCommand('getquests'),
    acceptQuest: (p) => sendCommand('acceptquest', p),
    completeQuest: (p) => sendCommand('completequest', p),
    interactQuest: (p) => sendCommand('interactquest', p),

    equip: (p) => sendCommand('equip', p),
    unequip: (p) => sendCommand('unequip', p),
    useConsumable: (p) => sendCommand('useconsumable', p),
    imbue: (p) => sendCommand('imbue', p),
    lockItem: (p) => sendCommand('lockitem', p),

    salvage: (p) => sendCommand('salvage', p),
    queueSalvage: (p) => sendCommand('queuesalvage', p),
    salvageAll: (p) => sendCommand('salvageall', p),
    autoSalvage: (p) => sendCommand('autosalvage', p),

    deposit: (p) => sendCommand('deposit', p),
    withdraw: (p) => sendCommand('withdraw', p),
    smelt: (p) => sendCommand('smelt', p),

    craft: (p) => sendCommand('craft', p),
    practiceEnchanting: () => sendCommand('practiceenchanting', {}),

    activateCompanion: (p) => sendCommand('activatecompanion', p),
    deactivateCompanion: (p) => sendCommand('deactivatecompanion', p),
    assignCompanionDuty: (p) => sendCommand('assigncompanionduty', p),
    recallCompanion: (p) => sendCommand('recallcompanion', p),

    placeBuilding: (p) => sendCommand('placebuilding', p),
    assignBuilder: (p) => sendCommand('assignbuilder', p),
    unassignBuilder: (p) => sendCommand('unassignbuilder', p),
    buildStaffEverything: () => sendCommand('buildstaffeverything', null),

    autoFarm: (p) => sendCommand('autofarm', p ?? null),

    combatUse: (p) => sendCommand('combat use', p),
    combatFlee: (p) => sendCommand('combat flee', p),

    viewVendor: (p) => sendCommand('viewvendor', p),
    buyItem: (p) => sendCommand('buyitem', p),
    sellItem: (p) => sendCommand('sellitem', p),

    flee: () => sendCommand('flee', null),
  }), [sendCommand]);
}
