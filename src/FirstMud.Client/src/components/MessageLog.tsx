import { useEffect, useRef } from 'react';
import type { GameMessage } from '../types/game';

interface Props {
  messages: GameMessage[];
}

const CATEGORY_COLORS: Record<GameMessage['category'], string> = {
  system:  '#888888',
  combat:  '#ff4444',
  quest:   '#ffcc00',
  loot:    '#00ccff',
  npc:     '#ffffff',
  wyrd:    '#cc88ff',
  error:   '#ff0000',
};

function formatTimestamp(iso: string): string {
  try {
    const d = new Date(iso);
    const hh = String(d.getHours()).padStart(2, '0');
    const mm = String(d.getMinutes()).padStart(2, '0');
    const ss = String(d.getSeconds()).padStart(2, '0');
    return `${hh}:${mm}:${ss}`;
  } catch {
    return '??:??:??';
  }
}

export default function MessageLog({ messages }: Props) {
  const bottomRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages]);

  return (
    <div style={{
      height: '100%',
      overflowY: 'auto',
      padding: '6px 10px',
      fontFamily: 'monospace',
      fontSize: '12px',
      background: '#0d0d0d',
      boxSizing: 'border-box',
      scrollbarWidth: 'thin',
      scrollbarColor: '#333333 #0d0d0d',
    }}>
      {messages.length === 0 && (
        <div style={{ color: '#333333' }}>No messages yet...</div>
      )}
      {messages.map((msg, idx) => (
        <div key={idx} style={{ marginBottom: '2px', lineHeight: '1.4' }}>
          <span style={{ color: '#444444', marginRight: '6px' }}>
            [{formatTimestamp(msg.timestamp)}]
          </span>
          <span style={{ color: '#555555', marginRight: '6px', textTransform: 'uppercase', fontSize: '10px' }}>
            {msg.category}
          </span>
          <span style={{ color: CATEGORY_COLORS[msg.category] }}>
            {msg.text}
          </span>
        </div>
      ))}
      <div ref={bottomRef} />
    </div>
  );
}
