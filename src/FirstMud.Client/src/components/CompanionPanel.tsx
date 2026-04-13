import React, { useState } from 'react';
import type { CompanionState, CompanionType, MagicElement, HomesteadDuty } from '../types/game';
import { useGameCommands, type SendCommandFn } from '../hooks/useGameCommands';

interface Props {
  companions: CompanionState[];
  activeCompanionIds: string[];
  onActivate: (id: string) => void;
  onDeactivate: (id: string) => void;
  onClose: () => void;
  sendCommand: SendCommandFn;
}

// ---- Layer ability descriptions per type --------------------------------

const ABILITY_LABELS: Record<CompanionType, string[]> = {
  Wildfolk: [
    'Bond 1: Elemental Touch (attack)',
    'Bond 2: Nature Mend (heal ally)',
    'Bond 3: Elemental Strike (medium attack)',
    'Bond 4: Pack Bond (party buff)',
    'Bond 5: Wild Revive (revive ally)',
    'Bond 6: Elemental Storm (AOE ultimate)',
  ],
  CapturedMonster: [
    'Bond 1: Claw (physical attack)',
    'Bond 2: Elemental Breath (medium attack)',
    'Bond 3: Frenzy (high damage)',
    'Bond 4: Terrify (debuff enemy)',
    'Bond 5: Devour (lifesteal)',
    'Bond 6: Rampage (triple-hit ultimate)',
  ],
  ArdweldConstruct: [
    'Bond 1: Shield Bash (low damage)',
    'Bond 2: Protect (redirect damage to self)',
    'Bond 3: Repair (self-heal)',
    'Bond 4: Fortify (party buff)',
    'Bond 5: Reflect (return damage)',
    'Bond 6: Aegis (absorb hits ultimate)',
  ],
  HiredHero: [
    'Bond 1: Sword Strike (physical)',
    'Bond 2: Quick Shot (ranged)',
    'Bond 3: Battle Cry (party buff)',
    'Bond 4: Tactical Strike (targets weakest foe)',
    'Bond 5: Rally (party heal)',
    'Bond 6: Commander (extra actions ultimate)',
  ],
  BoundShade: [
    'Bond 1: Sword Strike (physical)',
    'Bond 2: Quick Shot (ranged)',
    'Bond 3: Battle Cry (party buff)',
    'Bond 4: Tactical Strike (targets weakest foe)',
    'Bond 5: Rally (party heal)',
    'Bond 6: Commander (extra actions ultimate)',
  ],
};

// ---- Aptitude stars per companion type & duty ---------------------------

const APTITUDE: Record<CompanionType, Record<HomesteadDuty, number>> = {
  Wildfolk:         { Harvester: 3, Salvager: 1, Guard: 2, Crafter: 2 },
  CapturedMonster:  { Harvester: 2, Salvager: 1, Guard: 3, Crafter: 1 },
  ArdweldConstruct: { Harvester: 1, Salvager: 3, Guard: 3, Crafter: 3 },
  HiredHero:        { Harvester: 2, Salvager: 2, Guard: 2, Crafter: 2 },
  BoundShade:       { Harvester: 2, Salvager: 2, Guard: 2, Crafter: 1 },
};

const ALL_DUTIES: HomesteadDuty[] = ['Harvester', 'Salvager', 'Guard', 'Crafter'];

function AptitudeStars({ count }: { count: number }) {
  return (
    <span>
      {Array.from({ length: 3 }, (_, i) => (
        <span key={i} style={{ color: i < count ? '#ffcc00' : '#333333', fontSize: '11px' }}>★</span>
      ))}
    </span>
  );
}

// ---- Element colours --------------------------------------------------------

function elementColor(element: MagicElement): string {
  switch (element) {
    case 'Fire':   return '#ff6633';
    case 'Water':  return '#33aaff';
    case 'Earth':  return '#88bb44';
    case 'Air':    return '#ccddff';
    case 'Aether': return '#cc88ff';
  }
}

