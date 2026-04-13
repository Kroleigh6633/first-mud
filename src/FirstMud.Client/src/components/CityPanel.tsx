import React, { useState, useCallback } from 'react';
import { useGameCommands, type SendCommandFn } from '../hooks/useGameCommands';
import type {
  CityViewSnapshot,
  HomesteadBuilding,
  HutResident,
  BuildingType,
  CompanionState,
  CompanionType,
  HomesteadDuty,
  StorageItem,
  InventoryItem,
} from '../types/game';

// ─── Constants mirroring server-side data ────────────────────────────────────

// Duty for production buildings only — Hut is housing, not a workstation.
const BUILDING_DUTY: Partial<Record<BuildingType, HomesteadDuty>> = {
  Forge:          'Crafter',
  Fletcher:       'Crafter',
  Tannery:        'Crafter',
  EnchantingTower:'Crafter',
  AlchemistHut:   'Crafter',
  Stoneworker:    'Crafter',
  Woodworker:     'Harvester',
  MarketStall:    'Crafter',
  Farm:           'Harvester',
  Mine:           'Harvester',
  Barracks:       'Guard',
  Library:        'Salvager',
  Warehouse:      'Guard',
  Greenhouse:     'Harvester',
};

const HOUSING_TYPES = new Set<BuildingType>(['Hut']);

// How many workers each building type can support
const WORKER_CAPACITY: Record<BuildingType, number> = {
  Forge:           2,
  Tannery:         2,
  Farm:            3,
  Mine:            3,
  Woodworker:      2,
  AlchemistHut:    2,
  Stoneworker:     2,
  EnchantingTower: 1,
  MarketStall:     1,
  Library:         1,
  Barracks:        5,
  Warehouse:       1,
  Fletcher:        2,
  Hut:             0,
  Greenhouse:      2,
};

const CONSTRUCTION_COST: Record<BuildingType, { material: string; qty: number }[]> = {
  Forge:          [{ material: 'Wood', qty: 10 }, { material: 'Stone', qty: 5 }, { material: 'Iron Ore', qty: 5 }],
  Fletcher:       [{ material: 'Wood', qty: 10 }, { material: 'Sinew', qty: 3 }],
  Tannery:        [{ material: 'Wood', qty: 8 },  { material: 'Leather', qty: 5 }],
  EnchantingTower:[{ material: 'Stone', qty: 10 }, { material: 'Dravenite Dust', qty: 3 }, { material: 'Wood', qty: 5 }],
  AlchemistHut:   [{ material: 'Wood', qty: 8 },  { material: 'Herbs', qty: 3 }],
  Stoneworker:    [{ material: 'Stone', qty: 10 }, { material: 'Wood', qty: 5 }],
  Woodworker:     [{ material: 'Wood', qty: 10 }],
  MarketStall:    [{ material: 'Wood', qty: 8 }],
  Farm:           [{ material: 'Wood', qty: 8 },  { material: 'Stone', qty: 3 }],
  Mine:           [{ material: 'Stone', qty: 10 }, { material: 'Wood', qty: 5 }],
  Barracks:       [{ material: 'Stone', qty: 8 },  { material: 'Wood', qty: 5 }],
  Library:        [{ material: 'Wood', qty: 10 }, { material: 'Stone', qty: 5 }],
  Warehouse:      [{ material: 'Wood', qty: 12 }, { material: 'Stone', qty: 5 }],
  Hut:            [{ material: 'Wood', qty: 5 },  { material: 'Stone', qty: 3 }],
  Greenhouse:     [{ material: 'Wood', qty: 8 },  { material: 'Stone', qty: 5 }, { material: 'Sand', qty: 3 }],
};

const ALL_BUILDING_TYPES: BuildingType[] = [
  'Forge', 'Fletcher', 'Tannery', 'EnchantingTower', 'AlchemistHut',
  'Stoneworker', 'Woodworker', 'MarketStall', 'Farm', 'Mine',
  'Barracks', 'Library', 'Warehouse', 'Hut', 'Greenhouse',
];

// Aptitude per companion type per duty (mirrors CompanionPanel)
const APTITUDE: Record<CompanionType, Record<HomesteadDuty, number>> = {
  Wildfolk:         { Harvester: 3, Salvager: 1, Guard: 2, Crafter: 2 },
  CapturedMonster:  { Harvester: 2, Salvager: 1, Guard: 3, Crafter: 1 },
  ArdweldConstruct: { Harvester: 1, Salvager: 3, Guard: 3, Crafter: 3 },
  HiredHero:        { Harvester: 2, Salvager: 2, Guard: 2, Crafter: 2 },
  BoundShade:       { Harvester: 2, Salvager: 2, Guard: 2, Crafter: 1 },
};

// ─── Tab type ─────────────────────────────────────────────────────────────────

type CityTab = 'OVERVIEW' | 'PRODUCTION' | 'HOUSING';

// ─── Grid helpers ─────────────────────────────────────────────────────────────

/**
 * Returns the next available grid position not occupied by any existing building.
 * Fills row-major: (0,0),(1,0),...,(ROW_WIDTH-1,0),(0,1),(1,1),... so buildings
 * read as tidy rows/columns rather than a radial spiral.
 *
 * Pure / deterministic for a given set of occupied tiles.
 */
const ROW_WIDTH = 7; // buildings per row before wrapping to next row
function nextAvailablePosition(buildings: HomesteadBuilding[]): { x: number; y: number } {
  const occupied = new Set(buildings.map(b => `${b.gridX},${b.gridY}`));
  // Scan up to a generous ceiling: enough slots for >200 buildings
  for (let y = 0; y < 32; y++) {
    for (let x = 0; x < ROW_WIDTH; x++) {
      if (!occupied.has(`${x},${y}`)) return { x, y };
    }
  }
  return { x: 0, y: 0 };
}

