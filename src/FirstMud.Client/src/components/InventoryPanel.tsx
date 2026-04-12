import React, { useState } from 'react';
import type { InventorySnapshot, EquipmentSlots, InventoryItem } from '../types/game';

interface Props {
  snapshot: InventorySnapshot | null;
  equipment: EquipmentSlots;
  onClose: () => void;
  sendCommand: (command: string, payload?: unknown) => void;
  atHomestead?: boolean;
  hasSalvager?: boolean;
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

const salvageBtnDisabledStyle: React.CSSProperties = {
  ...salvageBtnStyle,
  border: '1px solid #555555',
  color: '#555555',
  cursor: 'not-allowed',
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

const storeBtnStyle: React.CSSProperties = {
  background: 'none',
  border: '1px solid #ccaa00',
  color: '#ccaa00',
  fontFamily: 'monospace',
  fontSize: '11px',
  padding: '1px 6px',
  cursor: 'pointer',
  marginLeft: '6px',
};

const queueSalvageBtnStyle: React.CSSProperties = {
  background: 'none',
  border: '1px solid #888844',
  color: '#aaa844',
  fontFamily: 'monospace',
  fontSize: '11px',
  padding: '1px 6px',
  cursor: 'pointer',
  marginLeft: '6px',
};

const lockBtnStyle: React.CSSProperties = {
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

const lockBtnLockedStyle: React.CSSProperties = {
  ...lockBtnStyle,
  color: '#ffcc00',
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

const imbueBtnStyle: React.CSSProperties = {
  background: 'none',
  border: '1px solid #cc44ff',
  color: '#cc44ff',
  fontFamily: 'monospace',
  fontSize: '11px',
  padding: '1px 6px',
  cursor: 'pointer',
  marginLeft: '6px',
};

const unequipBtnStyle: React.CSSProperties = {
  background: 'none',
  border: '1px solid #ff8800',
  color: '#ff8800',
  fontFamily: 'monospace',
  fontSize: '11px',
  padding: '1px 6px',
  cursor: 'pointer',
  marginLeft: '6px',
};


const imbuePanelStyle: React.CSSProperties = {
  background: '#0a0a1a',
  border: '1px solid #cc44ff',
  padding: '8px 12px',
  margin: '4px 0',
  fontSize: '12px',
};

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

function imbueSymbol(_type: string): string {
  return '✦';
}

function imbueColor(type: string): string {
  return imbueTypeColors[type] ?? '#888888';
}

function renderImbueSlots(item: InventoryItem): React.ReactNode {
  const maxSlots = item.maxImbueSlots ?? 1;
  const imbues = item.imbues ?? [];
  const slots: React.ReactNode[] = [];

  for (let i = 0; i < maxSlots; i++) {
    const imbue = imbues[i];
    if (imbue) {
      slots.push(
        <span
          key={i}
          title={`${imbue.type} +${Math.round(imbue.power * 100)}%`}
          style={{ color: imbueColor(imbue.type), cursor: 'help' }}
        >
          {imbueSymbol(imbue.type)}
        </span>
      );
    } else {
      slots.push(
        <span key={i} style={{ color: '#333333' }}>○</span>
      );
    }
  }

  return <span style={{ marginLeft: '6px', letterSpacing: '2px' }}>{slots}</span>;
}

/** Returns the max Workmanship the player can salvage given their SalvageSkill. */
function maxSalvageableWorkmanship(skill: number): number {
  if (skill <= 5) return 3;
  if (skill <= 10) return 5;
  if (skill <= 20) return 7;
  return 10;
}

function isEquippable(slot?: string): boolean {
  if (!slot) return false;
  return slot !== 'None';
}

function isSalvageable(category?: string): boolean {
  if (!category) return false;
  const c = category.toLowerCase();
  return c === 'weapon' || c === 'armor' || c === 'consumable';
}

function isEquipped(itemId: string, equipment: EquipmentSlots): boolean {
  if (equipment.equippedItems) {
    return Object.values(equipment.equippedItems).includes(itemId);
  }
  // Legacy fallback
  return equipment.weaponId === itemId
    || equipment.armorId === itemId
    || equipment.accessoryId === itemId;
}

export default function InventoryPanel({ snapshot, equipment, onClose, sendCommand, atHomestead = false, hasSalvager = false }: Props) {
  // imbuingItemId: the item currently waiting for a taper selection (null = none)
  const [imbuingItemId, setImbuingItemId] = useState<string | null>(null);

  const handleEquip = (itemId: string, slot?: string) => {
    sendCommand('equip', { itemId, slot });
  };

  const handleSalvage = (itemId: string) => {
    sendCommand('salvage', { itemId });
  };

  const handleQueueSalvage = (itemId: string) => {
    sendCommand('queuesalvage', { itemId });
  };

  const handleImbue = (itemId: string, taperId: string) => {
    sendCommand('imbue', { itemId, taperId });
    setImbuingItemId(null);
  };

  const handleSalvageAll = (category: string) => {
    sendCommand('salvageall', { category });
  };

  const handleAutoSalvageChange = (category: string, value: string) => {
    const maxWorkmanship = parseInt(value, 10);
    sendCommand('autosalvage', { category, maxWorkmanship });
  };

  const handleStore = (itemId: string) => {
    sendCommand('deposit', { itemId });
    // Re-request inventory after a short delay so the deposited item disappears
    // from the panel without the player needing to open Storage (V) first.
    setTimeout(() => sendCommand('openinventory', null), 400);
  };

  const handleToggleLock = (itemId: string) => {
    sendCommand('lockitem', { itemId });
  };

  const handleUnequip = (slot: string) => {
    sendCommand('unequip', { slot });
  };

  const salvageSkill = snapshot?.salvageSkill ?? 1;
  const maxSalvageable = maxSalvageableWorkmanship(salvageSkill);
  const weaponThreshold = snapshot?.autoSalvageWeaponThreshold ?? 0;
  const armorThreshold = snapshot?.autoSalvageArmorThreshold ?? 0;

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

        {/* Auto-Salvage Settings */}
        {snapshot && (
          <>
            <div style={sectionHeadingStyle}>
              <span>Auto-Salvage Settings</span>
            </div>
            <div style={{ padding: '6px 14px', display: 'flex', flexDirection: 'column', gap: '6px' }}>
              <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', fontSize: '12px', color: '#aaaaaa' }}>
                <span>Auto-salvage weapons &le;</span>
                <select
                  value={weaponThreshold}
                  onChange={(e) => handleAutoSalvageChange('weapon', e.target.value)}
                  style={{ background: '#0d0d0d', border: '1px solid #1a3a1a', color: '#ff8800', fontFamily: 'monospace', fontSize: '11px', padding: '1px 4px' }}
                  aria-label="auto-salvage weapon threshold"
                >
                  <option value={0}>Off</option>
                  <option value={1}>W1</option>
                  <option value={2}>W2</option>
                  <option value={3}>W3</option>
                  <option value={4}>W4</option>
                  <option value={5}>W5</option>
                </select>
              </div>
              <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', fontSize: '12px', color: '#aaaaaa' }}>
                <span>Auto-salvage armor &le;</span>
                <select
                  value={armorThreshold}
                  onChange={(e) => handleAutoSalvageChange('armor', e.target.value)}
                  style={{ background: '#0d0d0d', border: '1px solid #1a3a1a', color: '#ff8800', fontFamily: 'monospace', fontSize: '11px', padding: '1px 4px' }}
                  aria-label="auto-salvage armor threshold"
                >
                  <option value={0}>Off</option>
                  <option value={1}>W1</option>
                  <option value={2}>W2</option>
                  <option value={3}>W3</option>
                  <option value={4}>W4</option>
                  <option value={5}>W5</option>
                </select>
              </div>
            </div>
          </>
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
            {equippedItems.map((item) => {
              const isImbueable = (item.category === 'Weapon' || item.category === 'Armor' || item.category === 'Accessory');
              const hasOpenSlot = (item.imbues?.length ?? 0) < (item.maxImbueSlots ?? 1);
              const availableTapers = snapshot?.items.filter(t =>
                t.category === 'Reagent' &&
                t.id !== item.id &&
                ['fire shaping taper', 'water shaping taper', 'earth shaping taper', 'air shaping taper',
                 'fortitude taper', 'warding taper', 'wyrd shard', 'dravenite dust',
                 'fire shard', 'water shard', 'earth shard', 'air shard'].some(k => t.name.toLowerCase().includes(k.split(' ')[0]))
              ) ?? [];
              const isShowingImbuePanel = imbuingItemId === item.id;

              return (
                <div
                  key={item.id}
                  style={{
                    ...itemRowStyle,
                    ...(item.isLocked ? { borderLeft: '2px solid #ffcc00' } : {}),
                  }}
                  data-testid="equipped-item"
                >
                  <div style={itemNameStyle}>
                    <span>
                      {item.name}
                      {item.isStackable && (item.quantity ?? 1) > 1 && (
                        <span style={quantityTagStyle}>x{item.quantity}</span>
                      )}
                      {isImbueable && renderImbueSlots(item)}
                      <span style={equippedTagStyle}>
                        {item.slot && item.slot !== 'None' ? item.slot : 'equipped'}
                      </span>
                    </span>
                    <span style={{ display: 'flex', alignItems: 'center' }}>
                      <span style={{ color: '#888888', fontSize: '11px' }}>
                        {item.category ?? ''} W{item.workmanship}
                      </span>
                      {/* Lock/star toggle */}
                      <button
                        type="button"
                        style={item.isLocked ? lockBtnLockedStyle : lockBtnStyle}
                        onClick={() => handleToggleLock(item.id)}
                        title={item.isLocked ? 'Locked — click to unlock' : 'Click to lock (prevents salvage)'}
                        aria-label={item.isLocked ? `unlock ${item.name}` : `lock ${item.name}`}
                      >
                        ★
                      </button>
                      {/* Imbue button */}
                      {isImbueable && (
                        <button
                          type="button"
                          style={isShowingImbuePanel ? { ...imbueBtnStyle, background: '#1a0a2a' } : imbueBtnStyle}
                          onClick={() => setImbuingItemId(isShowingImbuePanel ? null : item.id)}
                          aria-label={`imbue ${item.name}`}
                          title={!hasOpenSlot ? 'All slots filled — overimbuing is risky!' : 'Imbue this item'}
                        >
                          imbue
                        </button>
                      )}
                      {/* Unequip button */}
                      {item.slot && item.slot !== 'None' && (
                        <button
                          type="button"
                          style={unequipBtnStyle}
                          onClick={() => handleUnequip(item.slot!)}
                          aria-label={`unequip ${item.name}`}
                          title={`Move ${item.name} back to inventory`}
                        >
                          unequip
                        </button>
                      )}
                    </span>
                  </div>
                  {item.description && <div style={itemDescStyle}>{item.description}</div>}
                  {/* Imbue sub-panel */}
                  {isShowingImbuePanel && (
                    <div style={imbuePanelStyle}>
                      <div style={{ color: '#cc44ff', marginBottom: '6px', fontSize: '11px' }}>
                        SELECT TAPER TO IMBUE — {item.name}
                        {!hasOpenSlot && (
                          <span style={{ color: '#ff4422', marginLeft: '8px' }}>
                            ⚠ OVERIMBUING: 50% catastrophic failure
                          </span>
                        )}
                      </div>
                      {availableTapers.length === 0 ? (
                        <div style={{ color: '#666666', fontStyle: 'italic' }}>
                          No imbuing reagents in inventory.
                        </div>
                      ) : (
                        availableTapers.map(taper => (
                          <div key={taper.id} style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '2px 0' }}>
                            <span style={{ color: '#ccaaff' }}>
                              {taper.name}
                              {(taper.quantity ?? 1) > 1 && <span style={quantityTagStyle}>x{taper.quantity}</span>}
                            </span>
                            <button
                              type="button"
                              style={imbueBtnStyle}
                              onClick={() => handleImbue(item.id, taper.id)}
                              aria-label={`apply ${taper.name} to ${item.name}`}
                            >
                              apply
                            </button>
                          </div>
                        ))
                      )}
                      <button
                        type="button"
                        style={{ ...imbueBtnStyle, marginTop: '4px', marginLeft: '0' }}
                        onClick={() => setImbuingItemId(null)}
                      >
                        cancel
                      </button>
                    </div>
                  )}
                </div>
              );
            })}
          </>
        )}

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
          <div style={emptyStyle} data-testid="inventory-empty">
            Your pack is empty.
          </div>
        ) : unequippedItems.length === 0 ? (
          <div style={emptyStyle}>All items are equipped.</div>
        ) : (
          unequippedItems.map((item) => {
            const isImbueable = (item.category === 'Weapon' || item.category === 'Armor' || item.category === 'Accessory');
            const hasOpenSlot = (item.imbues?.length ?? 0) < (item.maxImbueSlots ?? 1);
            const availableTapers = snapshot?.items.filter(t =>
              t.category === 'Reagent' &&
              t.id !== item.id &&
              ['fire shaping taper', 'water shaping taper', 'earth shaping taper', 'air shaping taper',
               'fortitude taper', 'warding taper', 'wyrd shard', 'dravenite dust',
               'fire shard', 'water shard', 'earth shard', 'air shard'].some(k => t.name.toLowerCase().includes(k.split(' ')[0]))
            ) ?? [];
            const isShowingImbuePanel = imbuingItemId === item.id;

            return (
              <div
                key={item.id}
                style={{
                  ...itemRowStyle,
                  ...(item.isLocked ? { borderLeft: '2px solid #ffcc00' } : {}),
                  ...(item.isUnstable ? { borderLeft: '2px solid #cc44ff', animation: 'none' } : {}),
                }}
              >
                <div style={itemNameStyle}>
                  <span>
                    {item.name}
                    {item.isStackable && (item.quantity ?? 1) > 1 && (
                      <span style={quantityTagStyle}>x{item.quantity}</span>
                    )}
                    {isImbueable && renderImbueSlots(item)}
                    {item.isUnstable && (
                      <span style={{ color: '#cc44ff', fontSize: '10px', marginLeft: '4px' }} title="Unstable — may lose imbues in combat">
                        [UNSTABLE]
                      </span>
                    )}
                  </span>
                  <span style={{ display: 'flex', alignItems: 'center' }}>
                    <span style={{ color: '#888888', fontSize: '11px' }}>
                      {item.category ?? ''} W{item.workmanship}
                    </span>
                    {/* Lock/star toggle — always visible */}
                    <button
                      type="button"
                      style={item.isLocked ? lockBtnLockedStyle : lockBtnStyle}
                      onClick={() => handleToggleLock(item.id)}
                      title={item.isLocked ? 'Locked — click to unlock' : 'Click to lock (prevents salvage)'}
                      aria-label={item.isLocked ? `unlock ${item.name}` : `lock ${item.name}`}
                    >
                      ★
                    </button>
                    {isEquippable(item.slot) && (
                      <button
                        type="button"
                        style={equipBtnStyle}
                        onClick={() => handleEquip(item.id, item.slot)}
                        aria-label={`equip ${item.name}`}
                        title={item.slot ? `Equip to ${item.slot} slot` : undefined}
                      >
                        equip
                      </button>
                    )}
                    {isImbueable && (
                        <button
                          type="button"
                          style={isShowingImbuePanel ? { ...imbueBtnStyle, background: '#1a0a2a' } : imbueBtnStyle}
                          onClick={() => setImbuingItemId(isShowingImbuePanel ? null : item.id)}
                          aria-label={`imbue ${item.name}`}
                          title={!hasOpenSlot ? 'All slots filled — overimbuing is risky!' : 'Imbue this item'}
                        >
                          imbue
                        </button>
                    )}
                    {atHomestead && (
                      <button
                        type="button"
                        style={storeBtnStyle}
                        onClick={() => handleStore(item.id)}
                        aria-label={`store ${item.name}`}
                      >
                        store
                      </button>
                    )}
                    {isSalvageable(item.category) && (() => {
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
                            onClick={() => !disabled && handleSalvage(item.id)}
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
                              onClick={() => handleQueueSalvage(item.id)}
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
                {/* Imbue sub-panel */}
                {isShowingImbuePanel && (
                  <div style={imbuePanelStyle}>
                    <div style={{ color: '#cc44ff', marginBottom: '6px', fontSize: '11px' }}>
                      SELECT TAPER TO IMBUE — {item.name}
                      {!hasOpenSlot && (
                        <span style={{ color: '#ff4422', marginLeft: '8px' }}>
                          ⚠ OVERIMBUING: 50% catastrophic failure
                        </span>
                      )}
                    </div>
                    {availableTapers.length === 0 ? (
                      <div style={{ color: '#666666', fontStyle: 'italic' }}>
                        No imbuing reagents in inventory.
                      </div>
                    ) : (
                      availableTapers.map(taper => (
                        <div key={taper.id} style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '2px 0' }}>
                          <span style={{ color: '#ccaaff' }}>
                            {taper.name}
                            {(taper.quantity ?? 1) > 1 && <span style={quantityTagStyle}>x{taper.quantity}</span>}
                          </span>
                          <button
                            type="button"
                            style={imbueBtnStyle}
                            onClick={() => handleImbue(item.id, taper.id)}
                            aria-label={`apply ${taper.name} to ${item.name}`}
                          >
                            apply
                          </button>
                        </div>
                      ))
                    )}
                    <button
                      type="button"
                      style={{ ...imbueBtnStyle, marginTop: '4px', marginLeft: '0' }}
                      onClick={() => setImbuingItemId(null)}
                    >
                      cancel
                    </button>
                  </div>
                )}
              </div>
            );
          })
        )}
      </div>
    </div>
  );
}
