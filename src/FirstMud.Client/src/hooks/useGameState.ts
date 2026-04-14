import { useCallback, useEffect, useRef, useState } from 'react';
import type {
  WorldStateSnapshot,
  GameMessage,
  ConnectionState,
  QuestNode,
  QuestCompleteResult,
  ZoneTile,
  ZoneView,
  ZoneRumorsEvent,
  InventorySnapshot,
  CombatUpdate,
  StorageViewSnapshot,
  AutoFarmStatus,
  EquipmentSlots,
  CompanionCapturedEvent,
  CompanionState,
  WanderingNpc,
  RecipeInfo,
  CraftingCompleteEvent,
  QuestWaypoint,
  QuestProgressMap,
  SmeltCompleteEvent,
  CityViewSnapshot,
} from '../types/game';
import { useHubConnection, type HubLifecycleEvent } from './useHubConnection';

/**
 * useGameState — game-specific domain hook.
 *
 * Responsibilities:
 *   - Subscribe to every SignalR event the first-mud server emits
 *     (WorldState, Inventory, CombatUpdate, QuestAccepted, CompanionList, ...).
 *   - Maintain the React state slices that components render from.
 *   - Translate transport lifecycle into user-visible system messages.
 *   - Preserve the legacy quirks (weavePercent 0-1 → 0-100 normalisation,
 *     auto-refetch after QuestAccepted / NewQuestAvailable, inventory refresh
 *     after EquipmentChanged / CraftingComplete, 5s combat auto-clear, ...).
 *
 * This hook MUST NOT know about:
 *   - SignalR APIs beyond the `registerHandlers` callback shape.
 *   - Connection lifecycle management (connect/reconnect/stop). That is
 *     entirely the province of useHubConnection.
 *   - The hub URL, auth payloads, or logging configuration.
 */

const MAX_MESSAGES = 200;
const STORAGE_KEY = 'firstmud_player';

interface StoredPlayer {
  id: string;
  name: string;
}

function getStoredPlayer(): StoredPlayer | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return null;
    return JSON.parse(raw) as StoredPlayer;
  } catch {
    return null;
  }
}

export interface GameStateResult {
  connectionState: ConnectionState;
  sendCommand: (command: string, payload?: unknown) => void;
  worldState: WorldStateSnapshot | null;
  messages: GameMessage[];
  appendMessage: (msg: GameMessage) => void;
  availableQuests: QuestNode[];
  fetchAvailableQuests: () => void;
  zoneTiles: ZoneTile[];
  /**
   * Zone-name keyed rumor map. Populated on auth/zone-enter, consumed by the
   * world-map tile profile card to whisper flavor on unexplored tiles.
   */
  zoneRumors: Record<string, string>;
  inventory: InventorySnapshot | null;
  combat: CombatUpdate | null;
  needsPlayerCreation: boolean;
  playerId: string | null;
  atHomestead: boolean;
  storageView: StorageViewSnapshot | null;
  autoFarmStatus: AutoFarmStatus | null;
  equipment: EquipmentSlots;
  capturedCompanions: CompanionCapturedEvent[];
  wanderingNpcs: WanderingNpc[];
  companionRoster: CompanionState[];
  recipes: RecipeInfo[];
  lastCraftResult: CraftingCompleteEvent | null;
  lastSmeltResult: SmeltCompleteEvent | null;
  questWaypoint: QuestWaypoint | null;
  questProgress: QuestProgressMap;
  completedQuestIds: string[];
  cityView: CityViewSnapshot | null;
  /**
   * Server-emitted harvest failure. The `seq` field increments on every new
   * failure so consumers (the auto-quest runner in particular) can distinguish
   * "new failure since last tick" from a stale value.
   */
  lastHarvestFailure: { reason: string; seq: number } | null;
}

