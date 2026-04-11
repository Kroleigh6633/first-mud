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
    if (!containerRef.current) return;

    const display = new ROT.Display({
      width: MAP_WIDTH,
      height: MAP_HEIGHT,
      fontSize: 14,
      fontFamily: 'monospace',
      bg: '#0d0d0d',
      fg: '#00ff41',
    });

    displayRef.current = display;
    containerRef.current.appendChild(display.getContainer()!);

    // Blink timer for player @
    blinkTimerRef.current = setInterval(() => {
      blinkRef.current = !blinkRef.current;
    }, 500);

    return () => {
      if (blinkTimerRef.current) clearInterval(blinkTimerRef.current);
      if (containerRef.current && display.getContainer()) {
        // eslint-disable-next-line react-hooks/exhaustive-deps
        containerRef.current.removeChild(display.getContainer()!);
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

      // Draw real zone tiles
      for (const tile of zoneTiles) {
        const screenX = tile.x - offsetX;
        const screenY = tile.y - offsetY;
        if (
          screenX >= 0 && screenX < MAP_WIDTH &&
          screenY >= 0 && screenY < MAP_HEIGHT
        ) {
          const [fg, bg] = tileColor(tile);
          display.draw(screenX, screenY, tile.asciiSymbol, fg, bg);
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
