import { useState, useEffect, useMemo, useRef } from 'react';
import type { WorldStateSnapshot, GameMessage, ConnectionState, QuestNode, ZoneTile, InventorySnapshot, CombatUpdate, StorageViewSnapshot, AutoFarmStatus, EquipmentSlots, WanderingNpc, CompanionState, RecipeInfo, CraftingCompleteEvent, QuestWaypoint, QuestProgressMap, SmeltCompleteEvent, CityViewSnapshot } from '../types/game';
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
import CityPanel from './CityPanel';
import AutoFarmPicker from './AutoFarmPicker';
import type { AutoFarmSettings } from './AutoFarmPicker';
import { useKeyboard } from '../hooks/useKeyboard';
import { useAudio } from '../hooks/useAudio';

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
  lastSmeltResult?: SmeltCompleteEvent | null;
  questWaypoint?: QuestWaypoint | null;
  questProgress?: QuestProgressMap;
  cityView?: CityViewSnapshot | null;
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

/**
 * Danger-aware variant of stepToward used by quest auto-run.
 * Returns { deltaX, deltaY } for a safe step, null if blocked by impassable
 * terrain, or 'danger' if every viable step toward the target would enter a
 * tile whose dangerEstimate exceeds safeCap.
 */
function stepTowardSafe(
  playerX: number,
  playerY: number,
  targetX: number,
  targetY: number,
  safeCap: number,
): { deltaX: number; deltaY: number } | null | 'danger' {
  const dx = Math.sign(targetX - playerX);
  const dy = Math.sign(targetY - playerY);

  // Candidates in preference order: diagonal first, then cardinal
  const candidates: Array<[number, number]> = [];
  if (dx !== 0 && dy !== 0) candidates.push([dx, dy]);
  if (dx !== 0) candidates.push([dx, 0]);
  if (dy !== 0) candidates.push([0, dy]);

  let anyPassable = false;
  for (const [cdx, cdy] of candidates) {
    const biome = getBiome(playerX + cdx, playerY + cdy);
    if (biome.type === 'water' || biome.type === 'snowMountain') continue;
    anyPassable = true;
    if (biome.dangerEstimate <= safeCap) {
      return { deltaX: cdx, deltaY: cdy };
    }
  }

  // No passable tile at all
  if (!anyPassable) return null;

  // Passable tiles exist but all exceed safeCap
  return 'danger';
}

/**
 * Sample 6 evenly-spaced points along the straight line from
 * (fromX, fromY) → (toX, toY) and return the maximum dangerEstimate seen.
 * Used to pre-screen whether a quest path is safe enough to attempt.
 */
