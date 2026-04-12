import type { QuestNode, QuestProgressMap } from '../types/game';

const FACTION_NAMES: Record<number, string> = {
  1: 'House Caervorn',
  2: 'Thornwood Covens',
  3: 'Emerald Compact',
  4: 'Gravenguard',
  5: 'Fairgean',
  6: 'Golvari',
  7: 'Ashen Court',
};

function getQuestType(title: string): 'kill' | 'gather' | 'deliver' | 'explore' {
  const t = title.toLowerCase();
  if (/defeat|slay|hunt/.test(t)) return 'kill';
  if (/gather|collect|retrieve/.test(t)) return 'gather';
  if (/deliver|escort|bring/.test(t)) return 'deliver';
  return 'explore';
}

interface Props {
  quests: QuestNode[];
  questProgress: QuestProgressMap;
  onAccept: (questId: string) => void;
  onComplete: (questId: string, outcome: string) => void;
  onNavigate: (questId: string) => void;
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

export default function QuestLog({ quests, questProgress, onAccept, onComplete, onNavigate, onClose }: Props) {
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
            quests.map(quest => {
              const progress = questProgress[quest.questId];
              const questType = getQuestType(quest.title);
              const isKillQuest = questType === 'kill';
              const killsDone = progress?.kills ?? 0;
              const killsRequired = progress?.required ?? 0;
              const killsComplete = isKillQuest && killsRequired > 0 && killsDone >= killsRequired;

              return (
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

                  {/* Kill quest progress bar */}
                  {quest.isTaken && isKillQuest && killsRequired > 0 && (
                    <div style={{ marginBottom: '6px' }}>
                      <div style={{ color: '#aaaaaa', fontSize: '11px', marginBottom: '3px' }}>
                        {killsComplete
                          ? '✓ Objective complete — navigate to waypoint and press [E]'
                          : `Enemies defeated: ${killsDone} / ${killsRequired}`}
                      </div>
                      <div style={{ height: '4px', background: '#1a3a1a', width: '100%' }}>
                        <div style={{
                          height: '100%',
                          width: `${Math.min(100, killsRequired > 0 ? (killsDone / killsRequired) * 100 : 0)}%`,
                          background: killsComplete ? '#00ff41' : '#00aa33',
                          transition: 'width 0.3s ease',
                        }} />
                      </div>
                    </div>
                  )}

                  {/* In-progress hint for non-kill quests */}
                  {quest.isTaken && !isKillQuest && (
                    <div style={{ color: '#888888', fontSize: '11px', marginBottom: '6px' }}>
                      {questType === 'explore'
                        ? 'Navigate to the waypoint to complete.'
                        : 'Gather the required items, then navigate to the waypoint and press [E].'}
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
                    {quest.isTaken && (
                      <button
                        style={btnStyle}
                        onClick={() => onNavigate(quest.questId)}
                      >
                        Navigate [N]
                      </button>
                    )}
                    {/* Manual complete fallback — visible but only works when objectives are done */}
                    {quest.isTaken && quest.possibleOutcomes.map(outcome => (
                      <button
                        key={outcome}
                        style={{
                          ...btnStyle,
                          opacity: killsComplete || !isKillQuest ? 1 : 0.4,
                          cursor: killsComplete || !isKillQuest ? 'pointer' : 'not-allowed',
                        }}
                        onClick={() => (killsComplete || !isKillQuest) && onComplete(quest.questId, outcome)}
                        title={isKillQuest && !killsComplete ? 'Defeat more enemies first' : undefined}
                      >
                        Complete
                      </button>
                    ))}
                  </div>
                </div>
              );
            })
          )}
        </div>
      </div>
    </div>
  );
}