// ---- Type icon ---------------------------------------------------------------

function typeIcon(type: CompanionType): string {
  switch (type) {
    case 'Wildfolk':         return '~';
    case 'CapturedMonster':  return '#';
    case 'ArdweldConstruct': return '[';
    case 'HiredHero':        return 'H';
    case 'BoundShade':       return '@';
  }
}

// ---- Bond stars -------------------------------------------------------------

function LayerStars({ layer }: { layer: number }) {
  return (
    <span>
      {Array.from({ length: 6 }, (_, i) => (
        <span key={i} style={{ color: i < layer ? '#ffcc00' : '#333333' }}>★</span>
      ))}
    </span>
  );
}

// ---- Drift progress ---------------------------------------------------------

const DRIFT_WARN_THRESHOLD = 30;

function driftLabel(drift: number): string | null {
  if (drift >= 40) return 'DANGER';
  if (drift >= DRIFT_WARN_THRESHOLD) return 'Drifting!';
  return null;
}

function driftColor(drift: number): string {
  if (drift >= 40) return '#ff4444';
  if (drift >= DRIFT_WARN_THRESHOLD) return '#ff8800';
  return '#00bb33';
}

// ---- Usage progress bar toward next layer ----------------------------------

const LAYER_THRESHOLDS: Record<CompanionType, number[]> = {
  Wildfolk:         [0, 200, 500, 1000, 2000, 4000],
  HiredHero:        [0, 200, 600, 1200, 2500, 5000],
  CapturedMonster:  [0, 150, 400, 900,  1800, 3600],
  ArdweldConstruct: [0, 500, 1500, 3000, 6000, 12000],
  BoundShade:       [0, 300, 700, 1500, 3000, 6000],
};

function UsageBar({ type, layer, usage }: { type: CompanionType; layer: number; usage: number }) {
  if (layer >= 6) {
    return <span style={{ color: '#ffcc00', fontSize: '10px' }}>Bond: MAX ✦</span>;
  }
  const thresholds = LAYER_THRESHOLDS[type] ?? LAYER_THRESHOLDS.Wildfolk;
  const current = thresholds[layer - 1] ?? 0;
  const next    = thresholds[layer] ?? 1;
  const gained  = usage - current;
  const needed  = next - current;
  const pct     = Math.min(1, gained / Math.max(needed, 1));
  const filled  = Math.round(pct * 10);
  return (
    <span style={{ fontSize: '10px' }}>
      <span style={{ color: '#00bb33' }}>{'|'.repeat(filled)}</span>
      <span style={{ color: '#333333' }}>{'|'.repeat(10 - filled)}</span>
      <span style={{ color: '#888888' }}> {gained}/{needed}</span>
    </span>
  );
}

// ---- Deactivate picker — shown inline after clicking Deactivate on an active companion ----

function DeactivateChoicePicker({
  companion,
  onRest,
  onQuickAssign,
  onCancel,
}: {
  companion: CompanionState;
  onRest: () => void;
  onQuickAssign: (duty: HomesteadDuty) => void;
  onCancel: () => void;
}) {
  const aptitude = APTITUDE[companion.type] ?? APTITUDE.HiredHero;

  return (
    <div style={deactivatePickerStyle}>
      <div style={{ color: '#ff8800', fontSize: '11px', marginBottom: '6px' }}>
        What should <span style={{ color: '#ffffff' }}>{companion.name}</span> do?
      </div>
      <div style={{ display: 'flex', flexWrap: 'wrap', gap: '6px', alignItems: 'center' }}>
        <button
          type="button"
          style={restBtnStyle}
          onClick={onRest}
          title="Deactivate — companion idles at homestead (assign duty later)"
        >
          Rest (idle)
        </button>
        {ALL_DUTIES.map(duty => {
          const apt = aptitude[duty] ?? 1;
          return (
            <button
              key={duty}
              type="button"
              style={quickAssignBtnStyle}
              onClick={() => onQuickAssign(duty)}
              title={`Deactivate and immediately assign to ${duty} duty`}
            >
              {duty} <AptitudeStars count={apt} />
            </button>
          );
        })}
        <button
          type="button"
          style={cancelPickerBtnStyle}
          onClick={onCancel}
          aria-label="cancel deactivate"
        >
          cancel
        </button>
      </div>
    </div>
  );
}

