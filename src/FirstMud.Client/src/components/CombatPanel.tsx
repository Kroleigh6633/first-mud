import { useState, useEffect } from 'react';
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

function EnemyRow({
  c,
  isCurrentActor,
  isTarget,
  onSelect,
}: {
  c: CombatantState;
  isCurrentActor: boolean;
  isTarget: boolean;
  onSelect: () => void;
}) {
  return (
    <div
      onClick={c.isDefeated ? undefined : onSelect}
      style={{
        display: 'flex', justifyContent: 'space-between', alignItems: 'center',
        padding: '4px 10px',
        border: isTarget ? '1px solid #ccaa00' : isCurrentActor ? '1px dashed #ff6600' : '1px solid transparent',
        background: isTarget ? '#1a1a00' : isCurrentActor ? '#1a0800' : 'transparent',
        opacity: c.isDefeated ? 0.35 : 1,
        marginBottom: '2px',
        cursor: c.isDefeated ? 'default' : 'pointer',
      }}
    >
      <div>
        {isTarget && <span style={{ color: '#ccaa00', marginRight: '4px' }}>▸</span>}
        {isCurrentActor && !isTarget && <span style={{ color: '#ff6600', marginRight: '4px' }}>⚔</span>}
        <span style={{ color: '#ff4444', fontWeight: isCurrentActor ? 'bold' : 'normal' }}>
          {c.name}
        </span>
        <span style={{ color: elementColor(c.element), fontSize: '10px', marginLeft: '6px' }}>
          [{c.element}]
        </span>
        {c.isDefeated && <span style={{ color: '#555', marginLeft: '6px' }}>DEFEATED</span>}
      </div>
      <div style={{ fontSize: '12px' }}>
        <HpBar current={c.currentHp} max={c.maxHp} color="#cc4444" />
        <span style={{ color: '#888', marginLeft: '4px' }}>{c.currentHp}/{c.maxHp}</span>
      </div>
    </div>
  );
}

function PartyRow({ c, isCurrentActor }: { c: CombatantState; isCurrentActor: boolean }) {
  return (
    <div style={{
      display: 'flex', justifyContent: 'space-between', alignItems: 'center',
      padding: '4px 10px',
      border: isCurrentActor ? '1px solid #00ff41' : '1px solid transparent',
      background: isCurrentActor ? '#001a00' : 'transparent',
      opacity: c.isDefeated ? 0.35 : 1,
      marginBottom: '2px',
    }}>
      <div>
        {isCurrentActor && <span style={{ color: '#00ff41', marginRight: '4px' }}>▸</span>}
        <span style={{ color: '#00ff41', fontWeight: isCurrentActor ? 'bold' : 'normal' }}>
          {c.name}
        </span>
        <span style={{ color: elementColor(c.element), fontSize: '10px', marginLeft: '6px' }}>
          [{c.element}]
        </span>
        {c.isDefeated && <span style={{ color: '#555', marginLeft: '6px' }}>DEFEATED</span>}
      </div>
      <div style={{ fontSize: '12px' }}>
        <HpBar current={c.currentHp} max={c.maxHp} color="#ff4444" />
        <span style={{ color: '#888', marginLeft: '4px' }}>{c.currentHp}/{c.maxHp}</span>
      </div>
    </div>
  );
}

// Abilities are now sourced from the current actor's CombatantState.

