import type { PlayerState, WeaveState, ReputationTier, ZoneTile, EquipmentSlots, CompanionState, MagicElement, CompanionType } from '../types/game';
import { getBiome } from '../utils/biome';

interface Props {
  player: PlayerState | null;
  currentTile: ZoneTile | null;
  equipment?: EquipmentSlots;
  companionRoster?: CompanionState[];
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

function HpBar({ currentHp, baseMaxHp, effectiveMaxHp, width }: { currentHp: number; baseMaxHp: number; effectiveMaxHp: number; width: number }) {
  const totalMax = Math.max(effectiveMaxHp, 1);
  const solidFilled = Math.round((Math.min(currentHp, baseMaxHp) / totalMax) * width);
  const gearZone = Math.round(((effectiveMaxHp - baseMaxHp) / totalMax) * width);
  const emptyZone = width - solidFilled - gearZone;
  return (
    <>
      <span style={{ color: '#ff4444' }}>{'\u2588'.repeat(solidFilled)}</span>
      <span style={{ color: '#222' }}>{'\u2588'.repeat(Math.max(emptyZone, 0))}</span>
      <span style={{ color: '#442222' }}>{'\u2588'.repeat(gearZone)}</span>
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

function companionElementColor(element: MagicElement): string {
  switch (element) {
    case 'Fire':   return '#ff6633';
    case 'Water':  return '#33aaff';
    case 'Earth':  return '#88bb44';
    case 'Air':    return '#ccddff';
    case 'Aether': return '#cc88ff';
  }
}

function companionTypeIcon(type: CompanionType): string {
  switch (type) {
    case 'Wildfolk':         return '~';
    case 'CapturedMonster':  return '#';
    case 'ArdweldConstruct': return '[';
    case 'HiredHero':        return 'H';
    case 'BoundShade':       return '@';
  }
}

function LayerStars({ layer }: { layer: number }) {
  return (
    <span>
      {Array.from({ length: 6 }, (_, i) => (
        <span key={i} style={{ color: i < layer ? '#ffcc00' : '#222222', fontSize: '10px' }}>★</span>
      ))}
    </span>
  );
}

function CompanionRow({ companion }: { companion: CompanionState }) {
  const isDrifting = companion.driftAccumulator >= 30;
  const isDanger   = companion.driftAccumulator >= 40;
  return (
    <div style={{ marginBottom: '4px', fontSize: '11px' }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: '4px' }}>
        <span style={{ color: companionElementColor(companion.element) }}>
          [{companionTypeIcon(companion.type)}]
        </span>
        <span style={{ color: '#00ccff' }}>{companion.name}</span>
        <LayerStars layer={companion.currentLayer} />
        {isDrifting && (
          <span style={{ color: isDanger ? '#ff4444' : '#ff8800', fontSize: '10px' }}>
            {isDanger ? '⚠ DANGER' : '⚠ Drifting!'}
          </span>
        )}
      </div>
      <div style={{ color: '#555555', paddingLeft: '4px' }}>
        Lv.{companion.level} {companion.type} · L{companion.currentLayer}
      </div>
    </div>
  );
}

export default function StatusPanel({ player, currentTile, equipment, companionRoster = [] }: Props) {
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
        {(() => {
          const base = player.maxHp;
          const effective = player.effectiveMaxHp ?? base;
          const gearBonus = effective - base;
          return (
            <>
              <HpBar currentHp={player.currentHp} baseMaxHp={base} effectiveMaxHp={effective} width={10} />
              <span style={{ color: '#888888' }}> {player.currentHp}/{base}</span>
              {gearBonus > 0 && (
                <span style={{ color: '#aa4444' }}> (+{gearBonus} gear)</span>
              )}
            </>
          );
        })()}
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
        <div data-testid="current-tile-empty">
          {(() => {
            const biome = getBiome(player.x, player.y);
            return (
              <>
                <div style={{ marginBottom: '2px' }}>
                  <span style={{ color: '#00ff41', fontWeight: 'bold' }}>{biome.name}</span>
                  {' '}
                  <span style={{ color: '#666666', fontStyle: 'italic' }}>[wilderness]</span>
                </div>
                <div style={{ marginBottom: '2px' }}>
                  <span style={{ color: '#888888' }}>Danger: </span>
                  <span style={{ color: dangerColor(biome.dangerEstimate) }}>
                    {biome.dangerEstimate}/10
                  </span>
                </div>
              </>
            );
          })()}
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

      <div style={sectionHeaderStyle}>Equipment</div>
      {[
        ['Melee', equipment?.meleeWeaponName],
        ['Ranged', equipment?.rangedWeaponName],
        ['Focus', equipment?.focusName],
        ['Head', equipment?.headName],
        ['Chest', equipment?.chestName],
        ['Legs', equipment?.legsName],
        ['Hands', equipment?.handsName],
        ['Feet', equipment?.feetName],
        ['Acc', equipment?.accessoryName],
      ].map(([label, name]) => (
        <div key={label as string} style={{ fontSize: '11px', marginBottom: '1px', display: 'flex', justifyContent: 'space-between' }}>
          <span style={{ color: '#888888', minWidth: '46px' }}>{label}:</span>
          {name ? (
            <span style={{ color: '#ffcc00', textAlign: 'right', flex: 1 }}>{name as string}</span>
          ) : (
            <span style={{ color: '#333333', textAlign: 'right', flex: 1 }}>—</span>
          )}
        </div>
      ))}

      <div style={sectionHeaderStyle}>Companions <span style={{ color: '#555555', fontWeight: 'normal' }}>[B]</span></div>
      {(player.activeCompanions?.length ?? 0) === 0 ? (
        <div style={{ color: '#888888', fontSize: '11px' }}>
          {player.activeCompanionIds.length === 0 ? 'None active' : `${player.activeCompanionIds.length} active`}
        </div>
      ) : (
        player.activeCompanions!.map(companion => (
          <CompanionRow key={companion.id} companion={companion} />
        ))
      )}

      {/* Homestead companions */}
      {(() => {
        const homesteadCompanions = companionRoster.filter(c => c.assignedDuty && !c.isActive);
        if (homesteadCompanions.length === 0) return null;
        return (
          <div style={{ marginTop: '4px', fontSize: '11px' }}>
            <div style={{ color: '#555555', fontSize: '10px', marginBottom: '3px' }}>HOMESTEAD DUTY</div>
            {homesteadCompanions.map(c => (
              <div key={c.id} style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '2px' }}>
                <span style={{ color: '#aaaaaa' }}>{c.name}</span>
                <span style={{ color: '#ccaa44' }}>{c.assignedDuty}</span>
              </div>
            ))}
          </div>
        );
      })()}

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
