import React from 'react';
import type { InventoryItem, EquipmentSlots } from '../types/game';
import {
  itemRowStyle, itemNameStyle, itemDescStyle,
  quantityTagStyle, equippedTagStyle,
  equipBtnStyle, salvageBtnStyle, salvageBtnDisabledStyle,
  storeBtnStyle, queueSalvageBtnStyle, lockBtnStyle, lockBtnLockedStyle,
  imbueBtnStyle, useBtnStyle, unequipBtnStyle,
} from './inventoryStyles';
import { renderImbueSlots, renderImbueDetails, ImbuePanel } from './ImbuePanel';

const TAPER_KEYWORDS = [
  'fire shaping taper', 'water shaping taper', 'earth shaping taper', 'air shaping taper',
  'fortitude taper', 'warding taper', 'wyrd shard', 'dravenite dust',
  'fire shard', 'water shard', 'earth shard', 'air shard',
];

export function isEquippable(slot?: string): boolean {
  return !!slot && slot !== 'None';
}

export function isSalvageable(category?: string): boolean {
  if (!category) return false;
  const c = category.toLowerCase();
  return c === 'weapon' || c === 'armor' || c === 'consumable';
}

export function isEquipped(itemId: string, equipment: EquipmentSlots): boolean {
  if (equipment.equippedItems) {
    return Object.values(equipment.equippedItems).includes(itemId);
  }
  return equipment.weaponId === itemId
    || equipment.armorId === itemId
    || equipment.accessoryId === itemId;
}

export function maxSalvageableWorkmanship(skill: number): number {
  if (skill <= 5) return 3;
  if (skill <= 10) return 5;
  if (skill <= 20) return 7;
  return 10;
}

// -------------------------------------------------------------------------
// Props
// -------------------------------------------------------------------------

interface InventoryItemRowProps {
  item: InventoryItem;
  allItems: InventoryItem[];
  isEquippedItem: boolean;
  maxSalvageable: number;
  atHomestead: boolean;
  hasSalvager: boolean;
  imbuingItemId: string | null;
  craftingSkill: number;
  onEquip: (itemId: string, slot?: string) => void;
  onUnequip: (slot: string) => void;
  onSalvage: (itemId: string) => void;
  onQueueSalvage: (itemId: string) => void;
  onStore: (itemId: string) => void;
  onUseConsumable: (itemId: string) => void;
  onToggleLock: (itemId: string) => void;
  onImbue: (itemId: string, taperId: string) => void;
  onSetImbuingItem: (itemId: string | null) => void;
}

// -------------------------------------------------------------------------
// Component
// -------------------------------------------------------------------------

