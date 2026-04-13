import React from 'react';

interface Props {
  onClose: () => void;
}

interface Binding {
  keys: string[];
  description: string;
}

interface Section {
  heading: string;
  bindings: Binding[];
}

const SECTIONS: Section[] = [
  {
    heading: 'Movement',
    bindings: [
      { keys: ['W', '↑'], description: 'move north' },
      { keys: ['S', '↓'], description: 'move south' },
      { keys: ['A', '←'], description: 'move west' },
      { keys: ['D', '→'], description: 'move east' },
    ],
  },
  {
    heading: 'Actions',
    bindings: [
      { keys: ['Enter'], description: 'interact with adjacent tile / NPC' },
      { keys: ['Space'], description: 'wait / pass turn' },
      { keys: ['E'], description: 'harvest resource at current zone' },
      { keys: ['P'], description: 'portal home (or portal back when at homestead)' },
      { keys: ['F'], description: 'start / stop auto-farm (5 min default)' },
      { keys: ['N'], description: 'auto-navigate to active quest waypoint (press again or move to cancel)' },
      { keys: ['L'], description: 'quest auto-run — accept all quests, navigate & complete them in order (press again to stop)' },
      { keys: ['I'], description: 'open inventory — includes Auto-Salvage Settings to auto-dismantle low-quality loot on pickup (set threshold per weapon/armor; Off = disabled)' },
    ],
  },
  {
    heading: 'Panels',
    bindings: [
      { keys: ['Q'], description: 'open / close quest log' },
      { keys: ['I'], description: 'open inventory' },
      { keys: ['C'], description: 'character sheet' },
      { keys: ['V'], description: 'open homestead storage vault (at homestead only)' },
      { keys: ['B'], description: 'open companion panel — manage your party of 3' },
      { keys: ['R'], description: 'open crafting panel — view recipes and craft items' },
      { keys: ['G'], description: 'open city panel — view homestead buildings and construction' },
      { keys: ['?', 'H'], description: 'open / close this help menu' },
      { keys: ['M'], description: 'mute / unmute audio (music + SFX)' },
      { keys: ['Esc'], description: 'close any open panel / cancel auto-farm' },
    ],
  },
];

const overlayStyle: React.CSSProperties = {
  position: 'fixed',
  inset: 0,
  background: 'rgba(0, 0, 0, 0.8)',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  zIndex: 150,
};

const panelStyle: React.CSSProperties = {
  background: '#0d0d0d',
  border: '1px solid #00ff41',
  fontFamily: 'monospace',
  fontSize: '13px',
  color: '#00ff41',
  width: '520px',
  maxHeight: '82vh',
  overflowY: 'auto',
  boxShadow: '0 0 40px rgba(0, 255, 65, 0.25)',
};

const headerStyle: React.CSSProperties = {
  display: 'flex',
  justifyContent: 'space-between',
  alignItems: 'center',
  padding: '10px 14px',
  borderBottom: '1px solid #1a3a1a',
  letterSpacing: '0.12em',
};

const sectionHeadingStyle: React.CSSProperties = {
  color: '#888888',
  fontSize: '11px',
  letterSpacing: '0.14em',
  textTransform: 'uppercase',
  margin: '14px 14px 6px',
  borderBottom: '1px solid #1a1a1a',
  paddingBottom: '4px',
};

const rowStyle: React.CSSProperties = {
  display: 'flex',
  padding: '3px 14px',
  gap: '16px',
  alignItems: 'baseline',
};

const keysStyle: React.CSSProperties = {
  color: '#00ccff',
  minWidth: '110px',
  fontSize: '12px',
};

const descStyle: React.CSSProperties = {
  color: '#cccccc',
  fontSize: '12px',
};

const footerStyle: React.CSSProperties = {
  padding: '10px 14px',
  borderTop: '1px solid #1a3a1a',
  color: '#888888',
  fontSize: '11px',
  textAlign: 'center',
};

const closeBtnStyle: React.CSSProperties = {
  background: 'none',
  border: '1px solid #00ff41',
  color: '#00ff41',
  fontFamily: 'monospace',
  fontSize: '12px',
  padding: '2px 10px',
  cursor: 'pointer',
};

export default function HelpOverlay({ onClose }: Props) {
  return (
    <div
      style={overlayStyle}
      data-testid="help-overlay"
      onClick={(e) => {
        // Click-outside to close
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div style={panelStyle}>
        <div style={headerStyle}>
          <span>HELP — KEY BINDINGS</span>
          <button type="button" style={closeBtnStyle} onClick={onClose} aria-label="close help">
            close [x]
          </button>
        </div>

        {SECTIONS.map((section) => (
          <div key={section.heading}>
            <div style={sectionHeadingStyle}>{section.heading}</div>
            {section.bindings.map((b) => (
              <div key={b.keys.join('+') + b.description} style={rowStyle}>
                <span style={keysStyle}>{b.keys.map((k) => `[${k}]`).join(' / ')}</span>
                <span style={descStyle}>{b.description}</span>
              </div>
            ))}
          </div>
        ))}

        <div style={footerStyle}>
          Press [?] or [H] at any time to toggle this menu. Press [Esc] to close.
        </div>
      </div>
    </div>
  );
}