// ─── Small display helpers ────────────────────────────────────────────────────

function buildingIcon(type: BuildingType): string {
  switch (type) {
    case 'Forge':           return '🔨';
    case 'Fletcher':        return '🏹';
    case 'Tannery':         return '🐂';
    case 'EnchantingTower': return '✨';
    case 'AlchemistHut':    return '⚗';
    case 'Stoneworker':     return '🪨';
    case 'Woodworker':      return '🌲';
    case 'MarketStall':     return '🛒';
    case 'Farm':            return '🌾';
    case 'Mine':            return '⛏';
    case 'Barracks':        return '⚔';
    case 'Library':         return '📚';
    case 'Warehouse':       return '📦';
    case 'Hut':             return '🏠';
    case 'Greenhouse':      return '🌿';
  }
}

function buildingColor(type: BuildingType): string {
  switch (type) {
    case 'Forge':
    case 'Fletcher':
    case 'Stoneworker':
    case 'Woodworker':      return '#ff8844';
    case 'Tannery':         return '#cc8844';
    case 'EnchantingTower':
    case 'AlchemistHut':    return '#cc88ff';
    case 'MarketStall':     return '#ffcc44';
    case 'Farm':            return '#88cc44';
    case 'Mine':            return '#888888';
    case 'Barracks':        return '#cc4444';
    case 'Library':         return '#44aacc';
    case 'Warehouse':       return '#aaaaaa';
    case 'Hut':             return '#cc9966';
    case 'Greenhouse':      return '#44cc88';
  }
}

function constructionTimeStr(pct: number): string {
  const remainingPercent = 100 - pct;
  const minutesRemaining = Math.ceil(remainingPercent / 5); // 5% per minute per builder
  return minutesRemaining >= 60
    ? `~${Math.floor(minutesRemaining / 60)}h ${minutesRemaining % 60}m`
    : `~${minutesRemaining}m`;
}

function ProgressBar({ pct }: { pct: number }) {
  const filled = Math.round(pct / 5); // 20 slots
  const timeStr = constructionTimeStr(pct);
  return (
    <span>
      <span style={{ color: '#00ff41' }}>{'█'.repeat(filled)}</span>
      <span style={{ color: '#222222' }}>{'█'.repeat(20 - filled)}</span>
      <span style={{ color: '#888888' }}> {pct}%</span>
      <span style={{ color: '#ff8800' }}> · {timeStr} remaining</span>
    </span>
  );
}

// ─── Props ────────────────────────────────────────────────────────────────────

interface Props {
  cityView: CityViewSnapshot | null;
  onClose: () => void;
  sendCommand: SendCommandFn;
  companionRoster?: CompanionState[];
  activeCompanionIds?: string[];
  storageItems?: StorageItem[];
  inventoryItems?: InventoryItem[];
}

// ─── BuildingRow ──────────────────────────────────────────────────────────────

interface BuildingRowProps {
  building: HomesteadBuilding;
  availableCompanions: CompanionState[];
  sendCommand: SendCommandFn;
}

