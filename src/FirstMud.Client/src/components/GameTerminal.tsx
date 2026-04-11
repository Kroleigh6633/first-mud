import { useState, useEffect } from 'react';
import type { WorldStateSnapshot, GameMessage, ConnectionState, QuestNode, ZoneTile } from '../types/game';
import WorldMap from './WorldMap';
import StatusPanel from './StatusPanel';
import MessageLog from './MessageLog';
import ConnectionStatus from './ConnectionStatus';
import QuestLog from './QuestLog';
import PlayerCreation from './PlayerCreation';
import { useKeyboard } from '../hooks/useKeyboard';

interface Props {
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

export default function GameTerminal({
  connectionState,
  sendCommand,
  worldState,
  messages,
  availableQuests,
  fetchAvailableQuests,
  zoneTiles,
  needsPlayerCreation,
}: Props) {
  const keyAction = useKeyboard();
  const [showQuestLog, setShowQuestLog] = useState(false);

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
        setShowQuestLog(prev => {
          const next = !prev;
          if (next) fetchAvailableQuests();
          return next;
        });
        break;
      case 'pass':
        sendCommand('pass');
        break;
    }
  }, [keyAction, sendCommand, fetchAvailableQuests]);

  const handleAcceptQuest = (questId: string) => {
    sendCommand('acceptquest', { questId });
  };

  const handleCompleteQuest = (questId: string, outcome: string) => {
    sendCommand('completequest', { questId, outcome });
  };

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
          <WorldMap worldState={worldState} zoneTiles={zoneTiles} />
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

      {/* Modals */}
      {needsPlayerCreation && (
        <PlayerCreation onCreated={() => { /* reload handled inside PlayerCreation */ }} />
      )}
      {!needsPlayerCreation && showQuestLog && (
        <QuestLog
          quests={availableQuests}
          onAccept={handleAcceptQuest}
          onComplete={handleCompleteQuest}
          onClose={() => setShowQuestLog(false)}
        />
      )}
    </div>
  );
}
