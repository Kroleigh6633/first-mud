import { useState, useEffect, useMemo, useRef, type ReactNode } from 'react';
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
import { useKeybinds, type KeyBinding } from '../hooks/useKeybinds';
import { useAudio } from '../hooks/useAudio';
import { useGameCommands, type SendCommandFn } from '../hooks/useGameCommands';

interface Props {
  connectionState: ConnectionState;
  sendCommand: SendCommandFn;
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
  const commands = useGameCommands(sendCommand);

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
  // When L-mode runs out of quests, it transitions into F-mode (auto-farm) and
  // sets this flag. While true, the L runner's tick is paused; instead, a
  // separate effect watches `availableQuests` and, when a new un-taken quest
  // appears, it stops the farm, clears this flag, and resumes the L runner
  // from index 0. The L key (and Escape) also stop the farm when this is set.
  const [autoQuestFarmingIdle, setAutoQuestFarmingIdle] = useState(false);
  // Edge-case 2: distinct stop-reason for "all available quests are
  // currently unwinnable" (every quest in the ordered list got skipped this
  // pass). When set, L is fully stopped (autoQuestActive=false) and the
  // banner shows AUTO-QUEST STOPPED — no actionable quests.
  const [autoQuestStoppedReason, setAutoQuestStoppedReason] =
    useState<null | 'no-actionable-quests'>(null);

  // Edge-case 1: who owns the currently-running auto-farm.
  //   'idle-runner' → L-mode parked into farm because it ran out of quests
  //   'user'        → user manually started farm (via [F] picker)
  //   null          → no farm active, or owner not tracked
  // L-stop only toggles the farm off when owner === 'idle-runner'.
  const autoFarmOwnerRef = useRef<'idle-runner' | 'user' | null>(null);

