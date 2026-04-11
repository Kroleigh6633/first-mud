import type { QuestNode } from '../types/game';

const FACTION_NAMES: Record<number, string> = {
  1: 'House Caervorn',
  2: 'Thornwood Covens',
  3: 'Emerald Compact',
  4: 'Gravenguard',
  5: 'Fairgean',
  6: 'Golvari',
  7: 'Ashen Court',
};

interface Props {
  quests: QuestNode[];
  onAccept: (questId: string) => void;
  onComplete: (questId: string, outcome: string) => void;
  onClose: () => void;
}

const overlayStyle: React.CSSProperties = {
  position: 'fixed',
  inset: 0,
  background: 'rgba(0, 0, 0, 0.75)',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  zIndex: 100,
};

const panelStyle: React.CSSProperties = {
  background: '#0d0d0d',
  border: '1px solid #00ff41',
  fontFamily: 'monospace',
  fontSize: '13px',
  color: '#00ff41',
  minWidth: '420px',
  maxWidth: '560px',
  maxHeight: '80vh',
  display: 'flex',
  flexDirection: 'column',
  boxShadow: '0 0 20px rgba(0, 255, 65, 0.15)',
};

const headerStyle: React.CSSProperties = {
  display: 'flex',
  justifyContent: 'space-between',
  alignItems: 'center',
  padding: '8px 12px',
  borderBottom: '1px solid #00ff41',
  color: '#00ff41',
  letterSpacing: '0.1em',
};

const questListStyle: React.CSSProperties = {
  overflowY: 'auto',
  flex: 1,
  scrollbarWidth: 'thin',
  scrollbarColor: '#1a3a1a #0d0d0d',
};

const questItemStyle: React.CSSProperties = {
  padding: '10px 12px',
  borderBottom: '1px solid #1a3a1a',
};

const btnStyle: React.CSSProperties = {
  background: 'none',
  border: '1px solid #00ff41',
  color: '#00ff41',
  fontFamily: 'monospace',
  fontSize: '11px',
  padding: '2px 8px',
  cursor: 'pointer',
  marginRight: '6px',
  marginTop: '4px',
};

const closeBtnStyle: React.CSSProperties = {
  background: 'none',
  border: 'none',
  color: '#888888',
  fontFamily: 'monospace',
  fontSize: '13px',
  cursor: 'pointer',
  padding: '0',
};

export default function QuestLog({ quests, onAccept, onComplete, onClose }: Props) {
  return (
    <div style={overlayStyle} onClick={onClose}>
      <div style={panelStyle} onClick={e => e.stopPropagation()}>
        <div style={headerStyle}>
          <span>QUEST LOG</span>
          <button style={closeBtnStyle} onClick={onClose}>[Q]</button>
        </div>

        <div style={questListStyle}>
          {quests.length === 0 ? (
            <div style={{ padding: '12px', color: '#555555' }}>
              (no quests available)
            </div>
          ) : (
            quests.map(quest => (
              <div key={quest.questId} style={questItemStyle}>
                <div style={{ marginBottom: '4px' }}>
                  <span style={{ color: '#00ff41', marginRight: '6px' }}>▶</span>
                  <span style={{ color: '#ffffff' }}>{quest.title}</span>
                  {quest.isWyrdQuest && (
                    <span style={{ color: '#cc88ff', marginLeft: '6px' }}>✦</span>
                  )}
                </div>
                <div style={{ color: '#888888', fontSize: '11px', marginBottom: '2px' }}>
                  {FACTION_NAMES[quest.factionId] ?? `Faction ${quest.factionId}`}
                  {' · '}
                  {quest.requiredTier === 0 ? 'Unknown' : `Tier ${quest.requiredTier}`}
                </div>
                <div style={{ color: '#ffcc00', fontSize: '11px', marginBottom: '6px' }}>
                  Reward: {quest.reputationReward} rep
                </div>
                {quest.description && (
                  <div style={{ color: '#aaaaaa', fontSize: '11px', marginBottom: '6px' }}>
                    {quest.description}
                  </div>
                )}
                <div>
                  {!quest.isTaken && (
                    <button
                      style={btnStyle}
                      onClick={() => onAccept(quest.questId)}
                    >
                      Accept
                    </button>
                  )}
                  {quest.isTaken && quest.possibleOutcomes.map(outcome => (
                    <button
                      key={outcome}
                      style={btnStyle}
                      onClick={() => onComplete(quest.questId, outcome)}
                    >
                      Complete: {outcome}
                    </button>
                  ))}
                </div>
              </div>
            ))
          )}
        </div>
      </div>
    </div>
  );
}
