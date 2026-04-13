import React, { useState, useCallback } from 'react';
import type {
  CityViewSnapshot,
  HomesteadBuilding,
  BuildingType,
  CompanionState,
  CompanionType,
  HomesteadDuty,
  StorageItem,
  InventoryItem,
} from '../types/game';

// ─── Constants mirroring server-side data ────────────────────────────────────

const BUILDING_DUTY: Record<BuildingType, HomesteadDuty> = {
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
  Hut:            'Guard',
};

const CONSTRUCTION_COST: Record<BuildingType, { material: string; qty: number }[]> = {
  Forge:          [{ material: 'Wood', qty: 20 }, { material: 'Stone', qty: 30 }, { material: 'Iron Ore', qty: 10 }],
  Fletcher:       [{ material: 'Wood', qty: 15 }, { material: 'Stone', qty: 10 }],
  Tannery:        [{ material: 'Wood', qty: 15 }, { material: 'Leather', qty: 10 }],
  EnchantingTower:[{ material: 'Stone', qty: 30 }, { material: 'Wood', qty: 10 }],
  AlchemistHut:   [{ material: 'Wood', qty: 10 }, { material: 'Herbs', qty: 15 }],
  Stoneworker:    [{ material: 'Stone', qty: 25 }, { material: 'Wood', qty: 10 }],
  Woodworker:     [{ material: 'Wood', qty: 25 }, { material: 'Stone', qty: 10 }],
  MarketStall:    [{ material: 'Wood', qty: 10 }, { material: 'Stone', qty: 5 }],
  Farm:           [{ material: 'Wood', qty: 5 },  { material: 'Stone', qty: 5 }],
  Mine:           [{ material: 'Stone', qty: 20 }, { material: 'Iron Ore', qty: 15 }, { material: 'Wood', qty: 10 }],
  Barracks:       [{ material: 'Stone', qty: 25 }, { material: 'Wood', qty: 15 }],
  Library:        [{ material: 'Wood', qty: 20 }, { material: 'Stone', qty: 15 }],
  Warehouse:      [{ material: 'Wood', qty: 25 }, { material: 'Stone', qty: 15 }],
  Hut:            [{ material: 'Wood', qty: 5 },  { material: 'Stone', qty: 3 }],
};

const ALL_BUILDING_TYPES: BuildingType[] = [
  'Forge', 'Fletcher', 'Tannery', 'EnchantingTower', 'AlchemistHut',
  'Stoneworker', 'Woodworker', 'MarketStall', 'Farm', 'Mine',
  'Barracks', 'Library', 'Warehouse', 'Hut',
];

// Aptitude per companion type per duty (mirrors CompanionPanel)
const APTITUDE: Record<CompanionType, Record<HomesteadDuty, number>> = {
  Wildfolk:         { Harvester: 3, Salvager: 1, Guard: 2, Crafter: 2 },
  CapturedMonster:  { Harvester: 2, Salvager: 1, Guard: 3, Crafter: 1 },
  ArdweldConstruct: { Harvester: 1, Salvager: 3, Guard: 3, Crafter: 3 },
  HiredHero:        { Harvester: 2, Salvager: 2, Guard: 2, Crafter: 2 },
  BoundShade:       { Harvester: 2, Salvager: 2, Guard: 2, Crafter: 1 },
};

// ─── Grid helpers ─────────────────────────────────────────────────────────────

/**
 * Returns the next available grid position not occupied by any existing building.
 * Searches outward from (0,0) in a spiral pattern.
 */
function nextAvailablePosition(buildings: HomesteadBuilding[]): { x: number; y: number } {
  const occupied = new Set(buildings.map(b => `${b.gridX},${b.gridY}`));
  const candidates: [number, number][] = [];
  for (let r = 0; r <= 6; r++) {
    for (let x = -r; x <= r; x++) {
      for (let y = -r; y <= r; y++) {
        if (Math.abs(x) === r || Math.abs(y) === r) {
          candidates.push([x, y]);
        }
      }
    }
  }
  for (const [x, y] of candidates) {
    if (!occupied.has(`${x},${y}`)) return { x, y };
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
  }
}

