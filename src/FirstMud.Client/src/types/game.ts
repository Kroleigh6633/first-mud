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
  tilesDiscovered?: number;
  effectiveStrength?: number;
  effectiveAgility?: number;
  effectiveIntellect?: number;
  effectiveFortitude?: number;
  effectiveSpeed?: number;
  effectiveMaxHp?: number;
  bonusStrikeDamage?: number;
  bonusSpellDamage?: number;
  activeCompanions?: CompanionState[];
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
  category:
    | 'system'
    | 'combat'
    | 'combat-trivial'
    | 'combat-easy'
    | 'combat-normal'
    | 'combat-hard'
    | 'combat-deadly'
    | 'quest'
    | 'loot'
    | 'loot-common'
    | 'loot-uncommon'
    | 'loot-rare'
    | 'loot-epic'
    | 'loot-legendary'
    | 'salvage-common'
    | 'salvage-uncommon'
    | 'salvage-rare'
    | 'npc'
    | 'wyrd'
    | 'warning'
    | 'error';
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

export interface AppliedImbue {
  type: string;
  power: number;
}

export interface InventoryItem {
  id: string;
  name: string;
  description: string;
  workmanship: number;
  category?: string;
  slot?: string;
  quantity?: number;
  isStackable?: boolean;
  isLocked?: boolean;
  isUnstable?: boolean;
  maxImbueSlots?: number;
  imbues?: AppliedImbue[];
}

export type EquipmentSlotName =
  | 'MeleeWeapon'
  | 'RangedWeapon'
  | 'Focus'
  | 'Head'
  | 'Chest'
  | 'Legs'
  | 'Hands'
  | 'Feet'
  | 'Accessory';

export interface EquipmentSlots {
  // Legacy ids (kept for backward compat during transition)
  weaponId?: string;
  armorId?: string;
  accessoryId?: string;
  // New 9-slot names
  meleeWeaponName?: string;
  rangedWeaponName?: string;
  focusName?: string;
  headName?: string;
  chestName?: string;
  legsName?: string;
  handsName?: string;
  feetName?: string;
  accessoryName?: string;
  // Map of slot name → item id (from EquipmentChanged events)
  equippedItems?: Record<string, string>;
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
  lastActionText?: string;
  dangerLevel?: number;
}

export interface InventorySnapshot {
  playerId: string;
  name: string;
  craftingSkill: number;
  salvageSkill: number;
  autoSalvageWeaponThreshold: number;
  autoSalvageArmorThreshold: number;
  items: InventoryItem[];
  // Legacy three-slot ids (may be absent after migration)
  equippedWeaponId?: string;
  equippedArmorId?: string;
  equippedAccessoryId?: string;
}

export type CompanionType = 'Wildfolk' | 'CapturedMonster' | 'ArdweldConstruct' | 'HiredHero' | 'BoundShade';
export type HomesteadDuty = 'Harvester' | 'Salvager' | 'Guard' | 'Crafter';

export interface CompanionState {
  id: string;
  name: string;
  type: CompanionType;
  element: MagicElement;
  level: number;
  currentLayer: number;
  usageCounter: number;
  driftAccumulator: number;
  isActive: boolean;
  relationshipDepth: number;
  assignedDuty?: HomesteadDuty | null;
  dutyStartedAt?: string | null;
}

export interface CompanionCapturedEvent {
  companionId: string;
  name: string;
  originalMonsterName?: string;
  element: string;
  type: string;
  layer?: number;
  isActive?: boolean;
}

export interface StorageItem {
  id: string;
  name: string;
  description: string;
  category: string;
  slot?: string;
  workmanship: number;
  quantity?: number;
  isStackable?: boolean;
  isUnstable?: boolean;
  maxImbueSlots?: number;
  imbues?: AppliedImbue[];
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
  slot?: string;
}

export interface AutoFarmStatus {
  active: boolean;
  /** Current loop state: idle | walking | fighting | resting | depositing */
  state?: string;
  kills?: number;
  items?: number;
  salvaged?: number;
  deposited?: number;
  biome?: string;
  dangerLevel?: number;
  reason?: string;
}

export interface RecipeIngredient {
  ingredientName: string;
  baseQuantity: number;
  category: string;
  /** Items of this ingredient held in inventory (undefined if not yet reported by server) */
  invCount?: number;
  /** Items of this ingredient in homestead storage (undefined if not yet reported by server) */
  storageCount?: number;
  /** Combined total across inventory + storage */
  totalCount?: number;
}

export interface RecipeInfo {
  recipeId: string;
  name: string;
  resultItemName: string;
  resultCategory: string;
  requiredCraftingSkill: number;
  requiredWorld: string;
  requiredTaperType?: string | null;
  baseWorkmanshipMin: number;
  baseWorkmanshipMax: number;
  isDiscoverable: boolean;
  ingredients: RecipeIngredient[];
}

export interface CraftingCompleteEvent {
  outcome: string;
  itemId?: string | null;
  itemName?: string | null;
  workmanship: number;
  category?: string | null;
  slot?: string | null;
  isDiscovery: boolean;
  message: string;
}

export interface WanderingNpc {
  id: string;
  name: string;
  role: string;
  x: number;
  y: number;
  dialogue: string;
}

export interface QuestWaypoint {
  questId: string;
  questTitle: string;
  targetX: number;
  targetY: number;
  description: string;
}

/** In-memory progress for a single quest (kills or items gathered). */
export interface QuestProgress {
  questId: string;
  kills: number;
  required: number;
}

/** Map of questId → QuestProgress, maintained entirely on the client. */
export type QuestProgressMap = Record<string, QuestProgress>;

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

export interface SmeltYield {
  name: string;
  quantity: number;
}

/** Full result from a SmeltComplete server event. */
export interface SmeltCompleteEvent {
  yields: SmeltYield[];
  message: string;
}

export type BuildingType =
  | 'Forge'
  | 'Fletcher'
  | 'Tannery'
  | 'EnchantingTower'
  | 'AlchemistHut'
  | 'Stoneworker'
  | 'Woodworker'
  | 'MarketStall'
  | 'Farm'
  | 'Mine'
  | 'Barracks'
  | 'Library'
  | 'Warehouse'
  | 'Hut';

export interface HomesteadBuilding {
  id: string;
  homesteadId: string;
  type: BuildingType;
  tier: number;
  gridX: number;
  gridY: number;
  isConstructed: boolean;
  constructionProgress: number;   // 0–100
  assignedCompanionId?: string | null;
  assignedCompanionName?: string | null;
}

export interface CityViewSnapshot {
  homesteadId: string;
  homesteadName: string;
  buildingCount: number;
  constructedCount: number;
  buildings: HomesteadBuilding[];
}
