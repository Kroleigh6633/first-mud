import type { WorldStateSnapshot, GameMessage, ConnectionState } from '../types/game';
import WorldMap from './WorldMap';
import StatusPanel from './StatusPanel';
import MessageLog from './MessageLog';
import ConnectionStatus from './ConnectionStatus';
import { useKeyboard } from '../hooks/useKeyboard';
import { useEffect } from 'react';

interface Props {
  connectionState: ConnectionState;
  sendCommand: (command: string, payload?: unknown) => void;
  worldState: WorldStateSnapshot | null;
  messages: GameMessage[];
}

export default function GameTerminal({ connectionState, sendCommand, worldState, messages }: Props) {
  const keyAction = useKeyboard();

  useEffect(() => {
    if (!keyAction) return;
    switch (keyAction.type) {
      case 'move':
        sendCommand('move', { dx: keyAction.dx, dy: keyAction.dy });
        break;
      case 'interact':
        sendCommand('interact');
        break;
      case 'inventory':
        sendCommand('inventory');
        break;
      case 'character':
        sendCommand('character');
        break;
      case 'quest':
        sendCommand('quest');
        break;
      case 'pass':
        sendCommand('pass');
        break;
    }
  }, [keyAction, sendCommand]);

  return (
    <div style={{
      display: 'grid',
      gridTemplateRows: '1fr 180px',
      gridTemplateColumns: '1fr',
      width: '100vw',
      height: '100vh',
      background: '#0d0d0d',
      color: '#00ff41',
      fontFamily: 'monospace',
      overflow: 'hidden',
      position: 'relative',
    }}>
      <ConnectionStatus state={connectionState} />

      {/* Top row: map + status */}
      <div style={{
        display: 'grid',
        gridTemplateColumns: '560px 1fr',
        overflow: 'hidden',
        borderBottom: '1px solid #1a3a1a',
      }}>
        {/* Map panel */}
        <div style={{
          borderRight: '1px solid #1a3a1a',
          display: 'flex',
          alignItems: 'flex-start',
          justifyContent: 'flex-start',
          overflow: 'hidden',
          padding: '4px',
          boxSizing: 'border-box',
        }}>
          <WorldMap worldState={worldState} />
        </div>

        {/* Status panel */}
        <div style={{ overflow: 'hidden' }}>
          <StatusPanel player={worldState?.player ?? null} />
        </div>
      </div>

      {/* Bottom: message log */}
      <div style={{ overflow: 'hidden' }}>
        <MessageLog messages={messages} />
      </div>
    </div>
  );
}