function samplePathDanger(
  fromX: number,
  fromY: number,
  toX: number,
  toY: number,
  samples = 6,
): number {
  let maxDanger = 0;
  for (let i = 1; i <= samples; i++) {
    const t = i / samples;
    const x = Math.round(fromX + (toX - fromX) * t);
    const y = Math.round(fromY + (toY - fromY) * t);
    const d = getBiome(x, y).dangerEstimate;
    if (d > maxDanger) maxDanger = d;
  }
  return maxDanger;
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
  lastSmeltResult = null,
  questWaypoint = null,
  questProgress = {},
  cityView = null,
}: Props) {
  const keyAction = useKeyboard();

  // Derive biome type from player position for audio
  const currentBiomeType = useMemo(() => {
    if (!worldState?.player) return 'grassland';
    return getBiome(worldState.player.x, worldState.player.y).type;
  }, [worldState?.player]);

  const { playSound, setMusicVolume, setSfxVolume, musicVolume, sfxVolume, isMuted, toggleMute } =
    useAudio(currentBiomeType, combat !== null, atHomestead);

  // Exploration: count unique visited tiles from localStorage (updated on each move)
  const [visitedTileCount, setVisitedTileCount] = useState(0);
  const playerId = worldState?.player?.id ?? null;
  const playerX = worldState?.player?.x;
  const playerY = worldState?.player?.y;
  useEffect(() => {
    if (!playerId) return;
    try {
      const raw = localStorage.getItem(`firstmud_visited_${playerId}`);
      if (!raw) { setVisitedTileCount(0); return; }
      const arr = JSON.parse(raw) as string[];
      setVisitedTileCount(arr.length);
    } catch {
      setVisitedTileCount(0);
    }
  }, [playerId, playerX, playerY]);

  const [showQuestLog, setShowQuestLog] = useState(false);
  const [showHelp, setShowHelp] = useState(false);
  const [showInventory, setShowInventory] = useState(false);
  const [showCharSheet, setShowCharSheet] = useState(false);
  const [showStorage, setShowStorage] = useState(false);
  const [showCompanions, setShowCompanions] = useState(false);
  const [showCrafting, setShowCrafting] = useState(false);
  const [showAutoFarmPicker, setShowAutoFarmPicker] = useState(false);
  const [showCity, setShowCity] = useState(false);
  const [statusCollapsed, setStatusCollapsed] = useState(false);
  const [autoNavigating, setAutoNavigating] = useState(false);
  const [autoQuestActive, setAutoQuestActive] = useState(false);
  const [autoQuestIndex, setAutoQuestIndex] = useState(0);
  const [_autoQuestTotal, setAutoQuestTotal] = useState(0);
  const [_autoQuestTitle, setAutoQuestTitle] = useState('');

  // Single interval ref for the auto-quest runner — avoids the broken
  // multi-effect chain.  Cleared whenever the run stops.
  const autoQuestIntervalRef = useRef<ReturnType<typeof setInterval> | null>(null);
  // Phase of the current auto-quest step: 'accept' | 'navigate' | 'interact'
  // Stored as a ref so the interval closure always reads the latest value.
  const autoQuestPhaseRef = useRef<'idle' | 'accept' | 'navigate' | 'interact'>('idle');
  // How many ticks we have been in 'accept' phase waiting for a waypoint
  const acceptWaitTicksRef = useRef(0);
  // Set to true when an Error message arrives while in the 'interact' phase —
  // tells the runner the quest failed (e.g. missing item) rather than completed.
  const autoQuestInteractFailedRef = useRef(false);
  // Set of quest indices that were skipped this run (incomplete, not failed permanently)
  const autoQuestSkippedIndicesRef = useRef<Set<number>>(new Set());
  // Timestamp (ms) before which portal commands are suppressed.
  // Set to Date.now() + 2000 whenever any portal command fires so that rapid
  // re-presses (or auto-quest ticks) cannot immediately toggle back.
  const portalCooldownUntilRef = useRef<number>(0);
  // Set to true by the auto-quest navigate phase when it has already sent a
  // portalback for the current homestead exit, so successive 300 ms ticks
  // don't spam duplicate commands while waiting for the player to arrive on
  // the world map.
  const autoQuestPortalSentRef = useRef(false);

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
  const combatRef = useRef(combat);
  // Track the previous combat value so we can detect the moment combat ends
  const prevCombatRef = useRef(combat);
  // Timestamp (ms) after which post-combat pause is over
  const combatEndResumeAtRef = useRef<number>(0);
  combatRef.current = combat;
  const currentTileRef = useRef(currentTile);
  currentTileRef.current = currentTile;
  const questWaypointRef = useRef(questWaypoint);
  if (questWaypointRef.current !== questWaypoint) {
    console.log('[questWaypoint prop] Changed:', questWaypoint);
  }
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

  // Wrap appendMessage so that Error events arriving during the 'interact'
  // phase are flagged as an interact failure (missing items, kills, etc.).
  // All other messages pass through unchanged.
  const wrappedAppendMessage = useMemo(() => {
    return (msg: GameMessage) => {
      if (msg.category === 'error' && autoQuestPhaseRef.current === 'interact') {
        autoQuestInteractFailedRef.current = true;
      }
      appendMessage(msg);
    };
  }, [appendMessage]);

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

  // Auto-navigate interval: steps toward the waypoint every 300ms.
  // Used by the [N] key manual navigate — auto-quest has its own loop.
  useEffect(() => {
    if (!autoNavigating) return;
    // Skip if the auto-quest runner owns navigation (it drives its own interval)
    if (autoQuestActiveRef.current) return;

    console.log('[auto-navigate] Starting standalone navigate interval');
    const intervalId = setInterval(() => {
      const wp = questWaypointRef.current;
      const player = worldStateRef.current?.player;
      if (!wp || !player) {
        console.log('[auto-navigate] No wp or player — stopping');
        setAutoNavigating(false);
        return;
      }

      const distX = Math.abs(wp.targetX - player.x);
      const distY = Math.abs(wp.targetY - player.y);
      console.log(`[auto-navigate] dist=(${distX},${distY}) player=(${player.x},${player.y}) target=(${wp.targetX},${wp.targetY})`);

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

      console.log(`[auto-navigate] Moving delta=(${step.deltaX},${step.deltaY})`);
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
        // Ignore rapid re-presses for 2 s after any portal command fires
        if (Date.now() < portalCooldownUntilRef.current) break;
        portalCooldownUntilRef.current = Date.now() + 2000;
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
        // Also refresh storage so storage-sourced ingredients show up as
        // selectable checkboxes in the crafting panel.
        if (atHomestead) sendCommand('openstorage', null);
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
          console.log('[L key] Stopping auto-quest run');
          setAutoQuestActive(false);
          setAutoNavigating(false);
          appendMessage({
            timestamp: new Date().toISOString(),
            category: 'quest',
            text: 'Quest auto-run stopped.',
          });
        } else {
          const quests = availableQuestsRef.current;
          console.log(`[L key] Starting auto-quest run. availableQuests.length=${quests.length}`);
          if (quests.length === 0) {
            appendMessage({
              timestamp: new Date().toISOString(),
              category: 'system',
              text: 'No quests available. Press [Q] to open the quest log.',
            });
            break;
          }
          // Sort: taken quests first, then by rep reward desc.
          // Per-quest path danger is pre-checked live when the waypoint arrives
          // (in the accept→navigate transition), so no upfront sort by danger is
          // possible here — waypoint coords are not known until accepted.
          const ordered = [...quests].sort((a, b) => {
            if (a.isTaken && !b.isTaken) return -1;
            if (!a.isTaken && b.isTaken) return 1;
            return b.reputationReward - a.reputationReward;
          });
          console.log('[L key] Ordered quests:', ordered.map(q => `${q.questId}(${q.title},taken=${q.isTaken})`));
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
      case 'city':
        sendCommand('viewcity', null);
        setShowCity(prev => !prev);
        break;
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
        setShowCity(false);
        break;
      case 'mute':
        toggleMute();
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
  }, [keyAction, sendCommand, fetchAvailableQuests, appendMessage, toggleMute]);

  // ── Audio SFX triggers from incoming messages ─────────────────────────────
  const lastMessageCountRef = useRef(0);
  useEffect(() => {
    const newMessages = messages.slice(lastMessageCountRef.current);
    lastMessageCountRef.current = messages.length;
    if (newMessages.length === 0) return;

    for (const msg of newMessages) {
      const t = msg.text.toLowerCase();

      // Combat events
      if (msg.category === 'system' || msg.category === 'combat' || msg.category === 'loot') {
        if (/critical/i.test(msg.text)) {
          playSound('crit');
        } else if (/\bmiss\b|dodged|evaded/i.test(t)) {
          playSound('miss');
        } else if (/\bdodge\b/i.test(t)) {
          playSound('dodge');
        } else if (/you strike|you hit|strikes for|hits for/i.test(t)) {
          playSound('strike');
        } else if (/healed|restores/i.test(t)) {
          playSound('heal');
        }
      }

      // Level up
      if (/level \d+|leveled up|reached level/i.test(msg.text)) {
        playSound('levelUp');
      }

      // Quest
      if (msg.category === 'quest' && /complete|complet/i.test(t)) {
        playSound('questComplete');
      }

      // Loot
      if (msg.category === 'loot') {
        if (/rare|legendary|epic|masterwork/i.test(t)) {
          playSound('rareLoot');
        } else {
          playSound('loot');
        }
      }

      // Portal
      if (/portal stirs|portal/i.test(t) && msg.category === 'wyrd') {
        playSound('portal');
      }

      // Defeat
      if (msg.category === 'system' && /defeated|you have been defeated/i.test(t)) {
        playSound('defeat');
      }

      // Capture
      if (msg.category === 'system' && /captured/i.test(t)) {
        playSound('capture');
      }

      // Harvest
      if (msg.category === 'loot' && /harvest/i.test(t)) {
        playSound('harvest');
      }
    }
  }, [messages, playSound]);

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

  // ── Auto-quest runner ────────────────────────────────────────────────────
  // A single setInterval drives the whole flow so there are no broken
  // cross-effect dependencies.  Phases per quest:
  //   'accept'   → send acceptquest, poll until questWaypointRef has the
  //                right questId (up to ~5 s), then transition to 'navigate'
  //   'navigate' → step toward waypoint each tick; on arrival → 'interact'
  //   'interact' → send interactquest, wait 1 s, advance to next quest index
  //
  // The interval reads everything via refs so it always has fresh values
  // without needing to be re-created on every render.
  useEffect(() => {
    if (!autoQuestActive) {
      // Clean up when stopped externally
      if (autoQuestIntervalRef.current !== null) {
        clearInterval(autoQuestIntervalRef.current);
        autoQuestIntervalRef.current = null;
      }
      autoQuestPhaseRef.current = 'idle';
      acceptWaitTicksRef.current = 0;
      autoQuestInteractFailedRef.current = false;
      autoQuestSkippedIndicesRef.current = new Set();
      autoQuestPortalSentRef.current = false;
      setAutoNavigating(false);
      return;
    }

    // Already running (e.g. index change re-fires this effect)
    if (autoQuestIntervalRef.current !== null) {
      clearInterval(autoQuestIntervalRef.current);
      autoQuestIntervalRef.current = null;
    }

    // Determine the ordered quest list and target quest.
    // Sort: taken quests first, then by rep reward desc.
    // Per-quest path danger is checked live when the waypoint arrives.
    const quests = availableQuestsRef.current;
    const ordered = [...quests].sort((a, b) => {
      if (a.isTaken && !b.isTaken) return -1;
      if (!a.isTaken && b.isTaken) return 1;
      return b.reputationReward - a.reputationReward;
    });

    const nextQuest = ordered[autoQuestIndexRef.current];

    if (!nextQuest) {
      console.log('[autoquest] No more quests — run complete');
      setAutoQuestActive(false);
      setAutoNavigating(false);
      // If every quest was skipped (none completed), tell the player why instead
      // of falsely claiming "all quests completed".
      const totalQuests = ordered.length;
      const skippedCount = autoQuestSkippedIndicesRef.current.size;
      if (skippedCount > 0 && skippedCount >= totalQuests) {
        wrappedAppendMessage({
          timestamp: new Date().toISOString(),
          category: 'quest',
          text: 'Quest auto-run paused — remaining quests need items/kills. Farm more or complete them manually.',
        });
      } else {
        wrappedAppendMessage({
          timestamp: new Date().toISOString(),
          category: 'quest',
          text: 'All quests completed! Quest auto-run finished.',
        });
      }
      autoQuestSkippedIndicesRef.current = new Set();
      return;
    }

    setAutoQuestTitle(nextQuest.title);
    console.log(`[autoquest] Starting quest ${autoQuestIndexRef.current}: "${nextQuest.title}" isTaken=${nextQuest.isTaken}`);

    // Reset the interact-failure flag before starting each quest
    autoQuestInteractFailedRef.current = false;

    // Fire acceptquest to get (or refresh) the waypoint
    sendCommand('acceptquest', { questId: nextQuest.questId });
    console.log(`[autoquest] Sent acceptquest for ${nextQuest.questId}`);
    autoQuestPhaseRef.current = 'accept';
    acceptWaitTicksRef.current = 0;

    // ── The main tick (300 ms) ──────────────────────────────────────────
    autoQuestIntervalRef.current = setInterval(() => {
      const phase = autoQuestPhaseRef.current;
      const wp = questWaypointRef.current;
      const player = worldStateRef.current?.player;

      console.log(`[autoquest tick] phase=${phase} wp=${wp?.questId ?? 'none'} target=${nextQuest.questId} player=(${player?.x},${player?.y})`);

      if (!player) {
        console.log('[autoquest tick] No player state yet — waiting');
        return;
      }

      // ── Pause during combat and briefly after ──────────────────────
      // Detect the moment combat ends: prev was non-null, now null
      if (prevCombatRef.current !== null && combatRef.current === null) {
        // Combat just ended — give the player 2 s to see the outcome screen
        combatEndResumeAtRef.current = Date.now() + 2000;
      }
      prevCombatRef.current = combatRef.current;

      if (combatRef.current !== null) {
        // ── Auto-flee if the fight is unwinnable ──────────────────────
        // Check if player HP is critically low vs total enemy HP remaining,
        // or if the encounter danger level far exceeds player level.
        const combat = combatRef.current;
        const combatPlayer = combat.combatants.find(c => c.isPlayerSide && !c.isDefeated);
        const enemies = combat.combatants.filter(c => !c.isPlayerSide && !c.isDefeated);
        const playerLevel = player.level ?? 1;

        // Flee conditions:
        //   1) Encounter dangerLevel (if present) exceeds player level + 3
        //   2) Player HP is below 25% of max AND total enemy HP > player HP
        const dangerTooHigh = combat.dangerLevel != null && combat.dangerLevel > playerLevel + 3;
        const playerHpLow =
          combatPlayer != null &&
          combatPlayer.currentHp / combatPlayer.maxHp < 0.25 &&
          enemies.reduce((sum, e) => sum + e.currentHp, 0) > combatPlayer.currentHp;

        if (dangerTooHigh || playerHpLow) {
          console.warn(`[autoquest combat] Unwinnable fight detected (dangerTooHigh=${dangerTooHigh}, playerHpLow=${playerHpLow}) — fleeing`);
          sendCommand('flee', null);
          autoQuestSkippedIndicesRef.current.add(autoQuestIndexRef.current);
          wrappedAppendMessage({
            timestamp: new Date().toISOString(),
            category: 'warning',
            text: `Quest '${nextQuest.title}': unwinnable fight — fleeing and skipping quest.`,
          });
          // Will advance to next quest once combat clears (post-combat pause)
          autoQuestPhaseRef.current = 'idle';
          if (autoQuestIntervalRef.current !== null) {
            clearInterval(autoQuestIntervalRef.current);
            autoQuestIntervalRef.current = null;
          }
          // Small delay then advance to next quest
          setTimeout(() => {
            setAutoQuestIndex(prev => prev + 1);
          }, 2500);
          return;
        }

        // Winnable or uncertain — wait for combat to finish before navigating
        return;
      }
      if (Date.now() < combatEndResumeAtRef.current) {
        // Short post-combat pause so the victory/defeat screen is visible
        return;
      }

      // ── Accept phase: wait for waypoint from server ─────────────────
      if (phase === 'accept') {
        acceptWaitTicksRef.current += 1;

        if (wp && wp.questId === nextQuest.questId) {
          console.log(`[autoquest] Waypoint arrived for "${nextQuest.title}" → (${wp.targetX},${wp.targetY}), switching to navigate`);

          // ── Pre-check: sample path danger before committing to navigate ──
          const playerLevelNow = player.level ?? 1;
          const hardCap = Math.min(Math.ceil(playerLevelNow * 0.7), 8);
          const pathMaxDanger = samplePathDanger(player.x, player.y, wp.targetX, wp.targetY, 6);
          if (pathMaxDanger > hardCap) {
            console.warn(`[autoquest] Path to "${nextQuest.title}" has max danger ${pathMaxDanger} (hard cap ${hardCap}) — skipping quest`);
            wrappedAppendMessage({
              timestamp: new Date().toISOString(),
              category: 'warning',
              text: `Quest '${nextQuest.title}' is in a danger ${pathMaxDanger} zone — too dangerous (safe limit: ${hardCap}). Skipping.`,
            });
            autoQuestPhaseRef.current = 'idle';
            autoQuestSkippedIndicesRef.current.add(autoQuestIndexRef.current);
            if (autoQuestIntervalRef.current !== null) {
              clearInterval(autoQuestIntervalRef.current);
              autoQuestIntervalRef.current = null;
            }
            setAutoQuestIndex(prev => prev + 1);
            return;
          }

          autoQuestPhaseRef.current = 'navigate';
          setAutoNavigating(true);
          wrappedAppendMessage({
            timestamp: new Date().toISOString(),
            category: 'quest',
            text: `Waypoint received — navigating to ${wp.questTitle}...`,
          });
          return;
        }

        // Retry acceptquest every ~3 s (10 ticks × 300 ms) in case the
        // server missed it or returned a "already taken" no-op
        if (acceptWaitTicksRef.current % 10 === 0) {
          console.log(`[autoquest] Still waiting for waypoint (tick ${acceptWaitTicksRef.current}) — re-sending acceptquest`);
          sendCommand('acceptquest', { questId: nextQuest.questId });
        }

        // Timeout after ~15 s (50 ticks) — skip this quest
        if (acceptWaitTicksRef.current > 50) {
          console.warn(`[autoquest] Waypoint timeout for "${nextQuest.title}" — skipping`);
          wrappedAppendMessage({
            timestamp: new Date().toISOString(),
            category: 'quest',
            text: `No waypoint for "${nextQuest.title}" — skipping.`,
          });
          autoQuestPhaseRef.current = 'idle';
          if (autoQuestIntervalRef.current !== null) {
            clearInterval(autoQuestIntervalRef.current);
            autoQuestIntervalRef.current = null;
          }
          setAutoQuestIndex(prev => prev + 1);
        }
        return;
      }

      // ── Navigate phase: step toward waypoint (danger-aware) ────────
      if (phase === 'navigate') {
        if (!wp || wp.questId !== nextQuest.questId) {
          console.warn('[autoquest navigate] Waypoint lost — returning to accept phase');
          autoQuestPhaseRef.current = 'accept';
          acceptWaitTicksRef.current = 0;
          sendCommand('acceptquest', { questId: nextQuest.questId });
          return;
        }

        // ── Homestead check: must portal back before navigating ────────
        // Use atHomesteadRef (the authoritative flag) rather than raw coords
        // so the check stays consistent with the P key handler.
        if (atHomesteadRef.current) {
          // Don't leave homestead until HP is full — homestead heals +10 HP/s
          const hp = player.currentHp;
          const maxHp = player.maxHp;
          if (hp < maxHp) {
            console.log(`[autoquest navigate] Waiting for full HP at homestead (${hp}/${maxHp})`);
            return; // still healing — skip this tick
          }

          if (!autoQuestPortalSentRef.current && Date.now() >= portalCooldownUntilRef.current) {
            console.log('[autoquest navigate] Player is at homestead — sending portalback before navigating');
            autoQuestPortalSentRef.current = true;
            portalCooldownUntilRef.current = Date.now() + 2000;
            sendCommand('portalback', null);
          }
          return; // wait for next tick when player has arrived on the world map
        }
        // Reset the portal-sent flag once we're back on the world map
        autoQuestPortalSentRef.current = false;

        // ── Post-combat heal check ─────────────────────────────────────
        // If HP is below 70% after a fight, portal home to heal before
        // continuing navigation. The homestead check above will wait for
        // full HP then send portalback automatically.
        const hpPercent = player.currentHp / (player.maxHp || 1);
        if (hpPercent < 0.7 && Date.now() >= portalCooldownUntilRef.current) {
          console.log(`[autoquest navigate] HP at ${Math.round(hpPercent * 100)}% — portaling home to heal`);
          portalCooldownUntilRef.current = Date.now() + 2000;
          sendCommand('portalhome', null);
          wrappedAppendMessage({
            timestamp: new Date().toISOString(),
            category: 'system',
            text: `Party needs healing (HP: ${player.currentHp}/${player.maxHp}) — portaling home...`,
          });
          return;
        }

        const distX = Math.abs(wp.targetX - player.x);
        const distY = Math.abs(wp.targetY - player.y);
        console.log(`[autoquest navigate] dist=(${distX},${distY}) to (${wp.targetX},${wp.targetY})`);

        if (distX <= 2 && distY <= 2) {
          console.log(`[autoquest navigate] Arrived at waypoint for "${nextQuest.title}" — interacting`);
          autoQuestInteractFailedRef.current = false;
          autoQuestPhaseRef.current = 'interact';
          setAutoNavigating(false);
          wrappedAppendMessage({
            timestamp: new Date().toISOString(),
            category: 'quest',
            text: `Arrived at ${wp.questTitle} — completing quest...`,
          });
          sendCommand('interactquest', { questId: wp.questId });
          return;
        }

        const safeStepCap = Math.min(Math.ceil((player.level ?? 1) * 0.6), 7);
        const step = stepTowardSafe(player.x, player.y, wp.targetX, wp.targetY, safeStepCap);

        if (step === null) {
          // Impassable terrain — fully blocked
          console.warn('[autoquest navigate] Path completely blocked (impassable terrain) — skipping quest');
          setAutoNavigating(false);
          autoQuestSkippedIndicesRef.current.add(autoQuestIndexRef.current);
          wrappedAppendMessage({
            timestamp: new Date().toISOString(),
            category: 'system',
            text: `Auto-quest: path to '${nextQuest.title}' is blocked by impassable terrain. Skipping to next quest.`,
          });
          autoQuestPhaseRef.current = 'idle';
          if (autoQuestIntervalRef.current !== null) {
            clearInterval(autoQuestIntervalRef.current);
            autoQuestIntervalRef.current = null;
          }
          setAutoQuestIndex(prev => prev + 1);
          return;
        }

        if (step === 'danger') {
          // All adjacent steps toward target exceed safeCap — skip quest
          console.warn(`[autoquest navigate] Next step into danger zone (safe cap: ${safeStepCap}) — skipping quest`);
          setAutoNavigating(false);
          autoQuestSkippedIndicesRef.current.add(autoQuestIndexRef.current);
          wrappedAppendMessage({
            timestamp: new Date().toISOString(),
            category: 'warning',
            text: `Quest route blocked by danger terrain (safe limit: ${safeStepCap}). Skipping '${nextQuest.title}' to next quest.`,
          });
          autoQuestPhaseRef.current = 'idle';
          if (autoQuestIntervalRef.current !== null) {
            clearInterval(autoQuestIntervalRef.current);
            autoQuestIntervalRef.current = null;
          }
          setAutoQuestIndex(prev => prev + 1);
          return;
        }

        console.log(`[autoquest navigate] Moving delta=(${step.deltaX},${step.deltaY})`);
        sendCommand('move', step);
        return;
      }

      // ── Interact phase: wait one beat for server to process, then advance ─
      if (phase === 'interact') {
        autoQuestPhaseRef.current = 'idle';
        if (autoQuestIntervalRef.current !== null) {
          clearInterval(autoQuestIntervalRef.current);
          autoQuestIntervalRef.current = null;
        }

        if (autoQuestInteractFailedRef.current) {
          // Quest couldn't be completed (missing items, not enough kills, etc.)
          // Log a yellow warning, record as skipped, and move on.
          console.warn(`[autoquest interact] Quest "${nextQuest.title}" failed — skipping`);
          autoQuestSkippedIndicesRef.current.add(autoQuestIndexRef.current);
          wrappedAppendMessage({
            timestamp: new Date().toISOString(),
            category: 'warning',
            text: `Quest '${nextQuest.title}' needs items/kills you don't have — skipping to next quest.`,
          });
          autoQuestInteractFailedRef.current = false;
        } else {
          console.log(`[autoquest interact] Quest "${nextQuest.title}" completed — advancing to next`);
        }

        // Advance index regardless — this re-fires the parent effect for the next quest
        setAutoQuestIndex(prev => prev + 1);
      }
    }, 300);

    // Cleanup when effect re-runs or component unmounts
    return () => {
      if (autoQuestIntervalRef.current !== null) {
        clearInterval(autoQuestIntervalRef.current);
        autoQuestIntervalRef.current = null;
      }
    };
  // Re-run when active state changes or we advance to the next quest index
  }, [autoQuestActive, autoQuestIndex, sendCommand, wrappedAppendMessage]);

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
          <WorldMap worldState={worldState} zoneTiles={zoneTiles} wanderingNpcs={wanderingNpcs} questWaypoint={questWaypoint} homesteadBuildings={cityView?.buildings ?? []} />
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
              visitedTileCount={visitedTileCount}
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

      {/* Audio controls — bottom-right, below help hint */}
      <div style={{
        position: 'absolute',
        bottom: '204px',
        right: '10px',
        display: 'flex',
        flexDirection: 'column',
        gap: '4px',
        fontFamily: 'monospace',
        fontSize: '11px',
        pointerEvents: 'auto',
      }}>
        {/* Mute toggle */}
        <button
          type="button"
          onClick={toggleMute}
          title="Toggle mute [M]"
          style={{
            background: 'none',
            border: 'none',
            color: isMuted ? '#ff6644' : '#444444',
            fontFamily: 'monospace',
            fontSize: '11px',
            cursor: 'pointer',
            padding: 0,
            letterSpacing: '0.05em',
            textAlign: 'left',
          }}
        >
          {isMuted ? '♪ [M] muted' : '♪ [M] sound on'}
        </button>
        {/* Volume sliders */}
        {!isMuted && (
          <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
            <label style={{ color: '#444', display: 'flex', alignItems: 'center', gap: '6px' }}>
              <span style={{ minWidth: '28px' }}>♫</span>
              <input
                type="range" min={0} max={1} step={0.05}
                value={musicVolume}
                onChange={e => setMusicVolume(parseFloat(e.target.value))}
                style={{ width: '70px', accentColor: '#00ff41', cursor: 'pointer' }}
                title="Music volume"
              />
            </label>
            <label style={{ color: '#444', display: 'flex', alignItems: 'center', gap: '6px' }}>
              <span style={{ minWidth: '28px' }}>⚡</span>
              <input
                type="range" min={0} max={1} step={0.05}
                value={sfxVolume}
                onChange={e => setSfxVolume(parseFloat(e.target.value))}
                style={{ width: '70px', accentColor: '#00ff41', cursor: 'pointer' }}
                title="SFX volume"
              />
            </label>
          </div>
        )}
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
      {combat && <CombatPanel combat={combat} sendCommand={sendCommand} autoFarmStatus={autoFarmStatus} forceAutoCombat={autoQuestActive} playSound={playSound} />}
      {showCity && (
        <CityPanel
          cityView={cityView ?? null}
          onClose={() => setShowCity(false)}
          sendCommand={sendCommand}
          companionRoster={companionRoster}
          activeCompanionIds={worldState?.player?.activeCompanionIds ?? []}
          storageItems={storageView?.items ?? []}
          inventoryItems={inventory?.items ?? []}
        />
      )}
      {showStorage && atHomestead && (
        <StoragePanel
          snapshot={storageView}
          inventoryItems={inventory?.items ?? []}
          onDeposit={handleDeposit}
          onWithdraw={handleWithdraw}
          onClose={() => setShowStorage(false)}
          atHomestead={atHomestead}
          onSmelt={(amount) => sendCommand('smelt', { amount })}
          lastSmeltResult={lastSmeltResult ?? null}
        />
      )}
      {showCompanions && (
        <CompanionPanel
          companions={companionRoster}
          activeCompanionIds={worldState?.player?.activeCompanionIds ?? []}
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
          onSalvage={(itemId) => sendCommand('salvage', { itemId })}
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
