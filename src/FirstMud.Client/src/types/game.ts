export type MagicElement = 'Fire' | 'Water' | 'Earth' | 'Air' | 'Aether';
export type MagicPolarity = 'None' | 'Shaping' | 'Unmaking' | 'TwiceBorn';
export type WorldId = 'Aeldran' | 'ArdweldRemnant' | 'FairgeanDeep' | 'GolvariDeeps' | 'WyrdPaths' | 'TheDream';
export type ReputationTier = 'Hostile' | 'Wary' | 'Unknown' | 'Known' | 'Trusted' | 'Honored' | 'Bound';
export type WeaveState = 'Full' | 'Steady' | 'Strained' | 'Critical' | 'Depleted';

export interface PlayerState {
  id: string;
  name: string;
  level: number;
  currentHp: number;
  maxHp: number;
  weavePercent: number;
  weaveState: WeaveState;
  x: number;
  y: number;
  world: WorldId;
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
