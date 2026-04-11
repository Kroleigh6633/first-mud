import { useState, useEffect, useRef, useCallback } from 'react';
import * as signalR from '@microsoft/signalr';
import type { WorldStateSnapshot, GameMessage, ConnectionState, QuestNode, QuestCompleteResult, ZoneTile, ZoneView } from '../types/game';

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

    const benign = /stopped during negotiation|abort(?:ed|error)/i.test(message);
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
  availableQuests: QuestNode[];
  fetchAvailableQuests: () => void;
  zoneTiles: ZoneTile[];
  needsPlayerCreation: boolean;
  playerId: string | null;
}

export function useGameConnection(): GameConnectionResult {
  const storedPlayer = getStoredPlayer();
  const resolvedPlayerId = storedPlayer?.id ?? null;

  const [connectionState, setConnectionState] = useState<ConnectionState>('disconnected');
  const [worldState, setWorldState] = useState<WorldStateSnapshot | null>(null);
  const [messages, setMessages] = useState<GameMessage[]>([]);
  const [availableQuests, setAvailableQuests] = useState<QuestNode[]>([]);
  const [zoneTiles, setZoneTiles] = useState<ZoneTile[]>([]);
  const [needsPlayerCreation, setNeedsPlayerCreation] = useState<boolean>(resolvedPlayerId === null);
  const [playerId] = useState<string | null>(resolvedPlayerId);
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
      setWorldState(snapshot);
    });

    // Server pushes this after Authenticate so the status panel populates
    connection.on('WorldState', (snapshot: WorldStateSnapshot) => {
      setWorldState(snapshot);
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

    connection.on('QuestAccepted', (payload: { questId?: string } | string) => {
      // Server broadcasts { PlayerId, QuestId } — pick out the id.
      const questId = typeof payload === 'string' ? payload : payload?.questId ?? 'unknown';
      appendMessage({
        timestamp: new Date().toISOString(),
        category: 'quest',
        text: `Quest accepted: ${questId}`,
      });
    });

    connection.on('QuestCompleted', (result: QuestCompleteResult) => {
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

    connection.on('PlayerLeveledUp', (newLevel: number) => {
      appendMessage({
        timestamp: new Date().toISOString(),
        category: 'system',
        text: `You have reached level ${newLevel}!`,
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
        if (/stopped during negotiation|abort/i.test(msg)) {
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
    availableQuests,
    fetchAvailableQuests,
    zoneTiles,
    needsPlayerCreation,
    playerId,
  };
}
