import React from 'react';
import type { InventorySnapshot } from '../types/game';

interface Props {
  snapshot: InventorySnapshot | null;
  onClose: () => void;
}

const overlayStyle: React.CSSProperties = {
  position: 'fixed',
  inset: 0,
  background: 'rgba(0, 0, 0, 0.8)',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  zIndex: 140,
};

const panelStyle: React.CSSProperties = {
  background: '#0d0d0d',
  border: '1px solid #00ff41',
  fontFamily: 'monospace',
  fontSize: '13px',
  color: '#00ff41',
  width: '560px',
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

const itemRowStyle: React.CSSProperties = {
  padding: '6px 14px',
  borderBottom: '1px dashed #1a1a1a',
};

const itemNameStyle: React.CSSProperties = {
  color: '#00ff41',
  display: 'flex',
  justifyContent: 'space-between',
  alignItems: 'baseline',
};

const itemDescStyle: React.CSSProperties = {
  color: '#aaaaaa',
  fontSize: '11px',
  marginTop: '2px',
};

const emptyStyle: React.CSSProperties = {
  padding: '20px 14px',
  color: '#666666',
  fontStyle: 'italic',
  textAlign: 'center',
};

const skillRowStyle: React.CSSProperties = {
  display: 'flex',
  justifyContent: 'space-between',
  padding: '3px 14px',
  color: '#aaaaaa',
  fontSize: '12px',
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

export default function InventoryPanel({ snapshot, onClose }: Props) {
  return (
    <div
      style={overlayStyle}
      data-testid="inventory-overlay"
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div style={panelStyle}>
        <div style={headerStyle}>
          <span>INVENTORY</span>
          <button type="button" style={closeBtnStyle} onClick={onClose} aria-label="close inventory">
            close [x] / esc
          </button>
        </div>

        <div style={sectionHeadingStyle}>Skills</div>
        {snapshot ? (
          <>
            <div style={skillRowStyle}>
              <span>Crafting</span>
              <span style={{ color: '#00ccff' }}>{snapshot.craftingSkill}</span>
            </div>
            <div style={skillRowStyle}>
              <span>Salvage</span>
              <span style={{ color: '#00ccff' }}>{snapshot.salvageSkill}</span>
            </div>
          </>
        ) : (
          <div style={emptyStyle}>loading...</div>
        )}

        <div style={sectionHeadingStyle}>Items</div>
        {!snapshot ? (
          <div style={emptyStyle}>loading...</div>
        ) : snapshot.items.length === 0 ? (
          <div style={emptyStyle} data-testid="inventory-empty">
            Your pack is empty.
          </div>
        ) : (
          snapshot.items.map((item) => (
            <div key={item.id} style={itemRowStyle}>
              <div style={itemNameStyle}>
                <span>{item.name}</span>
                <span style={{ color: '#888888', fontSize: '11px' }}>
                  Workmanship {item.workmanship}
                </span>
              </div>
              {item.description && <div style={itemDescStyle}>{item.description}</div>}
            </div>
          ))
        )}
      </div>
    </div>
  );
}
