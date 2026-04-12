import type { PlayerState, WeaveState, ReputationTier, ZoneTile } from '../types/game';

interface Props {
  player: PlayerState | null;
  currentTile: ZoneTile | null;
}

function Bar({ current, max, width, color }: { current: number; max: number; width: number; color: string }) {
  const filled = Math.round((current / Math.max(max, 1)) * width);
  return (
    <>
      <span style={{ color }}>{'\u2588'.repeat(filled)}</span>
      <span style={{ color: '#222' }}>{'\u2588'.repeat(width - filled)}</span>
    </>
  );
}

function weaveColor(state: WeaveState): string {
  switch (state) {
    case 'Full':      return '#00ff41';
    case 'Steady':    return '#00cc33';
    case 'Strained':  return '#ffcc00';
    case 'Critical':  return '#ff8800';
    case 'Depleted':  return '#ff4444';
  }
}

function reputationColor(tier: ReputationTier): string {
  switch (tier) {
    case 'Hostile':  return '#ff4444';
    case 'Wary':     return '#ff8800';
    case 'Unknown':  return '#888888';
    case 'Known':    return '#aaaaaa';
    case 'Trusted':  return '#00ccff';
    case 'Honored':  return '#00ff41';
    case 'Bound':    return '#cc88ff';
  }
}

const FACTIONS = [
  'House Caervorn',
  'Thornwood Covens',
  'The Compact',
  'Gravenguard',
  'Fairgean',
  'Golvari',
  'Ashen Court',
];

function dangerColor(level: number): string {
  if (level <= 3) return '#00bb33';
  if (level <= 6) return '#ccaa00';
  return '#cc2200';
}

export default function StatusPanel({ player, currentTile }: Props) {
  const panelStyle: React.CSSProperties = {
    padding: '8px 10px',
    fontFamily: 'monospace',
    fontSize: '13px',
    color: '#00ff41',
    background: '#0d0d0d',
    overflowY: 'auto',
    height: '100%',
    boxSizing: 'border-box',
  };

  const sectionHeaderStyle: React.CSSProperties = {
    color: '#888888',
    borderBottom: '1px solid #1a1a1a',
    marginBottom: '4px',
    marginTop: '10px',
    paddingBottom: '2px',
    fontSize: '11px',
    letterSpacing: '0.1em',
    textTransform: 'uppercase',
  };

  if (!player) {
    return (
      <div style={panelStyle}>
        <div style={sectionHeaderStyle}>Status</div>
        <div style={{ color: '#888888' }}>Awaiting connection...</div>
      </div>
    );
  }

  // Bars are rendered inline as JSX — see Bar component above.

  return (
    <div style={panelStyle}>
      <div style={sectionHeaderStyle}>Player</div>
      <div style={{ marginBottom: '4px' }}>
        [{player.name}] Lv.{player.level} Rider
      </div>
      <div style={{ marginBottom: '2px' }}>
        <span style={{ color: '#888888' }}>HP: </span>
        <Bar current={player.currentHp} max={player.maxHp} width={10} color="#ff4444" />
        <span style={{ color: '#888888' }}> {player.currentHp}/{player.maxHp}</span>
      </div>
      <div style={{ marginBottom: '2px' }}>
        <span style={{ color: '#888888' }}>Weave </span>
        <span style={{ color: '#555555', fontSize: '10px' }}>(mana)</span>
        <span style={{ color: '#888888' }}>: </span>
        <Bar current={player.weavePercent} max={100} width={10} color={weaveColor(player.weaveState)} />
        <span style={{ color: '#888888' }}> {player.weavePercent}%</span>
      </div>
      <div style={{ marginBottom: '2px' }}>
        <span style={{ color: '#888888' }}>World: </span>
        <span>{player.world}</span>
      </div>
      <div style={{ marginBottom: '2px' }} data-testid="player-pos">
        <span style={{ color: '#888888' }}>Pos: </span>
        <span>{player.x},{player.y}</span>
      </div>

      <div style={sectionHeaderStyle}>Location</div>
      {currentTile ? (
        <div data-testid="current-tile">
          <div style={{ marginBottom: '2px' }}>
            <span style={{ color: '#00ff41', fontWeight: 'bold' }}>{currentTile.name}</span>
            {' '}
            <span style={{ color: '#888888' }}>[{currentTile.asciiSymbol}]</span>
          </div>
          <div style={{ marginBottom: '2px' }}>
            <span style={{ color: '#888888' }}>Danger: </span>
            <span style={{ color: dangerColor(currentTile.dangerLevel) }}>
              {currentTile.dangerLevel}/10
            </span>
            {currentTile.isPortalZone && (
              <span style={{ color: '#cc88ff', marginLeft: '8px' }}>◈ portal</span>
            )}
          </div>
          <div style={{ color: '#aaaaaa', fontSize: '11px', marginTop: '4px', lineHeight: '1.35' }}>
            {currentTile.description}
          </div>
        </div>
      ) : (
        <div data-testid="current-tile-empty" style={{ color: '#666666', fontStyle: 'italic' }}>
          Open country. Nothing of note here.
        </div>
      )}

      <div style={sectionHeaderStyle}>Factions</div>
      {FACTIONS.map(faction => {
        const tier: ReputationTier = (player.factionTiers[faction] as ReputationTier) ?? 'Unknown';
        return (
          <div key={faction} style={{ marginBottom: '2px', display: 'flex', justifyContent: 'space-between' }}>
            <span style={{ color: '#aaaaaa', fontSize: '11px' }}>{faction}</span>
            <span style={{ color: reputationColor(tier), fontSize: '11px' }}>{tier}</span>
          </div>
        );
      })}

      <div style={sectionHeaderStyle}>Companions</div>
      {player.activeCompanionIds.length === 0 ? (
        <div style={{ color: '#888888', fontSize: '11px' }}>None</div>
      ) : (
        player.activeCompanionIds.map(id => (
          <div key={id} style={{ color: '#00ccff', fontSize: '11px' }}>{id}</div>
        ))
      )}

      <div style={sectionHeaderStyle}>Portals</div>
      {player.unlockedPortals.length === 0 ? (
        <div style={{ color: '#888888', fontSize: '11px' }}>None unlocked</div>
      ) : (
        player.unlockedPortals.map(portal => (
          <div key={portal} style={{ color: '#cc88ff', fontSize: '11px' }}>{portal}</div>
        ))
      )}
    </div>
  );
}
