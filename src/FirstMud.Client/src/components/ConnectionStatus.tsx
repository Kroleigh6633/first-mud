import type { ConnectionState } from '../types/game';

interface Props {
  state: ConnectionState;
}

export default function ConnectionStatus({ state }: Props) {
  const config: Record<ConnectionState, { symbol: string; label: string; color: string }> = {
    connected:    { symbol: '●', label: 'CONNECTED',    color: '#00ff41' },
    connecting:   { symbol: '◌', label: 'CONNECTING...', color: '#ffcc00' },
    disconnected: { symbol: '✕', label: 'DISCONNECTED', color: '#888888' },
    error:        { symbol: '✕', label: 'ERROR',        color: '#ff0000' },
  };

  const { symbol, label, color } = config[state];

  return (
    <div style={{
      position: 'absolute',
      top: '6px',
      right: '12px',
      color,
      fontSize: '12px',
      fontFamily: 'monospace',
      letterSpacing: '0.05em',
      userSelect: 'none',
      zIndex: 10,
    }}>
      {symbol} {label}
    </div>
  );
}
