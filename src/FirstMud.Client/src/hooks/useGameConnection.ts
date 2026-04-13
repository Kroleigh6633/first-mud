import { useState, useEffect, useRef, useCallback } from 'react';
import * as signalR from '@microsoft/signalr';
import type { WorldStateSnapshot, GameMessage, ConnectionState, QuestNode, QuestCompleteResult, ZoneTile, ZoneView, InventorySnapshot, CombatUpdate, StorageViewSnapshot, AutoFarmStatus, EquipmentSlots, CompanionCapturedEvent, CompanionState, WanderingNpc, RecipeInfo, CraftingCompleteEvent, QuestWaypoint, QuestProgressMap, SmeltCompleteEvent } from '../types/game';

// Same-origin path — Vite dev server proxies /gamehub to the gameserver
// container, so this works from the host browser and from inside the e2e
// playwright container alike.
const HUB_URL = '/gamehub';
const MAX_MESSAGES = 200;
const STORAGE_KEY = 'firstmud_player';

/**
 * Custom SignalR logger that silently drops the two benign "connection
 * was stopped during negotiation" / AbortError messages that the library
 * writes directly to console.error when React StrictMode dev-mode
 * double-mounts the effect. All other SignalR messages route normally.
 */
const quietSignalRLogger: signalR.ILogger = {
  log(logLevel: signalR.LogLevel, message: string) {
    if (logLevel < signalR.LogLevel.Warning) return;

    const benign = /stopped during negotiation|abort(?:ed|error)|bad gateway|502|status code/i.test(message);
    if (benign) return;

    switch (logLevel) {
      case signalR.LogLevel.Critical:
      case signalR.LogLevel.Error:
        console.error(`[SignalR] ${message}`);
        break;
      case signalR.LogLevel.Warning:
        console.warn(`[SignalR] ${message}`);
        break;
      default:
        console.log(`[SignalR] ${message}`);
    }
  },
};

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

export interface GameConnectionResult {
  connectionState: ConnectionState;
  sendCommand: (command: string, payload?: unknown) => void;
  worldState: WorldStateSnapshot | null;
  messages: GameMessage[];
  appendMessage: (msg: GameMessage) => void;
  availableQuests: QuestNode[];
  fetchAvailableQuests: () => void;
  zoneTiles: ZoneTile[];
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
}