function BuildingRow({ building, availableCompanions, sendCommand }: BuildingRowProps) {
  const commands = useGameCommands(sendCommand);
  const [selectedCompanionId, setSelectedCompanionId] = useState<string>('');
  const color = buildingColor(building.type);
  const icon = buildingIcon(building.type);
  const tierStr = '★'.repeat(building.tier);
  const isHousing = HOUSING_TYPES.has(building.type);
  const duty = isHousing ? undefined : BUILDING_DUTY[building.type];
  const workerCapacity = WORKER_CAPACITY[building.type] ?? 1;

  const handleAssign = () => {
    if (!selectedCompanionId) return;
    commands.assignBuilder({ companionId: selectedCompanionId, buildingId: building.id });
    setSelectedCompanionId('');
  };

  const handleUnassign = () => {
    commands.unassignBuilder({ buildingId: building.id });
  };

  const residents: HutResident[] = building.residents ?? [];
  const residentCapacity = building.residentCapacity ?? 0;

  return (
    <div style={{ marginBottom: '10px', borderLeft: `3px solid ${color}`, paddingLeft: '8px' }}>
      {/* Header row */}
      <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
        <span style={{ fontSize: '14px' }}>{icon}</span>
        <span style={{ color, fontWeight: 'bold' }}>{building.type.toUpperCase()}</span>
        <span style={{ color: '#ffcc00', fontSize: '10px' }}>{tierStr}</span>
        {!isHousing && building.isConstructed && (
          <span style={{ color: '#888888', fontSize: '10px', marginLeft: '4px' }}>
            {(building as any).workers?.length ?? (building.assignedCompanionName ? 1 : 0)}/{(building as any).workerCapacity ?? workerCapacity} workers
          </span>
        )}
        {!building.isConstructed && (
          <span style={{ color: '#ff8800', fontSize: '10px', marginLeft: 'auto' }}>
            {building.constructionProgress}% · {constructionTimeStr(building.constructionProgress)} remaining
          </span>
        )}
      </div>

      {/* Housing building (Hut) — show residents */}
      {isHousing && building.isConstructed && (
        <div style={{ fontSize: '11px', marginTop: '4px' }}>
          {residents.length > 0 ? (
            <div>
              <span style={{ color: '#aaaaaa' }}>
                Residents:{' '}
                <span style={{ color: '#00ccff' }}>
                  {residents.map(r => r.name).join(', ')}
                </span>{' '}
                <span style={{ color: '#888888' }}>({residents.length}/{residentCapacity})</span>
              </span>
            </div>
          ) : (
            <div style={{ color: '#555555' }}>
              No residents yet ({residentCapacity} capacity) — use <em>Build &amp; Staff Everything</em> to auto-fill.
            </div>
          )}
          <span style={{ color: '#444444', fontSize: '10px', display: 'block', marginTop: '2px' }}>
            Pos: ({building.gridX},{building.gridY})
          </span>
        </div>
      )}

      {/* Production building (constructed) — show workers (auto-managed) */}
      {!isHousing && building.isConstructed && (
        <div style={{ fontSize: '11px', marginTop: '4px' }}>
          {(() => {
            const workers: { id: string; name: string; duty?: string }[] = (building as any).workers ?? [];
            if (workers.length === 0 && building.assignedCompanionName) {
              // Legacy single-worker fallback
              workers.push({ id: building.assignedCompanionId ?? '', name: building.assignedCompanionName, duty: duty ?? undefined });
            }
            return workers.length > 0 ? (
              <div>
                {workers.map((w, i) => (
                  <div key={w.id || i} style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '2px' }}>
                    <span style={{ color: '#aaaaaa' }}>
                      Worker: <span style={{ color: '#00ccff' }}>{w.name}</span>
                      {(w.duty || duty) && (
                        <> <span style={{ color: '#888888', fontSize: '10px' }}>({w.duty || duty})</span></>
                      )}
                    </span>
                    <button
                      type="button"
                      onClick={handleUnassign}
                      style={unassignBtnStyle}
                      title="Recall companion from this building"
                    >
                      Unassign
                    </button>
                  </div>
                ))}
              </div>
            ) : (
              <div style={{ color: '#555555' }}>
                No workers assigned — the administrator will auto-fill when companions are available.
              </div>
            );
          })()}
          <span style={{ color: '#444444', fontSize: '10px', display: 'block', marginTop: '2px' }}>
            Pos: ({building.gridX},{building.gridY})
          </span>
        </div>
      )}

      {/* Under construction — show progress + builder (applies to both housing and production) */}
      {!building.isConstructed && (
        <div style={{ fontSize: '11px', marginTop: '2px' }}>
          <ProgressBar pct={building.constructionProgress} />
          {(building.assignedCompanionName || (building as any).workers?.length > 0) ? (
            <div style={{ marginTop: '3px' }}>
              {((building as any).workers ?? (building.assignedCompanionName ? [{ name: building.assignedCompanionName }] : [])).map((w: any, i: number) => (
                <div key={w.id || i} style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '2px' }}>
                  <span style={{ color: '#aaaaaa' }}>
                    Builder: <span style={{ color: '#00ccff' }}>{w.name}</span>
                  </span>
                  <button
                    type="button"
                    onClick={handleUnassign}
                    style={unassignBtnStyle}
                    title="Recall builder"
                  >
                    Unassign
                  </button>
                </div>
              ))}
            </div>
          ) : (
            <div style={{ marginTop: '4px' }}>
              <div style={{ color: '#888888', marginBottom: '4px' }}>
                No builder — the administrator will auto-assign one.
              </div>
              {availableCompanions.length === 0 ? (
                <div style={{ color: '#555555', fontSize: '10px' }}>
                  No available companions
                </div>
              ) : (
                <div style={{ display: 'flex', alignItems: 'center', gap: '6px', flexWrap: 'wrap' }}>
                  <select
                    value={selectedCompanionId}
                    onChange={e => setSelectedCompanionId(e.target.value)}
                    style={selectStyle}
                    aria-label={`Select builder for ${building.type}`}
                  >
                    <option value="">-- Select builder --</option>
                    {availableCompanions.map(c => {
                      const apt = APTITUDE[c.type]?.['Crafter'] ?? 1;
                      const stars = '★'.repeat(apt) + '☆'.repeat(3 - apt);
                      return (
                        <option key={c.id} value={c.id}>
                          {c.name} ({c.type}) Crafter: {stars}
                        </option>
                      );
                    })}
                  </select>
                  {selectedCompanionId && (
                    <button
                      type="button"
                      onClick={handleAssign}
                      style={assignBtnStyle}
                    >
                      Assign Builder
                    </button>
                  )}
                </div>
              )}
            </div>
          )}
        </div>
      )}
    </div>
  );
}

// ─── Place New Building section ───────────────────────────────────────────────

interface PlaceBuildingSectionProps {
  buildings: HomesteadBuilding[];
  storageItems: StorageItem[];
  inventoryItems: InventoryItem[];
  sendCommand: SendCommandFn;
  /** When set, only this building type is offered and it is pre-selected. */
  restrictTo?: BuildingType;
}

// All building types can be placed multiple times — scale production with more buildings
const MULTI_PLACE_TYPES = new Set<BuildingType>(ALL_BUILDING_TYPES);