export function useGameState(): GameStateResult {
  const storedPlayer = getStoredPlayer();
  const resolvedPlayerId = storedPlayer?.id ?? null;

  const [worldState, setWorldState] = useState<WorldStateSnapshot | null>(null);
  const [messages, setMessages] = useState<GameMessage[]>([]);
  const [availableQuests, setAvailableQuests] = useState<QuestNode[]>([]);
  const [zoneTiles, setZoneTiles] = useState<ZoneTile[]>([]);
  const [zoneRumors, setZoneRumors] = useState<Record<string, string>>({});
  const [inventory, setInventory] = useState<InventorySnapshot | null>(null);
  const [combat, setCombat] = useState<CombatUpdate | null>(null);
  const [needsPlayerCreation, setNeedsPlayerCreation] = useState<boolean>(resolvedPlayerId === null);
  const [playerId] = useState<string | null>(resolvedPlayerId);
  const [atHomestead, setAtHomestead] = useState<boolean>(false);
  const [storageView, setStorageView] = useState<StorageViewSnapshot | null>(null);
  const [autoFarmStatus, setAutoFarmStatus] = useState<AutoFarmStatus | null>(null);
  const [equipment, setEquipment] = useState<EquipmentSlots>({});
  const [capturedCompanions, setCapturedCompanions] = useState<CompanionCapturedEvent[]>([]);
  const [wanderingNpcs, setWanderingNpcs] = useState<WanderingNpc[]>([]);
  const [companionRoster, setCompanionRoster] = useState<CompanionState[]>([]);
  const [recipes, setRecipes] = useState<RecipeInfo[]>([]);
  const [lastCraftResult, setLastCraftResult] = useState<CraftingCompleteEvent | null>(null);
  const [lastSmeltResult, setLastSmeltResult] = useState<SmeltCompleteEvent | null>(null);
  const [questWaypoint, setQuestWaypoint] = useState<QuestWaypoint | null>(null);
  const [questProgress, setQuestProgress] = useState<QuestProgressMap>({});
  const [completedQuestIds, setCompletedQuestIds] = useState<string[]>([]);
  const [cityView, setCityView] = useState<CityViewSnapshot | null>(null);
  const [lastHarvestFailure, setLastHarvestFailure] = useState<{ reason: string; seq: number } | null>(null);

  const appendMessage = useCallback((msg: GameMessage) => {
    setMessages(prev => {
      const next = [...prev, msg];
      return next.length > MAX_MESSAGES ? next.slice(next.length - MAX_MESSAGES) : next;
    });
  }, []);

  const handleLifecycle = useCallback((event: HubLifecycleEvent) => {
    switch (event.kind) {
      case 'connected':
        appendMessage({
          timestamp: new Date().toISOString(),
          category: 'system',
          text: 'Connected to server.',
        });
        break;
      case 'authenticated':
        appendMessage({
          timestamp: new Date().toISOString(),
          category: 'system',
          text: `Authenticated. ${event.detail}`,
        });
        break;
      case 'reconnecting':
        appendMessage({
          timestamp: new Date().toISOString(),
          category: 'system',
          text: 'Reconnecting to server...',
        });
        break;
      case 'reconnected':
        appendMessage({
          timestamp: new Date().toISOString(),
          category: 'system',
          text: 'Reconnected to server.',
        });
        break;
      case 'disconnected':
        appendMessage({
          timestamp: new Date().toISOString(),
          category: 'system',
          text: 'Disconnected from server.',
        });
        break;
      case 'error':
        appendMessage({
          timestamp: new Date().toISOString(),
          category: 'error',
          text: event.text,
        });
        break;
    }
  }, [appendMessage]);

  const hub = useHubConnection({
    playerId,
    enabled: !needsPlayerCreation,
    onLifecycle: handleLifecycle,
  });
  const { connectionState, sendCommand, registerHandlers, connectionRef } = hub;

  // sendCommand is stable across renders (useCallback with [] in the hub hook),
  // but bind through a ref anyway so handler closures below always see the
  // latest reference without needing to be re-registered.
  const sendCommandRef = useRef(sendCommand);
  useEffect(() => {
    sendCommandRef.current = sendCommand;
  }, [sendCommand]);

  // Register all domain event handlers exactly once. The hub replays these
  // against each fresh HubConnection, so we don't resubscribe on reconnect.
  const registeredRef = useRef(false);
  useEffect(() => {
    if (registeredRef.current) return;
    registeredRef.current = true;

    registerHandlers(connection => {
      connection.on('Connected', () => {
        appendMessage({
          timestamp: new Date().toISOString(),
          category: 'system',
          text: 'Connected to server.',
        });
      });

      connection.on('Authenticated', (data: unknown) => {
        appendMessage({
          timestamp: new Date().toISOString(),
          category: 'system',
          text: `Authenticated. ${typeof data === 'string' ? data : ''}`,
        });
      });

      connection.on('WorldStateUpdate', (snapshot: WorldStateSnapshot) => {
        setWorldState({
          ...snapshot,
          player: { ...snapshot.player, weavePercent: Math.round(snapshot.player.weavePercent * 100) },
        });
      });

      connection.on('WorldState', (snapshot: WorldStateSnapshot) => {
        setWorldState({
          ...snapshot,
          player: { ...snapshot.player, weavePercent: Math.round(snapshot.player.weavePercent * 100) },
        });
      });

      connection.on('GameMessage', (msg: GameMessage) => {
        appendMessage(msg);
      });

      connection.on('Error', (errorText: string) => {
        appendMessage({
          timestamp: new Date().toISOString(),
          category: 'error',
          text: errorText,
        });
      });

      connection.on('QuestAccepted', (payload: { questId?: string; title?: string } | string) => {
        const title = typeof payload === 'string' ? payload : payload?.title ?? payload?.questId ?? 'unknown';
        appendMessage({
          timestamp: new Date().toISOString(),
          category: 'quest',
          text: `Quest accepted: ${title}`,
        });
        sendCommandRef.current('getquests');
      });

      connection.on('QuestCompleted', (result: QuestCompleteResult & { questId?: string }) => {
        appendMessage({
          timestamp: new Date().toISOString(),
          category: 'quest',
          text: result.message,
        });
        if (result.wyrdSettled) {
          appendMessage({
            timestamp: new Date().toISOString(),
            category: 'wyrd',
            text: 'Your wyrd settles slightly.',
          });
        }
        setQuestWaypoint(null);
        if (result.questId) {
          setQuestProgress(prev => {
            const next = { ...prev };
            delete next[result.questId!];
            return next;
          });
          // Track completed quest ids so the auto-quest runner can evaluate
          // `requires.priorQuests` preconditions on the next selection pass.
          setCompletedQuestIds(prev => prev.includes(result.questId!) ? prev : [...prev, result.questId!]);
        }
      });

      connection.on('QuestWaypoint', (wp: QuestWaypoint) => {
        setQuestWaypoint(wp);
        appendMessage({
          timestamp: new Date().toISOString(),
          category: 'quest',
          text: `Waypoint set: ${wp.questTitle} — navigate to (${wp.targetX}, ${wp.targetY}). Press [N] to auto-navigate.`,
        });
      });

      connection.on('QuestKillProgress', (payload: { questId: string; kills: number; required: number }) => {
        setQuestProgress(prev => ({
          ...prev,
          [payload.questId]: {
            questId: payload.questId,
            kills: payload.kills,
            gathered: prev[payload.questId]?.gathered,
            required: payload.required,
          },
        }));
        if (payload.kills >= payload.required) {
          appendMessage({
            timestamp: new Date().toISOString(),
            category: 'quest',
            text: `Quest objective complete! Navigate to the waypoint and press [E] to finish.`,
          });
        }
      });

      connection.on('QuestHarvestProgress', (payload: { questId: string; gathered: number; required: number }) => {
        setQuestProgress(prev => ({
          ...prev,
          [payload.questId]: {
            questId: payload.questId,
            kills: prev[payload.questId]?.kills ?? 0,
            gathered: payload.gathered,
            required: payload.required,
          },
        }));
        if (payload.gathered >= payload.required) {
          appendMessage({
            timestamp: new Date().toISOString(),
            category: 'quest',
            text: `Quest objective complete! Navigate to the waypoint and press [E] to finish.`,
          });
        }
      });

      connection.on('ReputationChanged', (payload: { playerId?: string; factionTiers?: Record<string, string> }) => {
        if (!payload?.factionTiers) return;
        setWorldState(prev => {
          if (!prev) return prev;
          return {
            ...prev,
            player: {
              ...prev.player,
              factionTiers: {
                ...prev.player.factionTiers,
                ...(payload.factionTiers as Record<string, import('../types/game').ReputationTier>),
              },
            },
          };
        });
      });

      connection.on('PlayerMoved', (payload: { x: number; y: number; zoneId?: string; world?: string }) => {
        if (!payload || typeof payload.x !== 'number' || typeof payload.y !== 'number') return;
        setWorldState(prev => {
          if (!prev) return prev;
          return {
            ...prev,
            player: {
              ...prev.player,
              x: payload.x,
              y: payload.y,
            },
          };
        });
      });

      connection.on('PlayerLeveledUp', (payload: { newLevel?: number; playerId?: string; message?: string } | number) => {
        const level = typeof payload === 'number' ? payload : payload?.newLevel ?? '?';
        appendMessage({
          timestamp: new Date().toISOString(),
          category: 'system',
          text: `You have reached level ${level}!`,
        });
      });

      connection.on('PortalUnlocked', () => {
        appendMessage({
          timestamp: new Date().toISOString(),
          category: 'wyrd',
          text: 'A portal stirs...',
        });
      });

      connection.on('AvailableQuests', (quests: QuestNode[]) => {
        setAvailableQuests(quests);
      });

      connection.on('ZoneView', (view: ZoneView) => {
        setZoneTiles(view.tiles ?? []);
      });

      connection.on('ZoneRumors', (event: ZoneRumorsEvent) => {
        const map: Record<string, string> = {};
        for (const entry of event.rumors ?? []) {
          if (entry && typeof entry.zoneName === 'string' && typeof entry.rumor === 'string') {
            map[entry.zoneName] = entry.rumor;
          }
        }
        setZoneRumors(map);
      });

      connection.on('Inventory', (snapshot: InventorySnapshot & { equippedItems?: Record<string, string> }) => {
        setInventory(snapshot);
        setEquipment(prev => {
          const equipped = snapshot.equippedItems ?? prev.equippedItems ?? {};
          const findName = (slotKey: string) => {
            const itemId = equipped[slotKey];
            if (!itemId) return undefined;
            return snapshot.items.find(i => i.id === itemId)?.name;
          };
          return {
            ...prev,
            equippedItems: equipped,
            meleeWeaponName:  findName('MeleeWeapon'),
            rangedWeaponName: findName('RangedWeapon'),
            focusName:        findName('Focus'),
            headName:         findName('Head'),
            chestName:        findName('Chest'),
            legsName:         findName('Legs'),
            handsName:        findName('Hands'),
            feetName:         findName('Feet'),
            accessoryName:    findName('Accessory'),
          };
        });
      });

      connection.on('CombatUpdate', (update: CombatUpdate) => {
        setCombat(update);
        if (update.state === 'Victory' || update.state === 'Defeat' || update.state === 'Fled') {
          setTimeout(() => setCombat(null), 5000);
        }
      });

      connection.on('AtHomestead', () => {
        setAtHomestead(true);
      });

      connection.on('LeftHomestead', () => {
        setAtHomestead(false);
        setStorageView(null);
      });

      connection.on('StorageView', (snapshot: StorageViewSnapshot) => {
        setStorageView(snapshot);
      });

      connection.on('StorageUpdated', () => {
        // Signal that storage changed — GameTerminal will re-fetch if panel is open
      });

      connection.on('LootDropped', (loot: { id: string; name: string; description: string; workmanship: number; category: string; slot?: string }) => {
        appendMessage({
          timestamp: new Date().toISOString(),
          category: 'loot',
          text: `You found: ${loot.name} [Workmanship ${loot.workmanship}]!`,
        });
      });

      connection.on('SalvageComplete', (payload: { yields?: Array<{ name: string; quantity: number }>; message?: string }) => {
        if (!payload?.yields?.length) return;
        const summary = payload.yields.map(y => `${y.name} x${y.quantity}`).join(', ');
        appendMessage({
          timestamp: new Date().toISOString(),
          category: 'loot',
          text: `Salvage yielded: ${summary}`,
        });
      });

      connection.on('SmeltComplete', (payload: { yields?: Array<{ name: string; quantity: number }>; message?: string }) => {
        if (!payload?.yields?.length) return;
        const summary = payload.yields.map(y => `${y.name} x${y.quantity}`).join(', ');
        appendMessage({
          timestamp: new Date().toISOString(),
          category: 'loot',
          text: `Smelted: ${summary}`,
        });
        setLastSmeltResult({ yields: payload.yields ?? [], message: payload.message ?? '' });
      });

      connection.on('HarvestComplete', (payload: { itemId?: string; name?: string; amount?: number; resourceType?: string }) => {
        appendMessage({
          timestamp: new Date().toISOString(),
          category: 'loot',
          text: `You harvested ${payload.amount ?? 1} unit(s) of ${payload.resourceType ?? 'resources'}.`,
        });
      });

      connection.on('HarvestFailed', (payload: { reason?: string }) => {
        const reason = payload?.reason ?? 'unknown';
        setLastHarvestFailure(prev => ({ reason, seq: (prev?.seq ?? 0) + 1 }));
      });

      connection.on('AutoFarmStatus', (status: AutoFarmStatus) => {
        setAutoFarmStatus(status.active ? status : null);
      });

      connection.on('EquipmentChanged', (payload: {
        equippedItems?: Record<string, string>;
        changedItemId?: string;
        changedItemName?: string;
        slot?: string;
        category?: string;
        replacedItemId?: string;
        removedSlot?: string;
        weaponId?: string;
        armorId?: string;
        accessoryId?: string;
      }) => {
        if (!payload) return;
        if (payload.equippedItems) {
          const slots = payload.equippedItems;
          setEquipment(prev => ({
            ...prev,
            equippedItems: slots,
          }));
          // Request a fresh inventory to get updated item names for equipment display.
          // This uses the raw connection.invoke because it straddles transport and
          // domain: it's a domain-motivated refetch that needs to fire *now* before
          // the next render regardless of sendCommand's internal state check.
          connection.invoke('SendCommand', 'openinventory', null).catch(() => {/* ignore */});
        } else {
          setEquipment({
            weaponId: payload.weaponId ?? undefined,
            armorId: payload.armorId ?? undefined,
            accessoryId: payload.accessoryId ?? undefined,
          });
        }
      });

      connection.on('CompanionCaptured', (captured: CompanionCapturedEvent) => {
        setCapturedCompanions(prev => [...prev, captured]);
        appendMessage({
          timestamp: new Date().toISOString(),
          category: 'system',
          text: `You captured ${captured.originalMonsterName ?? captured.name}! Named it '${captured.name}'. (${captured.element} ${captured.type})`,
        });
      });

      connection.on('CompanionList', (companions: CompanionState[]) => {
        setCompanionRoster(companions ?? []);
      });

      connection.on('PlayerHealed', (payload: { id: string; currentHp: number; maxHp: number; weavePercent?: number; weaveState?: string }) => {
        setWorldState(prev => {
          if (!prev) return prev;
          return {
            ...prev,
            player: {
              ...prev.player,
              currentHp: payload.currentHp,
              maxHp: payload.maxHp,
              ...(payload.weavePercent !== undefined ? { weavePercent: Math.round(payload.weavePercent * 100) } : {}),
              ...(payload.weaveState !== undefined ? { weaveState: payload.weaveState as import('../types/game').WeaveState } : {}),
            },
          };
        });
      });

      // -----------------------------------------------------------------------
      // DungeonMasterService events
      // -----------------------------------------------------------------------

      connection.on('WorldEvent', (event: { timestamp: string; category: string; text: string }) => {
        appendMessage({
          timestamp: event.timestamp,
          category: 'wyrd',
          text: event.text,
        });
      });

      connection.on('NpcAppeared', (npc: WanderingNpc) => {
        setWanderingNpcs(prev => {
          const filtered = prev.filter(n => n.id !== npc.id);
          return [...filtered, npc];
        });
      });

      connection.on('NpcDespawned', (payload: { id: string }) => {
        setWanderingNpcs(prev => prev.filter(n => n.id !== payload.id));
      });

      connection.on('NewQuestAvailable', (event: { questId: string; title: string; faction: string; repReward: number; announcementText?: string }) => {
        appendMessage({
          timestamp: new Date().toISOString(),
          category: 'quest',
          text: event.announcementText ?? `A new quest is available: "${event.title}" (${event.faction}, +${event.repReward} rep)`,
        });
        sendCommandRef.current('getquests');
      });

      connection.on('NewZoneDiscovered', (event: { name: string }) => {
        appendMessage({
          timestamp: new Date().toISOString(),
          category: 'wyrd',
          text: `Explorers report a new area: ${event.name}`,
        });
        sendCommandRef.current('enterzone', { worldId: 1, zoneId: '00000000-0000-0000-0000-000000000000' });
      });

      connection.on('RecipeList', (payload: { recipes: RecipeInfo[] }) => {
        setRecipes(payload?.recipes ?? []);
      });

      connection.on('CraftingComplete', (result: CraftingCompleteEvent) => {
        setLastCraftResult(result);
        if (result.outcome === 'Success' || result.outcome === 'Discovery') {
          connection.invoke('SendCommand', 'openinventory', null).catch(() => {/* ignore */});
        }
      });

      connection.on('CityView', (snapshot: CityViewSnapshot) => {
        setCityView(snapshot ?? null);
      });

      connection.on('CommandReceived', () => { /* ack — no action needed */ });

      connection.on('CompanionLayerUp', (payload: { companionId: string; name: string; newLayer: number }) => {
        appendMessage({
          timestamp: new Date().toISOString(),
          category: 'system',
          text: `${payload.name} has advanced to Layer ${payload.newLayer}! New abilities unlocked.`,
        });
        sendCommandRef.current('viewcompanions');
      });
    });
    // connectionRef is only consulted for hand-off; registerHandlers itself is stable.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [registerHandlers, appendMessage]);

  // Silence unused-import warning — connectionRef is part of the API surface
  // we pass through but we don't need it in this hook body.
  void connectionRef;

  const fetchAvailableQuests = useCallback(() => {
    sendCommand('getquests');
  }, [sendCommand]);

  useEffect(() => {
    if (playerId !== null) {
      setNeedsPlayerCreation(false);
    }
  }, [playerId]);

  return {
    connectionState,
    sendCommand,
    worldState,
    messages,
    appendMessage,
    availableQuests,
    fetchAvailableQuests,
    zoneTiles,
    zoneRumors,
    inventory,
    combat,
    needsPlayerCreation,
    playerId,
    atHomestead,
    storageView,
    autoFarmStatus,
    equipment,
    capturedCompanions,
    wanderingNpcs,
    companionRoster,
    recipes,
    lastCraftResult,
    lastSmeltResult,
    questWaypoint,
    questProgress,
    completedQuestIds,
    cityView,
    lastHarvestFailure,
  };
}