export function useGameConnection(): GameConnectionResult {
  const storedPlayer = getStoredPlayer();
  const resolvedPlayerId = storedPlayer?.id ?? null;

  const [connectionState, setConnectionState] = useState<ConnectionState>('disconnected');
  const [worldState, setWorldState] = useState<WorldStateSnapshot | null>(null);
  const [messages, setMessages] = useState<GameMessage[]>([]);
  const [availableQuests, setAvailableQuests] = useState<QuestNode[]>([]);
  const [zoneTiles, setZoneTiles] = useState<ZoneTile[]>([]);
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
  const connectionRef = useRef<signalR.HubConnection | null>(null);

  const appendMessage = useCallback((msg: GameMessage) => {
    setMessages(prev => {
      const next = [...prev, msg];
      return next.length > MAX_MESSAGES ? next.slice(next.length - MAX_MESSAGES) : next;
    });
  }, []);

  useEffect(() => {
    if (needsPlayerCreation) return;
    if (!playerId) return;

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(HUB_URL)
      .withAutomaticReconnect()
      .configureLogging(quietSignalRLogger)
      .build();

    connectionRef.current = connection;

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
      // Server sends weavePercent as a 0–1 fraction; normalise to 0–100 for display.
      setWorldState({
        ...snapshot,
        player: { ...snapshot.player, weavePercent: Math.round(snapshot.player.weavePercent * 100) },
      });
    });

    // Server pushes this after Authenticate so the status panel populates
    connection.on('WorldState', (snapshot: WorldStateSnapshot) => {
      // Server sends weavePercent as a 0–1 fraction; normalise to 0–100 for display.
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
      // Server broadcasts { PlayerId, QuestId, Title } — prefer the human-readable title.
      const title = typeof payload === 'string' ? payload : payload?.title ?? payload?.questId ?? 'unknown';
      appendMessage({
        timestamp: new Date().toISOString(),
        category: 'quest',
        text: `Quest accepted: ${title}`,
      });
      // Refresh the quest list so the accepted quest shows isTaken: true
      // (and the Accept button is replaced by Complete buttons).
      sendCommand('getquests');
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
      // Clear the quest waypoint and kill progress when any quest is completed
      setQuestWaypoint(null);
      if (result.questId) {
        setQuestProgress(prev => {
          const next = { ...prev };
          delete next[result.questId!];
          return next;
        });
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
        [payload.questId]: { questId: payload.questId, kills: payload.kills, required: payload.required },
      }));
      if (payload.kills >= payload.required) {
        appendMessage({
          timestamp: new Date().toISOString(),
          category: 'quest',
          text: `Quest objective complete! Navigate to the waypoint and press [E] to finish.`,
        });
      }
    });

    connection.on('ReputationChanged', (payload: { playerId?: string; factionTiers?: Record<string, string> }) => {
      // Server broadcasts { PlayerId, FactionTiers: { [factionId]: tier } }
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
      // Server broadcasts { Id, X, Y, ZoneId, World }.
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

    connection.on('Inventory', (snapshot: InventorySnapshot & { equippedItems?: Record<string, string> }) => {
      setInventory(snapshot);
      // The server now sends equippedItems in the Inventory payload so the client
      // always has the authoritative equipment state when the panel is (re)opened,
      // without depending solely on EquipmentChanged events.
      setEquipment(prev => {
        // Prefer the fresh equippedItems from the Inventory payload; fall back to
        // whatever was stored from the last EquipmentChanged event.
        const equipped = snapshot.equippedItems ?? prev.equippedItems ?? {};
        const findName = (slotKey: string) => {
          const itemId = equipped[slotKey];
          if (!itemId) return undefined;
          return snapshot.items.find(i => i.id === itemId)?.name;
        };
        return {
          ...prev,
          // Always overwrite equippedItems with the freshest known state
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
      // Auto-clear combat state when encounter ends
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
      // Store for animated reveal in StoragePanel
      setLastSmeltResult({ yields: payload.yields ?? [], message: payload.message ?? '' });
    });

    connection.on('HarvestComplete', (payload: { itemId?: string; name?: string; amount?: number; resourceType?: string }) => {
      appendMessage({
        timestamp: new Date().toISOString(),
        category: 'loot',
        text: `You harvested ${payload.amount ?? 1} unit(s) of ${payload.resourceType ?? 'resources'}.`,
      });
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
      // Legacy fields
      weaponId?: string;
      armorId?: string;
      accessoryId?: string;
    }) => {
      if (!payload) return;
      // New server sends equippedItems map; legacy fallback uses separate id fields
      if (payload.equippedItems) {
        const slots = payload.equippedItems;
        setEquipment(prev => ({
          ...prev,
          equippedItems: slots,
          // Populate named fields from the slot map so StatusPanel/CharacterSheet can display them
          // without needing to join against inventory (names are set via Inventory event)
        }));
        // Request a fresh inventory to get updated item names for equipment display
        connection.invoke('SendCommand', 'openinventory', null).catch(() => {/* ignore */});
      } else {
        // Legacy three-slot update
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
            // Server sends weavePercent as a 0–1 fraction; normalise to 0–100 for display.
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
        // Deduplicate by id
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
      // Auto-refresh quest log
      sendCommand('getquests');
    });

    connection.on('NewZoneDiscovered', (event: { name: string }) => {
      appendMessage({
        timestamp: new Date().toISOString(),
        category: 'wyrd',
        text: `Explorers report a new area: ${event.name}`,
      });
      // Re-enter current zone to refresh the zone view with the new zone included
      sendCommand('enterzone', { worldId: 1, zoneId: '00000000-0000-0000-0000-000000000000' });
    });

    connection.on('RecipeList', (payload: { recipes: RecipeInfo[] }) => {
      setRecipes(payload?.recipes ?? []);
    });

    connection.on('CraftingComplete', (result: CraftingCompleteEvent) => {
      setLastCraftResult(result);
      // Refresh inventory after a successful craft so the new item appears
      if (result.outcome === 'Success' || result.outcome === 'Discovery') {
        connection.invoke('SendCommand', 'openinventory', null).catch(() => {/* ignore */});
      }
    });

    // Server acknowledgment after every SendCommand — no client action needed.
    connection.on('CommandReceived', () => { /* ack — no action needed */ });

    // Companion advanced to a new layer — server also sends a GameMessage, so
    // just refresh the companion roster to pick up the new abilities.
    connection.on('CompanionLayerUp', (payload: { companionId: string; name: string; newLayer: number }) => {
      appendMessage({
        timestamp: new Date().toISOString(),
        category: 'system',
        text: `${payload.name} has advanced to Layer ${payload.newLayer}! New abilities unlocked.`,
      });
      // Refresh companion roster so the UI reflects the updated layer
      sendCommand('listcompanions');
    });

    connection.onreconnecting(() => {
      setConnectionState('connecting');
      appendMessage({
        timestamp: new Date().toISOString(),
        category: 'system',
        text: 'Reconnecting to server...',
      });
    });

    connection.onreconnected(() => {
      setConnectionState('connected');
      appendMessage({
        timestamp: new Date().toISOString(),
        category: 'system',
        text: 'Reconnected to server.',
      });
      connection.invoke('Authenticate', playerId).catch((err: unknown) => {
        console.error('Authenticate failed:', err);
      });
    });

    connection.onclose(() => {
      setConnectionState('disconnected');
      appendMessage({
        timestamp: new Date().toISOString(),
        category: 'system',
        text: 'Disconnected from server.',
      });
    });

    let cancelled = false;

    setConnectionState('connecting');
    connection
      .start()
      .then(() => {
        if (cancelled) return;
        setConnectionState('connected');
        return connection.invoke('Authenticate', playerId);
      })
      // Server auto-enqueues GetAvailableQuests + EnterZone in Authenticate
      // and also pushes a WorldState snapshot, so no further client-side
      // kickoff is needed here.
      .catch((err: unknown) => {
        // React StrictMode dev-mode double-mount aborts the first start()
        // mid-negotiation — that's benign and should not surface as a
        // user-visible error.
        if (cancelled) return;
        const msg = err instanceof Error ? err.message : String(err);
        if (/stopped during negotiation|abort|bad gateway|502|status code/i.test(msg)) {
          return;
        }
        console.error('Connection failed:', err);
        setConnectionState('error');
        appendMessage({
          timestamp: new Date().toISOString(),
          category: 'error',
          text: 'Failed to connect to game server.',
        });
      });

    return () => {
      cancelled = true;
      connection.stop().catch(() => {
        /* ignore stop errors during unmount */
      });
    };
  }, [appendMessage, needsPlayerCreation, playerId]);

  const sendCommand = useCallback((command: string, payload?: unknown) => {
    const connection = connectionRef.current;
    if (connection && connection.state === signalR.HubConnectionState.Connected) {
      // SignalR hub-method binding does NOT respect C# optional parameters,
      // so always pass an explicit value for payload (null if none).
      connection.invoke('SendCommand', command, payload ?? null).catch((err: unknown) => {
        console.error('SendCommand failed:', err);
      });
    }
  }, []);

  const fetchAvailableQuests = useCallback(() => {
    sendCommand('getquests');
  }, [sendCommand]);

  // If player creation state changes externally (after creation + reload), keep in sync
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
  };
}
