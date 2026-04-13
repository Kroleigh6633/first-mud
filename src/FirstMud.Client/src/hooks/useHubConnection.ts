import { useState, useEffect, useRef, useCallback } from 'react';
import * as signalR from '@microsoft/signalr';
import type { ConnectionState } from '../types/game';

/**
 * useHubConnection — game-agnostic SignalR transport hook.
 *
 * Responsibilities:
 *   - Build the HubConnection, start it, authenticate, handle reconnect/close.
 *   - Expose generic `on(eventName, handler)` subscription registration.
 *   - Expose `sendCommand(command, payload)` to invoke the server `SendCommand`
 *     hub method (the one universal command channel this game uses).
 *   - Surface a coarse `connectionState` to callers.
 *   - Emit lifecycle notifications (connected/disconnected/reconnecting/error)
 *     through a pluggable `onLifecycle` callback so UI code can translate them
 *     into user-visible messages without this hook knowing about message
 *     formats or categories.
 *
 * This hook MUST NOT know about:
 *   - Specific server event names (WorldState, Inventory, CombatUpdate, ...).
 *   - Game domain payload shapes (WorldStateSnapshot, QuestNode, etc.).
 *   - React state slices for inventory / quests / combat / companions.
 *   - Message formatting beyond raw lifecycle strings.
 *
 * If you find yourself importing a domain type from `../types/game` other
 * than `ConnectionState`, you're in the wrong hook — put it in useGameState.
 */

const HUB_URL = '/gamehub';

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

export type HubLifecycleEvent =
  | { kind: 'connected' }
  | { kind: 'authenticated'; detail: string }
  | { kind: 'reconnecting' }
  | { kind: 'reconnected' }
  | { kind: 'disconnected' }
  | { kind: 'error'; text: string };

export interface HubConnectionApi {
  connectionState: ConnectionState;
  sendCommand: (command: string, payload?: unknown) => void;
  /**
   * Register a raw SignalR event handler. Returns an unsubscribe function.
   * Safe to call from effects in consumer hooks; this hook re-invokes the
   * subscriber factory whenever the underlying connection is re-established.
   */
  registerHandlers: (register: (hub: signalR.HubConnection) => void) => void;
  /**
   * Direct access to the underlying connection — used by consumers that need
   * to invoke hub methods other than SendCommand (rare; today only the
   * re-authenticate-on-reconnect path, which this hook already handles).
   */
  connectionRef: React.MutableRefObject<signalR.HubConnection | null>;
}

export interface UseHubConnectionOptions {
  playerId: string | null;
  enabled: boolean;
  onLifecycle?: (event: HubLifecycleEvent) => void;
}

export function useHubConnection(options: UseHubConnectionOptions): HubConnectionApi {
  const { playerId, enabled, onLifecycle } = options;

  const [connectionState, setConnectionState] = useState<ConnectionState>('disconnected');
  const connectionRef = useRef<signalR.HubConnection | null>(null);

  // Stable ref for the lifecycle callback so the connection effect doesn't
  // re-run every time the parent re-renders with a fresh closure.
  const lifecycleRef = useRef<typeof onLifecycle>(onLifecycle);
  useEffect(() => {
    lifecycleRef.current = onLifecycle;
  }, [onLifecycle]);

  // Handler registrars accumulate here. The active connection effect replays
  // them against every fresh HubConnection it builds, so callers can register
  // once with a stable factory and not worry about reconnect lifecycles.
  const registrarsRef = useRef<Array<(hub: signalR.HubConnection) => void>>([]);

  const registerHandlers = useCallback((register: (hub: signalR.HubConnection) => void) => {
    registrarsRef.current.push(register);
    // If a connection already exists, apply immediately so late registrations
    // still take effect.
    const existing = connectionRef.current;
    if (existing) {
      register(existing);
    }
  }, []);

  useEffect(() => {
    if (!enabled) return;
    if (!playerId) return;

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(HUB_URL)
      .withAutomaticReconnect()
      .configureLogging(quietSignalRLogger)
      .build();

    connectionRef.current = connection;

    // Replay any pre-registered domain handlers.
    for (const register of registrarsRef.current) {
      register(connection);
    }

    connection.onreconnecting(() => {
      setConnectionState('connecting');
      lifecycleRef.current?.({ kind: 'reconnecting' });
    });

    connection.onreconnected(() => {
      setConnectionState('connected');
      lifecycleRef.current?.({ kind: 'reconnected' });
      connection.invoke('Authenticate', playerId).catch((err: unknown) => {
        console.error('Authenticate failed:', err);
      });
    });

    connection.onclose(() => {
      setConnectionState('disconnected');
      lifecycleRef.current?.({ kind: 'disconnected' });
    });

    let cancelled = false;

    setConnectionState('connecting');
    connection
      .start()
      .then(() => {
        if (cancelled) return;
        setConnectionState('connected');
        lifecycleRef.current?.({ kind: 'connected' });
        return connection.invoke('Authenticate', playerId);
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        const msg = err instanceof Error ? err.message : String(err);
        if (/stopped during negotiation|abort|bad gateway|502|status code/i.test(msg)) {
          return;
        }
        console.error('Connection failed:', err);
        setConnectionState('error');
        lifecycleRef.current?.({ kind: 'error', text: 'Failed to connect to game server.' });
      });

    return () => {
      cancelled = true;
      connection.stop().catch(() => {
        /* ignore stop errors during unmount */
      });
      if (connectionRef.current === connection) {
        connectionRef.current = null;
      }
    };
  }, [enabled, playerId]);

  const sendCommand = useCallback((command: string, payload?: unknown) => {
    const connection = connectionRef.current;
    if (connection && connection.state === signalR.HubConnectionState.Connected) {
      connection.invoke('SendCommand', command, payload ?? null).catch((err: unknown) => {
        console.error('SendCommand failed:', err);
      });
    }
  }, []);

  return {
    connectionState,
    sendCommand,
    registerHandlers,
    connectionRef,
  };
}
