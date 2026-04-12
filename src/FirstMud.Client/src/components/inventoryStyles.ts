import React from 'react';

export const overlayStyle: React.CSSProperties = {
  position: 'fixed',
  inset: 0,
  background: 'rgba(0, 0, 0, 0.8)',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  zIndex: 140,
};

export const panelStyle: React.CSSProperties = {
  background: '#0d0d0d',
  border: '1px solid #00ff41',
  fontFamily: 'monospace',
  fontSize: '13px',
  color: '#00ff41',
  width: '580px',
  maxHeight: '82vh',
  overflowY: 'auto',
  boxShadow: '0 0 40px rgba(0, 255, 65, 0.25)',
};

export const headerStyle: React.CSSProperties = {
  display: 'flex',
  justifyContent: 'space-between',
  alignItems: 'center',
  padding: '10px 14px',
  borderBottom: '1px solid #1a3a1a',
  letterSpacing: '0.12em',
};

export const sectionHeadingStyle: React.CSSProperties = {
  color: '#888888',
  fontSize: '11px',
  letterSpacing: '0.14em',
  textTransform: 'uppercase',
  margin: '14px 14px 4px',
  borderBottom: '1px solid #1a1a1a',
  paddingBottom: '4px',
  display: 'flex',
  justifyContent: 'space-between',
  alignItems: 'center',
};

export const itemRowStyle: React.CSSProperties = {
  padding: '6px 14px',
  borderBottom: '1px dashed #1a1a1a',
};

export const itemNameStyle: React.CSSProperties = {
  color: '#00ff41',
  display: 'flex',
  justifyContent: 'space-between',
  alignItems: 'baseline',
};

export const itemDescStyle: React.CSSProperties = {
  color: '#aaaaaa',
  fontSize: '11px',
  marginTop: '2px',
};

export const emptyStyle: React.CSSProperties = {
  padding: '20px 14px',
  color: '#666666',
  fontStyle: 'italic',
  textAlign: 'center',
};

export const skillRowStyle: React.CSSProperties = {
  display: 'flex',
  justifyContent: 'space-between',
  padding: '3px 14px',
  color: '#aaaaaa',
  fontSize: '12px',
};

export const closeBtnStyle: React.CSSProperties = {
  background: 'none',
  border: '1px solid #00ff41',
  color: '#00ff41',
  fontFamily: 'monospace',
  fontSize: '12px',
  padding: '2px 10px',
  cursor: 'pointer',
};

export const equipBtnStyle: React.CSSProperties = {
  background: 'none',
  border: '1px solid #00ccff',
  color: '#00ccff',
  fontFamily: 'monospace',
  fontSize: '11px',
  padding: '1px 6px',
  cursor: 'pointer',
  marginLeft: '8px',
};

export const salvageBtnStyle: React.CSSProperties = {
  background: 'none',
  border: '1px solid #ff8800',
  color: '#ff8800',
  fontFamily: 'monospace',
  fontSize: '11px',
  padding: '1px 6px',
  cursor: 'pointer',
  marginLeft: '6px',
};

export const salvageBtnDisabledStyle: React.CSSProperties = {
  ...salvageBtnStyle,
  border: '1px solid #555555',
  color: '#555555',
  cursor: 'not-allowed',
};

export const salvageAllBtnStyle: React.CSSProperties = {
  background: 'none',
  border: '1px solid #ff8800',
  color: '#ff8800',
  fontFamily: 'monospace',
  fontSize: '10px',
  padding: '1px 6px',
  cursor: 'pointer',
};

export const storeBtnStyle: React.CSSProperties = {
  background: 'none',
  border: '1px solid #ccaa00',
  color: '#ccaa00',
  fontFamily: 'monospace',
  fontSize: '11px',
  padding: '1px 6px',
  cursor: 'pointer',
  marginLeft: '6px',
};

export const queueSalvageBtnStyle: React.CSSProperties = {
  background: 'none',
  border: '1px solid #888844',
  color: '#aaa844',
  fontFamily: 'monospace',
  fontSize: '11px',
  padding: '1px 6px',
  cursor: 'pointer',
  marginLeft: '6px',
};

export const lockBtnStyle: React.CSSProperties = {
  background: 'none',
  border: 'none',
  color: '#888888',
  fontFamily: 'monospace',
  fontSize: '13px',
  padding: '0 4px',
  cursor: 'pointer',
  marginLeft: '4px',
  lineHeight: 1,
};

export const lockBtnLockedStyle: React.CSSProperties = {
  ...lockBtnStyle,
  color: '#ffcc00',
};

export const equippedTagStyle: React.CSSProperties = {
  color: '#00ff41',
  fontSize: '10px',
  border: '1px solid #00ff41',
  padding: '0 4px',
  marginLeft: '6px',
};

export const quantityTagStyle: React.CSSProperties = {
  color: '#ffdd44',
  fontSize: '11px',
  marginLeft: '4px',
};

export const slotsStyle: React.CSSProperties = {
  color: '#888888',
  fontSize: '11px',
  padding: '4px 14px 8px',
};

export const imbueBtnStyle: React.CSSProperties = {
  background: 'none',
  border: '1px solid #cc44ff',
  color: '#cc44ff',
  fontFamily: 'monospace',
  fontSize: '11px',
  padding: '1px 6px',
  cursor: 'pointer',
  marginLeft: '6px',
};

export const useBtnStyle: React.CSSProperties = {
  background: 'none',
  border: '1px solid #44ffaa',
  color: '#44ffaa',
  fontFamily: 'monospace',
  fontSize: '11px',
  padding: '1px 6px',
  cursor: 'pointer',
  marginLeft: '6px',
};

export const unequipBtnStyle: React.CSSProperties = {
  background: 'none',
  border: '1px solid #ff8800',
  color: '#ff8800',
  fontFamily: 'monospace',
  fontSize: '11px',
  padding: '1px 6px',
  cursor: 'pointer',
  marginLeft: '6px',
};

export const imbuePanelStyle: React.CSSProperties = {
  background: '#0a0a1a',
  border: '1px solid #cc44ff',
  padding: '8px 12px',
  margin: '4px 0',
  fontSize: '12px',
};

export const imbueTypeColors: Record<string, string> = {
  Fire: '#ff4422',
  Water: '#2288ff',
  Earth: '#88aa22',
  Air: '#aaccff',
  Protective: '#44ddaa',
  Fortifying: '#ffaa22',
  Wyrd: '#cc44ff',
  Restoration: '#44ff88',
};
