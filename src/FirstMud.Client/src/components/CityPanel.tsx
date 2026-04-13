import type { CityViewSnapshot, HomesteadBuilding, BuildingType } from '../types/game';

interface Props {
  cityView: CityViewSnapshot | null;
  onClose: () => void;
  sendCommand: (command: string, payload?: unknown) => void;
}

function buildingIcon(type: BuildingType): string {
  switch (type) {
    case 'Forge':          return '🔨';
    case 'Fletcher':       return '🏹';
    case 'Tannery':        return '🐂';
    case 'EnchantingTower':return '✨';
    case 'AlchemistHut':   return '⚗';
    case 'Stoneworker':    return '🪨';
    case 'Woodworker':     return '🌲';
    case 'MarketStall':    return '🛒';
    case 'Farm':           return '🌾';
    case 'Mine':           return '⛏';
    case 'Barracks':       return '⚔';
    case 'Library':        return '📚';
    case 'Warehouse':      return '📦';
  }
}

function buildingColor(type: BuildingType): string {
  switch (type) {
    case 'Forge':
    case 'Fletcher':
    case 'Stoneworker':
    case 'Woodworker':     return '#ff8844';
    case 'Tannery':        return '#cc8844';
    case 'EnchantingTower':
    case 'AlchemistHut':   return '#cc88ff';
    case 'MarketStall':    return '#ffcc44';
    case 'Farm':           return '#88cc44';
    case 'Mine':           return '#888888';
    case 'Barracks':       return '#cc4444';
    case 'Library':        return '#44aacc';
    case 'Warehouse':      return '#aaaaaa';
  }
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

function BuildingRow({ building }: { building: HomesteadBuilding }) {
  const color = buildingColor(building.type);
  const icon = buildingIcon(building.type);
  const tierStr = '★'.repeat(building.tier);

  return (
    <div style={{ marginBottom: '8px', borderLeft: `3px solid ${color}`, paddingLeft: '8px' }}>
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
      {building.isConstructed ? (
        <div style={{ fontSize: '11px', color: '#aaaaaa', marginTop: '2px' }}>
          {building.assignedCompanionName
            ? <span>Worker: <span style={{ color: '#00ccff' }}>{building.assignedCompanionName}</span></span>
            : <span style={{ color: '#888888' }}>No worker assigned — open Companions [B] to assign</span>
          }
          <span style={{ color: '#888888' }}>  Pos: ({building.gridX},{building.gridY})</span>
        </div>
      ) : (
        <div style={{ fontSize: '11px', marginTop: '2px' }}>
          <ProgressBar pct={building.constructionProgress} />
          {building.assignedCompanionName
            ? <span style={{ color: '#aaaaaa', marginLeft: '8px' }}>Builder: <span style={{ color: '#00ccff' }}>{building.assignedCompanionName}</span></span>
            : <span style={{ color: '#888888', marginLeft: '8px' }}>No builder — use [B] to assign a Crafter companion</span>
          }
        </div>
      )}
    </div>
  );
}

export default function CityPanel({ cityView, onClose, sendCommand }: Props) {
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
    width: '520px',
    maxHeight: '80vh',
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

  return (
    <div style={panelStyle}>
      <div style={headerStyle}>
        <span style={{ color: '#cc8844', letterSpacing: '0.15em' }}>
          HOMESTEAD CITY — {cityView.homesteadName}
        </span>
        <button onClick={onClose} style={{ background: 'none', border: 'none', color: '#888', cursor: 'pointer', fontSize: '16px' }}>✕</button>
      </div>

      <div style={{ ...bodyStyle, borderBottom: '1px solid #1a1a1a', color: '#aaaaaa', fontSize: '12px', paddingTop: '8px', paddingBottom: '8px' }}>
        <span style={{ color: '#ffcc00' }}>{cityView.constructedCount}</span> buildings complete
        {' · '}
        <span style={{ color: '#ff8800' }}>{underConstruction.length}</span> under construction
        {' · '}
        <span style={{ color: '#00ccff' }}>{assignedCount}</span> companions working
      </div>

      {constructed.length > 0 && (
        <>
          <div style={sectionStyle}>Completed Buildings</div>
          <div style={bodyStyle}>
            {constructed.map(b => <BuildingRow key={b.id} building={b} />)}
          </div>
        </>
      )}

      {underConstruction.length > 0 && (
        <>
          <div style={sectionStyle}>Under Construction</div>
          <div style={bodyStyle}>
            {underConstruction.map(b => <BuildingRow key={b.id} building={b} />)}
          </div>
        </>
      )}

      {cityView.buildings.length === 0 && (
        <div style={{ padding: '14px', color: '#888888', fontSize: '12px' }}>
          No buildings yet. Portal home [P] to seed starter buildings, then use the place building command.
        </div>
      )}

      <div style={{ ...sectionStyle, marginTop: '12px' }}>Actions</div>
      <div style={{ padding: '4px 14px 14px', display: 'flex', gap: '8px', flexWrap: 'wrap' }}>
        <button
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
        <span style={{ color: '#555555', fontSize: '11px', alignSelf: 'center' }}>
          Place buildings via chat: /placebuilding Forge 0 2
        </span>
      </div>
    </div>
  );
}
