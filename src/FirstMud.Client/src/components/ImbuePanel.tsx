import React from 'react';
import type { InventoryItem } from '../types/game';
import { imbueBtnStyle, imbuePanelStyle, quantityTagStyle, imbueTypeColors } from './inventoryStyles';

// -------------------------------------------------------------------------
// Taper metadata — description and emoji per taper name
// -------------------------------------------------------------------------

const IMBUE_DESCRIPTIONS: Record<string, string> = {
  'Fire Shaping Taper':  '+30% Fire damage on attacks',
  'Water Shaping Taper': '+30% Water damage on attacks',
  'Earth Shaping Taper': '+30% Earth damage on attacks',
  'Air Shaping Taper':   '+30% Air damage on attacks',
  'Warding Taper':       '+20% elemental damage resistance',
  'Fortitude Taper':     '+1-2 Workmanship (stat boost)',
  'Wyrd Shard':          '10% chance: tangle enemy Weave',
  'Dravenite Dust':      '15% lifesteal (heal on hit)',
};

const TAPER_EMOJI: Record<string, string> = {
  'Fire Shaping Taper':  '🔥',
  'Water Shaping Taper': '💧',
  'Earth Shaping Taper': '🌍',
  'Air Shaping Taper':   '💨',
  'Warding Taper':       '🛡️',
  'Fortitude Taper':     '💪',
  'Wyrd Shard':          '✦',
  'Dravenite Dust':      '💚',
};

function getTaperDescription(taperName: string): string {
  for (const [key, desc] of Object.entries(IMBUE_DESCRIPTIONS)) {
    if (taperName.toLowerCase().includes(key.toLowerCase())) return desc;
  }
  return '';
}

function getTaperEmoji(taperName: string): string {
  for (const [key, emoji] of Object.entries(TAPER_EMOJI)) {
    if (taperName.toLowerCase().includes(key.toLowerCase())) return emoji;
  }
  return '✦';
}

function getSuccessChance(craftingSkill: number): number {
  return Math.min(100, craftingSkill * 5 + 20);
}

function getCatastrophicChance(craftingSkill: number): number {
  // Risk scales down as skill increases; 0 risk at skill >= 14
  return Math.max(0, Math.round((14 - craftingSkill) * 0.5));
}

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
  craftingSkill: number;
  onImbue: (itemId: string, taperId: string) => void;
  onCancel: () => void;
}

export function ImbuePanel({ item, availableTapers, craftingSkill, onImbue, onCancel }: ImbuePanelProps) {
  const hasOpenSlot = (item.imbues?.length ?? 0) < (item.maxImbueSlots ?? 1);
  const successChance = getSuccessChance(craftingSkill);
  const catChance = getCatastrophicChance(craftingSkill);

  return (
    <div style={imbuePanelStyle}>
      <div style={{ color: '#cc44ff', marginBottom: '4px', fontSize: '11px' }}>
        SELECT TAPER TO IMBUE — {item.name}
        {!hasOpenSlot && (
          <span style={{ color: '#ff4422', marginLeft: '8px' }}>
            ⚠ OVERIMBUING: 50% catastrophic failure
          </span>
        )}
      </div>
      <div style={{ color: '#888888', fontSize: '10px', marginBottom: '6px' }}>
        <span style={{ color: successChance >= 80 ? '#44ff88' : successChance >= 50 ? '#ffcc44' : '#ff8844' }}>
          Success: {successChance}% at Skill {craftingSkill}
        </span>
        {catChance > 0 && (
          <span style={{ color: '#ff4422', marginLeft: '12px' }}>
            ⚠ {catChance}% catastrophic failure (lose existing imbue)
          </span>
        )}
      </div>
      {availableTapers.length === 0 ? (
        <div style={{ color: '#666666', fontStyle: 'italic' }}>
          No imbuing reagents in inventory.
        </div>
      ) : (
        availableTapers.map(taper => {
          const desc = getTaperDescription(taper.name);
          const emoji = getTaperEmoji(taper.name);
          return (
            <div
              key={taper.id}
              style={{
                display: 'grid',
                gridTemplateColumns: '1fr auto auto',
                alignItems: 'center',
                padding: '3px 0',
                gap: '8px',
                borderBottom: '1px solid #1a0a2a',
              }}
            >
              <span>
                <span style={{ color: '#ccaaff' }}>
                  {emoji} {taper.name}
                  {(taper.quantity ?? 1) > 1 && <span style={quantityTagStyle}>x{taper.quantity}</span>}
                </span>
                {desc && (
                  <span style={{ color: '#888888', fontSize: '10px', marginLeft: '8px' }}>
                    {desc}
                  </span>
                )}
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
          );
        })
      )}
      <button
        type="button"
        style={{ ...imbueBtnStyle, marginTop: '6px', marginLeft: '0' }}
        onClick={onCancel}
      >
        cancel
      </button>
    </div>
  );
}