export default function CombatPanel({ combat, sendCommand }: Props) {
  const isOver = combat.state === 'Victory' || combat.state === 'Defeat' || combat.state === 'Fled';
  const playerSide = combat.combatants.filter(c => c.isPlayerSide);
  const enemySide = combat.combatants.filter(c => !c.isPlayerSide);

  const currentActor = combat.combatants.find(c => c.id === combat.currentActorId);
  const isPlayerTurn = currentActor?.isPlayerSide === true;

  // Default target: first living enemy
  const firstLivingEnemy = enemySide.find(c => !c.isDefeated);
  const [selectedTargetId, setSelectedTargetId] = useState<string | null>(null);
  const targetId = selectedTargetId && enemySide.some(c => c.id === selectedTargetId && !c.isDefeated)
    ? selectedTargetId
    : firstLivingEnemy?.id ?? null;
  const targetName = enemySide.find(c => c.id === targetId)?.name ?? '???';

  const [autoCombat, setAutoCombat] = useState(false);

  // Auto-combat: fire whenever combat state changes and it's the player's turn.
  // Watches the entire combat object so the effect re-triggers on every
  // CombatUpdate — including after enemy turns that land back on the player
  // with the same currentActorId. The 300ms timeout + cleanup prevents
  // double-firing if the component re-renders mid-delay.
  useEffect(() => {
    if (!autoCombat || !isPlayerTurn || isOver) return;

    if (!firstLivingEnemy) {
      // No valid targets — stop auto-combat
      setAutoCombat(false);
      return;
    }

    const abilities = currentActor?.abilities ?? [];
    const hpPct = currentActor
      ? currentActor.currentHp / Math.max(currentActor.maxHp, 1)
      : 1;

    const restoreAbility = abilities.find(a => a.category === 'Heal');
    const strikeAbility = abilities.find(a => a.category === 'Attack');

    const chosenAbility = hpPct < 0.3 && restoreAbility ? restoreAbility : (strikeAbility ?? abilities[0]);

    if (!chosenAbility) return;

    const timer = setTimeout(() => {
      // For heals, target self (player); for attacks, target first living enemy
      const chosenTargetId = chosenAbility.category === 'Heal'
        ? (currentActor?.id ?? targetId)
        : (firstLivingEnemy?.id ?? targetId);

      sendCommand('combat use', {
        encounterId: combat.encounterId,
        abilityName: chosenAbility.name,
        targetId: chosenTargetId,
      });
    }, 300);

    return () => clearTimeout(timer);
  }, [autoCombat, isPlayerTurn, isOver, combat]);

  // Stop auto-combat when encounter ends
  useEffect(() => {
    if (isOver && autoCombat) {
      setAutoCombat(false);
    }
  }, [isOver]);

  // Keyboard shortcut 'a' to toggle auto-combat during combat
  useEffect(() => {
    if (isOver) return;
    const handleKey = (e: KeyboardEvent) => {
      if (e.key === 'a' || e.key === 'A') {
        e.preventDefault();
        e.stopPropagation();
        setAutoCombat(prev => !prev);
      }
    };
    window.addEventListener('keydown', handleKey, true); // capture phase — fires before global hook
    return () => window.removeEventListener('keydown', handleKey, true);
  }, [isOver]);

  const handleAbility = (abilityName: string) => {
    sendCommand('combat use', {
      encounterId: combat.encounterId,
      abilityName,
      targetId,
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
        fontSize: '13px', color: '#00ff41', width: '620px', maxHeight: '90vh',
        overflowY: 'auto', boxShadow: '0 0 60px rgba(255,68,68,0.3)',
      }}>
        {/* Header */}
        <div style={{
          padding: '10px 14px', borderBottom: '1px solid #331111',
          display: 'flex', justifyContent: 'space-between', alignItems: 'center',
          letterSpacing: '0.12em',
        }}>
          <span style={{ color: '#ff4444' }}>COMBAT — Round {combat.round}</span>
          <span style={{ color: isOver ? '#ccaa00' : '#888', fontSize: '11px' }}>{combat.state}</span>
        </div>

        {/* Turn banner */}
        {!isOver && currentActor && (
          <div style={{
            padding: '8px 14px',
            background: isPlayerTurn ? '#001a00' : '#1a0800',
            borderBottom: isPlayerTurn ? '2px solid #00ff41' : '2px solid #ff6600',
            textAlign: 'center', fontSize: '14px', letterSpacing: '0.15em',
          }}>
            {isPlayerTurn ? (
              <span style={{ color: '#00ff41' }}>
                ▸ YOUR TURN — {currentActor.name} — attacking <span style={{ color: '#ccaa00' }}>{targetName}</span>
                {autoCombat && (
                  <span style={{
                    color: '#ffcc00',
                    marginLeft: '10px',
                    fontWeight: 'bold',
                    animation: 'pulse 1s ease-in-out infinite',
                  }}>AUTO</span>
                )}
              </span>
            ) : (
              <span style={{ color: '#ff6600' }}>
                ⚔ ENEMY TURN — {currentActor.name} is acting...
                {autoCombat && (
                  <span style={{ color: '#ffcc00', marginLeft: '10px', fontWeight: 'bold' }}>AUTO</span>
                )}
              </span>
            )}
          </div>
        )}

        {/* Last action text */}
        {!isOver && combat.lastActionText && (
          <div style={{
            padding: '6px 14px',
            borderBottom: '1px solid #1a1a1a',
            textAlign: 'center',
            fontSize: '12px',
            color: combat.lastActionText.startsWith('CRITICAL')
              ? '#ffcc00'
              : combat.lastActionText.includes('miss') || combat.lastActionText.includes('dodged')
                ? '#888888'
                : '#cccccc',
            fontStyle: 'italic',
          }}>
            {combat.lastActionText}
          </div>
        )}

        {/* Enemies — clickable for target selection */}
        <div style={{ padding: '4px 0' }}>
          <div style={{
            color: '#888', fontSize: '10px', letterSpacing: '0.14em',
            textTransform: 'uppercase', padding: '6px 14px',
            display: 'flex', justifyContent: 'space-between',
          }}>
            <span>Enemies</span>
            {isPlayerTurn && <span style={{ color: '#555' }}>click to target</span>}
          </div>
          {enemySide.map(c => (
            <EnemyRow
              key={c.id}
              c={c}
              isCurrentActor={c.id === combat.currentActorId}
              isTarget={c.id === targetId}
              onSelect={() => setSelectedTargetId(c.id)}
            />
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
            <PartyRow key={c.id} c={c} isCurrentActor={c.id === combat.currentActorId} />
          ))}
        </div>

        {/* Actions — only when it's your turn */}
        {!isOver && isPlayerTurn && (
          <div style={{
            padding: '10px 14px', borderTop: '1px solid #1a3a1a',
          }}>
            <div style={{ color: '#888', fontSize: '10px', marginBottom: '6px' }}>
              Choose an ability to use against <span style={{ color: '#ccaa00' }}>{targetName}</span>:
            </div>
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: '8px' }}>
              {(currentActor?.abilities ?? []).map(a => (
                <button
                  key={a.name}
                  onClick={() => handleAbility(a.name)}
                  title={`${a.element} ${a.category} — power ${a.basePower}${a.weaveCost > 0 ? `, costs ${a.weaveCost} weave` : ''}`}
                  style={{
                    background: 'none',
                    border: `1px solid ${a.category === 'Heal' ? '#00ccff' : '#00ff41'}`,
                    color: a.category === 'Heal' ? '#00ccff' : '#00ff41',
                    fontFamily: 'monospace', fontSize: '12px', padding: '4px 12px',
                    cursor: 'pointer',
                  }}
                >
                  {a.name}
                  <span style={{ color: '#666', fontSize: '9px', marginLeft: '4px' }}>
                    {a.basePower > 0 ? `${a.basePower}` : ''}{a.weaveCost > 0 ? ` ◆${a.weaveCost}` : ''}
                  </span>
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
              <button
                onClick={() => setAutoCombat(prev => !prev)}
                title="Toggle auto-combat (press 'a')"
                style={{
                  background: autoCombat ? 'rgba(255,204,0,0.08)' : 'none',
                  border: autoCombat ? '1px solid #ffcc00' : '1px solid #336600',
                  color: autoCombat ? '#ffcc00' : '#336600',
                  fontFamily: 'monospace', fontSize: '12px', padding: '4px 12px',
                  cursor: 'pointer',
                  boxShadow: autoCombat ? '0 0 6px rgba(255,204,0,0.4)' : 'none',
                  transition: 'all 0.2s',
                }}
              >
                {autoCombat ? 'Auto ●' : 'Auto'}
              </button>
            </div>
          </div>
        )}

        {/* Enemy turn — no actions available, just a wait message */}
        {!isOver && !isPlayerTurn && (
          <div style={{
            padding: '10px 14px', borderTop: '1px solid #1a1a1a',
            textAlign: 'center', color: '#ff6600', fontSize: '11px',
          }}>
            ⚔ Waiting for {currentActor?.name ?? 'enemy'} to act...
          </div>
        )}

        {/* Outcome */}
        {isOver && (
          <div style={{
            padding: '16px 14px', borderTop: '1px solid #1a1a1a', textAlign: 'center',
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
