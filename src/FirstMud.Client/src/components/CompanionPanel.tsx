import React from 'react';
import type { CompanionState, CompanionType, MagicElement } from '../types/game';

interface Props {
  companions: CompanionState[];
  onActivate: (id: string) => void;
  onDeactivate: (id: string) => void;
  onClose: () => void;
}

// ---- Layer ability descriptions per type --------------------------------

const ABILITY_LABELS: Record<CompanionType, string[]> = {
  Wildfolk: [
    'L1: Elemental Touch (attack)',
    'L2: Nature Mend (heal ally)',
    'L3: Elemental Strike (medium attack)',
    'L4: Pack Bond (party buff)',
    'L5: Wild Revive (revive ally)',
    'L6: Elemental Storm (AOE ultimate)',
  ],
  CapturedMonster: [
    'L1: Claw (physical attack)',
    'L2: Elemental Breath (medium attack)',
    'L3: Frenzy (high damage)',
    'L4: Terrify (debuff enemy)',
    'L5: Devour (lifesteal)',
    'L6: Rampage (triple-hit ultimate)',
  ],
  ArdweldConstruct: [
    'L1: Shield Bash (low damage)',
    'L2: Protect (redirect damage to self)',
    'L3: Repair (self-heal)',
    'L4: Fortify (party buff)',
    'L5: Reflect (return damage)',
    'L6: Aegis (absorb hits ultimate)',
  ],
  HiredHero: [
    'L1: Sword Strike (physical)',
    'L2: Quick Shot (ranged)',
    'L3: Battle Cry (party buff)',
    'L4: Tactical Strike (targets weakest foe)',
    'L5: Rally (party heal)',
    'L6: Commander (extra actions ultimate)',
  ],
  BoundShade: [
    'L1: Sword Strike (physical)',
    'L2: Quick Shot (ranged)',
    'L3: Battle Cry (party buff)',
    'L4: Tactical Strike (targets weakest foe)',
    'L5: Rally (party heal)',
    'L6: Commander (extra actions ultimate)',
  ],
};

// ---- Element colours --------------------------------------------------------

function elementColor(element: MagicElement): string {
  switch (element) {
    case 'Fire':   return '#ff6633';
    case 'Water':  return '#33aaff';
    case 'Earth':  return '#88bb44';
    case 'Air':    return '#ccddff';
    case 'Aether': return '#cc88ff';
  }
}

// ---- Type icon ---------------------------------------------------------------

function typeIcon(type: CompanionType): string {
  switch (type) {
    case 'Wildfolk':         return '~';
    case 'CapturedMonster':  return '#';
    case 'ArdweldConstruct': return '[';
    case 'HiredHero':        return 'H';
    case 'BoundShade':       return '@';
  }
}

// ---- Layer stars -------------------------------------------------------------

function LayerStars({ layer }: { layer: number }) {
  return (
    <span>
      {Array.from({ length: 6 }, (_, i) => (
        <span key={i} style={{ color: i < layer ? '#ffcc00' : '#333333' }}>★</span>
      ))}
    </span>
  );
}

// ---- Drift progress ---------------------------------------------------------

const DRIFT_WARN_THRESHOLD = 30;

function driftLabel(drift: number): string | null {
  if (drift >= 40) return 'DANGER';
  if (drift >= DRIFT_WARN_THRESHOLD) return 'Drifting!';
  return null;
}

function driftColor(drift: number): string {
  if (drift >= 40) return '#ff4444';
  if (drift >= DRIFT_WARN_THRESHOLD) return '#ff8800';
  return '#00bb33';
}

// ---- Usage progress bar toward next layer ----------------------------------

const LAYER_THRESHOLDS: Record<CompanionType, number[]> = {
  Wildfolk:         [0, 200, 500, 1000, 2000, 4000],
  HiredHero:        [0, 200, 600, 1200, 2500, 5000],
  CapturedMonster:  [0, 150, 400, 900,  1800, 3600],
  ArdweldConstruct: [0, 500, 1500, 3000, 6000, 12000],
  BoundShade:       [0, 300, 700, 1500, 3000, 6000],
};

