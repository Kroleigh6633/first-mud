import React, { useState, useEffect, useRef } from 'react';
import type { StorageViewSnapshot, AppliedImbue, SmeltCompleteEvent } from '../types/game';
import { SoundEffects } from '../audio/SoundEffects';

/** Ores considered "rare" — get a gold flash + sound effect during animation. */
const RARE_ORES = new Set(['Silver Ore', 'Mithril Ore']);

interface Props {
  snapshot: StorageViewSnapshot | null;
  inventoryItems: { id: string; name: string; description: string; workmanship: number; category?: string; quantity?: number; isStackable?: boolean; isUnstable?: boolean; maxImbueSlots?: number; imbues?: AppliedImbue[] }[];
  onDeposit: (itemId: string) => void;
  onWithdraw: (itemId: string) => void;
  onClose: () => void;
  onSmelt?: (amount: number) => void;
  atHomestead?: boolean;
  lastSmeltResult?: SmeltCompleteEvent | null;
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

export default function StoragePanel({ snapshot, inventoryItems, onDeposit, onWithdraw, onClose, onSmelt, atHomestead, lastSmeltResult }: Props) {
  const [activeTab, setActiveTab] = useState<TabCategory>('All');
  const [smeltAmount, setSmeltAmount] = useState<number>(10);

  // ── Smelt animation state ──────────────────────────────────────────────────
  /**
   * Expanded list of individual ore discoveries to animate through.
   * Each SmeltYield { name, quantity } is "unrolled" so we reveal them
   * one at a time at 0.5 s intervals.
   */
  const [smeltQueue, setSmeltQueue] = useState<string[]>([]);
  // Running tally: oreName → count seen so far
  const [smeltTally, setSmeltTally] = useState<Map<string, number>>(new Map());
  const [smeltRevealIdx, setSmeltRevealIdx] = useState(0);
  // Track which result we've already started animating
  const lastSmeltRef = useRef<SmeltCompleteEvent | null>(null);

  useEffect(() => {
    if (!lastSmeltResult) return;
    if (lastSmeltResult === lastSmeltRef.current) return;
    lastSmeltRef.current = lastSmeltResult;

    // Unroll yields into individual ore names for one-by-one reveal
    const queue: string[] = [];
    for (const y of lastSmeltResult.yields) {
      for (let i = 0; i < y.quantity; i++) {
        queue.push(y.name);
      }
    }
    // Shuffle so rare ores can appear at any point (more exciting)
    for (let i = queue.length - 1; i > 0; i--) {
      const j = Math.floor(Math.random() * (i + 1));
      [queue[i], queue[j]] = [queue[j], queue[i]];
    }

    setSmeltQueue(queue);
    setSmeltTally(new Map());
    setSmeltRevealIdx(0);
  }, [lastSmeltResult]);

  // Tick: reveal next ore every 500 ms
  useEffect(() => {
    if (smeltRevealIdx >= smeltQueue.length) return;

    const timer = setTimeout(() => {
      const ore = smeltQueue[smeltRevealIdx];
      setSmeltTally(prev => {
        const next = new Map(prev);
        next.set(ore, (next.get(ore) ?? 0) + 1);
        return next;
      });

      // Play sound — rare ores get the special loot sound
      try {
        if (RARE_ORES.has(ore)) {
          SoundEffects.rareLoot();
        } else {
          SoundEffects.lootDrop();
        }
      } catch {
        // Audio not available
      }

      setSmeltRevealIdx(prev => prev + 1);
    }, 500);

    return () => clearTimeout(timer);
  }, [smeltRevealIdx, smeltQueue]);

  // Total Metal available across storage + inventory (for smelt UI)
  const storageMetal = snapshot
    ? snapshot.items
        .filter(i => i.name.toLowerCase() === 'metal' && i.category === 'Component')
        .reduce((sum, i) => sum + (i.quantity ?? 1), 0)
    : 0;
  const inventoryMetal = inventoryItems
    .filter(i => i.name.toLowerCase() === 'metal' && i.category === 'Component')
    .reduce((sum, i) => sum + (i.quantity ?? 1), 0);
  const totalMetal = storageMetal + inventoryMetal;
  const canSmelt = atHomestead && totalMetal > 0 && !!onSmelt;

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

        {/* Smelt Metal section — visible when player has Metal and is at homestead */}
        {canSmelt && (
          <div style={{ padding: '8px 14px', borderBottom: '1px solid #1a3a1a', background: '#0a1a0a' }}>
            <span style={{ color: '#ffaa22', fontSize: '11px', letterSpacing: '0.12em' }}>
              SMELT METAL
            </span>
            <span style={{ color: '#888888', fontSize: '10px', marginLeft: '8px' }}>
              ({totalMetal}x Metal available — discover what it contains)
            </span>
            <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginTop: '6px' }}>
              <label style={{ color: '#888888', fontSize: '11px' }}>Amount:</label>
              <input
                type="number"
                min={1}
                max={totalMetal}
                value={smeltAmount}
                onChange={e => setSmeltAmount(Math.max(1, Math.min(totalMetal, Number(e.target.value))))}
                style={{
                  background: '#0d0d0d',
                  border: '1px solid #555555',
                  color: '#00ff41',
                  fontFamily: 'monospace',
                  fontSize: '12px',
                  width: '60px',
                  padding: '2px 4px',
                }}
              />
              <button
                type="button"
                onClick={() => setSmeltAmount(totalMetal)}
                style={{ ...actionBtnStyle, borderColor: '#888888', color: '#888888' }}
              >
                all
              </button>
              <button
                type="button"
                onClick={() => onSmelt(smeltAmount)}
                style={{ ...actionBtnStyle, borderColor: '#ffaa22', color: '#ffaa22' }}
              >
                smelt
              </button>
              <span style={{ color: '#555555', fontSize: '10px', marginLeft: '4px' }}>
                → Iron / Copper / Tin / Silver / Mithril (skill-based)
              </span>
            </div>
          </div>
        )}

        {/* Smelt animation — one-by-one ore reveal */}
        {smeltQueue.length > 0 && (
          <div style={{ padding: '8px 14px', borderBottom: '1px solid #1a3a1a', background: '#080a08' }}>
            <div style={{ color: '#888', fontSize: '11px', letterSpacing: '0.1em', marginBottom: '4px' }}>
              SMELTING — Progress: {Math.min(smeltRevealIdx, smeltQueue.length)}/{smeltQueue.length}
            </div>
            <div style={{ fontSize: '12px', color: '#aaaaaa', minHeight: '18px' }}>
              {smeltRevealIdx > 0 && smeltRevealIdx <= smeltQueue.length && (() => {
                const latest = smeltQueue[smeltRevealIdx - 1];
                const isRare = RARE_ORES.has(latest);
                return (
                  <span style={{ color: isRare ? '#ffcc00' : '#00cc33' }}>
                    {isRare ? '★ ' : ''}Smelted → {latest}!
                  </span>
                );
              })()}
            </div>
            {smeltTally.size > 0 && (
              <div style={{ marginTop: '4px', fontSize: '11px', color: '#666', display: 'flex', flexWrap: 'wrap', gap: '8px' }}>
                {Array.from(smeltTally.entries()).map(([ore, count]) => (
                  <span
                    key={ore}
                    style={{ color: RARE_ORES.has(ore) ? '#ffcc00' : '#558855' }}
                  >
                    {ore} x{count}
                  </span>
                ))}
              </div>
            )}
            {smeltRevealIdx >= smeltQueue.length && smeltQueue.length > 0 && (
              <div style={{ marginTop: '4px', fontSize: '11px', color: '#44cc44' }}>
                Done! Ores added to storage.
              </div>
            )}
          </div>
        )}

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