function AptitudeStars({ count }: { count: number }) {
  return (
    <span>
      {Array.from({ length: 3 }, (_, i) => (
        <span key={i} style={{ color: i < count ? '#ffcc00' : '#333333', fontSize: '10px' }}>★</span>
      ))}
    </span>
  );
}

function ProgressBar({ pct }: { pct: number }) {
  const filled = Math.round(pct / 5); // 20 slots
  return (
    <span>
      <span style={{ color: '#00ff41' }}>{'█'.repeat(filled)}</span>
      <span style={{ color: '#222222' }}>{'█'.repeat(20 - filled)}</span>
      <span style={{ color: '#888888' }}> {pct}%</span>
    </span>
  );
}

// ─── Props ────────────────────────────────────────────────────────────────────

interface Props {
  cityView: CityViewSnapshot | null;
  onClose: () => void;
  sendCommand: (command: string, payload?: unknown) => void;
  companionRoster?: CompanionState[];
  activeCompanionIds?: string[];
  storageItems?: StorageItem[];
  inventoryItems?: InventoryItem[];
}

// ─── BuildingRow ──────────────────────────────────────────────────────────────

interface BuildingRowProps {
  building: HomesteadBuilding;
  availableCompanions: CompanionState[];
  sendCommand: (command: string, payload?: unknown) => void;
}