// ---- Duty assignment sub-panel (for inactive companions) -------------------

function HomesteadAssignPanel({
  companion,
  onAssign,
  onRecall,
}: {
  companion: CompanionState;
  onAssign: (duty: HomesteadDuty) => void;
  onRecall: () => void;
}) {
  const [selectedDuty, setSelectedDuty] = useState<HomesteadDuty>('Harvester');
  const aptitude = APTITUDE[companion.type] ?? APTITUDE.HiredHero;
  const isOnDuty = !!companion.assignedDuty;

  if (isOnDuty) {
    const dutyApt = aptitude[companion.assignedDuty!] ?? 1;
    return (
      <div style={dutyPanelStyle}>
        <div style={{ color: '#ccaa44', fontSize: '11px', marginBottom: '6px' }}>
          ON DUTY: <span style={{ color: '#ffcc00' }}>{companion.assignedDuty}</span>
          {' '}
          <AptitudeStars count={dutyApt} />
        </div>
        {companion.dutyStartedAt && (
          <div style={{ color: '#555555', fontSize: '10px', marginBottom: '6px' }}>
            Since: {new Date(companion.dutyStartedAt).toLocaleString()}
          </div>
        )}
        <button
          type="button"
          style={recallBtnStyle}
          onClick={onRecall}
          aria-label={`recall ${companion.name} from homestead`}
        >
          Recall to party
        </button>
      </div>
    );
  }

  // Idle (inactive, no duty) — show duty picker prominently
  return (
    <div style={{ ...dutyPanelStyle, borderColor: '#005533' }}>
      <div style={{ color: '#00cc88', fontSize: '11px', marginBottom: '8px', fontWeight: 'bold' }}>
        Assign Homestead Duty:
      </div>
      <div style={{ display: 'flex', flexDirection: 'column', gap: '5px', marginBottom: '8px' }}>
        {ALL_DUTIES.map(duty => {
          const apt = aptitude[duty] ?? 1;
          const isSelected = selectedDuty === duty;
          return (
            <label
              key={duty}
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: '6px',
                cursor: 'pointer',
                fontSize: '11px',
                padding: '3px 6px',
                background: isSelected ? '#001a0d' : 'transparent',
                border: isSelected ? '1px solid #00cc88' : '1px solid transparent',
              }}
            >
              <input
                type="radio"
                name={`duty-${companion.id}`}
                value={duty}
                checked={isSelected}
                onChange={() => setSelectedDuty(duty)}
                style={{ accentColor: '#00cc88' }}
              />
              <span style={{ color: isSelected ? '#00cc88' : '#aaaaaa', minWidth: '70px' }}>{duty}</span>
              <AptitudeStars count={apt} />
              <span style={{ color: '#555555', fontSize: '10px' }}>
                {duty === 'Harvester' && '(auto-gather resources)'}
                {duty === 'Salvager'  && '(process salvage queue)'}
                {duty === 'Guard'     && '+20% storage capacity'}
                {duty === 'Crafter'   && '(future: auto-craft)'}
              </span>
            </label>
          );
        })}
      </div>
      <button
        type="button"
        style={assignBtnStyle}
        onClick={() => onAssign(selectedDuty)}
        aria-label={`assign ${companion.name} to homestead`}
      >
        Assign to Homestead
      </button>
    </div>
  );
}

// ---- Companion card ---------------------------------------------------------

