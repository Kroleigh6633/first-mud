import { useState, useRef, useEffect } from 'react';

const STORAGE_KEY = 'firstmud_player';
// Same-origin — Vite dev server proxies /api to the gameserver container.
const API_BASE = '';

interface Props {
  onCreated: (playerId: string) => void;
}

const overlayStyle: React.CSSProperties = {
  position: 'fixed',
  inset: 0,
  background: 'rgba(0, 0, 0, 0.85)',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  zIndex: 200,
};

const panelStyle: React.CSSProperties = {
  background: '#0d0d0d',
  border: '1px solid #00ff41',
  fontFamily: 'monospace',
  fontSize: '13px',
  color: '#00ff41',
  width: '340px',
  boxShadow: '0 0 30px rgba(0, 255, 65, 0.2)',
};

const headerStyle: React.CSSProperties = {
  padding: '8px 12px',
  borderBottom: '1px solid #00ff41',
  letterSpacing: '0.12em',
  color: '#00ff41',
};

const bodyStyle: React.CSSProperties = {
  padding: '16px 12px',
};

const inputStyle: React.CSSProperties = {
  background: 'none',
  border: 'none',
  borderBottom: '1px solid #00ff41',
  color: '#00ff41',
  fontFamily: 'monospace',
  fontSize: '14px',
  outline: 'none',
  width: '100%',
  caretColor: '#00ff41',
  marginBottom: '16px',
};

const submitBtnStyle: React.CSSProperties = {
  background: 'none',
  border: '1px solid #00ff41',
  color: '#00ff41',
  fontFamily: 'monospace',
  fontSize: '13px',
  padding: '6px 16px',
  cursor: 'pointer',
  letterSpacing: '0.08em',
  width: '100%',
};

const errorStyle: React.CSSProperties = {
  color: '#ff4444',
  fontSize: '11px',
  marginBottom: '10px',
};

export default function PlayerCreation({ onCreated }: Props) {
  const [name, setName] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    inputRef.current?.focus();
  }, []);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    const trimmed = name.trim();
    if (!trimmed) {
      setError('A name is required.');
      return;
    }
    setError(null);
    setSubmitting(true);
    try {
      const response = await fetch(`${API_BASE}/api/players`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          name: trimmed,
          craftingSeed: Math.floor(Math.random() * 999999),
        }),
      });
      if (!response.ok) {
        const text = await response.text();
        setError(text || `Server error: ${response.status}`);
        setSubmitting(false);
        return;
      }
      const player = await response.json() as { id: string; name: string };
      localStorage.setItem(STORAGE_KEY, JSON.stringify({ id: player.id, name: player.name }));
      onCreated(player.id);
      window.location.reload();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Unknown error';
      setError(`Failed to create player: ${msg}`);
      setSubmitting(false);
    }
  };

  return (
    <div style={overlayStyle}>
      <div style={panelStyle}>
        <div style={headerStyle}>ENTER YOUR NAME, RIDER</div>
        <div style={bodyStyle}>
          <form onSubmit={handleSubmit}>
            <div style={{ display: 'flex', alignItems: 'center', marginBottom: '16px' }}>
              <span style={{ marginRight: '8px', color: '#00ff41' }}>&gt;</span>
              <input
                ref={inputRef}
                style={inputStyle}
                type="text"
                value={name}
                onChange={e => setName(e.target.value)}
                maxLength={32}
                autoComplete="off"
                spellCheck={false}
                disabled={submitting}
                placeholder="_"
              />
            </div>
            {error && <div style={errorStyle}>{error}</div>}
            <button style={submitBtnStyle} type="submit" disabled={submitting}>
              {submitting ? 'ENTERING THE REALM...' : 'BEGIN YOUR JOURNEY'}
            </button>
          </form>
        </div>
      </div>
    </div>
  );
}
