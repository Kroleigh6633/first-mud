import React from 'react';
import type { CombatUpdate, CombatantState } from '../types/game';

interface Props {
  combat: CombatUpdate;
  sendCommand: (command: string, payload?: unknown) => void;
}

function HpBar({ current, max, color }: { current: number; max: number; color: string }) {
  const pct = Math.round((current / Math.max(max, 1)) * 10);
  return (
    <>
      <span style={{ color }}>{'\u2588'.repeat(pct)}</span>
      <span style={{ color: '#222' }}>{'\u2588'.repeat(10 - pct)}</span>
    </>
  );
}

function elementColor(element: string): string {
  switch (element) {
    case 'Fire':   return '#ff4444';
    case 'Water':  return '#4488ff';
    case 'Earth':  return '#88aa44';
    case 'Air':    return '#ccccff';
    case 'Aether': return '#cc88ff';
    default:       return '#aaaaaa';
  }
}

function CombatantRow({ c, isCurrentActor }: { c: CombatantState; isCurrentActor: boolean }) {
  const nameColor = c.isPlayerSide ? '#00ff41' : '#ff4444';
  const border = isCurrentActor ? '1px solid #ccaa00' : '1px solid transparent';
  return (
    <div style={{
      display: 'flex', justifyContent: 'space-between', alignItems: 'center',
      padding: '4px 10px', border, opacity: c.isDefeated ? 0.35 : 1,
      marginBottom: '2px', background: isCurrentActor ? '#1a1a00' : 'transparent',
    }}>
      <div>
        <span style={{ color: nameColor, fontWeight: isCurrentActor ? 'bold' : 'normal' }}>
          {c.name}
        </span>
        <span style={{ color: elementColor(c.element), fontSize: '10px', marginLeft: '6px' }}>
          [{c.element}]
        </span>
        {c.isDefeated && <span style={{ color: '#555', marginLeft: '6px' }}>DEFEATED</span>}
      </div>
      <div style={{ fontSize: '12px' }}>
        <HpBar current={c.currentHp} max={c.maxHp} color={c.isPlayerSide ? '#ff4444' : '#cc4444'} />
        <span style={{ color: '#888', marginLeft: '4px' }}>{c.currentHp}/{c.maxHp}</span>
      </div>
    </div>
  );
}

const ABILITIES = ['Claw', 'Fire Breath', 'Water Jet', 'Strike'];

export default function CombatPanel({ combat, sendCommand }: Props) {
  const isOver = combat.state === 'Victory' || combat.state === 'Defeat' || combat.state === 'Fled';
  const playerSide = combat.combatants.filter(c => c.isPlayerSide);
  const enemySide = combat.combatants.filter(c => !c.isPlayerSide);
  const firstEnemy = enemySide.find(c => !c.isDefeated);

  const handleAbility = (abilityName: string) => {
    sendCommand('combat use', {
      encounterId: combat.encounterId,
      abilityName,
      targetId: firstEnemy?.id ?? null,
    });
  };

  const handleFlee = () => {
    sendCommand('combat flee', { encounterId: combat.encounterId });
  };

  return (
    <div style={{
      position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.85)',
      display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 160,
    }} data-testid="combat-panel">
      <div style={{
        background: '#0d0d0d', border: '1px solid #ff4444', fontFamily: 'monospace',
        fontSize: '13px', color: '#00ff41', width: '600px', maxHeight: '90vh',
        overflowY: 'auto', boxShadow: '0 0 60px rgba(255,68,68,0.3)',
      }}>
        {/* Header */}
        <div style={{
          padding: '10px 14px', borderBottom: '1px solid #331111',
          display: 'flex', justifyContent: 'space-between', alignItems: 'center',
          letterSpacing: '0.12em',
        }}>
          <span style={{ color: '#ff4444' }}>
            COMBAT — Round {combat.round}
          </span>
          <span style={{ color: isOver ? '#ccaa00' : '#888', fontSize: '11px' }}>
            {combat.state}
          </span>
        </div>

        {/* Enemies */}
        <div style={{ padding: '4px 0' }}>
          <div style={{
            color: '#888', fontSize: '10px', letterSpacing: '0.14em',
            textTransform: 'uppercase', padding: '6px 14px',
          }}>
            Enemies
          </div>
          {enemySide.map(c => (
            <CombatantRow key={c.id} c={c} isCurrentActor={c.id === combat.currentActorId} />
          ))}
        </div>

        {/* Player side */}
        <div style={{ padding: '4px 0', borderTop: '1px solid #1a1a1a' }}>
          <div style={{
            color: '#888', fontSize: '10px', letterSpacing: '0.14em',
            textTransform: 'uppercase', padding: '6px 14px',
          }}>
            Your party
          </div>
          {playerSide.map(c => (
            <CombatantRow key={c.id} c={c} isCurrentActor={c.id === combat.currentActorId} />
          ))}
        </div>

        {/* Actions */}
        {!isOver && (
          <div style={{
            padding: '10px 14px', borderTop: '1px solid #1a1a1a',
            display: 'flex', flexWrap: 'wrap', gap: '8px',
          }}>
            {ABILITIES.map(a => (
              <button
                key={a}
                onClick={() => handleAbility(a)}
                style={{
                  background: 'none', border: '1px solid #00ff41', color: '#00ff41',
                  fontFamily: 'monospace', fontSize: '12px', padding: '4px 12px',
                  cursor: 'pointer',
                }}
              >
                {a}
              </button>
            ))}
            <button
              onClick={handleFlee}
              style={{
                background: 'none', border: '1px solid #ccaa00', color: '#ccaa00',
                fontFamily: 'monospace', fontSize: '12px', padding: '4px 12px',
                cursor: 'pointer', marginLeft: 'auto',
              }}
            >
              Flee
            </button>
          </div>
        )}

        {/* Outcome */}
        {isOver && (
          <div style={{
            padding: '16px 14px', borderTop: '1px solid #1a1a1a',
            textAlign: 'center',
          }}>
            <div style={{
              fontSize: '16px', letterSpacing: '0.2em',
              color: combat.state === 'Victory' ? '#00ff41' : combat.state === 'Fled' ? '#ccaa00' : '#ff4444',
            }}>
              {combat.state === 'Victory' ? 'VICTORY!' : combat.state === 'Fled' ? 'ESCAPED' : 'DEFEATED'}
            </div>
            <div style={{ color: '#888', fontSize: '11px', marginTop: '6px' }}>
              (closing in 5 seconds...)
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