function CompanionCard({
  companion,
  activeCount,
  isInActiveParty,
  onActivate,
  onDeactivate,
  onAssign,
  onRecall,
  onQuickAssign,
}: {
  companion: CompanionState;
  activeCount: number;
  isInActiveParty: boolean;
  onActivate: () => void;
  onDeactivate: () => void;
  onAssign: (duty: HomesteadDuty) => void;
  onRecall: () => void;
  onQuickAssign: (duty: HomesteadDuty) => void;
}) {
  const [showDeactivatePicker, setShowDeactivatePicker] = useState(false);

  const drift = driftLabel(companion.driftAccumulator);
  const abilitiesForType = ABILITY_LABELS[companion.type] ?? ABILITY_LABELS.Wildfolk;
  const unlockedAbilities = abilitiesForType.slice(0, companion.currentLayer);
  // Use player's activeCompanionIds as ground truth — not the stale isActive flag on the DTO
  const isOnDuty = !!companion.assignedDuty && !isInActiveParty;
  const isIdleInactive = !isInActiveParty && !companion.assignedDuty;

  const handleDeactivateClick = () => {
    setShowDeactivatePicker(true);
  };

  const handleRest = () => {
    onDeactivate();
    setShowDeactivatePicker(false);
  };

  const handleQuickAssign = (duty: HomesteadDuty) => {
    onQuickAssign(duty);
    setShowDeactivatePicker(false);
  };

  return (
    <div style={cardStyle(isInActiveParty, isOnDuty)}>
      {/* Header row */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '4px' }}>
        <div>
          <span style={{ color: elementColor(companion.element), fontWeight: 'bold', marginRight: '6px' }}>
            [{typeIcon(companion.type)}]
          </span>
          <span style={{ color: isInActiveParty ? '#00ccff' : isOnDuty ? '#ccaa44' : '#cccccc', fontWeight: 'bold' }}>
            {companion.name}
          </span>
          {isInActiveParty && (
            <span style={{ color: '#00ccff', fontSize: '10px', marginLeft: '8px' }}>ACTIVE</span>
          )}
          {isOnDuty && (
            <span style={{ color: '#ccaa44', fontSize: '10px', marginLeft: '8px' }}>
              HOMESTEAD: {companion.assignedDuty?.toUpperCase()}
            </span>
          )}
          {isIdleInactive && (
            <span style={{ color: '#555555', fontSize: '10px', marginLeft: '8px' }}>IDLE</span>
          )}
          {drift && !isOnDuty && (
            <span style={{ color: driftColor(companion.driftAccumulator), fontSize: '10px', marginLeft: '8px' }}>
              ⚠ {drift}
            </span>
          )}
        </div>
        <div>
          {isInActiveParty ? (
            <button
              type="button"
              style={actionBtnStyle(showDeactivatePicker ? '#ff4444' : '#ff8800')}
              onClick={handleDeactivateClick}
              title="Choose what this companion does after leaving the party"
            >
              {showDeactivatePicker ? 'Deactivating...' : 'Deactivate'}
            </button>
          ) : isOnDuty ? (
            <button
              type="button"
              style={actionBtnStyle('#ccaa44')}
              onClick={onRecall}
              title="Recall from homestead duty and return to party pool"
            >
              Recall
            </button>
          ) : (
            <button
              type="button"
              style={actionBtnStyle(activeCount < 3 ? '#00ccff' : '#555555')}
              onClick={() => activeCount < 3 && onActivate()}
              disabled={activeCount >= 3}
              title={activeCount >= 3 ? 'Active party full (max 3)' : 'Add to active party'}
            >
              Activate
            </button>
          )}
        </div>
      </div>

      {/* Stat row */}
      <div style={{ fontSize: '11px', color: '#888888', marginBottom: '4px' }}>
        <span style={{ color: elementColor(companion.element) }}>{companion.element}</span>
        {' '}
        <span style={{ color: '#aaaaaa' }}>{companion.type}</span>
        {'  '}
        Lv.{companion.level}
        {'  '}
        <LayerStars layer={companion.currentLayer} />
        {' '}Bond {companion.currentLayer}
      </div>

      {/* Bond progress bar */}
      <div style={{ marginBottom: '4px' }}>
        <span style={{ color: '#666666', fontSize: '10px', marginRight: '4px' }}>Bond:</span>
        <UsageBar type={companion.type} layer={companion.currentLayer} usage={companion.usageCounter} />
      </div>

      {/* Drift bar — only for adventuring companions */}
      {!isOnDuty && (
        <div style={{ marginBottom: '4px', fontSize: '10px' }}>
          <span style={{ color: '#666666', marginRight: '4px' }}>Drift:</span>
          <span style={{ color: driftColor(companion.driftAccumulator) }}>
            {companion.driftAccumulator.toFixed(1)}/50.0
          </span>
          {!isInActiveParty && (
            <span style={{ color: '#555555', marginLeft: '6px' }}>(idle companions drift — assign duty to pause drift)</span>
          )}
        </div>
      )}

      {/* Unlocked abilities — shown for active and idle companions */}
      {!isOnDuty && (
        <div style={{ fontSize: '10px', color: '#555555', marginTop: '4px' }}>
          {unlockedAbilities.map((ability, i) => (
            <div key={i} style={{ color: '#666666' }}>
              <span style={{ color: '#ffcc00' }}>✓</span> {ability}
            </div>
          ))}
          {companion.currentLayer < 6 && (
            <div style={{ color: '#333333', marginTop: '2px' }}>
              <span style={{ color: '#444444' }}>○</span> {abilitiesForType[companion.currentLayer]} [locked]
            </div>
          )}
        </div>
      )}

      {/* Deactivate choice picker — inline prompt after clicking Deactivate */}
      {isInActiveParty && showDeactivatePicker && (
        <DeactivateChoicePicker
          companion={companion}
          onRest={handleRest}
          onQuickAssign={handleQuickAssign}
          onCancel={() => setShowDeactivatePicker(false)}
        />
      )}

      {/* Homestead assignment panel — shown for all inactive companions */}
      {!isInActiveParty && (
        <HomesteadAssignPanel
          companion={companion}
          onAssign={onAssign}
          onRecall={onRecall}
        />
      )}
    </div>
  );
}

