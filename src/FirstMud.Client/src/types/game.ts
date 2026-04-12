export type MagicElement = 'Fire' | 'Water' | 'Earth' | 'Air' | 'Aether';
export type MagicPolarity = 'None' | 'Shaping' | 'Unmaking' | 'TwiceBorn';
export type WorldId = 'Aeldran' | 'ArdweldRemnant' | 'FairgeanDeep' | 'GolvariDeeps' | 'WyrdPaths' | 'TheDream';
export type ReputationTier = 'Hostile' | 'Wary' | 'Unknown' | 'Known' | 'Trusted' | 'Honored' | 'Bound';
export type WeaveState = 'Full' | 'Steady' | 'Strained' | 'Critical' | 'Depleted';

export interface PlayerState {
  id: string;
  name: string;
  level: number;
  experience: number;
  currentHp: number;
  maxHp: number;
  actionPoints: number;
  maxActionPoints: number;
  weavePercent: number;
  weaveState: WeaveState;
  x: number;
  y: number;
  world: WorldId;
  strength: number;
  agility: number;
  intellect: number;
  fortitude: number;
  speed: number;
  craftingSkill: number;
  salvageSkill: number;
  primaryElement: MagicElement;
  polarity: MagicPolarity;
  elementRevealed: boolean;
  polarityRevealed: boolean;
  wyrdTangle: number;
  factionTiers: Record<string, ReputationTier>;
  activeCompanionIds: string[];
  unlockedPortals: WorldId[];
  currentQuestIds?: string[];
}

export interface QuestNode {
  questId: string;
  title: string;
  description: string;
  factionId: number;
  requiredTier: number;
  reputationReward: number;
  possibleOutcomes: string[];
  isWyrdQuest: boolean;
  isTaken: boolean;
}

export interface QuestCompleteResult {
  success: boolean;
  message: string;
  reputationGained: number;
  unlockedQuests: QuestNode[];
  wyrdSettled: boolean;
}

export interface AiPlayerState {
  id: string;
  name: string;
  currentFaction: string;
  state: string;
  currentQuestId?: string;
  totalQuestsCompleted: number;
}

export interface WorldStateSnapshot {
  player: PlayerState;
  aiPlayers: AiPlayerState[];
  worldName: string;
}

export interface GameMessage {
  timestamp: string;
  category: 'system' | 'combat' | 'quest' | 'loot' | 'npc' | 'wyrd' | 'error';
  text: string;
}

export type ConnectionState = 'disconnected' | 'connecting' | 'connected' | 'error';

export interface ZoneTile {
  worldId: string;
  zoneId: number;
  name: string;
  description: string;
  asciiSymbol: string;
  dangerLevel: number;
  isPortalZone: boolean;
  x: number;
  y: number;
}

export interface ZoneView {
  tiles: ZoneTile[];
}

export interface InventoryItem {
  id: string;
  name: string;
  description: string;
  workmanship: number;
  category?: string;
  quantity?: number;
  isStackable?: boolean;
}

export interface EquipmentSlots {
  weaponId?: string;
  armorId?: string;
  accessoryId?: string;
  weaponName?: string;
  armorName?: string;
  accessoryName?: string;
}

export interface AbilityState {
  name: string;
  basePower: number;
  weaveCost: number;
  element: string;
  category: string; // Attack, Heal, Buff
}

export interface CombatantState {
  id: string;
  name: string;
  combatantType: string;
  currentHp: number;
  maxHp: number;
  speed: number;
  element: string;
  isPlayerSide: boolean;
  isDefeated: boolean;
  abilities: AbilityState[];
}

export interface CombatUpdate {
  encounterId: string;
  state: string; // NotStarted, InProgress, Victory, Defeat, Fled
  combatants: CombatantState[];
  currentActorId: string;
  round: number;
}

export interface InventorySnapshot {
  playerId: string;
  name: string;
  craftingSkill: number;
  salvageSkill: number;
  items: InventoryItem[];
  equippedWeaponId?: string;
  equippedArmorId?: string;
  equippedAccessoryId?: string;
}

export interface CompanionCapturedEvent {
  companionId: string;
  name: string;
  element: string;
  type: string;
}

export interface StorageItem {
  id: string;
  name: string;
  description: string;
  category: string;
  workmanship: number;
}

export interface StorageViewSnapshot {
  homesteadId: string;
  name: string;
  storageSlots: number;
  usedSlots: number;
  items: StorageItem[];
}

export interface LootDropEvent {
  id: string;
  name: string;
  description: string;
  workmanship: number;
  category: string;
}

export interface AutoFarmStatus {
  active: boolean;
  durationSeconds?: number;
  kills?: number;
  items?: number;
  reason?: string;
}

export interface WanderingNpc {
  id: string;
  name: string;
  role: string;
  x: number;
  y: number;
  dialogue: string;
}

export interface WorldEvent {
  timestamp: string;
  category: string;
  text: string;
}

export interface NewQuestEvent {
  questId: string;
  title: string;
  faction: string;
  repReward: number;
  difficulty: number;
}

export interface NewZoneEvent {
  zoneId: string;
  zoneNumber: number;
  name: string;
  description: string;
  asciiSymbol: string;
  dangerLevel: number;
  x: number;
  y: number;
  worldId: string;
}
