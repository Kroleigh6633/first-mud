import { useState } from 'react';
import type { InventorySnapshot, EquipmentSlots } from '../types/game';
import {
  overlayStyle, panelStyle, headerStyle, sectionHeadingStyle,
  skillRowStyle, closeBtnStyle, salvageAllBtnStyle, slotsStyle, emptyStyle,
} from './inventoryStyles';
import InventoryItemRow, { isEquipped, maxSalvageableWorkmanship } from './InventoryItemRow';

interface Props {
  snapshot: InventorySnapshot | null;
  equipment: EquipmentSlots;
  onClose: () => void;
  sendCommand: (command: string, payload?: unknown) => void;
  atHomestead?: boolean;
  hasSalvager?: boolean;
}

export default function InventoryPanel({ snapshot, equipment, onClose, sendCommand, atHomestead = false, hasSalvager = false }: Props) {
  const [imbuingItemId, setImbuingItemId] = useState<string | null>(null);

  const handleEquip       = (itemId: string, slot?: string) => sendCommand('equip', { itemId, slot });
  const handleUnequip     = (slot: string)                  => sendCommand('unequip', { slot });
  const handleSalvage     = (itemId: string)                => sendCommand('salvage', { itemId });
  const handleQueueSalvage= (itemId: string)                => sendCommand('queuesalvage', { itemId });
  const handleSalvageAll  = (category: string)              => sendCommand('salvageall', { category });
  const handleToggleLock  = (itemId: string)                => sendCommand('lockitem', { itemId });

  const handleAutoSalvageChange = (category: string, value: string) =>
    sendCommand('autosalvage', { category, maxWorkmanship: parseInt(value, 10) });

  const handleStore = (itemId: string) => {
    sendCommand('deposit', { itemId });
    setTimeout(() => sendCommand('openinventory', null), 400);
  };

  const handleUseConsumable = (itemId: string) => {
    sendCommand('useconsumable', { itemId });
    setTimeout(() => sendCommand('openinventory', null), 400);
  };

  const handleImbue = (itemId: string, taperId: string) => {
    sendCommand('imbue', { itemId, taperId });
    setImbuingItemId(null);
  };

  const salvageSkill    = snapshot?.salvageSkill ?? 1;
  const craftingSkill   = snapshot?.craftingSkill ?? 1;
  const maxSalvageable  = maxSalvageableWorkmanship(salvageSkill);
  const weaponThreshold = snapshot?.autoSalvageWeaponThreshold ?? 0;
  const armorThreshold  = snapshot?.autoSalvageArmorThreshold ?? 0;

  const allItems       = snapshot?.items ?? [];
  const equippedItems  = allItems.filter(i => isEquipped(i.id, equipment));
  const unequippedItems= allItems.filter(i => !isEquipped(i.id, equipment));

  const slotsUsed  = unequippedItems.length;
  const totalItems = allItems.reduce((acc, i) => acc + (i.quantity ?? 1), 0);
  const maxSlots   = 20;

  const hasWeapons = unequippedItems.some(i => i.category?.toLowerCase() === 'weapon');
  const hasArmor   = unequippedItems.some(i => i.category?.toLowerCase() === 'armor');

  const sharedRowProps = {
    allItems,
    maxSalvageable,
    atHomestead,
    hasSalvager,
    imbuingItemId,
    craftingSkill,
    onEquip: handleEquip,
    onUnequip: handleUnequip,
    onSalvage: handleSalvage,
    onQueueSalvage: handleQueueSalvage,
    onStore: handleStore,
    onUseConsumable: handleUseConsumable,
    onToggleLock: handleToggleLock,
    onImbue: handleImbue,
    onSetImbuingItem: setImbuingItemId,
  };

  return (
    <div
      style={overlayStyle}
      data-testid="inventory-overlay"
      onClick={(e) => { if (e.target === e.currentTarget) onClose(); }}
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

        {/* Auto-Salvage Settings */}
        {snapshot && (
          <>
            <div style={sectionHeadingStyle}>
              <span>Auto-Salvage Settings</span>
            </div>
            <div style={{ padding: '6px 14px', display: 'flex', flexDirection: 'column', gap: '6px' }}>
              {(['weapon', 'armor'] as const).map(cat => (
                <div key={cat} style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', fontSize: '12px', color: '#aaaaaa' }}>
                  <span>Auto-salvage {cat}s &le;</span>
                  <select
                    value={cat === 'weapon' ? weaponThreshold : armorThreshold}
                    onChange={(e) => handleAutoSalvageChange(cat, e.target.value)}
                    style={{ background: '#0d0d0d', border: '1px solid #1a3a1a', color: '#ff8800', fontFamily: 'monospace', fontSize: '11px', padding: '1px 4px' }}
                    aria-label={`auto-salvage ${cat} threshold`}
                  >
                    {[0, 1, 2, 3, 4, 5, 6, 7, 8].map(v => (
                      <option key={v} value={v}>{v === 0 ? 'Off' : `W${v}`}</option>
                    ))}
                  </select>
                </div>
              ))}
            </div>
          </>
        )}

        {/* Bulk salvage buttons */}
        {(hasWeapons || hasArmor) && (
          <div style={{ padding: '4px 14px 8px', display: 'flex', gap: '8px' }}>
            {hasWeapons && (
              <button type="button" style={salvageAllBtnStyle} onClick={() => handleSalvageAll('Weapon')} aria-label="salvage all weapons">
                salvage all weapons
              </button>
            )}
            {hasArmor && (
              <button type="button" style={salvageAllBtnStyle} onClick={() => handleSalvageAll('Armor')} aria-label="salvage all armor">
                salvage all armor
              </button>
            )}
          </div>
        )}

        {/* Equipped section */}
        {equippedItems.length > 0 && (
          <>
            <div style={sectionHeadingStyle}><span>Equipped</span></div>
            {equippedItems.map(item => (
              <InventoryItemRow key={item.id} item={item} isEquippedItem {...sharedRowProps} />
            ))}
          </>
        )}

        {/* Items section */}
        <div style={sectionHeadingStyle}>
          <span>Items</span>
          {!atHomestead && (
            <span style={{ color: '#666666', fontSize: '10px', fontStyle: 'italic' }}>
              [P] portal home · [V] storage
            </span>
          )}
        </div>
        {!snapshot ? (
          <div style={emptyStyle}>loading...</div>
        ) : unequippedItems.length === 0 && equippedItems.length === 0 ? (
          <div style={emptyStyle} data-testid="inventory-empty">Your pack is empty.</div>
        ) : unequippedItems.length === 0 ? (
          <div style={emptyStyle}>All items are equipped.</div>
        ) : (
          unequippedItems.map(item => (
            <InventoryItemRow key={item.id} item={item} isEquippedItem={false} {...sharedRowProps} />
          ))
        )}
      </div>
    </div>
  );
}