// ---- Section header ---------------------------------------------------------

function SectionHeader({ label, count, note }: { label: string; count: number; note?: string }) {
  return (
    <div style={sectionHeaderStyle}>
      <span style={{ color: '#00ff41', letterSpacing: '0.15em' }}>{label}</span>
      <span style={{ color: '#555555', marginLeft: '8px' }}>({count})</span>
      {note && <span style={{ color: '#444444', marginLeft: '12px', fontSize: '10px' }}>{note}</span>}
    </div>
  );
}

// ---- Main component ---------------------------------------------------------

const overlayStyle: React.CSSProperties = {
  position: 'fixed',
  inset: 0,
  background: 'rgba(0,0,0,0.82)',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  zIndex: 160,
};

const panelStyle: React.CSSProperties = {
  background: '#0d0d0d',
  border: '1px solid #00ccff',
  fontFamily: 'monospace',
  fontSize: '12px',
  color: '#00ff41',
  width: '660px',
  maxHeight: '85vh',
  overflowY: 'auto',
  boxShadow: '0 0 40px rgba(0, 204, 255, 0.2)',
};

const headerStyle: React.CSSProperties = {
  display: 'flex',
  justifyContent: 'space-between',
  alignItems: 'center',
  padding: '10px 14px',
  borderBottom: '1px solid #1a3a3a',
  letterSpacing: '0.1em',
};

const closeBtnStyle: React.CSSProperties = {
  background: 'none',
  border: '1px solid #00ccff',
  color: '#00ccff',
  fontFamily: 'monospace',
  fontSize: '12px',
  padding: '2px 10px',
  cursor: 'pointer',
};

