import React, { useState } from 'react';
import type { StorageViewSnapshot, AppliedImbue } from '../types/game';

interface Props {
  snapshot: StorageViewSnapshot | null;
  inventoryItems: { id: string; name: string; description: string; workmanship: number; category?: string; quantity?: number; isStackable?: boolean; isUnstable?: boolean; maxImbueSlots?: number; imbues?: AppliedImbue[] }[];
  onDeposit: (itemId: string) => void;
  onWithdraw: (itemId: string) => void;
  onClose: () => void;
  onSmelt?: (amount: number) => void;
  atHomestead?: boolean;
}

interface GroupedItem<T> {
  representative: T;
  ids: string[];
  count: number;
  totalQuantity: number;
}

type TabCategory = 'All' | 'Weapon' | 'Armor' | 'Component' | 'Reagent' | 'Consumable';

const imbueTypeColors: Record<string, string> = {
  Fire: '#ff4422',
  Water: '#2288ff',
  Earth: '#88aa22',
  Air: '#aaccff',
  Protective: '#44ddaa',
  Fortifying: '#ffaa22',
  Wyrd: '#cc44ff',
  Restoration: '#44ff88',
};

function imbueColor(type: string): string {
  return imbueTypeColors[type] ?? '#888888';
}

function renderImbueSlots(item: { maxImbueSlots?: number; imbues?: AppliedImbue[] }): React.ReactNode {
  const maxSlots = item.maxImbueSlots ?? 1;
  const imbues = item.imbues ?? [];
  const slots: React.ReactNode[] = [];
  for (let i = 0; i < maxSlots; i++) {
    const imbue = imbues[i];
    if (imbue) {
      slots.push(
        <span key={i} title={`${imbue.type} +${Math.round(imbue.power * 100)}%`} style={{ color: imbueColor(imbue.type), cursor: 'help' }}>✦</span>
      );
    } else {
      slots.push(<span key={i} style={{ color: '#333333' }}>○</span>);
    }
  }
  return <span style={{ marginLeft: '6px', letterSpacing: '2px' }}>{slots}</span>;
}

function renderImbueDetails(imbues: AppliedImbue[] | undefined): React.ReactNode {
  if (!imbues || imbues.length === 0) return null;
  const parts = imbues.map(im => {
    const pct = Math.round(im.power * 100);
    return (
      <span key={`${im.type}-${im.power}`} style={{ color: imbueColor(im.type) }}>
        {im.type}{pct > 0 ? ` +${pct}%` : ''}
      </span>
    );
  });
  const joined: React.ReactNode[] = [];
  parts.forEach((p, i) => { joined.push(p); if (i < parts.length - 1) joined.push(<span key={`sep-${i}`} style={{ color: '#555' }}>, </span>); });
  return (
    <div style={{ fontSize: '10px', marginTop: '2px', color: '#888888' }}>
      imbues: {joined}
    </div>
  );
}

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

function groupItems<T extends { id: string; name: string; workmanship: number; category?: string; quantity?: number; isStackable?: boolean }>(
  items: T[]
): GroupedItem<T>[] {
  const map = new Map<string, GroupedItem<T>>();
  for (const item of items) {
    const key = `${item.name}||${item.workmanship}||${item.category ?? ''}`;
    const qty = item.quantity ?? 1;
    const existing = map.get(key);
    if (existing) {
      existing.ids.push(item.id);
      existing.count += 1;
      existing.totalQuantity += qty;
    } else {
      map.set(key, { representative: item, ids: [item.id], count: 1, totalQuantity: qty });
    }
  }
  return Array.from(map.values());
}

