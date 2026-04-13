import { useEffect, useRef } from 'react';
import type { ZoneTile } from '../types/game';
import type { BiomeType } from '../utils/biome';

export interface TileProfileData {
  /** Grid coord x */
  gx: number;
  /** Grid coord y */
  gy: number;
  /** Biome type derived from client biome util */
  biome: BiomeType;
  /** Zone at this tile, if any */
  zone: ZoneTile | null;
  /** Whether this tile has ever been within fog-of-war visibility */
  isKnown: boolean;
}

interface Props {
  /** Screen-space anchor (client x,y from the click event) */
  anchorX: number;
  anchorY: number;
  data: TileProfileData;
  onClose: () => void;
}

const BIOME_LABEL: Record<BiomeType, string> = {
  water:        'Water',
  sand:         'Sand / Beach',
  grassland:    'Grassland',
  lightForest:  'Light Forest',
  denseForest:  'Dense Forest',
  mountain:     'Mountain',
  snowMountain: 'Snow Mountain',
  path:         'Path',
};

// Rough per-biome flora hint — placeholder until the herbology tiering content
// lands.  The herbology agent will replace this with data-driven per-biome
// herb lists; for now we show a biome-tagged placeholder so the card is not
// empty.
function floraPlaceholder(biome: BiomeType): string[] {
  switch (biome) {
    case 'grassland':    return ['Common herbs', 'Wildflowers (tier I)'];
    case 'lightForest':  return ['Forest herbs', 'Mushrooms (tier I–II)'];
    case 'denseForest':  return ['Rare forest herbs (tier II–III)', 'Moss'];
    case 'mountain':     return ['Hardy alpine flora (tier II)'];
    case 'snowMountain': return ['Frost lichen (tier III+)'];
    case 'sand':         return ['Sparse desert flora'];
    case 'water':        return ['Kelp, reeds'];
    case 'path':         return ['Trampled grass'];
    default:             return [];
  }
}

function dangerWord(level: number): string {
  if (level <= 3) return 'Safe';
  if (level <= 6) return 'Hazardous';
  return 'Deadly';
}

export default function TileProfileCard({ anchorX, anchorY, data, onClose }: Props) {
  const rootRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        e.preventDefault();
        onClose();
      }
    };
    const onDown = (e: MouseEvent) => {
      const el = rootRef.current;
      if (!el) return;
      if (!el.contains(e.target as Node)) onClose();
    };
    window.addEventListener('keydown', onKey);
    // Use a slight delay so the click that opened the card doesn't immediately close it.
    const timer = window.setTimeout(() => {
      window.addEventListener('mousedown', onDown);
    }, 0);
    return () => {
      window.removeEventListener('keydown', onKey);
      window.removeEventListener('mousedown', onDown);
      window.clearTimeout(timer);
    };
  }, [onClose]);

  // Position the card so it doesn't fall off-screen.  Width/height are fixed
  // enough to make a simple edge-clamp reliable; if real dimensions differ
  // slightly the clamp still keeps the card on-screen.
  const CARD_W = 260;
  const CARD_H = 280;
  const vw = typeof window !== 'undefined' ? window.innerWidth  : 1024;
  const vh = typeof window !== 'undefined' ? window.innerHeight : 768;
  let left = anchorX + 12;
  let top  = anchorY + 12;
  if (left + CARD_W > vw - 8) left = Math.max(8, anchorX - CARD_W - 12);
  if (top  + CARD_H > vh - 8) top  = Math.max(8, anchorY - CARD_H - 12);

  const { gx, gy, biome, zone, isKnown } = data;

  return (
    <div
      ref={rootRef}
      role="dialog"
      aria-label="Tile profile"
      style={{
        position: 'fixed',
        left, top,
        width: CARD_W,
        maxHeight: CARD_H,
        overflowY: 'auto',
        background: 'rgba(18, 14, 10, 0.96)',
        border: '1px solid #7a5530',
        borderRadius: 4,
        color: '#e8dcc0',
        font: '12px/1.4 monospace',
        padding: '10px 12px',
        zIndex: 1000,
        boxShadow: '0 4px 16px rgba(0,0,0,0.6)',
      }}
    >
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', marginBottom: 6 }}>
        <strong style={{ color: '#ffcc66' }}>Tile ({gx}, {gy})</strong>
        <button
          onClick={onClose}
          style={{
            background: 'transparent',
            border: 'none',
            color: '#886644',
            cursor: 'pointer',
            font: 'inherit',
          }}
          aria-label="Close profile"
        >[x]</button>
      </div>

      <div style={{ marginBottom: 4 }}>
        <span style={{ color: '#aa8866' }}>Biome:</span> {BIOME_LABEL[biome] ?? biome}
      </div>

      {!isKnown ? (
        <div style={{ color: '#886644', fontStyle: 'italic', marginTop: 8 }}>
          Unexplored territory — venture here to learn what dwells within.
        </div>
      ) : zone ? (
        <>
          <div style={{ marginBottom: 4 }}>
            <span style={{ color: '#aa8866' }}>Zone:</span> {zone.name}
          </div>
          <div style={{ marginBottom: 4 }}>
            <span style={{ color: '#aa8866' }}>Danger:</span>{' '}
            <span style={{ color: zone.dangerLevel <= 3 ? '#66dd44' : zone.dangerLevel <= 6 ? '#ffbb22' : '#ff3311' }}>
              {dangerWord(zone.dangerLevel)} (lvl {zone.dangerLevel})
            </span>
            {zone.isPortalZone && <span style={{ color: '#cc44ff', marginLeft: 6 }}>[Portal]</span>}
          </div>
          <div style={{ marginBottom: 4, color: '#aa8866' }}>— Resources —</div>
          <div style={{ marginBottom: 8, color: '#886644', fontStyle: 'italic' }}>
            Resource node data coming soon.
          </div>
          <div style={{ marginBottom: 4, color: '#aa8866' }}>— Beasts —</div>
          <div style={{ marginBottom: 8, color: '#886644', fontStyle: 'italic' }}>
            Spawn pool data coming soon.
          </div>
          <div style={{ marginBottom: 4, color: '#aa8866' }}>— Flora —</div>
          <ul style={{ margin: '0 0 8px 14px', padding: 0 }}>
            {floraPlaceholder(biome).map(f => <li key={f}>{f}</li>)}
          </ul>
          <div style={{ marginBottom: 4, color: '#aa8866' }}>— NPCs —</div>
          <div style={{ color: '#886644', fontStyle: 'italic' }}>
            NPC roster data coming soon.
          </div>
        </>
      ) : (
        <>
          <div style={{ marginTop: 6, color: '#aa8866' }}>— Flora —</div>
          <ul style={{ margin: '4px 0 0 14px', padding: 0 }}>
            {floraPlaceholder(biome).map(f => <li key={f}>{f}</li>)}
          </ul>
          <div style={{ marginTop: 8, color: '#886644', fontStyle: 'italic' }}>
            No zone at this tile.
          </div>
        </>
      )}

      <div style={{ marginTop: 10, paddingTop: 6, borderTop: '1px solid #3a2c1a', color: '#886644', fontSize: 10 }}>
        Click elsewhere or press Esc to close.
      </div>
    </div>
  );
}