const cardStyle = (isActive: boolean, isOnDuty: boolean): React.CSSProperties => ({
  margin: '6px 12px',
  padding: '8px 10px',
  border: `1px solid ${isOnDuty ? '#ccaa44' : isActive ? '#00ccff' : '#1a3a1a'}`,
  background: isOnDuty ? '#0a0d00' : isActive ? '#0a1520' : '#0a0a0a',
  position: 'relative',
});

const actionBtnStyle = (color: string): React.CSSProperties => ({
  background: 'none',
  border: `1px solid ${color}`,
  color: color,
  fontFamily: 'monospace',
  fontSize: '11px',
  padding: '2px 8px',
  cursor: 'pointer',
  marginLeft: '6px',
});

const dutyPanelStyle: React.CSSProperties = {
  background: '#0a0800',
  border: '1px solid #443300',
  padding: '8px 10px',
  marginTop: '8px',
  fontSize: '11px',
};

const assignBtnStyle: React.CSSProperties = {
  background: 'none',
  border: '1px solid #00cc88',
  color: '#00cc88',
  fontFamily: 'monospace',
  fontSize: '11px',
  padding: '3px 12px',
  cursor: 'pointer',
};

const recallBtnStyle: React.CSSProperties = {
  background: 'none',
  border: '1px solid #00ccff',
  color: '#00ccff',
  fontFamily: 'monospace',
  fontSize: '11px',
  padding: '2px 10px',
  cursor: 'pointer',
};

const sectionHeaderStyle: React.CSSProperties = {
  padding: '6px 14px 4px',
  fontSize: '11px',
  borderTop: '1px solid #1a1a1a',
  marginTop: '4px',
};

const deactivatePickerStyle: React.CSSProperties = {
  background: '#100800',
  border: '1px solid #ff8800',
  padding: '8px 10px',
  marginTop: '8px',
  fontSize: '11px',
};

const restBtnStyle: React.CSSProperties = {
  background: 'none',
  border: '1px solid #666666',
  color: '#aaaaaa',
  fontFamily: 'monospace',
  fontSize: '11px',
  padding: '3px 10px',
  cursor: 'pointer',
};

const quickAssignBtnStyle: React.CSSProperties = {
  background: 'none',
  border: '1px solid #ccaa44',
  color: '#ccaa44',
  fontFamily: 'monospace',
  fontSize: '11px',
  padding: '3px 8px',
  cursor: 'pointer',
};

const cancelPickerBtnStyle: React.CSSProperties = {
  background: 'none',
  border: '1px solid #333333',
  color: '#555555',
  fontFamily: 'monospace',
  fontSize: '10px',
  padding: '2px 6px',
  cursor: 'pointer',
  marginLeft: '4px',
};