function UsageBar({ type, layer, usage }: { type: CompanionType; layer: number; usage: number }) {
  if (layer >= 6) {
    return <span style={{ color: '#ffcc00', fontSize: '10px' }}>MAX LAYER</span>;
  }
  const thresholds = LAYER_THRESHOLDS[type] ?? LAYER_THRESHOLDS.Wildfolk;
  const current = thresholds[layer - 1] ?? 0;
  const next    = thresholds[layer] ?? 1;
  const gained  = usage - current;
  const needed  = next - current;
  const pct     = Math.min(1, gained / Math.max(needed, 1));
  const filled  = Math.round(pct * 10);
  return (
    <span style={{ fontSize: '10px' }}>
      <span style={{ color: '#00bb33' }}>{'|'.repeat(filled)}</span>
      <span style={{ color: '#333333' }}>{'|'.repeat(10 - filled)}</span>
      <span style={{ color: '#888888' }}> {gained}/{needed}</span>
    </span>
  );
}

// ---- Main component ---------------------------------------------------------

const overlayStyle: React.CSSProperties = {
  position: 'fixed',
  inset: 0,
  background: 'rgba(0,0,0,0.82)',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  zIndex: 160,
};

const panelStyle: React.CSSProperties = {
  background: '#0d0d0d',
  border: '1px solid #00ccff',
  fontFamily: 'monospace',
  fontSize: '12px',
  color: '#00ff41',
  width: '620px',
  maxHeight: '85vh',
  overflowY: 'auto',
  boxShadow: '0 0 40px rgba(0, 204, 255, 0.2)',
};

const headerStyle: React.CSSProperties = {
  display: 'flex',
  justifyContent: 'space-between',
  alignItems: 'center',
  padding: '10px 14px',
  borderBottom: '1px solid #1a3a3a',
  letterSpacing: '0.1em',
};

const closeBtnStyle: React.CSSProperties = {
  background: 'none',
  border: '1px solid #00ccff',
  color: '#00ccff',
  fontFamily: 'monospace',
  fontSize: '12px',
  padding: '2px 10px',
  cursor: 'pointer',
};

const cardStyle = (isActive: boolean): React.CSSProperties => ({
  margin: '8px 12px',
  padding: '8px 10px',
  border: `1px solid ${isActive ? '#00ccff' : '#1a2a1a'}`,
  background: isActive ? '#0a1520' : '#0a0a0a',
  position: 'relative',
});

const actionBtnStyle = (color: string): React.CSSProperties => ({
  background: 'none',
  border: `1px solid ${color}`,
  color: color,
  fontFamily: 'monospace',
  fontSize: '11px',
  padding: '2px 8px',
  cursor: 'pointer',
  marginLeft: '6px',
});

