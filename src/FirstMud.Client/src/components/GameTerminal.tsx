import { useState, useEffect } from 'react';
import type { WorldStateSnapshot, GameMessage, ConnectionState, QuestNode, ZoneTile } from '../types/game';
import WorldMap from './WorldMap';
import StatusPanel from './StatusPanel';
import MessageLog from './MessageLog';
import ConnectionStatus from './ConnectionStatus';
import QuestLog from './QuestLog';
import PlayerCreation from './PlayerCreation';
import HelpOverlay from './HelpOverlay';
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
  const [showHelp, setShowHelp] = useState(false);

  useEffect(() => {
    if (!keyAction) return;
    switch (keyAction.type) {
      case 'move':
        // Server-side MoveCommand expects `deltaX`/`deltaY`, not dx/dy.
        sendCommand('move', { deltaX: keyAction.dx, deltaY: keyAction.dy });
        break;
      case 'interact':
        // Server-side InteractCommand expects an `objectId` — send a nil
        // guid for "interact with whatever is under me" until we have
        // real objects to click on.
        sendCommand('interact', { objectId: '00000000-0000-0000-0000-000000000000' });
        break;
      case 'inventory':
        // Server hub parses "openinventory", not "inventory".
        sendCommand('openinventory');
        break;
      case 'character':
        // No server-side CharacterCommand yet — silently ignore so we
        // don't flood the console with "Unknown command" errors.
        break;
      case 'quest':
        setShowQuestLog(prev => {
          const next = !prev;
          if (next) fetchAvailableQuests();
          return next;
        });
        break;
      case 'help':
        setShowHelp(prev => !prev);
        break;
      case 'escape':
        setShowHelp(false);
        setShowQuestLog(false);
        break;
      case 'pass':
        // No server-side PassCommand yet — silently ignore.
        break;
    }
  }, [keyAction, sendCommand, fetchAvailableQuests]);

  const handleAcceptQuest = (questId: string) => {
    sendCommand('acceptquest', { questId });
  };

  const handleCompleteQuest = (questId: string, outcome: string) => {
    // Server parses `chosenOutcome`, not `outcome`.
    sendCommand('completequest', { questId, chosenOutcome: outcome });
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

      {/* Help hint — bottom-right corner */}
      <div
        style={{
          position: 'absolute',
          bottom: '186px',
          right: '10px',
          color: '#666666',
          fontSize: '11px',
          letterSpacing: '0.05em',
          pointerEvents: 'none',
          fontFamily: 'monospace',
        }}
      >
        press [?] for help
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
      {showHelp && <HelpOverlay onClose={() => setShowHelp(false)} />}
    </div>
  );
}
