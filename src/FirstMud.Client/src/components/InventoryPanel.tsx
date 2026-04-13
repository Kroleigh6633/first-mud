import { useState } from 'react';
import type { InventorySnapshot, EquipmentSlots } from '../types/game';
import {
  overlayStyle, panelStyle, headerStyle, sectionHeadingStyle,
  skillRowStyle, closeBtnStyle, salvageAllBtnStyle, slotsStyle, emptyStyle,
} from './inventoryStyles';
import InventoryItemRow, { isEquipped, maxSalvageableWorkmanship } from './InventoryItemRow';
import { useGameCommands, type SendCommandFn } from '../hooks/useGameCommands';

interface Props {
  snapshot: InventorySnapshot | null;
  equipment: EquipmentSlots;
  onClose: () => void;
  sendCommand: SendCommandFn;
  atHomestead?: boolean;
  hasSalvager?: boolean;
}

export default function InventoryPanel({ snapshot, equipment, onClose, sendCommand, atHomestead = false, hasSalvager = false }: Props) {
  const commands = useGameCommands(sendCommand);
  const [imbuingItemId, setImbuingItemId] = useState<string | null>(null);

  const handleEquip       = (itemId: string, slot?: string) => commands.equip({ itemId, slot });
  const handleUnequip     = (slot: string)                  => commands.unequip({ slot });
  const handleSalvage     = (itemId: string)                => commands.salvage({ itemId });
  const handleQueueSalvage= (itemId: string)                => commands.queueSalvage({ itemId });
  const handleSalvageAll  = (category: string)              => commands.salvageAll({ category });
  const handleToggleLock  = (itemId: string)                => commands.lockItem({ itemId });

  const handleAutoSalvageChange = (category: string, value: string) =>
    commands.autoSalvage({ category, maxWorkmanship: parseInt(value, 10) });

  const handleStore = (itemId: string) => {
    commands.deposit({ itemId });
    setTimeout(() => commands.openInventory(), 400);
  };

  const handleUseConsumable = (itemId: string) => {
    commands.useConsumable({ itemId });
    setTimeout(() => commands.openInventory(), 400);
  };

  const handleImbue = (itemId: string, taperId: string) => {
    commands.imbue({ itemId, taperId });
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

  // Practice enchanting eligibility: have imbue-able items AND tapers
  const imbueableItems = unequippedItems.filter(i =>
    (i.category?.toLowerCase() === 'weapon' || i.category?.toLowerCase() === 'armor')
    && !i.isLocked
    && (i.imbues?.length ?? 0) < (i.maxImbueSlots ?? 1)
  );
  const hasTapers = allItems.some(i => i.category === 'Reagent');
  const canPracticeEnchanting = imbueableItems.length > 0 && hasTapers;

  const handlePracticeEnchanting = () => commands.practiceEnchanting();

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
        {(hasWeapons || hasArmor || canPracticeEnchanting) && (
          <div style={{ padding: '4px 14px 8px', display: 'flex', gap: '8px', flexWrap: 'wrap' }}>
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
            {canPracticeEnchanting && (
              <button
                type="button"
                style={{
                  ...salvageAllBtnStyle,
                  borderColor: '#9944ff',
                  color: '#cc88ff',
                  background: '#1a0033',
                }}
                onClick={handlePracticeEnchanting}
                aria-label="practice enchanting"
                title={`Imbue ${imbueableItems.length} item(s) with available tapers, then auto-salvage fully imbued items`}
              >
                practice enchanting ({imbueableItems.length})
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
