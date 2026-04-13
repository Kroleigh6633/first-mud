import type { PlayerState, WeaveState, ReputationTier, ZoneTile, EquipmentSlots, CompanionState, MagicElement, CompanionType } from '../types/game';
import { getBiome } from '../utils/biome';

interface Props {
  player: PlayerState | null;
  currentTile: ZoneTile | null;
  equipment?: EquipmentSlots;
  companionRoster?: CompanionState[];
  visitedTileCount?: number;
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

/** Matches difficultyBorderColor in WorldMap.tsx — relative to player level */
function dangerColor(dangerLevel: number, playerLevel: number): string {
  const diff = dangerLevel - playerLevel;
  if (diff <= -3) return '#44aa44';
  if (diff === -2) return '#66bb44';
  if (diff === -1) return '#88cc44';
  if (diff ===  0) return '#cccc44';
  if (diff ===  1) return '#ddaa33';
  if (diff ===  2) return '#dd7722';
  if (diff ===  3) return '#cc4422';
  if (diff ===  4) return '#cc2222';
  if (diff ===  5) return '#881111';
  return '#440808';
}

function difficultyLabel(dangerLevel: number, playerLevel: number): string {
  const diff = dangerLevel - playerLevel;
  if (diff <= -3) return 'trivial';
  if (diff <= -1) return 'easy';
  if (diff ===  0) return 'fair';
  if (diff <=  2) return 'challenging';
  if (diff <=  4) return 'dangerous';
  return 'deadly';
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

const LAYER_THRESHOLDS: Record<CompanionType, number[]> = {
  Wildfolk:         [0, 200, 500, 1000, 2000, 4000],
  HiredHero:        [0, 200, 600, 1200, 2500, 5000],
  CapturedMonster:  [0, 150, 400, 900,  1800, 3600],
  ArdweldConstruct: [0, 500, 1500, 3000, 6000, 12000],
  BoundShade:       [0, 300, 700, 1500, 3000, 6000],
};

function BondStars({ layer }: { layer: number }) {
  return (
    <span>
      {Array.from({ length: 6 }, (_, i) => (
        <span key={i} style={{ color: i < layer ? '#ffcc00' : '#222222', fontSize: '10px' }}>★</span>
      ))}
    </span>
  );
}

function companionStatusLabel(
  companion: CompanionState,
  activeCompanionIds: string[],
): { label: string; color: string } {
  // A companion is truly "active" only if its ID appears in the player's
  // 3-slot combat party list — not just because isActive is set on the entity.
  if (activeCompanionIds.includes(companion.id)) {
    return { label: 'ACTIVE (adventuring)', color: '#00ff41' };
  }
  if (companion.assignedDuty) {
    const dutyColors: Record<string, string> = {
      Guard:     '#ccaa44',
      Harvester: '#ccaa44',
      Salvager:  '#ccaa44',
      Crafter:   '#ccaa44',
    };
    const color = dutyColors[companion.assignedDuty] ?? '#ccaa44';
    return { label: `${companion.assignedDuty.toUpperCase()} (homestead)`, color };
  }
  return { label: 'IDLE', color: '#888888' };
}

function CompanionRow({
  companion,
  activeCompanionIds,
}: {
  companion: CompanionState;
  activeCompanionIds: string[];
}) {
  const isDrifting = companion.driftAccumulator >= 30;
  const isDanger   = companion.driftAccumulator >= 40;
  const status = companionStatusLabel(companion, activeCompanionIds);

  const layerBarContent = (() => {
    if (companion.currentLayer >= 6) {
      return <span style={{ color: '#ffcc00' }}>MAX ✦</span>;
    }
    const thresholds = LAYER_THRESHOLDS[companion.type] ?? LAYER_THRESHOLDS.Wildfolk;
    const current = thresholds[companion.currentLayer - 1] ?? 0;
    const next    = thresholds[companion.currentLayer] ?? 1;
    const gained  = companion.usageCounter - current;
    const needed  = next - current;
    return (
      <>
        <Bar current={gained} max={needed} width={8} color="#ccaa00" />
        <span style={{ color: '#888888' }}> {gained}/{needed}</span>
      </>
    );
  })();

  return (
    <div style={{ marginBottom: '4px', fontSize: '11px' }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: '4px', flexWrap: 'wrap' }}>
        <span style={{ color: companionElementColor(companion.element) }}>
          [{companionTypeIcon(companion.type)}]
        </span>
        <span style={{ color: '#00ccff' }}>{companion.name}</span>
        <BondStars layer={companion.currentLayer} />
        <span style={{ color: status.color, fontSize: '10px' }}>{status.label}</span>
        {isDrifting && (
          <span style={{ color: isDanger ? '#ff4444' : '#ff8800', fontSize: '10px' }}>
            {isDanger ? '⚠ DANGER' : '⚠ drifting'}
          </span>
        )}
      </div>
      <div style={{ color: '#555555', paddingLeft: '4px' }}>
        Lv.{companion.level} {companion.type} · Bond {companion.currentLayer}
      </div>
      <div style={{ paddingLeft: '4px' }}>
        <span style={{ color: '#888888' }}>Bond: </span>
        {layerBarContent}
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
        {(() => {
          const xpForNext = player.level * player.level * 100;
          return (
            <>
              <span style={{ color: '#888888' }}>XP: </span>
              <Bar current={player.experience} max={xpForNext} width={12} color="#00ccff" />
              <span style={{ color: '#888888' }}> {player.experience}/{xpForNext}</span>
            </>
          );
        })()}
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
            <span style={{ color: dangerColor(currentTile.dangerLevel, player.level) }}>
              {currentTile.dangerLevel}/10
            </span>
            <span style={{ color: dangerColor(currentTile.dangerLevel, player.level), marginLeft: '6px', fontSize: '11px' }}>
              ({difficultyLabel(currentTile.dangerLevel, player.level)})
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
                  <span style={{ color: dangerColor(biome.dangerEstimate, player.level) }}>
                    {biome.dangerEstimate}/10
                  </span>
                  <span style={{ color: dangerColor(biome.dangerEstimate, player.level), marginLeft: '6px', fontSize: '11px' }}>
                    ({difficultyLabel(biome.dangerEstimate, player.level)})
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

      <div style={sectionHeaderStyle}>
        Companions [{companionRoster.length > 0 ? companionRoster.length : (player.activeCompanions?.length ?? 0)}]
        {' '}<span style={{ color: '#555555', fontWeight: 'normal' }}>[B]</span>
      </div>
      {companionRoster.length > 0 ? (
        companionRoster
          .slice()
          .sort((a, b) => {
            // Active (in party) first, then homestead duty, then idle; within group sort by layer desc
            const rank = (c: CompanionState) =>
              player.activeCompanionIds.includes(c.id) ? 0 : c.assignedDuty ? 1 : 2;
            const r = rank(a) - rank(b);
            return r !== 0 ? r : b.currentLayer - a.currentLayer;
          })
          .map(companion => (
            <CompanionRow
              key={companion.id}
              companion={companion}
              activeCompanionIds={player.activeCompanionIds}
            />
          ))
      ) : (player.activeCompanions?.length ?? 0) === 0 ? (
        <div style={{ color: '#888888', fontSize: '11px' }}>No companions</div>
      ) : (
        player.activeCompanions!
          .slice()
          .sort((a, b) => b.currentLayer - a.currentLayer)
          .map(companion => (
            <CompanionRow
              key={companion.id}
              companion={companion}
              activeCompanionIds={player.activeCompanionIds}
            />
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
