import { useState, useEffect, useMemo, useRef } from 'react';
import type { WorldStateSnapshot, GameMessage, ConnectionState, QuestNode, ZoneTile, InventorySnapshot } from '../types/game';
import WorldMap from './WorldMap';
import StatusPanel from './StatusPanel';
import MessageLog from './MessageLog';
import ConnectionStatus from './ConnectionStatus';
import QuestLog from './QuestLog';
import PlayerCreation from './PlayerCreation';
import HelpOverlay from './HelpOverlay';
import InventoryPanel from './InventoryPanel';
import { useKeyboard } from '../hooks/useKeyboard';

interface Props {
  connectionState: ConnectionState;
  sendCommand: (command: string, payload?: unknown) => void;
  worldState: WorldStateSnapshot | null;
  messages: GameMessage[];
  appendMessage: (msg: GameMessage) => void;
  availableQuests: QuestNode[];
  fetchAvailableQuests: () => void;
  zoneTiles: ZoneTile[];
  inventory: InventorySnapshot | null;
  needsPlayerCreation: boolean;
  playerId: string | null;
}

function findTileAt(tiles: ZoneTile[], x: number, y: number): ZoneTile | null {
  return tiles.find(t => t.x === x && t.y === y) ?? null;
}

export default function GameTerminal({
  connectionState,
  sendCommand,
  worldState,
  messages,
  appendMessage,
  availableQuests,
  fetchAvailableQuests,
  zoneTiles,
  inventory,
  needsPlayerCreation,
}: Props) {
  const keyAction = useKeyboard();
  const [showQuestLog, setShowQuestLog] = useState(false);
  const [showHelp, setShowHelp] = useState(false);
  const [showInventory, setShowInventory] = useState(false);

  // Compute the zone tile the player is currently standing on, if any
  const currentTile = useMemo(() => {
    if (!worldState?.player) return null;
    return findTileAt(zoneTiles, worldState.player.x, worldState.player.y);
  }, [worldState?.player, zoneTiles]);

  // When the player arrives at a new zone tile (or leaves one), narrate it
  // into the message log so the user knows what they're walking on.
  const lastZoneIdRef = useRef<number | null>(null);
  useEffect(() => {
    const currentZoneId = currentTile?.zoneId ?? null;
    if (lastZoneIdRef.current === currentZoneId) return;
    // Skip the very first transition (initial page load), which would log
    // a false "You arrive at Open country."
    if (lastZoneIdRef.current !== null) {
      if (currentTile) {
        appendMessage({
          timestamp: new Date().toISOString(),
          category: 'system',
          text: `You arrive at ${currentTile.name}. ${currentTile.description}`,
        });
      } else {
        appendMessage({
          timestamp: new Date().toISOString(),
          category: 'system',
          text: 'You leave the marked paths. Open country stretches ahead.',
        });
      }
    }
    lastZoneIdRef.current = currentZoneId;
  }, [currentTile, appendMessage]);

  useEffect(() => {
    if (!keyAction) return;
    switch (keyAction.type) {
      case 'move':
        // Server-side MoveCommand expects `deltaX`/`deltaY`, not dx/dy.
        sendCommand('move', { deltaX: keyAction.dx, deltaY: keyAction.dy });
        break;
      case 'interact':
        // Describe what's at the player's feet. The server-side
        // InteractCommand doesn't broadcast anything back yet, so do the
        // feedback entirely on the client for now.
        if (currentTile) {
          appendMessage({
            timestamp: new Date().toISOString(),
            category: 'npc',
            text: `You take stock of ${currentTile.name}. ${currentTile.description}`,
          });
        } else {
          appendMessage({
            timestamp: new Date().toISOString(),
            category: 'system',
            text: 'There is nothing here to interact with. Keep riding.',
          });
        }
        break;
      case 'inventory':
        // Ask the server for the latest inventory and open the panel.
        sendCommand('openinventory');
        setShowInventory(prev => !prev);
        break;
      case 'character':
        // No server-side CharacterCommand yet — silently ignore.
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
        setShowInventory(false);
        break;
      case 'pass':
        // No server-side PassCommand yet — silently ignore.
        break;
    }
  }, [keyAction, sendCommand, fetchAvailableQuests, currentTile, appendMessage]);

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
          <StatusPanel player={worldState?.player ?? null} currentTile={currentTile} />
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
      {showInventory && (
        <InventoryPanel snapshot={inventory} onClose={() => setShowInventory(false)} />
      )}
      {showHelp && <HelpOverlay onClose={() => setShowHelp(false)} />}
    </div>
  );
}
