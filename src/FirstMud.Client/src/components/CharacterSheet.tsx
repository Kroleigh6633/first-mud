import React from 'react';
import type { PlayerState } from '../types/game';

interface Props {
  player: PlayerState | null;
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

export default function CharacterSheet({ player, onClose }: Props) {
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
        <div style={rowStyle}>
          <span style={{ color: '#aaa' }}>HP</span>
          <span style={{ color: '#ff4444' }}>{player.currentHp} / {player.maxHp}</span>
        </div>
        <div style={rowStyle}>
          <span style={{ color: '#aaa' }}>Weave (mana)</span>
          <span style={{ color: '#00ccff' }}>{player.weavePercent}% — {player.weaveState}</span>
        </div>
        <div style={rowStyle}>
          <span style={{ color: '#aaa' }}>Action Points</span>
          <span style={{ color: '#ccaa00' }}>{player.actionPoints ?? '?'} / {player.maxActionPoints ?? '?'}</span>
        </div>
        <div style={rowStyle}>
          <span style={{ color: '#aaa' }}>Experience</span>
          <span style={{ color: '#aaa' }}>{player.experience ?? 0}</span>
        </div>

        <div style={sectionStyle}>Attributes</div>
        {stats.map(([name, val]) => (
          <div key={name as string} style={rowStyle}>
            <span style={{ color: '#aaa' }}>{name}</span>
            <span style={{ color: '#00ff41' }}>{val ?? '?'}</span>
          </div>
        ))}

        <div style={sectionStyle}>Skills</div>
        {skills.map(([name, val]) => (
          <div key={name as string} style={rowStyle}>
            <span style={{ color: '#aaa' }}>{name}</span>
            <span style={{ color: '#00ccff' }}>{val ?? 0}</span>
          </div>
        ))}

        <div style={sectionStyle}>Magic</div>
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
      </div>
    </div>
  );
}
