import { useEffect, useRef } from 'react';
import * as ROT from 'rot-js';
import type { WorldStateSnapshot, ZoneTile } from '../types/game';

interface Props {
  worldState: WorldStateSnapshot | null;
  zoneTiles: ZoneTile[];
}

const MAP_WIDTH = 40;
const MAP_HEIGHT = 20;

// Colour scheme:
//   danger 1-3 → green
//   danger 4-6 → yellow
//   danger 7-10 → red
//   portal zone → magenta
//   fallback → grey
function tileColor(tile: ZoneTile): [string, string] {
  const bg = '#0d0d0d';
  if (tile.isPortalZone) return ['#cc44cc', bg];
  if (tile.dangerLevel <= 3) return ['#00bb33', bg];
  if (tile.dangerLevel <= 6) return ['#ccaa00', bg];
  return ['#cc2200', bg];
}

export default function WorldMap({ worldState, zoneTiles }: Props) {
  const containerRef = useRef<HTMLDivElement>(null);
  const displayRef = useRef<ROT.Display | null>(null);
  const blinkRef = useRef<boolean>(false);
  const blinkTimerRef = useRef<ReturnType<typeof setInterval> | null>(null);

  // Initialise rot.js display once
  useEffect(() => {
    const container = containerRef.current;
    if (!container) return;

    // React StrictMode double-mounts effects in dev. Clear any canvas
    // left over from a prior mount so we don't end up with ghost @s.
    while (container.firstChild) {
      container.removeChild(container.firstChild);
    }

    const display = new ROT.Display({
      width: MAP_WIDTH,
      height: MAP_HEIGHT,
      fontSize: 14,
      fontFamily: 'monospace',
      bg: '#0d0d0d',
      fg: '#00ff41',
    });

    displayRef.current = display;
    const canvas = display.getContainer();
    if (canvas) container.appendChild(canvas);

    // Blink timer for player @
    blinkTimerRef.current = setInterval(() => {
      blinkRef.current = !blinkRef.current;
    }, 500);

    return () => {
      if (blinkTimerRef.current) clearInterval(blinkTimerRef.current);
      if (canvas && canvas.parentNode === container) {
        container.removeChild(canvas);
      }
      displayRef.current = null;
    };
  }, []);

  // Re-draw every ~100ms so blink works
  useEffect(() => {
    const render = () => {
      const display = displayRef.current;
      if (!display) return;

      display.clear();

      const playerX = worldState?.player?.x ?? 0;
      const playerY = worldState?.player?.y ?? 0;

      // Viewport offset: centre on player
      const offsetX = playerX - Math.floor(MAP_WIDTH / 2);
      const offsetY = playerY - Math.floor(MAP_HEIGHT / 2);

      // Fill background
      for (let screenY = 0; screenY < MAP_HEIGHT; screenY++) {
        for (let screenX = 0; screenX < MAP_WIDTH; screenX++) {
          display.draw(screenX, screenY, ' ', '#0d0d0d', '#0d0d0d');
        }
      }

      // Draw real zone tiles — each zone covers a 3×3 area with its
      // symbol at the center and dim dots around the perimeter so the
      // player can see the walkable zone boundaries.
      for (const tile of zoneTiles) {
        const [fg] = tileColor(tile);
        const dimFg = fg + '44'; // 25% alpha
        const dimBg = '#111111';

        // Draw surrounding 3×3 halo (excluding center)
        for (let dy = -1; dy <= 1; dy++) {
          for (let dx = -1; dx <= 1; dx++) {
            if (dx === 0 && dy === 0) continue;
            const sx = tile.x + dx - offsetX;
            const sy = tile.y + dy - offsetY;
            if (sx >= 0 && sx < MAP_WIDTH && sy >= 0 && sy < MAP_HEIGHT) {
              display.draw(sx, sy, '\u00b7', dimFg, dimBg); // middle dot ·
            }
          }
        }

        // Draw center symbol
        const screenX = tile.x - offsetX;
        const screenY = tile.y - offsetY;
        if (
          screenX >= 0 && screenX < MAP_WIDTH &&
          screenY >= 0 && screenY < MAP_HEIGHT
        ) {
          display.draw(screenX, screenY, tile.asciiSymbol, fg, '#0d0d0d');
        }
      }

      // Draw AI players
      if (worldState) {
        for (const ai of worldState.aiPlayers) {
          // AiPlayerState has no position coords — skip for now
          void ai;
        }
      }

      // Draw player
      const playerScreenX = playerX - offsetX;
      const playerScreenY = playerY - offsetY;
      if (
        playerScreenX >= 0 && playerScreenX < MAP_WIDTH &&
        playerScreenY >= 0 && playerScreenY < MAP_HEIGHT
      ) {
        const playerFg = blinkRef.current ? '#ffffff' : '#cccccc';
        display.draw(playerScreenX, playerScreenY, '@', playerFg, '#0d0d0d');
      }
    };

    const timer = setInterval(render, 100);
    render(); // immediate first draw
    return () => clearInterval(timer);
  }, [worldState, zoneTiles]);

  return (
    <div
      ref={containerRef}
      style={{
        background: '#0d0d0d',
        lineHeight: 0,
        overflow: 'hidden',
        userSelect: 'none',
      }}
    />
  );
}
