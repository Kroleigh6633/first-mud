import { useState, useEffect, useMemo, useRef } from 'react';
import type { WorldStateSnapshot, GameMessage, ConnectionState, QuestNode, ZoneTile, InventorySnapshot, CombatUpdate, StorageViewSnapshot, AutoFarmStatus, EquipmentSlots, WanderingNpc, CompanionState } from '../types/game';
import WorldMap from './WorldMap';
import StatusPanel from './StatusPanel';
import MessageLog from './MessageLog';
import ConnectionStatus from './ConnectionStatus';
import QuestLog from './QuestLog';
import PlayerCreation from './PlayerCreation';
import HelpOverlay from './HelpOverlay';
import InventoryPanel from './InventoryPanel';
import StoragePanel from './StoragePanel';
import CharacterSheet from './CharacterSheet';
import CombatPanel from './CombatPanel';
import CompanionPanel from './CompanionPanel';
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
  combat: CombatUpdate | null;
  needsPlayerCreation: boolean;
  playerId: string | null;
  atHomestead: boolean;
  storageView: StorageViewSnapshot | null;
  autoFarmStatus: AutoFarmStatus | null;
  equipment: EquipmentSlots;
  capturedCompanions?: unknown[];
  wanderingNpcs?: WanderingNpc[];
  companionRoster?: CompanionState[];
}

/**
 * Finds the zone tile the player is standing in. Each zone occupies a 3×3
 * area centered on its grid coordinate — so the player doesn't have to
 * land on the exact pixel. Exact matches take priority; then within-1.
 */
