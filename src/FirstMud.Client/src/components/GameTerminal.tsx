import { useState, useEffect, useMemo, useRef } from 'react';
import type { WorldStateSnapshot, GameMessage, ConnectionState, QuestNode, ZoneTile, InventorySnapshot, CombatUpdate, StorageViewSnapshot, AutoFarmStatus, EquipmentSlots, WanderingNpc, CompanionState, RecipeInfo, CraftingCompleteEvent, QuestWaypoint, QuestProgressMap } from '../types/game';
import WorldMap from './WorldMap';
import { getBiome } from '../utils/biome';
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
import CraftingPanel from './CraftingPanel';
import AutoFarmPicker from './AutoFarmPicker';
import type { AutoFarmSettings } from './AutoFarmPicker';
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
  recipes?: RecipeInfo[];
  lastCraftResult?: CraftingCompleteEvent | null;
  questWaypoint?: QuestWaypoint | null;
  questProgress?: QuestProgressMap;
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

// Simple step-toward-waypoint helper used by auto-navigate
function stepToward(
  playerX: number,
  playerY: number,
  targetX: number,
  targetY: number,
  getB: (x: number, y: number) => { type: string },
): { deltaX: number; deltaY: number } | null {
  const dx = Math.sign(targetX - playerX);
  const dy = Math.sign(targetY - playerY);

  // Try direct diagonal/cardinal step first
  if (dx !== 0 || dy !== 0) {
    const directBiome = getB(playerX + dx, playerY + dy);
    if (directBiome.type !== 'water' && directBiome.type !== 'snowMountain') {
      return { deltaX: dx, deltaY: dy };
    }
  }

  // Try horizontal only
  if (dx !== 0) {
    const hBiome = getB(playerX + dx, playerY);
    if (hBiome.type !== 'water' && hBiome.type !== 'snowMountain')
      return { deltaX: dx, deltaY: 0 };
  }

  // Try vertical only
  if (dy !== 0) {
    const vBiome = getB(playerX, playerY + dy);
    if (vBiome.type !== 'water' && vBiome.type !== 'snowMountain')
      return { deltaX: 0, deltaY: dy };
  }

  return null; // stuck
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
  recipes = [],
  lastCraftResult = null,
  questWaypoint = null,
  questProgress = {},
}: Props) {
  const keyAction = useKeyboard();
  const [showQuestLog, setShowQuestLog] = useState(false);
  const [showHelp, setShowHelp] = useState(false);
  const [showInventory, setShowInventory] = useState(false);
  const [showCharSheet, setShowCharSheet] = useState(false);
  const [showStorage, setShowStorage] = useState(false);
  const [showCompanions, setShowCompanions] = useState(false);
  const [showCrafting, setShowCrafting] = useState(false);
  const [showAutoFarmPicker, setShowAutoFarmPicker] = useState(false);
  const [statusCollapsed, setStatusCollapsed] = useState(false);
  const [autoNavigating, setAutoNavigating] = useState(false);
  const [autoQuestActive, setAutoQuestActive] = useState(false);
  const [autoQuestIndex, setAutoQuestIndex] = useState(0);
  const [_autoQuestTotal, setAutoQuestTotal] = useState(0);
  const [_autoQuestTitle, setAutoQuestTitle] = useState('');

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
  const questWaypointRef = useRef(questWaypoint);
  questWaypointRef.current = questWaypoint;
  const autoNavigatingRef = useRef(autoNavigating);
  autoNavigatingRef.current = autoNavigating;
  const worldStateRef = useRef(worldState);
  worldStateRef.current = worldState;
  const questProgressRef = useRef(questProgress);
  questProgressRef.current = questProgress;
  const availableQuestsRef = useRef(availableQuests);
  availableQuestsRef.current = availableQuests;
  const autoQuestActiveRef = useRef(autoQuestActive);
  autoQuestActiveRef.current = autoQuestActive;
  const autoQuestIndexRef = useRef(autoQuestIndex);
  autoQuestIndexRef.current = autoQuestIndex;

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
        const { x, y } = worldState?.player ?? { x: 0, y: 0 };
        const biome = getBiome(x, y);
        appendMessage({
          timestamp: new Date().toISOString(),
          category: 'system',
          text: `You leave the marked paths. ${biome.name} stretches ahead.`,
        });
      }
    }
    lastZoneIdRef.current = currentZoneId;
  }, [currentTile, appendMessage]);

  // Auto-navigate interval: steps toward the waypoint every 300ms
  useEffect(() => {
    if (!autoNavigating) return;

    const intervalId = setInterval(() => {
      const wp = questWaypointRef.current;
      const player = worldStateRef.current?.player;
      if (!wp || !player) {
        setAutoNavigating(false);
        return;
      }

      const distX = Math.abs(wp.targetX - player.x);
      const distY = Math.abs(wp.targetY - player.y);

      if (distX <= 2 && distY <= 2) {
        setAutoNavigating(false);
        appendMessage({
          timestamp: new Date().toISOString(),
          category: 'quest',
          text: `Arrived at waypoint: ${wp.questTitle}.`,
        });
        // Auto-attempt quest interaction on arrival
        sendCommand('interactquest', { questId: wp.questId });
        return;
      }

      const step = stepToward(player.x, player.y, wp.targetX, wp.targetY, getBiome);
      if (!step) {
        setAutoNavigating(false);
        appendMessage({
          timestamp: new Date().toISOString(),
          category: 'system',
          text: 'Auto-navigate: path blocked.',
        });
        return;
      }

      sendCommand('move', step);
    }, 300);

    return () => clearInterval(intervalId);
  }, [autoNavigating, sendCommand, appendMessage]);

  useEffect(() => {
    if (!keyAction) return;
    switch (keyAction.type) {
      case 'move':
        // Cancel auto-navigate on manual movement
        if (autoNavigatingRef.current) {
          setAutoNavigating(false);
          appendMessage({
            timestamp: new Date().toISOString(),
            category: 'system',
            text: 'Auto-navigate cancelled.',
          });
        }
        // Server-side MoveCommand expects `deltaX`/`deltaY`, not dx/dy.
        sendCommand('move', { deltaX: keyAction.dx, deltaY: keyAction.dy });
        break;
      case 'interact': {
        // Check if the player is within 2 tiles of a quest waypoint first
        const wp = questWaypointRef.current;
        const player = worldStateRef.current?.player;
        if (wp && player) {
          const dx = Math.abs(wp.targetX - player.x);
          const dy = Math.abs(wp.targetY - player.y);
          if (dx <= 2 && dy <= 2) {
            sendCommand('interactquest', { questId: wp.questId });
            break;
          }
        }
        // Fall back to zone tile description
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
        if (autoFarmRef.current?.active) {
          // Already running — stop it
          sendCommand('autofarm', null);
        } else {
          // Show the picker panel instead of immediately starting
          setShowAutoFarmPicker(true);
        }
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
      case 'crafting':
        sendCommand('viewrecipes', null);
        setShowCrafting(prev => !prev);
        break;
      case 'navigate': {
        const wp = questWaypointRef.current;
        if (!wp) {
          // Try to auto-accept the first available quest then navigate
          const quests = availableQuestsRef.current;
          const first = quests.find(q => !q.isTaken) ?? quests[0];
          if (first && !first.isTaken) {
            sendCommand('acceptquest', { questId: first.questId });
            appendMessage({
              timestamp: new Date().toISOString(),
              category: 'quest',
              text: `Accepted "${first.title}". Waiting for waypoint — press [N] again to navigate.`,
            });
          } else {
            appendMessage({
              timestamp: new Date().toISOString(),
              category: 'system',
              text: 'No active quest waypoint. Open the quest log [Q] to accept a quest.',
            });
          }
          break;
        }
        if (autoNavigatingRef.current) {
          setAutoNavigating(false);
          appendMessage({
            timestamp: new Date().toISOString(),
            category: 'system',
            text: 'Auto-navigate cancelled.',
          });
        } else {
          setAutoNavigating(true);
          appendMessage({
            timestamp: new Date().toISOString(),
            category: 'system',
            text: `Navigating to ${wp.questTitle}... Press [N] or any movement key to cancel.`,
          });
        }
        break;
      }
      case 'autoquest': {
        if (autoQuestActiveRef.current) {
          // Stop auto-quest run
          setAutoQuestActive(false);
          setAutoNavigating(false);
          appendMessage({
            timestamp: new Date().toISOString(),
            category: 'quest',
            text: 'Quest auto-run stopped.',
          });
        } else {
          const quests = availableQuestsRef.current;
          if (quests.length === 0) {
            appendMessage({
              timestamp: new Date().toISOString(),
              category: 'system',
              text: 'No quests available. Press [Q] to open the quest log.',
            });
            break;
          }
          // Sort same as QuestLog: taken first, then by rep reward desc
          const ordered = [...quests].sort((a, b) => {
            if (a.isTaken && !b.isTaken) return -1;
            if (!a.isTaken && b.isTaken) return 1;
            return b.reputationReward - a.reputationReward;
          });
          setAutoQuestActive(true);
          setAutoQuestIndex(0);
          setAutoQuestTotal(ordered.length);
          setAutoQuestTitle(ordered[0]?.title ?? '');
          appendMessage({
            timestamp: new Date().toISOString(),
            category: 'quest',
            text: `Quest auto-run started — ${ordered.length} quest${ordered.length !== 1 ? 's' : ''} queued. Press [L] to stop.`,
          });
        }
        break;
      }
      case 'escape':
        setAutoNavigating(false);
        setAutoQuestActive(false);
        setShowHelp(false);
        setShowQuestLog(false);
        setShowInventory(false);
        setShowCharSheet(false);
        setShowStorage(false);
        setShowCompanions(false);
        setShowCrafting(false);
        setShowAutoFarmPicker(false);
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

  const handleAutoFarmStart = (settings: AutoFarmSettings) => {
    setShowAutoFarmPicker(false);
    sendCommand('autofarm', {
      targetZone: settings.targetZone
        ? { x: settings.targetZone.x, y: settings.targetZone.y }
        : null,
      maxDanger: settings.maxDanger,
      priority: settings.priority,
    });
  };

  // Tracks the questId we are currently waiting on a waypoint for
  const pendingWaypointQuestIdRef = useRef<string | null>(null);

  // Auto-quest orchestration: when autoQuestActive, walk through unfinished quests
  // in rep-reward order, accepting each and auto-navigating.
  useEffect(() => {
    if (!autoQuestActive) return;

    const quests = availableQuestsRef.current;
    // Sort by rep reward descending; taken quests first
    const ordered = [...quests].sort((a, b) => {
      if (a.isTaken && !b.isTaken) return -1;
      if (!a.isTaken && b.isTaken) return 1;
      return b.reputationReward - a.reputationReward;
    });

    const nextQuest = ordered[autoQuestIndexRef.current];

    if (!nextQuest) {
      // All done
      setAutoQuestActive(false);
      pendingWaypointQuestIdRef.current = null;
      appendMessage({
        timestamp: new Date().toISOString(),
        category: 'quest',
        text: 'All quests completed! Quest auto-run finished.',
      });
      return;
    }

    setAutoQuestTitle(nextQuest.title);

    // Accept if not taken
    if (!nextQuest.isTaken) {
      sendCommand('acceptquest', { questId: nextQuest.questId });
    }

    // Mark that we are waiting for this quest's waypoint
    pendingWaypointQuestIdRef.current = nextQuest.questId;

    // Start auto-navigate immediately if the waypoint is already present
    const wp = questWaypointRef.current;
    if (wp && wp.questId === nextQuest.questId) {
      pendingWaypointQuestIdRef.current = null;
      setAutoNavigating(true);
    }
    // Otherwise the questWaypoint watcher below will fire once the server
    // responds with the waypoint and start navigation at that point.
  }, [autoQuestActive, autoQuestIndex, sendCommand, appendMessage]);

  // Watch for the questWaypoint to arrive while auto-quest is waiting for it,
  // then kick off navigation.  This is the fix for the L-key not moving bug:
  // acceptquest is async — the waypoint arrives after the orchestration effect
  // runs, so we need a separate effect that reacts to the new prop value.
  useEffect(() => {
    if (!questWaypoint) return;
    if (!autoQuestActiveRef.current) return;
    if (pendingWaypointQuestIdRef.current !== questWaypoint.questId) return;

    // The waypoint we were waiting for has arrived — start navigating
    pendingWaypointQuestIdRef.current = null;
    setAutoNavigating(true);
    appendMessage({
      timestamp: new Date().toISOString(),
      category: 'quest',
      text: `Waypoint received — navigating to ${questWaypoint.questTitle}...`,
    });
  }, [questWaypoint, appendMessage]);

  // When auto-quest is running and we arrive (autoNavigating stops), advance to next quest
  useEffect(() => {
    if (!autoQuestActiveRef.current) return;
    if (autoNavigating) return; // still walking
    // We just stopped navigating — assume current quest was handled, move on
    // Small delay so server processes completion before we start next quest
    const timer = setTimeout(() => {
      if (!autoQuestActiveRef.current) return;
      setAutoQuestIndex(prev => prev + 1);
    }, 800);
    return () => clearTimeout(timer);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [autoNavigating]);

  const handleAcceptQuest = (questId: string) => {
    sendCommand('acceptquest', { questId });
  };

  const handleAcceptAll = () => {
    const unaccepted = availableQuests.filter(q => !q.isTaken);
    unaccepted.forEach((q, i) => {
      setTimeout(() => {
        sendCommand('acceptquest', { questId: q.questId });
      }, i * 120);
    });
    if (unaccepted.length > 0) {
      appendMessage({
        timestamp: new Date().toISOString(),
        category: 'quest',
        text: `Accepting ${unaccepted.length} quest${unaccepted.length !== 1 ? 's' : ''}...`,
      });
    }
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
        gridTemplateColumns: statusCollapsed ? '1fr 40px' : '1fr 320px',
        overflow: 'hidden',
        borderBottom: '1px solid #1a3a1a',
        transition: 'grid-template-columns 0.15s ease',
      }}>
        {/* Map panel — fills remaining space */}
        <div style={{
          borderRight: '1px solid #1a3a1a',
          overflow: 'hidden',
          position: 'relative',
        }}>
          <WorldMap worldState={worldState} zoneTiles={zoneTiles} wanderingNpcs={wanderingNpcs} questWaypoint={questWaypoint} />
        </div>

        {/* Status panel — collapsible */}
        <div style={{
          overflow: 'hidden',
          display: 'flex',
          flexDirection: 'column',
          position: 'relative',
        }}>
          {/* Collapse/expand toggle */}
          <button
            onClick={() => setStatusCollapsed(c => !c)}
            style={{
              position: 'absolute',
              top: '4px',
              left: statusCollapsed ? '4px' : '4px',
              zIndex: 10,
              background: '#111',
              border: '1px solid #1a3a1a',
              color: '#00ff41',
              fontFamily: 'monospace',
              fontSize: '13px',
              lineHeight: 1,
              cursor: 'pointer',
              padding: '2px 5px',
              userSelect: 'none',
            }}
            title={statusCollapsed ? 'Expand status panel' : 'Collapse status panel'}
          >
            {statusCollapsed ? '\u00bb' : '\u00ab'}
          </button>

          {statusCollapsed ? (
            /* Collapsed: thin bar with HP + Weave mini-bars */
            <div style={{
              display: 'flex',
              flexDirection: 'column',
              alignItems: 'center',
              paddingTop: '28px',
              gap: '6px',
            }}>
              {/* HP mini-bar */}
              <div style={{ position: 'relative', width: '20px', height: '80px', background: '#1a0a0a', border: '1px solid #3a1a1a' }}>
                <div style={{
                  position: 'absolute',
                  bottom: 0,
                  width: '100%',
                  height: `${worldState?.player ? (worldState.player.currentHp / worldState.player.maxHp) * 100 : 0}%`,
                  background: '#aa2200',
                }} />
              </div>
              {/* Weave mini-bar */}
              <div style={{ position: 'relative', width: '20px', height: '80px', background: '#0a0a1a', border: '1px solid #1a1a3a' }}>
                <div style={{
                  position: 'absolute',
                  bottom: 0,
                  width: '100%',
                  height: `${worldState?.player?.weavePercent ?? 0}%`,
                  background: '#2255aa',
                }} />
              </div>
            </div>
          ) : (
            <StatusPanel
              player={worldState?.player ?? null}
              currentTile={currentTile}
              equipment={equipment}
              companionRoster={companionRoster}
            />
          )}
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
            display: 'flex',
            gap: '12px',
            alignItems: 'center',
          }}
        >
          <span style={{ color: '#44ff88', fontWeight: 'bold' }}>AUTO-FARM ACTIVE</span>
          {autoFarmStatus.state && (
            <span style={{ color: '#aaffcc', textTransform: 'capitalize' }}>
              [{autoFarmStatus.state}]
            </span>
          )}
          <span>Kills: {autoFarmStatus.kills ?? 0}</span>
          <span>Items: {autoFarmStatus.items ?? 0}</span>
          <span>Salvaged: {autoFarmStatus.salvaged ?? 0}</span>
          <span>Deposited: {autoFarmStatus.deposited ?? 0}</span>
          {autoFarmStatus.biome && (
            <span style={{ color: '#88ccaa' }}>
              {autoFarmStatus.biome}{autoFarmStatus.dangerLevel !== undefined ? ` (D${autoFarmStatus.dangerLevel})` : ''}
            </span>
          )}
          <span style={{ color: '#666' }}>· [F] stop</span>
        </div>
      )}

      {/* Quest auto-run status bar */}
      {autoQuestActive && (
        <div
          style={{
            position: 'absolute',
            top: autoFarmStatus?.active ? '52px' : '28px',
            left: '50%',
            transform: 'translateX(-50%)',
            background: '#120d1a',
            border: '1px solid #cc88ff',
            color: '#cc88ff',
            fontSize: '11px',
            fontFamily: 'monospace',
            padding: '3px 14px',
            letterSpacing: '0.08em',
            pointerEvents: 'none',
            display: 'flex',
            gap: '10px',
            alignItems: 'center',
          }}
        >
          <span style={{ color: '#ee88ff', fontWeight: 'bold' }}>QUEST AUTO-RUN</span>
          <span>{autoQuestIndex + 1}/{_autoQuestTotal}</span>
          {_autoQuestTitle ? <span style={{ color: '#ddaaff' }}>— {_autoQuestTitle}</span> : null}
          <span style={{ color: '#666' }}>· [L] stop</span>
        </div>
      )}

      {/* Auto-navigate status bar */}
      {autoNavigating && questWaypoint && (
        <div
          style={{
            position: 'absolute',
            top: autoFarmStatus?.active ? '28px' : '4px',
            left: '50%',
            transform: 'translateX(-50%)',
            background: '#0d1020',
            border: '1px solid #00aacc',
            color: '#00e5ff',
            fontSize: '11px',
            fontFamily: 'monospace',
            padding: '3px 14px',
            letterSpacing: '0.08em',
            pointerEvents: 'none',
            display: 'flex',
            gap: '10px',
            alignItems: 'center',
          }}
        >
          <span style={{ color: '#44ccff', fontWeight: 'bold' }}>NAVIGATING</span>
          <span>{questWaypoint.questTitle}</span>
          <span style={{ color: '#666' }}>→ ({questWaypoint.targetX}, {questWaypoint.targetY})</span>
          <span style={{ color: '#666' }}>· [N] cancel</span>
        </div>
      )}

      {/* Modals */}
      {needsPlayerCreation && (
        <PlayerCreation onCreated={() => { /* reload handled inside PlayerCreation */ }} />
      )}
      {!needsPlayerCreation && showQuestLog && (
        <QuestLog
          quests={availableQuests}
          questProgress={questProgress}
          playerX={worldState?.player?.x}
          playerY={worldState?.player?.y}
          onAccept={handleAcceptQuest}
          onAcceptAll={handleAcceptAll}
          onComplete={handleCompleteQuest}
          onNavigate={(questId) => {
            setShowQuestLog(false);
            const quest = availableQuests.find(q => q.questId === questId);
            // Auto-accept if not yet taken, then navigate
            if (quest && !quest.isTaken) {
              sendCommand('acceptquest', { questId });
              appendMessage({
                timestamp: new Date().toISOString(),
                category: 'quest',
                text: `Accepted "${quest.title}". Waypoint set — press [N] to begin navigating.`,
              });
            } else if (questWaypoint?.questId === questId) {
              setAutoNavigating(true);
              appendMessage({
                timestamp: new Date().toISOString(),
                category: 'system',
                text: `Navigating to ${questWaypoint.questTitle}... Press [N] or any movement key to cancel.`,
              });
            } else {
              appendMessage({
                timestamp: new Date().toISOString(),
                category: 'quest',
                text: 'Waypoint not yet set. Press [N] to navigate once the quest is accepted.',
              });
            }
          }}
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
          hasSalvager={companionRoster.some(c => c.assignedDuty === 'Salvager')}
        />
      )}
      {showCharSheet && (
        <CharacterSheet player={worldState?.player ?? null} equipment={equipment} onClose={() => setShowCharSheet(false)} />
      )}
      {combat && <CombatPanel combat={combat} sendCommand={sendCommand} autoFarmStatus={autoFarmStatus} />}
      {showStorage && atHomestead && (
        <StoragePanel
          snapshot={storageView}
          inventoryItems={inventory?.items ?? []}
          onDeposit={handleDeposit}
          onWithdraw={handleWithdraw}
          onClose={() => setShowStorage(false)}
          atHomestead={atHomestead}
          onSmelt={(amount) => sendCommand('smelt', { amount })}
        />
      )}
      {showCompanions && (
        <CompanionPanel
          companions={companionRoster}
          onActivate={(id) => sendCommand('activatecompanion', { companionId: id })}
          onDeactivate={(id) => sendCommand('deactivatecompanion', { companionId: id })}
          onClose={() => setShowCompanions(false)}
          sendCommand={sendCommand}
        />
      )}
      {showCrafting && (
        <CraftingPanel
          recipes={recipes}
          inventoryItems={inventory?.items ?? []}
          storageItems={storageView?.items ?? []}
          craftingSkill={inventory?.craftingSkill ?? worldState?.player?.craftingSkill ?? 1}
          lastCraftResult={lastCraftResult ?? null}
          onCraft={(recipeId, componentIds, taperId) =>
            sendCommand('craft', { recipeId, componentIds, taperId })}
          onRequestRecipes={() => sendCommand('viewrecipes', null)}
          onClose={() => setShowCrafting(false)}
        />
      )}
      {showHelp && <HelpOverlay onClose={() => setShowHelp(false)} />}
      {showAutoFarmPicker && (
        <AutoFarmPicker
          zoneTiles={zoneTiles}
          playerLevel={worldState?.player?.level ?? 1}
          autoSalvageWeaponThreshold={inventory?.autoSalvageWeaponThreshold ?? 0}
          autoSalvageArmorThreshold={inventory?.autoSalvageArmorThreshold ?? 0}
          onStart={handleAutoFarmStart}
          onCancel={() => setShowAutoFarmPicker(false)}
        />
      )}
    </div>
  );
}
