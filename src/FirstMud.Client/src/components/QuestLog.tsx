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

function questDistance(quest: QuestNode, playerX?: number, playerY?: number): number | null {
  // QuestNode doesn't carry coordinates directly; distance is only available
  // when a waypoint is active. Return null when unknown.
  void quest; void playerX; void playerY;
  return null;
}

interface Props {
  quests: QuestNode[];
  questProgress: QuestProgressMap;
  playerX?: number;
  playerY?: number;
  onAccept: (questId: string) => void;
  onAcceptAll: () => void;
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

const subHeaderStyle: React.CSSProperties = {
  display: 'flex',
  justifyContent: 'space-between',
  alignItems: 'center',
  padding: '6px 12px',
  borderBottom: '1px solid #1a3a1a',
  background: '#070f07',
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

const acceptBtnStyle: React.CSSProperties = {
  background: '#1a1200',
  border: '2px solid #ffcc00',
  color: '#ffcc00',
  fontFamily: 'monospace',
  fontSize: '12px',
  fontWeight: 'bold',
  padding: '4px 14px',
  cursor: 'pointer',
  marginRight: '6px',
  marginTop: '4px',
  letterSpacing: '0.05em',
};

const acceptAllBtnStyle: React.CSSProperties = {
  background: '#1a1200',
  border: '1px solid #ffcc00',
  color: '#ffcc00',
  fontFamily: 'monospace',
  fontSize: '11px',
  padding: '2px 10px',
  cursor: 'pointer',
  letterSpacing: '0.04em',
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

export default function QuestLog({ quests, questProgress, playerX, playerY, onAccept, onAcceptAll, onComplete, onNavigate, onClose }: Props) {
  // Sort: accepted/in-progress first, then available by rep reward desc
  const sortedQuests = [...quests].sort((a, b) => {
    if (a.isTaken && !b.isTaken) return -1;
    if (!a.isTaken && b.isTaken) return 1;
    // Both available: sort by rep reward descending
    return b.reputationReward - a.reputationReward;
  });

  const availableCount = quests.filter(q => !q.isTaken).length;
  const inProgressCount = quests.filter(q => q.isTaken).length;

  return (
    <div style={overlayStyle} onClick={onClose}>
      <div style={panelStyle} onClick={e => e.stopPropagation()}>
        <div style={headerStyle}>
          <span>
            QUEST LOG
            {inProgressCount > 0 && (
              <span style={{ color: '#aaffcc', fontSize: '11px', marginLeft: '10px', fontWeight: 'normal' }}>
                {inProgressCount} active
              </span>
            )}
            {availableCount > 0 && (
              <span style={{ color: '#888888', fontSize: '11px', marginLeft: '8px', fontWeight: 'normal' }}>
                {availableCount} available
              </span>
            )}
          </span>
          <div style={{ display: 'flex', gap: '8px', alignItems: 'center' }}>
            <button style={closeBtnStyle} onClick={onClose}>[Q]</button>
          </div>
        </div>

        {/* Accept All toolbar */}
        {availableCount > 0 && (
          <div style={subHeaderStyle}>
            <span style={{ color: '#888888', fontSize: '11px' }}>
              {availableCount} quest{availableCount !== 1 ? 's' : ''} available to accept
            </span>
            <button style={acceptAllBtnStyle} onClick={onAcceptAll}>
              [Accept All {availableCount}]
            </button>
          </div>
        )}

        <div style={questListStyle}>
          {quests.length === 0 ? (
            <div style={{ padding: '12px', color: '#555555' }}>
              (no quests available)
            </div>
          ) : (
            sortedQuests.map(quest => {
              const progress = questProgress[quest.questId];
              const questType = getQuestType(quest.title);
              const isKillQuest = questType === 'kill';
              const killsDone = progress?.kills ?? 0;
              const killsRequired = progress?.required ?? 0;
              const killsComplete = isKillQuest && killsRequired > 0 && killsDone >= killsRequired;
              const dist = questDistance(quest, playerX, playerY);

              return (
                <div
                  key={quest.questId}
                  style={{
                    ...questItemStyle,
                    background: quest.isTaken ? '#070f07' : 'transparent',
                    borderLeft: quest.isTaken ? '3px solid #00aa33' : '3px solid #1a1a00',
                  }}
                >
                  <div style={{ marginBottom: '4px', display: 'flex', alignItems: 'center', gap: '6px' }}>
                    <span style={{ color: quest.isTaken ? '#00ff41' : '#888888' }}>
                      {quest.isTaken ? '▶' : '○'}
                    </span>
                    <span style={{ color: '#ffffff', flex: 1 }}>{quest.title}</span>
                    {quest.isWyrdQuest && (
                      <span style={{ color: '#cc88ff' }}>✦</span>
                    )}
                    {quest.isTaken && (
                      <span style={{ color: '#00aa33', fontSize: '10px', letterSpacing: '0.08em' }}>
                        IN PROGRESS
                      </span>
                    )}
                    {!quest.isTaken && (
                      <span style={{ color: '#555555', fontSize: '10px', letterSpacing: '0.08em' }}>
                        AVAILABLE
                      </span>
                    )}
                  </div>
                  <div style={{ color: '#888888', fontSize: '11px', marginBottom: '2px' }}>
                    {FACTION_NAMES[quest.factionId] ?? `Faction ${quest.factionId}`}
                    {' · '}
                    {quest.requiredTier === 0 ? 'Unknown' : `Tier ${quest.requiredTier}`}
                    {dist !== null && (
                      <span style={{ color: '#556655' }}> · ~{dist} tiles away</span>
                    )}
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
                    {/* AVAILABLE: prominent Accept button, no Navigate/Complete */}
                    {!quest.isTaken && (
                      <button
                        style={acceptBtnStyle}
                        onClick={() => onAccept(quest.questId)}
                      >
                        [Accept]
                      </button>
                    )}

                    {/* IN PROGRESS: Navigate and Complete buttons */}
                    {quest.isTaken && (
                      <button
                        style={btnStyle}
                        onClick={() => onNavigate(quest.questId)}
                      >
                        Navigate [N]
                      </button>
                    )}
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