export default function InventoryItemRow({
  item,
  allItems,
  isEquippedItem,
  maxSalvageable,
  atHomestead,
  hasSalvager,
  imbuingItemId,
  craftingSkill,
  onEquip,
  onUnequip,
  onSalvage,
  onQueueSalvage,
  onStore,
  onUseConsumable,
  onToggleLock,
  onImbue,
  onSetImbuingItem,
}: InventoryItemRowProps) {
  const isImbueable = item.category === 'Weapon' || item.category === 'Armor' || item.category === 'Accessory';
  const hasOpenSlot = (item.imbues?.length ?? 0) < (item.maxImbueSlots ?? 1);
  const isShowingImbuePanel = imbuingItemId === item.id;

  const availableTapers = allItems.filter(t =>
    t.category === 'Reagent' &&
    t.id !== item.id &&
    TAPER_KEYWORDS.some(k => t.name.toLowerCase().includes(k.split(' ')[0]))
  );

  const rowStyle: React.CSSProperties = {
    ...itemRowStyle,
    ...(item.isLocked ? { borderLeft: '2px solid #ffcc00' } : {}),
    ...(item.isUnstable ? { borderLeft: '2px solid #cc44ff' } : {}),
  };

  return (
    <div key={item.id} style={rowStyle} data-testid={isEquippedItem ? 'equipped-item' : undefined}>
      <div style={itemNameStyle}>
        <span>
          {item.name}
          {item.isStackable && (item.quantity ?? 1) > 1 && (
            <span style={quantityTagStyle}>x{item.quantity}</span>
          )}
          {isImbueable && renderImbueSlots(item)}
          {isEquippedItem && (
            <span style={equippedTagStyle}>
              {item.slot && item.slot !== 'None' ? item.slot : 'equipped'}
            </span>
          )}
          {item.isUnstable && !isEquippedItem && (
            <span style={{ color: '#cc44ff', fontSize: '10px', marginLeft: '4px' }} title="Unstable — may lose imbues in combat">
              [UNSTABLE]
            </span>
          )}
        </span>
        <span style={{ display: 'flex', alignItems: 'center' }}>
          <span style={{ color: '#888888', fontSize: '11px' }}>
            {(() => {
              const slotLabel = item.slot && item.slot !== 'None'
                ? item.slot.replace(/([A-Z])/g, ' $1').trim()
                : item.category;
              return slotLabel ?? '';
            })()} W{item.workmanship}
          </span>
          {/* Lock/star toggle */}
          <button
            type="button"
            style={item.isLocked ? lockBtnLockedStyle : lockBtnStyle}
            onClick={() => onToggleLock(item.id)}
            title={item.isLocked ? 'Locked — click to unlock' : 'Click to lock (prevents salvage)'}
            aria-label={item.isLocked ? `unlock ${item.name}` : `lock ${item.name}`}
          >
            ★
          </button>
          {/* Equip (unequipped only) */}
          {!isEquippedItem && isEquippable(item.slot) && (
            <button
              type="button"
              style={equipBtnStyle}
              onClick={() => onEquip(item.id, item.slot)}
              aria-label={`equip ${item.name}`}
              title={item.slot ? `Equip to ${item.slot} slot` : undefined}
            >
              equip
            </button>
          )}
          {/* Imbue */}
          {isImbueable && (
            <button
              type="button"
              style={isShowingImbuePanel ? { ...imbueBtnStyle, background: '#1a0a2a' } : imbueBtnStyle}
              onClick={() => onSetImbuingItem(isShowingImbuePanel ? null : item.id)}
              aria-label={`imbue ${item.name}`}
              title={!hasOpenSlot ? 'All slots filled — overimbuing is risky!' : 'Imbue this item'}
            >
              imbue
            </button>
          )}
          {/* Use consumable */}
          {!isEquippedItem && item.category === 'Consumable' && (
            <button
              type="button"
              style={useBtnStyle}
              onClick={() => onUseConsumable(item.id)}
              aria-label={`use ${item.name}`}
              title={`Use ${item.name}`}
            >
              use
            </button>
          )}
          {/* Store at homestead */}
          {!isEquippedItem && atHomestead && (
            <button
              type="button"
              style={storeBtnStyle}
              onClick={() => onStore(item.id)}
              aria-label={`store ${item.name}`}
            >
              store
            </button>
          )}
          {/* Unequip */}
          {isEquippedItem && item.slot && item.slot !== 'None' && (
            <button
              type="button"
              style={unequipBtnStyle}
              onClick={() => onUnequip(item.slot!)}
              aria-label={`unequip ${item.name}`}
              title={`Move ${item.name} back to inventory`}
            >
              unequip
            </button>
          )}
          {/* Salvage */}
          {!isEquippedItem && isSalvageable(item.category) && (() => {
            const tooHighSkill = (item.workmanship ?? 1) > maxSalvageable;
            const locked = item.isLocked ?? false;
            const disabled = tooHighSkill || locked;
            const titleText = locked
              ? `${item.name} is locked — unlock (★) to salvage`
              : tooHighSkill
              ? `Skill too low (need skill to reach W${item.workmanship})`
              : undefined;
            return (
              <>
                <button
                  type="button"
                  style={disabled ? salvageBtnDisabledStyle : salvageBtnStyle}
                  onClick={() => !disabled && onSalvage(item.id)}
                  disabled={disabled}
                  title={titleText}
                  aria-label={`salvage ${item.name}`}
                >
                  salvage
                </button>
                {hasSalvager && !locked && (
                  <button
                    type="button"
                    style={queueSalvageBtnStyle}
                    onClick={() => onQueueSalvage(item.id)}
                    title="Add to homestead salvage queue (Salvager companion will process it)"
                    aria-label={`queue ${item.name} for homestead salvage`}
                  >
                    queue
                  </button>
                )}
              </>
            );
          })()}
        </span>
      </div>
      {item.description && <div style={itemDescStyle}>{item.description}</div>}
      {isImbueable && renderImbueDetails(item)}
      {isShowingImbuePanel && (
        <ImbuePanel
          item={item}
          availableTapers={availableTapers}
          craftingSkill={craftingSkill}
          onImbue={onImbue}
          onCancel={() => onSetImbuingItem(null)}
        />
      )}
    </div>
  );
}
