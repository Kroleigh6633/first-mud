import { useState, useEffect, useRef, useCallback } from 'react';
import * as signalR from '@microsoft/signalr';
import type { WorldStateSnapshot, GameMessage, ConnectionState } from '../types/game';

const HUB_URL = 'http://localhost:5000/gamehub';
const DEV_PLAYER_ID = '00000000-0000-0000-0000-000000000001';
const MAX_MESSAGES = 200;

export interface GameConnectionResult {
  connectionState: ConnectionState;
  sendCommand: (command: string, payload?: unknown) => void;
  worldState: WorldStateSnapshot | null;
  messages: GameMessage[];
}

export function useGameConnection(): GameConnectionResult {
  const [connectionState, setConnectionState] = useState<ConnectionState>('disconnected');
  const [worldState, setWorldState] = useState<WorldStateSnapshot | null>(null);
  const [messages, setMessages] = useState<GameMessage[]>([]);
  const connectionRef = useRef<signalR.HubConnection | null>(null);

  const appendMessage = useCallback((msg: GameMessage) => {
    setMessages(prev => {
      const next = [...prev, msg];
      return next.length > MAX_MESSAGES ? next.slice(next.length - MAX_MESSAGES) : next;
    });
  }, []);

  useEffect(() => {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(HUB_URL)
      .withAutomaticReconnect()
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
      connection.invoke('Authenticate', DEV_PLAYER_ID).catch((err: unknown) => {
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

    setConnectionState('connecting');
    connection
      .start()
      .then(() => {
        setConnectionState('connected');
        return connection.invoke('Authenticate', DEV_PLAYER_ID);
      })
      .catch((err: unknown) => {
        console.error('Connection failed:', err);
        setConnectionState('error');
        appendMessage({
          timestamp: new Date().toISOString(),
          category: 'error',
          text: 'Failed to connect to game server.',
        });
      });

    return () => {
      connection.stop();
    };
  }, [appendMessage]);

  const sendCommand = useCallback((command: string, payload?: unknown) => {
    const connection = connectionRef.current;
    if (connection && connection.state === signalR.HubConnectionState.Connected) {
      connection.invoke('SendCommand', command, payload).catch((err: unknown) => {
        console.error('SendCommand failed:', err);
      });
    }
  }, []);

  return { connectionState, sendCommand, worldState, messages };
}
