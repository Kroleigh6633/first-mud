import React from 'react';
import type { InventoryItem } from '../types/game';
import { imbueBtnStyle, imbuePanelStyle, quantityTagStyle, imbueTypeColors } from './inventoryStyles';

// -------------------------------------------------------------------------
// Pure helpers — no state, no side-effects
// -------------------------------------------------------------------------

export function imbueColor(type: string): string {
  return imbueTypeColors[type] ?? '#888888';
}

export function renderImbueDetails(item: InventoryItem): React.ReactNode {
  const imbues = item.imbues;
  if (!imbues || imbues.length === 0) return null;
  const parts = imbues.map(im => {
    const pct = Math.round(im.power * 100);
    const label = im.type === 'Fortifying'
      ? `${im.type} +${(item.workmanship ?? 1)}W`
      : pct > 0
        ? `${im.type} +${pct}%`
        : im.type;
    return (
      <span key={`${im.type}-${im.power}`} style={{ color: imbueColor(im.type) }}>
        {label}
      </span>
    );
  });
  const joined: React.ReactNode[] = [];
  parts.forEach((p, i) => {
    joined.push(p);
    if (i < parts.length - 1) joined.push(<span key={`sep-${i}`} style={{ color: '#555555' }}>, </span>);
  });
  return (
    <div style={{ fontSize: '10px', marginTop: '2px', color: '#888888' }}>
      imbues: {joined}
    </div>
  );
}

export function renderImbueSlots(item: InventoryItem): React.ReactNode {
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
          ✦
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

// -------------------------------------------------------------------------
// ImbuePanel sub-component
// -------------------------------------------------------------------------

interface ImbuePanelProps {
  item: InventoryItem;
  availableTapers: InventoryItem[];
  onImbue: (itemId: string, taperId: string) => void;
  onCancel: () => void;
}

export function ImbuePanel({ item, availableTapers, onImbue, onCancel }: ImbuePanelProps) {
  const hasOpenSlot = (item.imbues?.length ?? 0) < (item.maxImbueSlots ?? 1);

  return (
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
              onClick={() => onImbue(item.id, taper.id)}
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
        onClick={onCancel}
      >
        cancel
      </button>
    </div>
  );
}