function findTileAt(tiles: ZoneTile[], x: number, y: number): ZoneTile | null {
  const exact = tiles.find(t => t.x === x && t.y === y);
  if (exact) return exact;
  let best: ZoneTile | null = null;
  let bestDist = Infinity;
  for (const t of tiles) {
    const dx = Math.abs(t.x - x);
    const dy = Math.abs(t.y - y);
    if (dx <= 1 && dy <= 1) {
      const dist = dx + dy;
      if (dist < bestDist) { bestDist = dist; best = t; }
    }
  }
  return best;
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
  combat,
  needsPlayerCreation,
  atHomestead,
  storageView,
  autoFarmStatus,
  equipment,
  wanderingNpcs = [],
  companionRoster = [],
}: Props) {
  const keyAction = useKeyboard();
  const [showQuestLog, setShowQuestLog] = useState(false);
  const [showHelp, setShowHelp] = useState(false);
  const [showInventory, setShowInventory] = useState(false);
  const [showCharSheet, setShowCharSheet] = useState(false);
  const [showStorage, setShowStorage] = useState(false);
  const [showCompanions, setShowCompanions] = useState(false);

  // Use refs for values that the key handler reads but should NOT
  // cause the effect to re-fire when they change. This prevents the
  // portal infinite loop: atHomestead toggling → effect re-runs →
  // sends opposite portal command → toggles again.
  // Compute the zone tile the player is currently standing on, if any
  const currentTile = useMemo(() => {
    if (!worldState?.player) return null;
    return findTileAt(zoneTiles, worldState.player.x, worldState.player.y);
  }, [worldState?.player, zoneTiles]);

  const atHomesteadRef = useRef(atHomestead);
  atHomesteadRef.current = atHomestead;
  const autoFarmRef = useRef(autoFarmStatus);
  autoFarmRef.current = autoFarmStatus;
  const currentTileRef = useRef(currentTile);
  currentTileRef.current = currentTile;

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
      case 'interact': {
        const tile = currentTileRef.current;
        if (tile) {
          appendMessage({
            timestamp: new Date().toISOString(),
            category: 'npc',
            text: `You take stock of ${tile.name}. ${tile.description}`,
          });
        } else {
          appendMessage({
            timestamp: new Date().toISOString(),
            category: 'system',
            text: 'There is nothing here to interact with. Keep riding.',
          });
        }
        break;
      }
      case 'inventory':
        // Ask the server for the latest inventory and open the panel.
        sendCommand('openinventory');
        setShowInventory(prev => !prev);
        break;
      case 'character':
        setShowCharSheet(prev => !prev);
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
      case 'portal':
        if (atHomesteadRef.current) {
          sendCommand('portalback', null);
        } else {
          sendCommand('portalhome', null);
        }
        break;
      case 'harvest':
        sendCommand('harvest', null);
        break;
      case 'autofarm':
        sendCommand('autofarm', { durationSeconds: 300 });
        break;
      case 'storage':
        if (!atHomesteadRef.current) {
          appendMessage({
            timestamp: new Date().toISOString(),
            category: 'system',
            text: 'You must be at your homestead to access storage. Press [P] to portal home.',
          });
        } else {
          sendCommand('openstorage', null);
          setShowStorage(prev => !prev);
        }
        break;
      case 'companions':
        sendCommand('viewcompanions', null);
        setShowCompanions(prev => !prev);
        break;
      case 'escape':
        setShowHelp(false);
        setShowQuestLog(false);
        setShowInventory(false);
        setShowCharSheet(false);
        setShowStorage(false);
        setShowCompanions(false);
        break;
      case 'pass':
        appendMessage({
          timestamp: new Date().toISOString(),
          category: 'system',
          text: 'You wait. The world continues around you.',
        });
        break;
    }
  // Only re-run when keyAction or the stable callbacks change — NOT when
  // atHomestead / autoFarmStatus / currentTile change (read via refs).
  }, [keyAction, sendCommand, fetchAvailableQuests, appendMessage]);

  const handleAcceptQuest = (questId: string) => {
    sendCommand('acceptquest', { questId });
  };

  const handleCompleteQuest = (questId: string, outcome: string) => {
    // Server parses `chosenOutcome`, not `outcome`.
    sendCommand('completequest', { questId, chosenOutcome: outcome });
  };

  const handleDeposit = (itemId: string) => {
    sendCommand('deposit', { itemId });
    // Re-fetch storage and inventory after deposit
    setTimeout(() => {
      sendCommand('openstorage', null);
      sendCommand('openinventory', null);
    }, 300);
  };

  const handleWithdraw = (itemId: string) => {
    sendCommand('withdraw', { itemId });
    setTimeout(() => {
      sendCommand('openstorage', null);
      sendCommand('openinventory', null);
    }, 300);
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
          <WorldMap worldState={worldState} zoneTiles={zoneTiles} wanderingNpcs={wanderingNpcs} />
        </div>

        {/* Status panel */}
        <div style={{ overflow: 'hidden' }}>
          <StatusPanel player={worldState?.player ?? null} currentTile={currentTile} equipment={equipment} />
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

      {/* Homestead indicator */}
      {atHomestead && (
        <div
          style={{
            position: 'absolute',
            bottom: '186px',
            left: '10px',
            color: '#ccff88',
            fontSize: '11px',
            letterSpacing: '0.05em',
            pointerEvents: 'none',
            fontFamily: 'monospace',
          }}
        >
          ◈ Homestead — healing +10 HP/s · [V] storage · [P] portal back
        </div>
      )}

      {/* Auto-farm status bar */}
      {autoFarmStatus?.active && (
        <div
          style={{
            position: 'absolute',
            top: '4px',
            left: '50%',
            transform: 'translateX(-50%)',
            background: '#0d1a0d',
            border: '1px solid #00aa33',
            color: '#00ff41',
            fontSize: '11px',
            fontFamily: 'monospace',
            padding: '3px 14px',
            letterSpacing: '0.08em',
            pointerEvents: 'none',
          }}
        >
          AUTO-FARM ACTIVE · Press [F] to stop
        </div>
      )}

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
        <InventoryPanel
          snapshot={inventory}
          equipment={equipment}
          onClose={() => setShowInventory(false)}
          sendCommand={sendCommand}
          atHomestead={atHomestead}
        />
      )}
      {showCharSheet && (
        <CharacterSheet player={worldState?.player ?? null} equipment={equipment} onClose={() => setShowCharSheet(false)} />
      )}
      {combat && <CombatPanel combat={combat} sendCommand={sendCommand} />}
      {showStorage && atHomestead && (
        <StoragePanel
          snapshot={storageView}
          inventoryItems={inventory?.items ?? []}
          onDeposit={handleDeposit}
          onWithdraw={handleWithdraw}
          onClose={() => setShowStorage(false)}
        />
      )}
      {showCompanions && (
        <CompanionPanel
          companions={companionRoster}
          onActivate={(id) => sendCommand('activatecompanion', { companionId: id })}
          onDeactivate={(id) => sendCommand('deactivatecompanion', { companionId: id })}
          onClose={() => setShowCompanions(false)}
        />
      )}
      {showHelp && <HelpOverlay onClose={() => setShowHelp(false)} />}
    </div>
  );
}