function PlaceBuildingSection({ buildings, storageItems, inventoryItems, sendCommand, restrictTo }: PlaceBuildingSectionProps) {
  const commands = useGameCommands(sendCommand);
  const placedTypes = new Set(buildings.map(b => b.type));
  // Multi-place types are always available; single-place types only if not yet placed
  const availableTypes = restrictTo
    ? [restrictTo]
    : ALL_BUILDING_TYPES.filter(t => MULTI_PLACE_TYPES.has(t) || !placedTypes.has(t));
  const [selectedType, setSelectedType] = useState<BuildingType | ''>(restrictTo ?? '');

  if (availableTypes.length === 0) {
    return (
      <div style={{ color: '#555555', fontSize: '11px', padding: '4px 0' }}>
        All building types already placed.
      </div>
    );
  }

  const costs = selectedType ? CONSTRUCTION_COST[selectedType] : null;

  // Count material availability across storage + inventory
  const matCount = (name: string): number => {
    const fromStorage = storageItems
      .filter(i => i.name.toLowerCase() === name.toLowerCase())
      .reduce((sum, i) => sum + (i.quantity ?? 1), 0);
    const fromInv = inventoryItems
      .filter(i => i.name.toLowerCase() === name.toLowerCase())
      .reduce((sum, i) => sum + (i.quantity ?? 1), 0);
    return fromStorage + fromInv;
  };

  const canAfford = costs
    ? costs.every(c => matCount(c.material) >= c.qty)
    : false;

  const handlePlace = () => {
    if (!selectedType) return;
    const pos = nextAvailablePosition(buildings);
    commands.placeBuilding({ buildingType: selectedType, gridX: pos.x, gridY: pos.y });
    // When restricted to a single type, keep it selected for quick repeat placing
    if (!restrictTo) setSelectedType('');
  };

  return (
    <div style={{ fontSize: '11px' }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap', marginBottom: '6px' }}>
        {restrictTo ? (
          <span style={{ color: '#aaaaaa' }}>{buildingIcon(restrictTo)} {restrictTo}</span>
        ) : (
          <select
            value={selectedType}
            onChange={e => setSelectedType(e.target.value as BuildingType | '')}
            style={selectStyle}
            aria-label="Select building type to place"
          >
            <option value="">-- Select building type --</option>
            {availableTypes.map(t => (
              <option key={t} value={t}>
                {buildingIcon(t)} {t}
              </option>
            ))}
          </select>
        )}
        <button
          type="button"
          onClick={handlePlace}
          disabled={!selectedType || !canAfford}
          style={{
            ...assignBtnStyle,
            opacity: (!selectedType || !canAfford) ? 0.45 : 1,
            cursor: (!selectedType || !canAfford) ? 'not-allowed' : 'pointer',
          }}
          title={!canAfford && selectedType ? 'Insufficient materials' : 'Place building at next available position'}
        >
          Place
        </button>
      </div>

      {costs && (
        <div style={{ color: '#aaaaaa', fontSize: '10px' }}>
          <span style={{ color: '#888888' }}>Cost: </span>
          {costs.map((c, i) => {
            const have = matCount(c.material);
            const ok = have >= c.qty;
            return (
              <span key={c.material}>
                {i > 0 && <span style={{ color: '#444444' }}>, </span>}
                <span style={{ color: ok ? '#00cc88' : '#cc4444' }} title={ok ? 'Available' : `Need ${c.qty - have} more`}>
                  {c.material} {have}/{c.qty}
                </span>
              </span>
            );
          })}
          {!canAfford && (
            <span style={{ color: '#cc4444', marginLeft: '8px' }}>Insufficient materials</span>
          )}
        </div>
      )}
    </div>
  );
}

// ─── Button styles ────────────────────────────────────────────────────────────

const assignBtnStyle: React.CSSProperties = {
  background: 'none',
  border: '1px solid #00cc88',
  color: '#00cc88',
  fontFamily: 'monospace',
  fontSize: '11px',
  padding: '3px 10px',
  cursor: 'pointer',
};

const unassignBtnStyle: React.CSSProperties = {
  background: 'none',
  border: '1px solid #888888',
  color: '#888888',
  fontFamily: 'monospace',
  fontSize: '10px',
  padding: '2px 7px',
  cursor: 'pointer',
};

const selectStyle: React.CSSProperties = {
  background: '#1a1a1a',
  border: '1px solid #333333',
  color: '#aaaaaa',
  fontFamily: 'monospace',
  fontSize: '11px',
  padding: '3px 6px',
  cursor: 'pointer',
  minWidth: '220px',
};

// ─── Workforce stats helper ───────────────────────────────────────────────────

interface WorkforceStats {
  total: number;
  adventuring: number;
  working: number;
  builders: number;
  guards: number;
  housed: number;
  housingCapacity: number;
  hutCount: number;
  homeless: number;
  unemployed: number;
  productionBuildings: HomesteadBuilding[];
  housingBuildings: HomesteadBuilding[];
  underConstruction: HomesteadBuilding[];
  allProductionStaffed: boolean;
  buildingsNeedingBuilders: HomesteadBuilding[];
}

function computeWorkforceStats(
  buildings: HomesteadBuilding[],
  companionRoster: CompanionState[],
  activeCompanionIds: string[],
): WorkforceStats {
  const total = companionRoster.length;
  const adventuring = activeCompanionIds.length;

  const productionBuildings = buildings.filter(
    b => b.isConstructed && !HOUSING_TYPES.has(b.type)
  );
  const housingBuildings = buildings.filter(
    b => b.isConstructed && HOUSING_TYPES.has(b.type)
  );
  const underConstruction = buildings.filter(b => !b.isConstructed);

  // Count by companion duty (not building type) for accurate workforce totals
  const nonAdventuring = companionRoster.filter(c => !activeCompanionIds.includes(c.id));

  // Builders = companions assigned to under-construction buildings
  const builderIds = new Set(
    underConstruction.flatMap(b => {
      const workers: { id: string }[] = (b as any).workers ?? [];
      return workers.map(w => w.id);
    })
  );

  const builders = builderIds.size;
  const working = nonAdventuring.filter(
    c => c.assignedDuty && c.assignedDuty !== 'Guard' && !builderIds.has(c.id)
  ).length;
  const guards = nonAdventuring.filter(
    c => c.assignedDuty === 'Guard'
  ).length;

  const housed = housingBuildings.reduce((sum, b) => sum + (b.residents?.length ?? 0), 0);
  const housingCapacity = housingBuildings.reduce((sum, b) => sum + (b.residentCapacity ?? 0), 0);
  const hutCount = housingBuildings.length;
  const homeless = Math.max(0, total - adventuring - housed);

  const assignedOrActive = new Set([
    ...activeCompanionIds,
    ...buildings.flatMap(b => {
      const workers: { id: string }[] = (b as any).workers ?? [];
      const workerIds = workers.map(w => w.id);
      // Also include legacy single assignedCompanionId
      if (b.assignedCompanionId && !workerIds.includes(b.assignedCompanionId))
        workerIds.push(b.assignedCompanionId);
      return workerIds;
    }),
  ]);
  const unemployed = companionRoster.filter(
    c => !assignedOrActive.has(c.id) && !c.assignedDuty
  ).length;

  const allProductionStaffed = productionBuildings.length > 0 &&
    productionBuildings.every(b => !!b.assignedCompanionName);

  const buildingsNeedingBuilders = underConstruction.filter(b => !b.assignedCompanionName && !((b as any).workers?.length > 0));

  return {
    total, adventuring, working, builders, guards, housed, housingCapacity, hutCount,
    homeless, unemployed, productionBuildings, housingBuildings, underConstruction,
    allProductionStaffed, buildingsNeedingBuilders,
  };
}

// ─── Overview tab ─────────────────────────────────────────────────────────────

interface OverviewTabProps {
  stats: WorkforceStats;
}

function OverviewTab({ stats }: OverviewTabProps) {
  const dividerStyle: React.CSSProperties = {
    color: '#333333',
    margin: '4px 0',
    fontSize: '11px',
  };

  const rowStyle: React.CSSProperties = {
    display: 'flex',
    gap: '6px',
    fontSize: '11px',
    margin: '2px 0',
  };

  const labelStyle: React.CSSProperties = { color: '#888888', minWidth: '160px' };

  // Derive recommendation list
  const recommendations: { ok: boolean; text: string }[] = [];

  if (stats.homeless > 0) {
    const hutsNeeded = Math.ceil(stats.homeless / 3); // 3 capacity per hut
    recommendations.push({ ok: false, text: `${stats.homeless} companions need housing — the administrator will auto-build ${hutsNeeded} hut${hutsNeeded !== 1 ? 's' : ''} when materials are available` });
  }

  if (stats.guards === 0 && stats.productionBuildings.length > 0) {
    recommendations.push({ ok: false, text: 'No guards assigned — assign guards for storage bonus' });
  }

  if (stats.buildingsNeedingBuilders.length > 0) {
    recommendations.push({
      ok: false,
      text: `${stats.buildingsNeedingBuilders.length} building${stats.buildingsNeedingBuilders.length !== 1 ? 's' : ''} under construction with no builder — assign a builder`,
    });
  }

  if (stats.allProductionStaffed && stats.productionBuildings.length > 0) {
    recommendations.push({ ok: true, text: 'All production buildings staffed' });
  }

  if (recommendations.length === 0) {
    recommendations.push({ ok: true, text: 'City is running well — no immediate actions needed' });
  }

  // Collect production building type names for summary
  const prodTypeNames = stats.productionBuildings.map(b => b.type);
  const prodTypesSummary = prodTypeNames.length > 0
    ? prodTypeNames.slice(0, 4).join(', ') + (prodTypeNames.length > 4 ? ', ...' : '')
    : 'none';

  return (
    <div style={{ padding: '8px 14px 14px' }}>
      {/* Workforce summary */}
      <div style={{ color: '#888888', fontSize: '11px', letterSpacing: '0.12em', textTransform: 'uppercase', marginBottom: '6px', borderBottom: '1px solid #1a1a1a', paddingBottom: '3px' }}>
        Workforce Summary
      </div>
      <div style={dividerStyle}>{'─'.repeat(36)}</div>

      <div style={rowStyle}>
        <span style={labelStyle}>Total companions:</span>
        <span style={{ color: '#ffcc00' }}>{stats.total}</span>
      </div>
      <div style={{ ...rowStyle, marginLeft: '12px' }}>
        <span style={labelStyle}>Adventuring:</span>
        <span style={{ color: '#00ccff' }}>{stats.adventuring}</span>
      </div>
      <div style={{ ...rowStyle, marginLeft: '12px' }}>
        <span style={labelStyle}>Working (production):</span>
        <span style={{ color: '#00cc88' }}>{stats.working}</span>
      </div>
      <div style={{ ...rowStyle, marginLeft: '12px' }}>
        <span style={labelStyle}>Builders:</span>
        <span style={{ color: stats.builders > 0 ? '#ff8800' : '#555555' }}>{stats.builders}</span>
      </div>
      <div style={{ ...rowStyle, marginLeft: '12px' }}>
        <span style={labelStyle}>Guards:</span>
        <span style={{ color: '#cc4444' }}>{stats.guards}</span>
      </div>
      <div style={{ ...rowStyle, marginLeft: '12px' }}>
        <span style={labelStyle}>Housed:</span>
        <span style={{ color: '#cc9966' }}>
          {stats.housed}/{stats.housingCapacity}{' '}
          <span style={{ color: '#666666' }}>({stats.hutCount} hut{stats.hutCount !== 1 ? 's' : ''})</span>
        </span>
      </div>
      <div style={{ ...rowStyle, marginLeft: '12px' }}>
        <span style={labelStyle}>Homeless:</span>
        <span style={{ color: stats.homeless > 0 ? '#ff8800' : '#555555' }}>{stats.homeless}</span>
      </div>
      <div style={{ ...rowStyle, marginLeft: '12px' }}>
        <span style={labelStyle}>Unassigned:</span>
        <span style={{ color: stats.unemployed > 0 ? '#ffcc00' : '#555555' }}>{stats.unemployed}</span>
        {stats.unemployed === 0 && <span style={{ color: '#555555', marginLeft: '4px' }}>(all auto-assigned)</span>}
      </div>

      {/* Building summary */}
      <div style={{ color: '#888888', fontSize: '11px', letterSpacing: '0.12em', textTransform: 'uppercase', marginTop: '12px', marginBottom: '6px', borderBottom: '1px solid #1a1a1a', paddingBottom: '3px' }}>
        Building Summary
      </div>
      <div style={dividerStyle}>{'─'.repeat(36)}</div>

      <div style={rowStyle}>
        <span style={labelStyle}>Production:</span>
        <span style={{ color: '#ff8844' }}>
          {stats.productionBuildings.length}{' '}
          <span style={{ color: '#666666' }}>({prodTypesSummary})</span>
        </span>
      </div>
      <div style={rowStyle}>
        <span style={labelStyle}>Housing:</span>
        <span style={{ color: '#cc9966' }}>
          {stats.hutCount} hut{stats.hutCount !== 1 ? 's' : ''}{' '}
          <span style={{ color: '#666666' }}>({stats.housingCapacity} capacity)</span>
        </span>
      </div>
      <div style={rowStyle}>
        <span style={labelStyle}>Under construction:</span>
        <span style={{ color: stats.underConstruction.length > 0 ? '#ff8800' : '#555555' }}>
          {stats.underConstruction.length}
        </span>
      </div>

      {/* Recommendations */}
      <div style={{ color: '#888888', fontSize: '11px', letterSpacing: '0.12em', textTransform: 'uppercase', marginTop: '12px', marginBottom: '6px', borderBottom: '1px solid #1a1a1a', paddingBottom: '3px' }}>
        Recommendations
      </div>
      <div style={dividerStyle}>{'─'.repeat(36)}</div>
      {recommendations.map((r, i) => (
        <div key={i} style={{ ...rowStyle, gap: '6px', alignItems: 'flex-start' }}>
          <span style={{ color: r.ok ? '#00cc88' : '#ff8800', flexShrink: 0 }}>{r.ok ? '✓' : '⚠'}</span>
          <span style={{ color: r.ok ? '#00cc88' : '#ffcc44' }}>{r.text}</span>
        </div>
      ))}
    </div>
  );
}

// ─── Production tab ───────────────────────────────────────────────────────────

interface ProductionTabProps {
  buildings: HomesteadBuilding[];
  availableCompanions: CompanionState[];
  sendCommand: SendCommandFn;
  storageItems: StorageItem[];
  inventoryItems: InventoryItem[];
}

function ProductionTab({ buildings, availableCompanions, sendCommand, storageItems, inventoryItems }: ProductionTabProps) {
  const productionBuildings = buildings.filter(b => !HOUSING_TYPES.has(b.type));
  const constructed = productionBuildings.filter(b => b.isConstructed);
  const underConstruction = productionBuildings.filter(b => !b.isConstructed);

  if (productionBuildings.length === 0) {
    return (
      <div style={{ padding: '14px', color: '#888888', fontSize: '12px' }}>
        No production buildings yet. Use <em>Build &amp; Staff Everything</em> or place buildings below.
      </div>
    );
  }

  return (
    <div style={{ padding: '4px 14px 14px' }}>
      {constructed.length > 0 && (
        <>
          <div style={{ color: '#888888', fontSize: '11px', letterSpacing: '0.12em', textTransform: 'uppercase', margin: '10px 0 4px', borderBottom: '1px solid #1a1a1a', paddingBottom: '3px' }}>
            Completed ({constructed.length})
          </div>
          {constructed.map(b => (
            <BuildingRow
              key={b.id}
              building={b}
              availableCompanions={availableCompanions}
              sendCommand={sendCommand}
            />
          ))}
        </>
      )}
      {underConstruction.length > 0 && (
        <>
          <div style={{ color: '#888888', fontSize: '11px', letterSpacing: '0.12em', textTransform: 'uppercase', margin: '10px 0 4px', borderBottom: '1px solid #1a1a1a', paddingBottom: '3px' }}>
            Under Construction ({underConstruction.length})
          </div>
          {underConstruction.map(b => (
            <BuildingRow
              key={b.id}
              building={b}
              availableCompanions={availableCompanions}
              sendCommand={sendCommand}
            />
          ))}
        </>
      )}
      <div style={{ color: '#888888', fontSize: '11px', letterSpacing: '0.12em', textTransform: 'uppercase', margin: '14px 0 4px', borderBottom: '1px solid #1a1a1a', paddingBottom: '3px' }}>
        Place New Building
      </div>
      <PlaceBuildingSection
        buildings={buildings}
        storageItems={storageItems}
        inventoryItems={inventoryItems}
        sendCommand={sendCommand}
      />
    </div>
  );
}

// ─── Housing tab ──────────────────────────────────────────────────────────────

interface HousingTabProps {
  buildings: HomesteadBuilding[];
  availableCompanions: CompanionState[];
  sendCommand: SendCommandFn;
  storageItems: StorageItem[];
  inventoryItems: InventoryItem[];
}

function HousingTab({ buildings, availableCompanions, sendCommand, storageItems, inventoryItems }: HousingTabProps) {
  const huts = buildings.filter(b => HOUSING_TYPES.has(b.type));
  const constructedHuts = huts.filter(b => b.isConstructed);
  const hutUnderConstruction = huts.filter(b => !b.isConstructed);

  const totalHoused = constructedHuts.reduce((sum, b) => sum + (b.residents?.length ?? 0), 0);
  const totalCapacity = constructedHuts.reduce((sum, b) => sum + (b.residentCapacity ?? 0), 0);

  return (
    <div style={{ padding: '4px 14px 14px' }}>
      {huts.length > 0 && (
        <div style={{ fontSize: '11px', color: '#cc9966', marginBottom: '8px', marginTop: '6px' }}>
          Total housed:{' '}
          <span style={{ color: '#ffcc00' }}>{totalHoused}</span>
          {' / '}
          <span style={{ color: '#aaaaaa' }}>{totalCapacity}</span>
          {' capacity across '}
          <span style={{ color: '#ffcc00' }}>{constructedHuts.length}</span>
          {' hut'}{constructedHuts.length !== 1 ? 's' : ''}
        </div>
      )}

      {constructedHuts.length > 0 && (
        <>
          <div style={{ color: '#888888', fontSize: '11px', letterSpacing: '0.12em', textTransform: 'uppercase', margin: '6px 0 4px', borderBottom: '1px solid #1a1a1a', paddingBottom: '3px' }}>
            Huts ({constructedHuts.length})
          </div>
          {constructedHuts.map(b => (
            <BuildingRow
              key={b.id}
              building={b}
              availableCompanions={availableCompanions}
              sendCommand={sendCommand}
            />
          ))}
        </>
      )}

      {hutUnderConstruction.length > 0 && (
        <>
          <div style={{ color: '#888888', fontSize: '11px', letterSpacing: '0.12em', textTransform: 'uppercase', margin: '10px 0 4px', borderBottom: '1px solid #1a1a1a', paddingBottom: '3px' }}>
            Under Construction ({hutUnderConstruction.length})
          </div>
          {hutUnderConstruction.map(b => (
            <BuildingRow
              key={b.id}
              building={b}
              availableCompanions={availableCompanions}
              sendCommand={sendCommand}
            />
          ))}
        </>
      )}

      {huts.length === 0 && (
        <div style={{ padding: '10px 0', color: '#888888', fontSize: '12px' }}>
          No huts yet — build huts to house companions.
        </div>
      )}

      <div style={{ color: '#888888', fontSize: '11px', letterSpacing: '0.12em', textTransform: 'uppercase', margin: '14px 0 4px', borderBottom: '1px solid #1a1a1a', paddingBottom: '3px' }}>
        Build More Huts
      </div>
      <PlaceBuildingSection
        buildings={buildings}
        storageItems={storageItems}
        inventoryItems={inventoryItems}
        sendCommand={sendCommand}
        restrictTo="Hut"
      />
    </div>
  );
}

// ─── Main CityPanel ───────────────────────────────────────────────────────────

export default function CityPanel({
  cityView,
  onClose,
  sendCommand,
  companionRoster = [],
  activeCompanionIds = [],
  storageItems = [],
  inventoryItems = [],
}: Props) {
  const commands = useGameCommands(sendCommand);
  const [activeTab, setActiveTab] = useState<CityTab>('OVERVIEW');

  const panelStyle: React.CSSProperties = {
    position: 'fixed',
    top: '50%',
    left: '50%',
    transform: 'translate(-50%, -50%)',
    background: '#0d0d0d',
    border: '1px solid #cc8844',
    fontFamily: 'monospace',
    fontSize: '13px',
    color: '#00ff41',
    width: '580px',
    maxHeight: '85vh',
    overflowY: 'auto',
    zIndex: 100,
    boxShadow: '0 0 40px rgba(204, 136, 68, 0.3)',
  };

  const headerStyle: React.CSSProperties = {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    padding: '10px 14px',
    borderBottom: '1px solid #3a2a1a',
    background: '#1a1208',
    position: 'sticky',
    top: 0,
    zIndex: 1,
  };

  if (!cityView) {
    return (
      <div style={panelStyle}>
        <div style={headerStyle}>
          <span style={{ color: '#cc8844', letterSpacing: '0.15em' }}>HOMESTEAD CITY</span>
          <button onClick={onClose} style={{ background: 'none', border: 'none', color: '#888', cursor: 'pointer', fontSize: '16px' }}>✕</button>
        </div>
        <div style={{ padding: '20px 14px', color: '#888888' }}>
          Loading city data... <span style={{ color: '#555' }}>(portal home first)</span>
        </div>
      </div>
    );
  }

  const underConstruction = cityView.buildings.filter(b => !b.isConstructed);

  // Companions available for assignment:
  //   - not in the active adventuring party (use authoritative activeCompanionIds, not stale c.isActive)
  //   - not already assigned to a building in this city view
  //   - not on any homestead duty (assignedDuty covers companions on duty but not yet mapped to a building)
  const assignedCompanionIds = new Set(
    cityView.buildings
      .filter(b => b.assignedCompanionId)
      .map(b => b.assignedCompanionId as string)
  );
  const availableCompanions = companionRoster.filter(
    c =>
      !activeCompanionIds.includes(c.id) &&
      !assignedCompanionIds.has(c.id) &&
      !c.assignedDuty
  );

  const stats = computeWorkforceStats(cityView.buildings, companionRoster, activeCompanionIds);

  // Auto-assign all: for each PRODUCTION building without a worker, pick best-aptitude available companion.
  // Housing buildings (Huts) are excluded — residents are assigned via BuildStaffEverything.
  const handleAutoAssignAll = useCallback(() => {
    // Track which companions we've already "used" in this batch
    const usedIds = new Set<string>();
    const buildingsNeedingWorkers = cityView.buildings.filter(
      b => !HOUSING_TYPES.has(b.type) && !b.assignedCompanionName
    );

    for (const building of buildingsNeedingWorkers) {
      const duty = building.isConstructed
        ? (BUILDING_DUTY[building.type] ?? 'Crafter' as HomesteadDuty)
        : 'Crafter' as HomesteadDuty;
      const best = availableCompanions
        .filter(c => !usedIds.has(c.id))
        .sort((a, b) => {
          const aApt = APTITUDE[a.type]?.[duty] ?? 1;
          const bApt = APTITUDE[b.type]?.[duty] ?? 1;
          return bApt - aApt;
        })[0];

      if (best) {
        commands.assignBuilder({ companionId: best.id, buildingId: building.id });
        usedIds.add(best.id);
      }
    }
  }, [cityView.buildings, availableCompanions, commands]);

  // One-click master action: server seeds all missing buildings then auto-staffs empty slots
  const handleBuildStaffEverything = useCallback(() => {
    commands.buildStaffEverything();
  }, [commands]);

  const unassignedBuildings = cityView.buildings.filter(
    b => !HOUSING_TYPES.has(b.type) && !b.assignedCompanionName
  );
  const canAutoAssign = unassignedBuildings.length > 0 && availableCompanions.length > 0;

  // Tab style helpers
  const tabStyle = (tab: CityTab): React.CSSProperties => ({
    background: 'none',
    border: 'none',
    borderBottom: activeTab === tab ? '2px solid #cc8844' : '2px solid transparent',
    color: activeTab === tab ? '#cc8844' : '#666666',
    fontFamily: 'monospace',
    fontSize: '11px',
    letterSpacing: '0.1em',
    padding: '6px 14px 5px',
    cursor: 'pointer',
    transition: 'color 0.1s',
  });

  return (
    <div style={panelStyle}>
      {/* Header */}
      <div style={headerStyle}>
        <span style={{ color: '#cc8844', letterSpacing: '0.15em' }}>
          HOMESTEAD CITY — {cityView.homesteadName}
        </span>
        <button onClick={onClose} style={{ background: 'none', border: 'none', color: '#888', cursor: 'pointer', fontSize: '16px' }}>✕</button>
      </div>

      {/* Tab bar */}
      <div style={{ display: 'flex', borderBottom: '1px solid #1a1a1a', background: '#111111', position: 'sticky', top: '41px', zIndex: 1 }}>
        <button type="button" style={tabStyle('OVERVIEW')} onClick={() => setActiveTab('OVERVIEW')}>OVERVIEW</button>
        <button type="button" style={tabStyle('PRODUCTION')} onClick={() => setActiveTab('PRODUCTION')}>PRODUCTION</button>
        <button type="button" style={tabStyle('HOUSING')} onClick={() => setActiveTab('HOUSING')}>HOUSING</button>
      </div>

      {/* Action toolbar — always visible */}
      <div style={{ padding: '6px 14px 6px', borderBottom: '1px solid #1a1a1a', display: 'flex', gap: '8px', alignItems: 'center', flexWrap: 'wrap' }}>
        <button
          type="button"
          onClick={handleBuildStaffEverything}
          style={{
            background: '#1a0a00',
            border: '1px solid #cc8844',
            color: '#cc8844',
            fontFamily: 'monospace',
            fontSize: '11px',
            padding: '4px 12px',
            cursor: 'pointer',
            fontWeight: 'bold',
          }}
          title="Seed all missing starter buildings then auto-assign the best companion to every empty slot"
        >
          Build &amp; Staff Everything
        </button>
        <button
          type="button"
          onClick={handleAutoAssignAll}
          disabled={!canAutoAssign}
          style={{
            background: canAutoAssign ? '#0a1f0a' : '#111111',
            border: `1px solid ${canAutoAssign ? '#00cc88' : '#333333'}`,
            color: canAutoAssign ? '#00cc88' : '#444444',
            fontFamily: 'monospace',
            fontSize: '11px',
            padding: '4px 12px',
            cursor: canAutoAssign ? 'pointer' : 'not-allowed',
          }}
          title={canAutoAssign ? 'Assign best available companion to each empty building' : 'No unassigned buildings or no available companions'}
        >
          Auto-Assign All
        </button>
        <button
          type="button"
          onClick={() => { commands.viewCity(); }}
          style={{
            background: '#1a1a1a',
            border: '1px solid #333',
            color: '#aaaaaa',
            fontFamily: 'monospace',
            fontSize: '11px',
            padding: '4px 10px',
            cursor: 'pointer',
          }}
        >
          ↻ Refresh
        </button>
        {/* Quick status pills */}
        <span style={{ color: '#555555', fontSize: '10px', marginLeft: 'auto' }}>
          <span style={{ color: '#ffcc00' }}>{cityView.constructedCount}</span> built
          {' · '}
          <span style={{ color: underConstruction.length > 0 ? '#ff8800' : '#555555' }}>{underConstruction.length}</span> building
          {' · '}
          <span style={{ color: availableCompanions.length > 0 ? '#00cc88' : '#555555' }}>{availableCompanions.length}</span> idle
        </span>
      </div>

      {/* Tab content */}
      {activeTab === 'OVERVIEW' && (
        <OverviewTab stats={stats} />
      )}
      {activeTab === 'PRODUCTION' && (
        <ProductionTab
          buildings={cityView.buildings}
          availableCompanions={availableCompanions}
          sendCommand={sendCommand}
          storageItems={storageItems}
          inventoryItems={inventoryItems}
        />
      )}
      {activeTab === 'HOUSING' && (
        <HousingTab
          buildings={cityView.buildings}
          availableCompanions={availableCompanions}
          sendCommand={sendCommand}
          storageItems={storageItems}
          inventoryItems={inventoryItems}
        />
      )}
    </div>
  );
}