export default function CompanionPanel({ companions, activeCompanionIds, onActivate, onDeactivate, onClose, sendCommand }: Props) {
  const commands = useGameCommands(sendCommand);
  // Use player.activeCompanionIds as ground truth, not companion.isActive (which can be stale)
  const activeCompanions    = companions.filter(c => activeCompanionIds.includes(c.id));
  const dutyCompanions      = companions.filter(c => !activeCompanionIds.includes(c.id) && !!c.assignedDuty);
  const idleCompanions      = companions.filter(c => !activeCompanionIds.includes(c.id) && !c.assignedDuty);
  const activeCount         = activeCompanions.length;

  const handleAssign = (companionId: string, duty: HomesteadDuty) => {
    commands.assignCompanionDuty({ companionId, duty });
  };

  const handleRecall = (companionId: string) => {
    commands.recallCompanion({ companionId });
  };

  // One-click: deactivate then assign duty in sequence
  const handleQuickAssign = (companionId: string, duty: HomesteadDuty) => {
    onDeactivate(companionId);
    setTimeout(() => {
      commands.assignCompanionDuty({ companionId, duty });
    }, 300);
  };

  const renderCard = (companion: CompanionState) => (
    <CompanionCard
      key={companion.id}
      companion={companion}
      activeCount={activeCount}
      isInActiveParty={activeCompanionIds.includes(companion.id)}
      onActivate={() => onActivate(companion.id)}
      onDeactivate={() => onDeactivate(companion.id)}
      onAssign={(duty) => handleAssign(companion.id, duty)}
      onRecall={() => handleRecall(companion.id)}
      onQuickAssign={(duty) => handleQuickAssign(companion.id, duty)}
    />
  );

  return (
    <div
      style={overlayStyle}
      data-testid="companion-panel"
      onClick={(e) => { if (e.target === e.currentTarget) onClose(); }}
    >
      <div style={panelStyle}>
        <div style={headerStyle}>
          <span>
            COMPANIONS — {activeCount}/3 adventuring
            {dutyCompanions.length > 0 && (
              <span style={{ color: '#ccaa44', marginLeft: '12px' }}>
                {dutyCompanions.length} on homestead duty
              </span>
            )}
          </span>
          <button type="button" style={closeBtnStyle} onClick={onClose} aria-label="close companion panel">
            close [x]
          </button>
        </div>

        {/* Strategy note */}
        <div style={{ padding: '6px 14px', fontSize: '11px', color: '#888888', borderBottom: '1px solid #1a1a1a' }}>
          Choose 3 companions to adventure — or assign inactive companions to homestead duty for passive income.
          Higher bond = stronger combat abilities. Use-or-lose: idle companions drift and lose bond.
        </div>
        {/* Bond tooltip */}
        <div style={{ padding: '4px 14px 6px', fontSize: '10px', color: '#555555', borderBottom: '1px solid #1a1a1a' }}>
          <span style={{ color: '#666666' }}>Bond level (1–6).</span>{' '}
          Higher bond = stronger abilities. Increases through combat (+10) and homestead duty (+3). Idle companions drift and lose bond.
        </div>

        {companions.length === 0 && (
          <div style={{ padding: '14px', color: '#888888' }}>
            You have no companions yet. Defeat monsters in combat — some can be captured!
          </div>
        )}

        {/* ---- ADVENTURING section ---- */}
        {companions.length > 0 && (
          <>
            <SectionHeader
              label="ADVENTURING"
              count={activeCount}
              note={activeCount === 3 ? 'party full' : `${3 - activeCount} slot${3 - activeCount !== 1 ? 's' : ''} open`}
            />
            {activeCompanions.length === 0 ? (
              <div style={{ padding: '6px 14px 10px', color: '#444444', fontSize: '11px' }}>
                (no active companions — activate one below)
              </div>
            ) : (
              activeCompanions.map(renderCard)
            )}

            {/* ---- HOMESTEAD DUTY section ---- */}
            <SectionHeader
              label="HOMESTEAD DUTY"
              count={dutyCompanions.length}
              note={dutyCompanions.length === 0 ? 'deactivate a companion to assign duty' : undefined}
            />
            {dutyCompanions.length === 0 ? (
              <div style={{ padding: '6px 14px 10px', color: '#444444', fontSize: '11px' }}>
                (none assigned)
              </div>
            ) : (
              dutyCompanions.map(renderCard)
            )}

            {/* ---- IDLE section ---- */}
            <SectionHeader
              label="IDLE"
              count={idleCompanions.length}
              note={idleCompanions.length > 0 ? 'drifting — assign duty to pause drift' : undefined}
            />
            {idleCompanions.length === 0 ? (
              <div style={{ padding: '6px 14px 10px', color: '#444444', fontSize: '11px' }}>
                (none)
              </div>
            ) : (
              idleCompanions.map(renderCard)
            )}
          </>
        )}

        <div style={{ padding: '8px 14px', borderTop: '1px solid #1a1a1a', fontSize: '11px', color: '#888888' }}>
          [B] to close · Homestead duty: Harvester, Salvager, Guard, Crafter
        </div>
      </div>
    </div>
  );
}