export default function CompanionPanel({ companions, onActivate, onDeactivate, onClose }: Props) {
  const activeCount = companions.filter(c => c.isActive).length;

  return (
    <div
      style={overlayStyle}
      data-testid="companion-panel"
      onClick={(e) => { if (e.target === e.currentTarget) onClose(); }}
    >
      <div style={panelStyle}>
        <div style={headerStyle}>
          <span>COMPANIONS — {activeCount}/3 active</span>
          <button type="button" style={closeBtnStyle} onClick={onClose} aria-label="close companion panel">
            close [x]
          </button>
        </div>

        {/* Strategy note */}
        <div style={{ padding: '6px 14px', fontSize: '11px', color: '#888888', borderBottom: '1px solid #1a1a1a' }}>
          Choose 3 companions — they fight autonomously in combat.
          Higher layers = stronger abilities. Use-or-lose: idle companions drift and lose layers.
        </div>

        {companions.length === 0 && (
          <div style={{ padding: '14px', color: '#888888' }}>
            You have no companions yet. Defeat monsters in combat — some can be captured!
          </div>
        )}

        {companions.map(companion => {
          const drift = driftLabel(companion.driftAccumulator);
          const abilitiesForType = ABILITY_LABELS[companion.type] ?? ABILITY_LABELS.Wildfolk;
          const unlockedAbilities = abilitiesForType.slice(0, companion.currentLayer);

          return (
            <div key={companion.id} style={cardStyle(companion.isActive)}>
              {/* Header row */}
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '4px' }}>
                <div>
                  <span style={{ color: elementColor(companion.element), fontWeight: 'bold', marginRight: '6px' }}>
                    [{typeIcon(companion.type)}]
                  </span>
                  <span style={{ color: companion.isActive ? '#00ccff' : '#cccccc', fontWeight: 'bold' }}>
                    {companion.name}
                  </span>
                  {companion.isActive && (
                    <span style={{ color: '#00ccff', fontSize: '10px', marginLeft: '8px' }}>ACTIVE</span>
                  )}
                  {drift && (
                    <span style={{ color: driftColor(companion.driftAccumulator), fontSize: '10px', marginLeft: '8px' }}>
                      ⚠ {drift}
                    </span>
                  )}
                </div>
                <div>
                  {companion.isActive ? (
                    <button type="button" style={actionBtnStyle('#ff8800')} onClick={() => onDeactivate(companion.id)}>
                      Deactivate
                    </button>
                  ) : (
                    <button
                      type="button"
                      style={actionBtnStyle(activeCount < 3 ? '#00ccff' : '#555555')}
                      onClick={() => activeCount < 3 && onActivate(companion.id)}
                      disabled={activeCount >= 3}
                      title={activeCount >= 3 ? 'Active party full (max 3)' : 'Add to active party'}
                    >
                      Activate
                    </button>
                  )}
                </div>
              </div>

              {/* Stat row */}
              <div style={{ fontSize: '11px', color: '#888888', marginBottom: '4px' }}>
                <span style={{ color: elementColor(companion.element) }}>{companion.element}</span>
                {' '}
                <span style={{ color: '#aaaaaa' }}>{companion.type}</span>
                {'  '}
                Lv.{companion.level}
                {'  '}
                <LayerStars layer={companion.currentLayer} />
                {' '}Layer {companion.currentLayer}
              </div>

              {/* Layer progress bar */}
              <div style={{ marginBottom: '4px' }}>
                <span style={{ color: '#666666', fontSize: '10px', marginRight: '4px' }}>Progress:</span>
                <UsageBar type={companion.type} layer={companion.currentLayer} usage={companion.usageCounter} />
              </div>

              {/* Drift bar */}
              <div style={{ marginBottom: '4px', fontSize: '10px' }}>
                <span style={{ color: '#666666', marginRight: '4px' }}>Drift:</span>
                <span style={{ color: driftColor(companion.driftAccumulator) }}>
                  {companion.driftAccumulator.toFixed(1)}/50.0
                </span>
                {!companion.isActive && (
                  <span style={{ color: '#555555', marginLeft: '6px' }}>(inactive companions drift faster)</span>
                )}
              </div>

              {/* Unlocked abilities */}
              <div style={{ fontSize: '10px', color: '#555555', marginTop: '4px' }}>
                {unlockedAbilities.map((ability, i) => (
                  <div key={i} style={{ color: '#666666' }}>
                    <span style={{ color: '#ffcc00' }}>✓</span> {ability}
                  </div>
                ))}
                {companion.currentLayer < 6 && (
                  <div style={{ color: '#333333', marginTop: '2px' }}>
                    <span style={{ color: '#444444' }}>○</span> {abilitiesForType[companion.currentLayer]} [locked]
                  </div>
                )}
              </div>
            </div>
          );
        })}

        <div style={{ padding: '8px 14px', borderTop: '1px solid #1a1a1a', fontSize: '11px', color: '#888888' }}>
          [B] to close · Assign to Homestead: coming soon
        </div>
      </div>
    </div>
  );
}