function BuildingRow({ building, availableCompanions, sendCommand }: BuildingRowProps) {
  const [selectedCompanionId, setSelectedCompanionId] = useState<string>('');
  const color = buildingColor(building.type);
  const icon = buildingIcon(building.type);
  const tierStr = '★'.repeat(building.tier);
  const duty = BUILDING_DUTY[building.type];

  // Sort available companions by aptitude for this building's duty (best first)
  const sorted = [...availableCompanions].sort((a, b) => {
    const aApt = APTITUDE[a.type]?.[duty] ?? 1;
    const bApt = APTITUDE[b.type]?.[duty] ?? 1;
    return bApt - aApt;
  });

  const handleAssign = () => {
    if (!selectedCompanionId) return;
    sendCommand('assignbuilder', { companionId: selectedCompanionId, buildingId: building.id });
    setSelectedCompanionId('');
  };

  const handleUnassign = () => {
    sendCommand('unassignbuilder', { buildingId: building.id });
  };

  return (
    <div style={{ marginBottom: '10px', borderLeft: `3px solid ${color}`, paddingLeft: '8px' }}>
      {/* Header row */}
      <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
        <span style={{ fontSize: '14px' }}>{icon}</span>
        <span style={{ color, fontWeight: 'bold' }}>{building.type.toUpperCase()}</span>
        <span style={{ color: '#ffcc00', fontSize: '10px' }}>{tierStr}</span>
        {!building.isConstructed && (
          <span style={{ color: '#ff8800', fontSize: '10px', marginLeft: 'auto' }}>
            Under Construction
          </span>
        )}
      </div>

      {/* Constructed — show worker info or assign dropdown */}
      {building.isConstructed ? (
        <div style={{ fontSize: '11px', marginTop: '4px' }}>
          {building.assignedCompanionName ? (
            <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
              <span style={{ color: '#aaaaaa' }}>
                Worker: <span style={{ color: '#00ccff' }}>{building.assignedCompanionName}</span>
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
          ) : (
            <div style={{ marginTop: '4px' }}>
              <div style={{ color: '#888888', marginBottom: '4px' }}>
                No worker — assign a companion:
              </div>
              {sorted.length === 0 ? (
                <div style={{ color: '#555555', fontSize: '10px' }}>
                  No available companions (all active or already assigned)
                </div>
              ) : (
                <div style={{ display: 'flex', alignItems: 'center', gap: '6px', flexWrap: 'wrap' }}>
                  <select
                    value={selectedCompanionId}
                    onChange={e => setSelectedCompanionId(e.target.value)}
                    style={selectStyle}
                    aria-label={`Select companion for ${building.type}`}
                  >
                    <option value="">-- Select companion --</option>
                    {sorted.map(c => {
                      const apt = APTITUDE[c.type]?.[duty] ?? 1;
                      const stars = '★'.repeat(apt) + '☆'.repeat(3 - apt);
                      return (
                        <option key={c.id} value={c.id}>
                          {c.name} ({c.type}) {duty}: {stars}
                        </option>
                      );
                    })}
                  </select>
                  {selectedCompanionId && (
                    <>
                      <span style={{ color: '#888888', fontSize: '10px' }}>
                        {duty}: <AptitudeStars count={APTITUDE[sorted.find(c => c.id === selectedCompanionId)?.type ?? 'HiredHero']?.[duty] ?? 1} />
                      </span>
                      <button
                        type="button"
                        onClick={handleAssign}
                        style={assignBtnStyle}
                      >
                        Assign
                      </button>
                    </>
                  )}
                </div>
              )}
            </div>
          )}
          <span style={{ color: '#444444', fontSize: '10px', display: 'block', marginTop: '2px' }}>
            Pos: ({building.gridX},{building.gridY})
          </span>
        </div>
      ) : (
        /* Under construction — show progress + builder */
        <div style={{ fontSize: '11px', marginTop: '2px' }}>
          <ProgressBar pct={building.constructionProgress} />
          {building.assignedCompanionName ? (
            <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginTop: '3px' }}>
              <span style={{ color: '#aaaaaa' }}>
                Builder: <span style={{ color: '#00ccff' }}>{building.assignedCompanionName}</span>
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
          ) : (
            <div style={{ marginTop: '4px' }}>
              <div style={{ color: '#888888', marginBottom: '4px' }}>
                No builder — assign a Crafter companion:
              </div>
              {sorted.length === 0 ? (
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
                    {sorted.map(c => {
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
  sendCommand: (command: string, payload?: unknown) => void;
}

// Building types that can be placed multiple times (e.g. housing)
const MULTI_PLACE_TYPES = new Set<BuildingType>(['Hut']);

function PlaceBuildingSection({ buildings, storageItems, inventoryItems, sendCommand }: PlaceBuildingSectionProps) {
  const placedTypes = new Set(buildings.map(b => b.type));
  // Multi-place types are always available; single-place types only if not yet placed
  const availableTypes = ALL_BUILDING_TYPES.filter(t => MULTI_PLACE_TYPES.has(t) || !placedTypes.has(t));
  const [selectedType, setSelectedType] = useState<BuildingType | ''>('');

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
    sendCommand('placebuilding', { buildingType: selectedType, gridX: pos.x, gridY: pos.y });
    setSelectedType('');
  };

  return (
    <div style={{ fontSize: '11px' }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap', marginBottom: '6px' }}>
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
                <span style={{ color: ok ? '#00cc88' : '#cc4444' }}>
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
    width: '560px',
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

  const sectionStyle: React.CSSProperties = {
    color: '#888888',
    fontSize: '11px',
    letterSpacing: '0.12em',
    textTransform: 'uppercase',
    margin: '10px 14px 4px',
    borderBottom: '1px solid #1a1a1a',
    paddingBottom: '3px',
  };

  const bodyStyle: React.CSSProperties = {
    padding: '4px 14px 14px',
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

  const constructed = cityView.buildings.filter(b => b.isConstructed);
  const underConstruction = cityView.buildings.filter(b => !b.isConstructed);
  const assignedCount = cityView.buildings.filter(b => b.assignedCompanionName).length;

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

  // Auto-assign all: for each building without a worker, pick best-aptitude available companion
  const handleAutoAssignAll = useCallback(() => {
    // Track which companions we've already "used" in this batch
    const usedIds = new Set<string>();
    const buildingsNeedingWorkers = cityView.buildings.filter(b => !b.assignedCompanionName);

    for (const building of buildingsNeedingWorkers) {
      const duty = building.isConstructed ? BUILDING_DUTY[building.type] : 'Crafter' as HomesteadDuty;
      const best = availableCompanions
        .filter(c => !usedIds.has(c.id))
        .sort((a, b) => {
          const aApt = APTITUDE[a.type]?.[duty] ?? 1;
          const bApt = APTITUDE[b.type]?.[duty] ?? 1;
          return bApt - aApt;
        })[0];

      if (best) {
        sendCommand('assignbuilder', { companionId: best.id, buildingId: building.id });
        usedIds.add(best.id);
      }
    }
  }, [cityView.buildings, availableCompanions, sendCommand]);

  // One-click master action: server seeds all missing buildings then auto-staffs empty slots
  const handleBuildStaffEverything = useCallback(() => {
    sendCommand('buildstaffeverything', null);
  }, [sendCommand]);

  const unassignedBuildings = cityView.buildings.filter(b => !b.assignedCompanionName);
  const canAutoAssign = unassignedBuildings.length > 0 && availableCompanions.length > 0;

  return (
    <div style={panelStyle}>
      <div style={headerStyle}>
        <span style={{ color: '#cc8844', letterSpacing: '0.15em' }}>
          HOMESTEAD CITY — {cityView.homesteadName}
        </span>
        <button onClick={onClose} style={{ background: 'none', border: 'none', color: '#888', cursor: 'pointer', fontSize: '16px' }}>✕</button>
      </div>

      {/* Summary row */}
      <div style={{ ...bodyStyle, borderBottom: '1px solid #1a1a1a', color: '#aaaaaa', fontSize: '12px', paddingTop: '8px', paddingBottom: '8px' }}>
        <span style={{ color: '#ffcc00' }}>{cityView.constructedCount}</span> buildings complete
        {' · '}
        <span style={{ color: '#ff8800' }}>{underConstruction.length}</span> under construction
        {' · '}
        <span style={{ color: '#00ccff' }}>{assignedCount}</span> companions working
        {' · '}
        <span style={{ color: availableCompanions.length > 0 ? '#00cc88' : '#555555' }}>
          {availableCompanions.length} available
        </span>
      </div>

      {/* Master action + Auto-assign all + Refresh */}
      <div style={{ padding: '6px 14px 8px', borderBottom: '1px solid #1a1a1a', display: 'flex', gap: '8px', alignItems: 'center', flexWrap: 'wrap' }}>
        {/* Build & Staff Everything — the one-click master button */}
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
          onClick={() => { sendCommand('viewcity', null); }}
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
        {unassignedBuildings.length > 0 && availableCompanions.length === 0 && (
          <span style={{ color: '#555555', fontSize: '10px' }}>
            All companions active or on duty — deactivate one to assign
          </span>
        )}
      </div>

      {/* Completed buildings */}
      {constructed.length > 0 && (
        <>
          <div style={sectionStyle}>Completed Buildings</div>
          <div style={bodyStyle}>
            {constructed.map(b => (
              <BuildingRow
                key={b.id}
                building={b}
                availableCompanions={availableCompanions}
                sendCommand={sendCommand}
              />
            ))}
          </div>
        </>
      )}

      {/* Under construction */}
      {underConstruction.length > 0 && (
        <>
          <div style={sectionStyle}>Under Construction</div>
          <div style={bodyStyle}>
            {underConstruction.map(b => (
              <BuildingRow
                key={b.id}
                building={b}
                availableCompanions={availableCompanions}
                sendCommand={sendCommand}
              />
            ))}
          </div>
        </>
      )}

      {cityView.buildings.length === 0 && (
        <div style={{ padding: '14px', color: '#888888', fontSize: '12px' }}>
          No buildings yet. Portal home [P] to seed starter buildings.
        </div>
      )}

      {/* Place new building */}
      <div style={sectionStyle}>Place New Building</div>
      <div style={bodyStyle}>
        <PlaceBuildingSection
          buildings={cityView.buildings}
          storageItems={storageItems}
          inventoryItems={inventoryItems}
          sendCommand={sendCommand}
        />
      </div>
    </div>
  );
}
