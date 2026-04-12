import React from 'react';
import type { PlayerState, EquipmentSlots } from '../types/game';

interface Props {
  player: PlayerState | null;
  equipment?: EquipmentSlots;
  onClose: () => void;
}

const overlayStyle: React.CSSProperties = {
  position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.8)',
  display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 140,
};

const panelStyle: React.CSSProperties = {
  background: '#0d0d0d', border: '1px solid #00ff41', fontFamily: 'monospace',
  fontSize: '13px', color: '#00ff41', width: '480px', maxHeight: '82vh',
  overflowY: 'auto', boxShadow: '0 0 40px rgba(0,255,65,0.25)',
};

const headerStyle: React.CSSProperties = {
  display: 'flex', justifyContent: 'space-between', alignItems: 'center',
  padding: '10px 14px', borderBottom: '1px solid #1a3a1a', letterSpacing: '0.12em',
};

const sectionStyle: React.CSSProperties = {
  color: '#888888', fontSize: '11px', letterSpacing: '0.14em', textTransform: 'uppercase',
  margin: '14px 14px 6px', borderBottom: '1px solid #1a1a1a', paddingBottom: '4px',
};

const rowStyle: React.CSSProperties = {
  display: 'flex', justifyContent: 'space-between', padding: '3px 14px',
};

const closeBtnStyle: React.CSSProperties = {
  background: 'none', border: '1px solid #00ff41', color: '#00ff41',
  fontFamily: 'monospace', fontSize: '12px', padding: '2px 10px', cursor: 'pointer',
};

const hintStyle: React.CSSProperties = {
  padding: '1px 14px 6px', color: '#555', fontSize: '10px', lineHeight: '1.4',
  fontStyle: 'italic',
};

const EQUIPMENT_SLOTS: Array<[string, keyof EquipmentSlots]> = [
  ['Melee Weapon',  'meleeWeaponName'],
  ['Ranged Weapon', 'rangedWeaponName'],
  ['Focus',         'focusName'],
  ['Head',          'headName'],
  ['Chest',         'chestName'],
  ['Legs',          'legsName'],
  ['Hands',         'handsName'],
  ['Feet',          'feetName'],
  ['Accessory',     'accessoryName'],
];

export default function CharacterSheet({ player, equipment, onClose }: Props) {
  if (!player) return null;

  const stats = [
    ['Strength', player.strength],
    ['Agility', player.agility],
    ['Intellect', player.intellect],
    ['Fortitude', player.fortitude],
    ['Speed', player.speed],
  ];

  const skills = [
    ['Crafting Skill', player.craftingSkill],
    ['Salvage Skill', player.salvageSkill],
  ];

  return (
    <div style={overlayStyle} data-testid="charsheet-overlay"
         onClick={(e) => { if (e.target === e.currentTarget) onClose(); }}>
      <div style={panelStyle}>
        <div style={headerStyle}>
          <span>CHARACTER — {player.name}</span>
          <button type="button" style={closeBtnStyle} onClick={onClose} aria-label="close character sheet">
            close [x]
          </button>
        </div>

        <div style={sectionStyle}>Vitals</div>
        <div style={rowStyle}>
          <span style={{ color: '#aaa' }}>Level</span>
          <span style={{ color: '#00ff41' }}>{player.level}</span>
        </div>
        <div style={hintStyle}>
          Next level at {player.level * player.level * 100} XP. Levels increase all stats.
        </div>
        <div style={rowStyle}>
          <span style={{ color: '#aaa' }}>HP</span>
          <span style={{ color: '#ff4444' }}>{player.currentHp} / {player.maxHp}</span>
        </div>
        <div style={hintStyle}>
          Health. Heals +10/s at your homestead [P to portal home]. Armor adds bonus HP in combat.
        </div>
        <div style={rowStyle}>
          <span style={{ color: '#aaa' }}>Weave (mana)</span>
          <span style={{ color: '#00ccff' }}>{player.weavePercent}% — {player.weaveState}</span>
        </div>
        <div style={hintStyle}>
          Magical energy. Regenerates +1 every 10s while exploring, +5/s at homestead.
          Powers "Weave Bolt" in combat (costs 10 Weave, deals {30} damage).
        </div>
        <div style={rowStyle}>
          <span style={{ color: '#aaa' }}>Action Points</span>
          <span style={{ color: '#ccaa00' }}>{player.actionPoints ?? '?'} / {player.maxActionPoints ?? '?'}</span>
        </div>
        <div style={hintStyle}>
          Stamina reserve. Will be consumed by special actions in future updates.
        </div>
        <div style={rowStyle}>
          <span style={{ color: '#aaa' }}>Experience</span>
          <span style={{ color: '#aaa' }}>{player.experience ?? 0} / {player.level * player.level * 100}</span>
        </div>
        <div style={hintStyle}>
          Earned from combat (+20 per enemy level), quests, and harvesting [E] (+5 each).
        </div>

        <div style={sectionStyle}>Attributes</div>
        <div style={hintStyle}>
          All attributes increase by +1 per level up. Affect combat calculations.
        </div>
        {stats.map(([name, val]) => (
          <div key={name as string} style={rowStyle}>
            <span style={{ color: '#aaa' }}>{name}</span>
            <span style={{ color: '#00ff41' }}>{val ?? '?'}</span>
          </div>
        ))}

        <div style={sectionStyle}>Skills</div>
        <div style={hintStyle}>
          Crafting improves with each successful craft. Salvage improves with salvaging.
        </div>
        {skills.map(([name, val]) => (
          <div key={name as string} style={rowStyle}>
            <span style={{ color: '#aaa' }}>{name}</span>
            <span style={{ color: '#00ccff' }}>{val ?? 0}</span>
          </div>
        ))}

        <div style={sectionStyle}>Magic</div>
        <div style={hintStyle}>
          Your element and polarity are hidden until revealed through quests or magical events.
          Five elements: Fire, Water, Earth, Air, Aether. Polarity: Shaping or Unmaking.
        </div>
        <div style={rowStyle}>
          <span style={{ color: '#aaa' }}>Element</span>
          <span style={{ color: player.elementRevealed ? '#00ff41' : '#555' }}>
            {player.elementRevealed ? (player.primaryElement ?? '???') : '??? (hidden)'}
          </span>
        </div>
        <div style={rowStyle}>
          <span style={{ color: '#aaa' }}>Polarity</span>
          <span style={{ color: player.polarityRevealed ? '#00ff41' : '#555' }}>
            {player.polarityRevealed ? (player.polarity ?? '???') : '??? (hidden)'}
          </span>
        </div>
        <div style={rowStyle}>
          <span style={{ color: '#aaa' }}>Wyrd Tangle</span>
          <span style={{ color: (player.wyrdTangle ?? 0) > 0 ? '#cc88ff' : '#555' }}>
            {player.wyrdTangle ?? 0}
          </span>
        </div>

        <div style={sectionStyle}>Equipment</div>
        <div style={hintStyle}>
          Each slot provides stat bonuses in combat. Melee/Ranged/Hands boost Strike damage.
          Focus boosts Weave Bolt. Armor slots (Head/Chest/Legs/Acc) add bonus HP. Feet add Speed.
        </div>
        {EQUIPMENT_SLOTS.map(([label, key]) => {
          const name = equipment?.[key] as string | undefined;
          return (
            <div key={label} style={rowStyle}>
              <span style={{ color: '#aaa' }}>{label}</span>
              <span style={{ color: name ? '#ffcc00' : '#444444' }}>
                {name ?? '—'}
              </span>
            </div>
          );
        })}
      </div>
    </div>
  );
}
