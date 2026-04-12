import React, { useState } from 'react';
import type { StorageViewSnapshot } from '../types/game';

interface Props {
  snapshot: StorageViewSnapshot | null;
  inventoryItems: { id: string; name: string; description: string; workmanship: number }[];
  onDeposit: (itemId: string) => void;
  onWithdraw: (itemId: string) => void;
  onClose: () => void;
}

type TabCategory = 'All' | 'Weapon' | 'Armor' | 'Component' | 'Reagent' | 'Consumable';

const overlayStyle: React.CSSProperties = {
  position: 'fixed',
  inset: 0,
  background: 'rgba(0, 0, 0, 0.85)',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  zIndex: 145,
};

const panelStyle: React.CSSProperties = {
  background: '#0d0d0d',
  border: '1px solid #00ff41',
  fontFamily: 'monospace',
  fontSize: '13px',
  color: '#00ff41',
  width: '660px',
  maxHeight: '85vh',
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

const tabBarStyle: React.CSSProperties = {
  display: 'flex',
  borderBottom: '1px solid #1a3a1a',
  padding: '0 14px',
  gap: '2px',
};

const sectionHeadingStyle: React.CSSProperties = {
  color: '#888888',
  fontSize: '11px',
  letterSpacing: '0.14em',
  textTransform: 'uppercase',
  margin: '12px 14px 6px',
  borderBottom: '1px solid #1a1a1a',
  paddingBottom: '4px',
};

const itemRowStyle: React.CSSProperties = {
  padding: '5px 14px',
  borderBottom: '1px dashed #1a1a1a',
  display: 'flex',
  justifyContent: 'space-between',
  alignItems: 'center',
};

const emptyStyle: React.CSSProperties = {
  padding: '16px 14px',
  color: '#666666',
  fontStyle: 'italic',
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

const actionBtnStyle: React.CSSProperties = {
  background: 'none',
  border: '1px solid #00cc33',
  color: '#00cc33',
  fontFamily: 'monospace',
  fontSize: '11px',
  padding: '1px 8px',
  cursor: 'pointer',
  marginLeft: '8px',
};

const TABS: TabCategory[] = ['All', 'Weapon', 'Armor', 'Component', 'Reagent', 'Consumable'];

export default function StoragePanel({ snapshot, inventoryItems, onDeposit, onWithdraw, onClose }: Props) {
  const [activeTab, setActiveTab] = useState<TabCategory>('All');

  const filteredStorageItems = snapshot
    ? snapshot.items.filter(i => activeTab === 'All' || i.category === activeTab)
    : [];

  return (
    <div
      style={overlayStyle}
      data-testid="storage-overlay"
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div style={panelStyle}>
        <div style={headerStyle}>
          <span>
            HOMESTEAD STORAGE
            {snapshot && (
              <span style={{ color: '#888888', marginLeft: '12px', fontSize: '11px' }}>
                {snapshot.usedSlots}/{snapshot.storageSlots} slots used
              </span>
            )}
          </span>
          <button type="button" style={closeBtnStyle} onClick={onClose} aria-label="close storage">
            close [x] / esc
          </button>
        </div>

        {/* Tab bar */}
        <div style={tabBarStyle}>
          {TABS.map(tab => (
            <button
              key={tab}
              type="button"
              onClick={() => setActiveTab(tab)}
              style={{
                background: 'none',
                border: 'none',
                borderBottom: activeTab === tab ? '2px solid #00ff41' : '2px solid transparent',
                color: activeTab === tab ? '#00ff41' : '#666666',
                fontFamily: 'monospace',
                fontSize: '11px',
                padding: '6px 10px',
                cursor: 'pointer',
                letterSpacing: '0.08em',
              }}
            >
              {tab.toUpperCase()}
            </button>
          ))}
        </div>

        {/* Storage contents */}
        <div style={sectionHeadingStyle}>In Storage</div>
        {!snapshot ? (
          <div style={emptyStyle}>Loading storage...</div>
        ) : filteredStorageItems.length === 0 ? (
          <div style={emptyStyle}>Storage is empty.</div>
        ) : (
          filteredStorageItems.map(item => (
            <div key={item.id} style={itemRowStyle}>
              <div>
                <span style={{ color: '#00ff41' }}>{item.name}</span>
                <span style={{ color: '#888888', fontSize: '11px', marginLeft: '10px' }}>
                  W{item.workmanship}
                </span>
                <span style={{ color: '#555555', fontSize: '11px', marginLeft: '6px' }}>
                  [{item.category}]
                </span>
              </div>
              <button
                type="button"
                style={actionBtnStyle}
                onClick={() => onWithdraw(item.id)}
              >
                withdraw
              </button>
            </div>
          ))
        )}

        {/* Inventory — items to deposit */}
        <div style={sectionHeadingStyle}>Your Inventory (deposit)</div>
        {inventoryItems.length === 0 ? (
          <div style={emptyStyle}>Inventory is empty.</div>
        ) : (
          inventoryItems.map(item => (
            <div key={item.id} style={itemRowStyle}>
              <div>
                <span style={{ color: '#ccff88' }}>{item.name}</span>
                <span style={{ color: '#888888', fontSize: '11px', marginLeft: '10px' }}>
                  W{item.workmanship}
                </span>
              </div>
              <button
                type="button"
                style={{ ...actionBtnStyle, borderColor: '#ccaa00', color: '#ccaa00' }}
                onClick={() => onDeposit(item.id)}
              >
                deposit
              </button>
            </div>
          ))
        )}
      </div>
    </div>
  );
}
