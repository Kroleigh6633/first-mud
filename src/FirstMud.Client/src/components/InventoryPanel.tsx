import React from 'react';
import type { InventorySnapshot, EquipmentSlots } from '../types/game';

interface Props {
  snapshot: InventorySnapshot | null;
  equipment: EquipmentSlots;
  onClose: () => void;
  sendCommand: (command: string, payload?: unknown) => void;
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
  width: '580px',
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
  margin: '14px 14px 4px',
  borderBottom: '1px solid #1a1a1a',
  paddingBottom: '4px',
  display: 'flex',
  justifyContent: 'space-between',
  alignItems: 'center',
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

const equipBtnStyle: React.CSSProperties = {
  background: 'none',
  border: '1px solid #00ccff',
  color: '#00ccff',
  fontFamily: 'monospace',
  fontSize: '11px',
  padding: '1px 6px',
  cursor: 'pointer',
  marginLeft: '8px',
};

const salvageBtnStyle: React.CSSProperties = {
  background: 'none',
  border: '1px solid #ff8800',
  color: '#ff8800',
  fontFamily: 'monospace',
  fontSize: '11px',
  padding: '1px 6px',
  cursor: 'pointer',
  marginLeft: '6px',
};

const salvageAllBtnStyle: React.CSSProperties = {
  background: 'none',
  border: '1px solid #ff8800',
  color: '#ff8800',
  fontFamily: 'monospace',
  fontSize: '10px',
  padding: '1px 6px',
  cursor: 'pointer',
};

const equippedTagStyle: React.CSSProperties = {
  color: '#00ff41',
  fontSize: '10px',
  border: '1px solid #00ff41',
  padding: '0 4px',
  marginLeft: '6px',
};

const quantityTagStyle: React.CSSProperties = {
  color: '#ffdd44',
  fontSize: '11px',
  marginLeft: '4px',
};

const slotsStyle: React.CSSProperties = {
  color: '#888888',
  fontSize: '11px',
  padding: '4px 14px 8px',
};

function isEquippable(category?: string): boolean {
  if (!category) return false;
  const c = category.toLowerCase();
  return c === 'weapon' || c === 'armor' || c === 'accessory' || c === 'component';
}

function isSalvageable(category?: string): boolean {
  if (!category) return false;
  const c = category.toLowerCase();
  return c === 'weapon' || c === 'armor' || c === 'consumable';
}

function isEquipped(itemId: string, equipment: EquipmentSlots): boolean {
  return equipment.weaponId === itemId
    || equipment.armorId === itemId
    || equipment.accessoryId === itemId;
}

export default function InventoryPanel({ snapshot, equipment, onClose, sendCommand }: Props) {
  const handleEquip = (itemId: string) => {
    sendCommand('equip', { itemId });
  };

  const handleSalvage = (itemId: string) => {
    sendCommand('salvage', { itemId });
  };

  const handleSalvageAll = (category: string) => {
    sendCommand('salvageall', { category });
  };

  // Collect equipped item ids for display at the top
  const equippedItems = snapshot?.items.filter(i => isEquipped(i.id, equipment)) ?? [];
  const unequippedItems = snapshot?.items.filter(i => !isEquipped(i.id, equipment)) ?? [];

  // Compute slot usage: stacks count as 1 slot regardless of quantity; non-stackable count 1 each
  const allItems = snapshot?.items ?? [];
  const slotsUsed = unequippedItems.length; // each row = 1 inventory slot
  const totalItems = allItems.reduce((acc, i) => acc + (i.quantity ?? 1), 0);
  const maxSlots = 20; // matches Player._maxInventorySlots default

  // Group unequipped items by category for bulk salvage buttons
  const hasWeapons = unequippedItems.some(i => i.category?.toLowerCase() === 'weapon');
  const hasArmor   = unequippedItems.some(i => i.category?.toLowerCase() === 'armor');

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

        <div style={skillRowStyle}>
          <span>Crafting</span>
          <span style={{ color: '#00ccff' }}>{snapshot?.craftingSkill ?? '—'}</span>
        </div>
        <div style={skillRowStyle}>
          <span>Salvage</span>
          <span style={{ color: '#00ccff' }}>{snapshot?.salvageSkill ?? '—'}</span>
        </div>

        {snapshot && (
          <div style={slotsStyle}>
            Slots: {slotsUsed}/{maxSlots} &nbsp;|&nbsp; Items: {totalItems}
          </div>
        )}

        {/* Bulk salvage buttons */}
        {(hasWeapons || hasArmor) && (
          <div style={{ padding: '4px 14px 8px', display: 'flex', gap: '8px' }}>
            {hasWeapons && (
              <button
                type="button"
                style={salvageAllBtnStyle}
                onClick={() => handleSalvageAll('Weapon')}
                aria-label="salvage all weapons"
              >
                salvage all weapons
              </button>
            )}
            {hasArmor && (
              <button
                type="button"
                style={salvageAllBtnStyle}
                onClick={() => handleSalvageAll('Armor')}
                aria-label="salvage all armor"
              >
                salvage all armor
              </button>
            )}
          </div>
        )}

        {equippedItems.length > 0 && (
          <>
            <div style={sectionHeadingStyle}>
              <span>Equipped</span>
            </div>
            {equippedItems.map((item) => (
              <div key={item.id} style={itemRowStyle} data-testid="equipped-item">
                <div style={itemNameStyle}>
                  <span>
                    {item.name}
                    {item.isStackable && (item.quantity ?? 1) > 1 && (
                      <span style={quantityTagStyle}>x{item.quantity}</span>
                    )}
                    <span style={equippedTagStyle}>equipped</span>
                  </span>
                  <span style={{ color: '#888888', fontSize: '11px' }}>
                    {item.category ?? ''} W{item.workmanship}
                  </span>
                </div>
                {item.description && <div style={itemDescStyle}>{item.description}</div>}
              </div>
            ))}
          </>
        )}

        <div style={sectionHeadingStyle}>
          <span>Items</span>
        </div>
        {!snapshot ? (
          <div style={emptyStyle}>loading...</div>
        ) : unequippedItems.length === 0 && equippedItems.length === 0 ? (
          <div style={emptyStyle} data-testid="inventory-empty">
            Your pack is empty.
          </div>
        ) : unequippedItems.length === 0 ? (
          <div style={emptyStyle}>All items are equipped.</div>
        ) : (
          unequippedItems.map((item) => (
            <div key={item.id} style={itemRowStyle}>
              <div style={itemNameStyle}>
                <span>
                  {item.name}
                  {item.isStackable && (item.quantity ?? 1) > 1 && (
                    <span style={quantityTagStyle}>x{item.quantity}</span>
                  )}
                </span>
                <span style={{ display: 'flex', alignItems: 'center' }}>
                  <span style={{ color: '#888888', fontSize: '11px' }}>
                    {item.category ?? ''} W{item.workmanship}
                  </span>
                  {isEquippable(item.category) && (
                    <button
                      type="button"
                      style={equipBtnStyle}
                      onClick={() => handleEquip(item.id)}
                      aria-label={`equip ${item.name}`}
                    >
                      equip
                    </button>
                  )}
                  {isSalvageable(item.category) && (
                    <button
                      type="button"
                      style={salvageBtnStyle}
                      onClick={() => handleSalvage(item.id)}
                      aria-label={`salvage ${item.name}`}
                    >
                      salvage
                    </button>
                  )}
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