  // Edge-case 3: deterministic phase reset for L-mode when transitioning
  // from idle-farm back to active questing. Replaces the fragile 50 ms
  // setTimeout. Sequence: 'idle' → 'resuming' (cleanup pass scheduled) →
  // 'running' (interval re-armed). A dedicated effect drives the
  // 'resuming' → 'running' edge by toggling autoQuestActive off then on.
  const autoQuestPhaseResetRef = useRef<'idle' | 'running' | 'resuming'>('idle');
  // Bumped whenever we want to force a re-resume to fire (the watcher only
  // re-runs on state changes, so we read this from a separate state slot).
  const [autoQuestResumeTick, setAutoQuestResumeTick] = useState(0);

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
  // Clear ownership whenever the farm transitions to inactive (server-side
  // termination, or any toggle path we may have missed). Prevents stale
  // owner state from blocking subsequent [F] toggles.
  useEffect(() => {
    if (!autoFarmStatus?.active) {
      autoFarmOwnerRef.current = null;
    }
  }, [autoFarmStatus?.active]);
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
  const autoQuestFarmingIdleRef = useRef(autoQuestFarmingIdle);
  autoQuestFarmingIdleRef.current = autoQuestFarmingIdle;

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
        commands.interactQuest({ questId: wp.questId });
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
      commands.move(step);
    }, 300);

    return () => clearInterval(intervalId);
  }, [autoNavigating, commands, appendMessage]);

  // ── Keybinds ─────────────────────────────────────────────────────────────
  // The bindings table is the single source of truth for keyboard shortcuts.
  // Each handler is the body of what used to be a `case` in the giant switch.
  // Reads of mutable game state (atHomestead, autoFarm status, currentTile,
  // questWaypoint, …) go through *Ref values so this array doesn't need to be
  // rebuilt on every state change — `useKeybinds` reads it via a ref anyway,
  // but keeping handlers ref-driven preserves the original behaviour where the
  // listener saw stale-but-via-ref state without re-attaching.
  const move = (dx: number, dy: number) => {
    if (autoNavigatingRef.current) {
      setAutoNavigating(false);
      appendMessage({
        timestamp: new Date().toISOString(),
        category: 'system',
        text: 'Auto-navigate cancelled.',
      });
    }
    // Server-side MoveCommand expects `deltaX`/`deltaY`, not dx/dy.
    commands.move({ deltaX: dx, deltaY: dy });
  };

  const keybindings: KeyBinding[] = [
    { keys: ['ArrowUp', 'w', 'W'],    description: 'move north', handler: () => move(0, -1) },
    { keys: ['ArrowDown', 's', 'S'],  description: 'move south', handler: () => move(0,  1) },
    { keys: ['ArrowLeft', 'a', 'A'],  description: 'move west',  handler: () => move(-1, 0) },
    { keys: ['ArrowRight', 'd', 'D'], description: 'move east',  handler: () => move( 1, 0) },

    { keys: ['Enter'], description: 'interact', handler: () => {
      const wp = questWaypointRef.current;
      const player = worldStateRef.current?.player;
      if (wp && player) {
        const dx = Math.abs(wp.targetX - player.x);
        const dy = Math.abs(wp.targetY - player.y);
        if (dx <= 2 && dy <= 2) {
          commands.interactQuest({ questId: wp.questId });
          return;
        }
      }
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
    } },

    { keys: ['i', 'I'], description: 'open inventory', handler: () => {
      commands.openInventory();
      setShowInventory(prev => !prev);
    } },

    { keys: ['c', 'C'], description: 'character sheet', handler: () => setShowCharSheet(prev => !prev) },

    { keys: ['q', 'Q'], description: 'quest log', handler: () => {
      setShowQuestLog(prev => {
        const next = !prev;
        if (next) fetchAvailableQuests();
        return next;
      });
    } },

    { keys: [' '], description: 'wait / pass turn', handler: () => {
      appendMessage({
        timestamp: new Date().toISOString(),
        category: 'system',
        text: 'You wait. The world continues around you.',
      });
    } },

    { keys: ['?', 'h', 'H'], description: 'help', handler: () => setShowHelp(prev => !prev) },

    { keys: ['p', 'P'], description: 'portal home / back', handler: () => {
      if (Date.now() < portalCooldownUntilRef.current) return;
      portalCooldownUntilRef.current = Date.now() + 2000;
      if (atHomesteadRef.current) {
        commands.portalBack();
      } else {
        commands.portalHome();
      }
    } },

    { keys: ['e', 'E'], description: 'harvest', handler: () => commands.harvest() },

    { keys: ['f', 'F'], description: 'auto-farm toggle / open picker', handler: () => {
      // Edge-case 1: if the farm currently running is owned by L-idle, treat
      // [F] as "user wants to take over" → open the picker. The picker's
      // confirm will replace the running farm with user-tuned settings and
      // flip ownership to 'user'. If [F] is pressed and the user already
      // owns the farm (manual session), toggle it off normally.
      if (autoFarmRef.current?.active && autoFarmOwnerRef.current === 'user') {
        commands.autoFarm();
        autoFarmOwnerRef.current = null;
      } else {
        // Either no farm is running, or an idle-runner owns it. Either way,
        // open the picker so the user can specify their own settings.
        setShowAutoFarmPicker(true);
      }
    } },

    { keys: ['v', 'V'], description: 'storage (homestead only)', handler: () => {
      if (!atHomesteadRef.current) {
        appendMessage({
          timestamp: new Date().toISOString(),
          category: 'system',
          text: 'You must be at your homestead to access storage. Press [P] to portal home.',
        });
      } else {
        commands.openStorage();
        setShowStorage(prev => !prev);
      }
    } },

    { keys: ['b', 'B'], description: 'companions', handler: () => {
      commands.viewCompanions();
      setShowCompanions(prev => !prev);
    } },

    { keys: ['r', 'R'], description: 'crafting', handler: () => {
      commands.viewRecipes();
      if (atHomestead) commands.openStorage();
      setShowCrafting(prev => !prev);
    } },

    { keys: ['n', 'N'], description: 'navigate to quest waypoint', handler: () => {
      const wp = questWaypointRef.current;
      if (!wp) {
        const quests = availableQuestsRef.current;
        const first = quests.find(q => !q.isTaken) ?? quests[0];
        if (first && !first.isTaken) {
          commands.acceptQuest({ questId: first.questId });
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
        return;
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
    } },

    { keys: ['l', 'L'], description: 'auto-quest run toggle', handler: () => {
      if (autoQuestActiveRef.current) {
        console.log('[L key] Stopping auto-quest run');
        // Edge-case 1: only stop the farm if L owns it. If the user took
        // over with [F], leave their farm running.
        if (
          autoQuestFarmingIdleRef.current &&
          autoFarmRef.current?.active &&
          autoFarmOwnerRef.current === 'idle-runner'
        ) {
          console.log('[L key] Stopping auto-farm (idle-runner owned)');
          commands.autoFarm();
          autoFarmOwnerRef.current = null;
        }
        setAutoQuestFarmingIdle(false);
        setAutoQuestStoppedReason(null);
        setAutoQuestActive(false);
        setAutoNavigating(false);
        autoQuestPhaseResetRef.current = 'idle';
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
          return;
        }
        const ordered = [...quests].sort((a, b) => {
          if (a.isTaken && !b.isTaken) return -1;
          if (!a.isTaken && b.isTaken) return 1;
          return b.reputationReward - a.reputationReward;
        });
        console.log('[L key] Ordered quests:', ordered.map(q => `${q.questId}(${q.title},taken=${q.isTaken})`));
        setAutoQuestStoppedReason(null);
        autoQuestPhaseResetRef.current = 'running';
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
    } },

    { keys: ['m', 'M'], description: 'mute audio', handler: () => toggleMute() },

    { keys: ['g', 'G'], description: 'city panel', handler: () => {
      commands.viewCity();
      setShowCity(prev => !prev);
    } },

    { keys: ['Escape'], description: 'close panels / cancel', handler: () => {
      setAutoNavigating(false);
      // Edge-case 1: only shut the farm down on Escape if L owns it.
      if (
        autoQuestFarmingIdleRef.current &&
        autoFarmRef.current?.active &&
        autoFarmOwnerRef.current === 'idle-runner'
      ) {
        commands.autoFarm();
        autoFarmOwnerRef.current = null;
      }
      setAutoQuestFarmingIdle(false);
      setAutoQuestStoppedReason(null);
      setAutoQuestActive(false);
      autoQuestPhaseResetRef.current = 'idle';
      setShowHelp(false);
      setShowQuestLog(false);
      setShowInventory(false);
      setShowCharSheet(false);
      setShowStorage(false);
      setShowCompanions(false);
      setShowCrafting(false);
      setShowAutoFarmPicker(false);
      setShowCity(false);
    } },
  ];

  useKeybinds(keybindings);

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
    // Edge-case 1: if an idle-runner farm is currently running, stop it first
    // so the new user-tuned farm starts with a clean slate, then flip
    // ownership. The L runner stays active in 'farming idle' mode (banner
    // changes to "WAITING — user farming"); when new quests arrive the
    // watcher will re-engage L and stop the user farm too (per spec).
    if (autoFarmRef.current?.active && autoFarmOwnerRef.current === 'idle-runner') {
      commands.autoFarm(); // toggle off the idle-runner farm
    }
    autoFarmOwnerRef.current = 'user';
    commands.autoFarm({
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
      setAutoNavigating(false);
      const totalQuests = ordered.length;
      const skippedCount = autoQuestSkippedIndicesRef.current.size;
      // Edge-case 2: if every quest in this pass was skipped (impassable
      // terrain, danger cap, missing prereqs, interact failure, etc.), we
      // do NOT silently transition to farm. The user needs to see that
      // their auto-quest loop is stuck so they can intervene.
      const allSkipped = totalQuests > 0 && skippedCount >= totalQuests;
      autoQuestSkippedIndicesRef.current = new Set();

      if (allSkipped) {
        console.log('[autoquest] All quests unwinnable this pass — stopping cleanly');
        autoQuestPhaseResetRef.current = 'idle';
        setAutoQuestFarmingIdle(false);
        setAutoQuestActive(false);
        setAutoQuestIndex(0);
        setAutoQuestTotal(0);
        setAutoQuestTitle('');
        setAutoQuestStoppedReason('no-actionable-quests');
        wrappedAppendMessage({
          timestamp: new Date().toISOString(),
          category: 'warning',
          text: 'Auto-quest stopped — no actionable quests (check quest log [Q]).',
        });
        return;
      }

      console.log('[autoquest] No more quests — transitioning to auto-farm until new quests arrive');
      // Remain in L mode but drop into auto-farm. A separate effect watches
      // availableQuests and will resume the quest loop when new ones appear.
      // Conservative defaults: stay in current zone, cap danger at a safe
      // floor relative to player level, balanced priority.
      if (!autoFarmRef.current?.active) {
        const player = worldStateRef.current?.player;
        const level = player?.level ?? 1;
        const maxDanger = Math.min(Math.max(level - 1, 1), 3);
        commands.autoFarm({
          targetZone: null,
          maxDanger,
          priority: 'balanced',
        });
        autoFarmOwnerRef.current = 'idle-runner';
      }
      setAutoQuestFarmingIdle(true);
      // Reset index so the next batch of quests starts clean
      setAutoQuestIndex(0);
      setAutoQuestTotal(0);
      setAutoQuestTitle('');

      wrappedAppendMessage({
        timestamp: new Date().toISOString(),
        category: 'quest',
        text: 'All quests completed — auto-farming while waiting for new quests. Press [L] to stop.',
      });
      return;
    }

    setAutoQuestTitle(nextQuest.title);
    console.log(`[autoquest] Starting quest ${autoQuestIndexRef.current}: "${nextQuest.title}" isTaken=${nextQuest.isTaken}`);

    // Reset the interact-failure flag before starting each quest
    autoQuestInteractFailedRef.current = false;

    // Fire acceptquest to get (or refresh) the waypoint
    commands.acceptQuest({ questId: nextQuest.questId });
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
          commands.flee();
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
          commands.acceptQuest({ questId: nextQuest.questId });
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
          commands.acceptQuest({ questId: nextQuest.questId });
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
            commands.portalBack();
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
          commands.portalHome();
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
          commands.interactQuest({ questId: wp.questId });
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
        commands.move(step);
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
  }, [autoQuestActive, autoQuestIndex, commands, wrappedAppendMessage]);

  // ── Farming-idle watcher ─────────────────────────────────────────────────
  // While auto-quest is active but has run out of work, it parks itself in
  // auto-farm. This effect watches `availableQuests` and, when a new un-taken
  // quest appears, stops the farm and kicks the quest runner back into gear.
  useEffect(() => {
    if (!autoQuestFarmingIdle) return;
    // We want ANY quest that the L runner would attempt — non-empty list.
    // (The sort in the runner already prefers taken > higher rep.)
    if (availableQuests.length === 0) return;

    console.log('[autoquest idle] New quests available — resuming L mode');
    // Edge-case 1: stop whichever farm is running, regardless of owner. Per
    // spec, even a user-owned farm yields when fresh quests appear (that's
    // the whole point of L). Owner is cleared so subsequent toggles behave.
    if (autoFarmRef.current?.active) {
      commands.autoFarm(); // toggle off
    }
    autoFarmOwnerRef.current = null;
    setAutoQuestFarmingIdle(false);

    // Edge-case 3: deterministic phase reset replaces setTimeout(50). Mark
    // the L runner as "resuming"; the cleanup-pass effect (below) will fire
    // synchronously after this state batch, do its teardown, and then a
    // dedicated effect watching the resume tick will re-arm the runner.
    autoQuestPhaseResetRef.current = 'resuming';
    setAutoQuestActive(false);
    setAutoQuestResumeTick(t => t + 1);
  }, [autoQuestFarmingIdle, availableQuests, commands]);

  // Edge-case 3: drives the 'resuming' → 'running' transition. Runs after
  // the cleanup pass triggered by setAutoQuestActive(false) has completed
  // (React commits state batches in order, so by the time this effect fires
  // for the resume tick the runner effect has already torn down its
  // interval). No timer involved.
  useEffect(() => {
    if (autoQuestResumeTick === 0) return;
    if (autoQuestPhaseResetRef.current !== 'resuming') return;
    if (availableQuestsRef.current.length === 0) return;

    autoQuestPhaseResetRef.current = 'running';
    setAutoQuestIndex(0);
    setAutoQuestTotal(availableQuestsRef.current.length);
    setAutoQuestActive(true);
    wrappedAppendMessage({
      timestamp: new Date().toISOString(),
      category: 'quest',
      text: `New quest${availableQuestsRef.current.length !== 1 ? 's' : ''} available — resuming auto-quest run.`,
    });
  }, [autoQuestResumeTick, wrappedAppendMessage]);

  const handleAcceptQuest = (questId: string) => {
    commands.acceptQuest({ questId });
  };

  const handleAcceptAll = () => {
    const unaccepted = availableQuests.filter(q => !q.isTaken);
    unaccepted.forEach((q, i) => {
      setTimeout(() => {
        commands.acceptQuest({ questId: q.questId });
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
    commands.completeQuest({ questId, chosenOutcome: outcome });
  };

  const handleDeposit = (itemId: string) => {
    commands.deposit({ itemId });
    // Re-fetch storage and inventory after deposit
    setTimeout(() => {
      commands.openStorage();
      commands.openInventory();
    }, 300);
  };

  const handleWithdraw = (itemId: string) => {
    commands.withdraw({ itemId });
    setTimeout(() => {
      commands.openStorage();
      commands.openInventory();
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
      {(autoQuestActive || autoQuestStoppedReason !== null) && (() => {
        // Decide which mode label + suffix to render. Order matters:
        //   1. Stopped reason (L is no longer active)
        //   2. Farming-idle with user-owned farm  → "WAITING — user farming"
        //   3. Farming-idle with idle-runner farm → "FARMING IDLE — waiting…"
        //   4. Active questing                    → "ACTIVE"
        const stopped = autoQuestStoppedReason === 'no-actionable-quests';
        const userOwnsFarm =
          autoQuestFarmingIdle && autoFarmOwnerRef.current === 'user';
        const farmingIdle = autoQuestFarmingIdle && !userOwnsFarm;
        const active = autoQuestActive && !autoQuestFarmingIdle;

        let label = 'AUTO-QUEST (ACTIVE)';
        let suffix: ReactNode = null;
        let borderColor = '#cc88ff';
        let labelColor = '#ee88ff';
        if (stopped) {
          label = 'AUTO-QUEST STOPPED';
          suffix = <span style={{ color: '#ffaaaa' }}>— no actionable quests (check quest log)</span>;
          borderColor = '#cc4444';
          labelColor = '#ff8888';
        } else if (userOwnsFarm) {
          label = 'AUTO-QUEST (WAITING';
          suffix = <span style={{ color: '#ddaaff' }}>— user farming)</span>;
        } else if (farmingIdle) {
          label = 'AUTO-QUEST (FARMING IDLE';
          suffix = <span style={{ color: '#ddaaff' }}>— waiting for new quests)</span>;
        } else if (active) {
          label = 'AUTO-QUEST (ACTIVE)';
        }

        return (
          <div
            style={{
              position: 'absolute',
              top: autoFarmStatus?.active ? '52px' : '28px',
              left: '50%',
              transform: 'translateX(-50%)',
              background: '#120d1a',
              border: `1px solid ${borderColor}`,
              color: borderColor,
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
            <span style={{ color: labelColor, fontWeight: 'bold' }}>{label}</span>
            {active && <span>{autoQuestIndex + 1}/{_autoQuestTotal}</span>}
            {active && _autoQuestTitle ? <span style={{ color: '#ddaaff' }}>— {_autoQuestTitle}</span> : null}
            {suffix}
            {!stopped && <span style={{ color: '#666' }}>· [L] stop</span>}
            {stopped && <span style={{ color: '#666' }}>· [L] dismiss</span>}
          </div>
        );
      })()}

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
              commands.acceptQuest({ questId });
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
          onSmelt={(amount) => commands.smelt({ amount })}
          lastSmeltResult={lastSmeltResult ?? null}
        />
      )}
      {showCompanions && (
        <CompanionPanel
          companions={companionRoster}
          activeCompanionIds={worldState?.player?.activeCompanionIds ?? []}
          onActivate={(id) => commands.activateCompanion({ companionId: id })}
          onDeactivate={(id) => commands.deactivateCompanion({ companionId: id })}
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
            commands.craft({ recipeId, componentIds, taperId })}
          onSalvage={(itemId) => commands.salvage({ itemId })}
          onRequestRecipes={() => commands.viewRecipes()}
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
