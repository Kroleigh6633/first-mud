import { useState } from 'react';
import type { ZoneTile } from '../types/game';

export interface AutoFarmSettings {
  targetZone: ZoneTile | null;
  maxDanger: number;
  priority: 'combat' | 'harvest' | 'balanced';
}

interface Props {
  zoneTiles: ZoneTile[];
  playerLevel: number;
  autoSalvageWeaponThreshold: number;
  autoSalvageArmorThreshold: number;
  onStart: (settings: AutoFarmSettings) => void;
  onCancel: () => void;
}

const overlayStyle: React.CSSProperties = {
  position: 'fixed',
  inset: 0,
  background: 'rgba(0, 0, 0, 0.80)',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  zIndex: 120,
};

const panelStyle: React.CSSProperties = {
  background: '#0d0d0d',
  border: '1px solid #00ff41',
  fontFamily: 'monospace',
  fontSize: '13px',
  color: '#00ff41',
  width: '440px',
  padding: '0',
  boxShadow: '0 0 40px rgba(0, 255, 65, 0.2)',
};

const headerStyle: React.CSSProperties = {
  display: 'flex',
  justifyContent: 'space-between',
  alignItems: 'center',
  padding: '10px 14px',
  borderBottom: '1px solid #1a3a1a',
  letterSpacing: '0.12em',
};

const rowStyle: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '10px',
  padding: '8px 14px',
  borderBottom: '1px solid #111',
};

const labelStyle: React.CSSProperties = {
  color: '#88aa88',
  width: '90px',
  flexShrink: 0,
};

const selectStyle: React.CSSProperties = {
  background: '#0d1a0d',
  border: '1px solid #1a3a1a',
  color: '#00ff41',
  fontFamily: 'monospace',
  fontSize: '12px',
  padding: '3px 6px',
  flex: 1,
  cursor: 'pointer',
};

const footerStyle: React.CSSProperties = {
  display: 'flex',
  gap: '10px',
  padding: '12px 14px',
  justifyContent: 'flex-end',
  borderTop: '1px solid #1a3a1a',
};

const btnStyle: React.CSSProperties = {
  background: '#0d1a0d',
  border: '1px solid #00aa33',
  color: '#00ff41',
  fontFamily: 'monospace',
  fontSize: '12px',
  padding: '5px 18px',
  cursor: 'pointer',
  letterSpacing: '0.08em',
};

const cancelBtnStyle: React.CSSProperties = {
  ...btnStyle,
  border: '1px solid #1a3a1a',
  color: '#557755',
};

const dividerStyle: React.CSSProperties = {
  borderTop: '1px solid #1a3a1a',
  margin: '0',
};

/** Returns a color matching the danger level (1-10). */
function dangerColor(d: number): string {
  if (d <= 2) return '#44ff88';
  if (d <= 4) return '#aaff44';
  if (d <= 6) return '#ffcc00';
  if (d <= 8) return '#ff8800';
  return '#ff3322';
}

export default function AutoFarmPicker({
  zoneTiles,
  playerLevel,
  autoSalvageWeaponThreshold,
  autoSalvageArmorThreshold,
  onStart,
  onCancel,
}: Props) {
  const defaultMaxDanger = Math.min(playerLevel + 2, 10);
  const [targetZone, setTargetZone] = useState<ZoneTile | null>(null);
  const [maxDanger, setMaxDanger] = useState<number>(defaultMaxDanger);
  const [priority, setPriority] = useState<'combat' | 'harvest' | 'balanced'>('balanced');

  // Only show visited zones (all zoneTiles received are already known/visited)
  const visitableZones = zoneTiles.slice().sort((a, b) => a.dangerLevel - b.dangerLevel);

  const filledBars = maxDanger;
  const totalBars = 10;

  return (
    <div style={overlayStyle} onClick={onCancel}>
      <div style={panelStyle} onClick={e => e.stopPropagation()}>
        {/* Header */}
        <div style={headerStyle}>
          <span>AUTO-FARM SETTINGS</span>
          <button
            onClick={onCancel}
            style={{ background: 'none', border: 'none', color: '#557755', fontFamily: 'monospace', fontSize: '14px', cursor: 'pointer' }}
          >
            ✕
          </button>
        </div>

        <div style={dividerStyle} />

        {/* Target zone */}
        <div style={rowStyle}>
          <span style={labelStyle}>Target:</span>
          <select
            style={selectStyle}
            value={targetZone ? targetZone.zoneId.toString() : ''}
            onChange={e => {
              const val = e.target.value;
              if (!val) {
                setTargetZone(null);
              } else {
                const found = visitableZones.find(z => z.zoneId.toString() === val) ?? null;
                setTargetZone(found);
              }
            }}
          >
            <option value="">Current Location</option>
            {visitableZones.map(z => (
              <option key={z.zoneId} value={z.zoneId.toString()}>
                {z.name} (danger {z.dangerLevel})
              </option>
            ))}
          </select>
        </div>

        {/* Max danger slider */}
        <div style={rowStyle}>
          <span style={labelStyle}>Max Danger:</span>
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flex: 1 }}>
            {/* Segmented bar */}
            <div style={{ display: 'flex', gap: '2px' }}>
              {Array.from({ length: totalBars }, (_, i) => {
                const level = i + 1;
                const filled = level <= filledBars;
                return (
                  <div
                    key={level}
                    onClick={() => setMaxDanger(level)}
                    style={{
                      width: '16px',
                      height: '14px',
                      background: filled ? dangerColor(filledBars) : '#1a1a1a',
                      border: `1px solid ${filled ? dangerColor(filledBars) : '#333'}`,
                      cursor: 'pointer',
                      opacity: filled ? 1 : 0.4,
                    }}
                    title={`Set max danger to ${level}`}
                  />
                );
              })}
            </div>
            <span style={{ color: dangerColor(maxDanger), minWidth: '32px' }}>
              {maxDanger}/10
            </span>
          </div>
        </div>

        {/* Priority radio */}
        <div style={{ ...rowStyle, alignItems: 'flex-start' }}>
          <span style={{ ...labelStyle, paddingTop: '2px' }}>Priority:</span>
          <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
            {(['combat', 'harvest', 'balanced'] as const).map(p => (
              <label
                key={p}
                style={{ display: 'flex', alignItems: 'center', gap: '8px', cursor: 'pointer', color: priority === p ? '#00ff41' : '#557755' }}
              >
                <input
                  type="radio"
                  name="priority"
                  value={p}
                  checked={priority === p}
                  onChange={() => setPriority(p)}
                  style={{ accentColor: '#00ff41', cursor: 'pointer' }}
                />
                {p === 'combat' && 'Combat (fight everything)'}
                {p === 'harvest' && 'Harvest (prioritize gathering)'}
                {p === 'balanced' && 'Balanced (fight + gather)'}
              </label>
            ))}
          </div>
        </div>

        {/* Auto-salvage info (read-only) */}
        <div style={{ padding: '6px 14px 8px', color: '#445544', fontSize: '11px', borderBottom: '1px solid #111' }}>
          Auto-salvage: weapons ≤ W{autoSalvageWeaponThreshold}, armor ≤ W{autoSalvageArmorThreshold}
          <span style={{ marginLeft: '8px', color: '#334433' }}>(change in inventory [I])</span>
        </div>

        {/* Buttons */}
        <div style={footerStyle}>
          <button style={cancelBtnStyle} onClick={onCancel}>Cancel</button>
          <button
            style={btnStyle}
            onClick={() => onStart({ targetZone, maxDanger, priority })}
          >
            Start Farming
          </button>
        </div>
      </div>
    </div>
  );
}