export default function StoragePanel({ snapshot, inventoryItems, onDeposit, onWithdraw, onClose, onSmelt, atHomestead }: Props) {
  const [activeTab, setActiveTab] = useState<TabCategory>('All');
  const [smeltAmount, setSmeltAmount] = useState<number>(10);

  const filteredStorageItems = snapshot
    ? snapshot.items.filter(i => activeTab === 'All' || i.category === activeTab)
    : [];

  const groupedStorageItems = groupItems(filteredStorageItems);

  const filteredInventoryItems = activeTab === 'All'
    ? inventoryItems
    : inventoryItems.filter(i => i.category === activeTab);

  const groupedInventoryItems = groupItems(filteredInventoryItems);

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
        ) : groupedStorageItems.length === 0 ? (
          <div style={emptyStyle}>Storage is empty.</div>
        ) : (
          groupedStorageItems.map(group => {
            const item = group.representative;
            const isImbueable = item.category === 'Weapon' || item.category === 'Armor' || item.category === 'Accessory';
            const isStackable = item.isStackable ?? (item.category === 'Component' || item.category === 'Reagent' || item.category === 'Consumable');
            const displayQty = isStackable ? group.totalQuantity : group.count;
            return (
              <div key={item.id} style={{ ...itemRowStyle, ...(item.isUnstable ? { borderLeft: '2px solid #cc44ff' } : {}) }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                  <div>
                    <span style={{ color: '#00ff41' }}>{item.name}</span>
                    {isImbueable && renderImbueSlots(item)}
                    <span style={{ color: '#888888', fontSize: '11px', marginLeft: '10px' }}>
                      W{item.workmanship}
                    </span>
                    <span style={{ color: '#555555', fontSize: '11px', marginLeft: '6px' }}>
                      [{item.category}]
                    </span>
                    {displayQty > 1 && (
                      <span style={{ color: '#ffdd44', fontSize: '11px', marginLeft: '6px' }}>
                        x{displayQty}
                      </span>
                    )}
                    {item.isUnstable && (
                      <span style={{ color: '#cc44ff', fontSize: '10px', marginLeft: '4px' }}>
                        [UNSTABLE]
                      </span>
                    )}
                  </div>
                  <button
                    type="button"
                    style={actionBtnStyle}
                    onClick={() => onWithdraw(group.ids[0])}
                  >
                    withdraw
                  </button>
                </div>
                {isImbueable && renderImbueDetails(item.imbues)}
              </div>
            );
          })
        )}

        {/* Inventory — items to deposit */}
        <div style={sectionHeadingStyle}>Your Inventory (deposit)</div>
        {groupedInventoryItems.length === 0 ? (
          <div style={emptyStyle}>
            {activeTab === 'All' ? 'Inventory is empty.' : `No ${activeTab.toLowerCase()} items in inventory.`}
          </div>
        ) : (
          groupedInventoryItems.map(group => {
            const item = group.representative;
            const isImbueable = item.category === 'Weapon' || item.category === 'Armor' || item.category === 'Accessory';
            const isStackable = item.isStackable ?? (item.category === 'Component' || item.category === 'Reagent' || item.category === 'Consumable');
            const displayQty = isStackable ? group.totalQuantity : group.count;
            return (
              <div key={item.id} style={{ ...itemRowStyle, ...(item.isUnstable ? { borderLeft: '2px solid #cc44ff' } : {}) }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                  <div>
                    <span style={{ color: '#ccff88' }}>{item.name}</span>
                    {isImbueable && renderImbueSlots(item)}
                    <span style={{ color: '#888888', fontSize: '11px', marginLeft: '10px' }}>
                      W{item.workmanship}
                    </span>
                    {item.category && (
                      <span style={{ color: '#555555', fontSize: '11px', marginLeft: '6px' }}>
                        [{item.category}]
                      </span>
                    )}
                    {displayQty > 1 && (
                      <span style={{ color: '#ffdd44', fontSize: '11px', marginLeft: '6px' }}>
                        x{displayQty}
                      </span>
                    )}
                    {item.isUnstable && (
                      <span style={{ color: '#cc44ff', fontSize: '10px', marginLeft: '4px' }}>
                        [UNSTABLE]
                      </span>
                    )}
                  </div>
                  <button
                    type="button"
                    style={{ ...actionBtnStyle, borderColor: '#ccaa00', color: '#ccaa00' }}
                    onClick={() => onDeposit(group.ids[0])}
                  >
                    deposit
                  </button>
                </div>
                {isImbueable && renderImbueDetails(item.imbues)}
              </div>
            );
          })
        )}
      </div>
    </div>
  );
}
