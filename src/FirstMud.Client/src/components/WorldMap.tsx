import { useEffect, useRef } from 'react';
import * as ROT from 'rot-js';
import type { WorldStateSnapshot, ZoneTile } from '../types/game';

interface Props {
  worldState: WorldStateSnapshot | null;
  zoneTiles: ZoneTile[];
}

const MAP_WIDTH = 40;
const MAP_HEIGHT = 20;

function tileColor(tile: ZoneTile): [string, string] {
  const bg = '#0d0d0d';
  if (tile.isPortalZone) return ['#cc44cc', bg];
  if (tile.dangerLevel <= 3) return ['#00bb33', bg];
  if (tile.dangerLevel <= 6) return ['#ccaa00', bg];
  return ['#cc2200', bg];
}

// Deterministic hash for procedural terrain — same (x,y) always gives
// the same value so the landscape is stable as the player scrolls.
function terrainHash(x: number, y: number): number {
  let h = (x * 374761 + y * 668265) & 0x7fffffff;
  h = ((h >> 16) ^ h) * 0x45d9f3b;
  h = ((h >> 16) ^ h) * 0x45d9f3b;
  return ((h >> 16) ^ h) & 0xff;
}

interface TerrainGlyph {
  ch: string;
  fg: string;
  bg: string;
}

// Generates ambient terrain for a world-space coordinate. Returns null
// for cells that should stay blank (sparse coverage for readability).
function getTerrainAt(wx: number, wy: number): TerrainGlyph | null {
  const h = terrainHash(wx, wy);

  // ~45% of cells get terrain, rest stay dark for contrast
  if (h > 115) return null;

  if (h < 20) return { ch: '\u2663', fg: '#0a3a0a', bg: '#0d0d0d' }; // ♣ dark trees
  if (h < 35) return { ch: '.', fg: '#1a2a1a', bg: '#0d0d0d' }; // sparse grass
  if (h < 48) return { ch: ',', fg: '#1a2a1a', bg: '#0d0d0d' }; // grass variant
  if (h < 55) return { ch: '"', fg: '#1a2a0a', bg: '#0d0d0d' }; // tall grass
  if (h < 62) return { ch: '~', fg: '#0a1a2a', bg: '#0d0d0d' }; // water/stream
  if (h < 70) return { ch: '\u25b2', fg: '#1a1a1a', bg: '#0d0d0d' }; // ▲ mountain
  if (h < 80) return { ch: '\u00b7', fg: '#1a1a0a', bg: '#0d0d0d' }; // · pebbles
  if (h < 90) return { ch: ';', fg: '#1a2a1a', bg: '#0d0d0d' }; // scrub
  if (h < 100) return { ch: '\'', fg: '#0a2a1a', bg: '#0d0d0d' }; // moss
  if (h < 108) return { ch: '\u2022', fg: '#1a1a1a', bg: '#0d0d0d' }; // • rocks
  return { ch: '`', fg: '#151515', bg: '#0d0d0d' }; // gravel
}

// Draw faint paths connecting nearby zones so the map feels connected.
function drawPaths(
  display: ROT.Display,
  tiles: ZoneTile[],
  offsetX: number,
  offsetY: number,
) {
  const pathColor = '#1a1a0a';
  for (let i = 0; i < tiles.length; i++) {
    for (let j = i + 1; j < tiles.length; j++) {
      const a = tiles[i];
      const b = tiles[j];
      const dist = Math.abs(a.x - b.x) + Math.abs(a.y - b.y);
      if (dist > 18) continue; // only connect close zones

      // Bresenham-ish path: step from a to b
      const steps = Math.max(Math.abs(b.x - a.x), Math.abs(b.y - a.y));
      if (steps === 0) continue;
      for (let s = 1; s < steps; s++) {
        const t = s / steps;
        const px = Math.round(a.x + (b.x - a.x) * t);
        const py = Math.round(a.y + (b.y - a.y) * t);
        const sx = px - offsetX;
        const sy = py - offsetY;
        if (sx >= 0 && sx < MAP_WIDTH && sy >= 0 && sy < MAP_HEIGHT) {
          // Alternate between path chars for texture
          const ch = (s % 3 === 0) ? '\u00b7' : '\u2500'; // · or ─
          display.draw(sx, sy, ch, pathColor, '#0d0d0d');
        }
      }
    }
  }
}

export default function WorldMap({ worldState, zoneTiles }: Props) {
  const containerRef = useRef<HTMLDivElement>(null);
  const displayRef = useRef<ROT.Display | null>(null);
  const blinkRef = useRef<boolean>(false);
  const blinkTimerRef = useRef<ReturnType<typeof setInterval> | null>(null);

  useEffect(() => {
    const container = containerRef.current;
    if (!container) return;

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

  useEffect(() => {
    const render = () => {
      const display = displayRef.current;
      if (!display) return;

      display.clear();

      const playerX = worldState?.player?.x ?? 0;
      const playerY = worldState?.player?.y ?? 0;
      const offsetX = playerX - Math.floor(MAP_WIDTH / 2);
      const offsetY = playerY - Math.floor(MAP_HEIGHT / 2);

      // Layer 1: procedural terrain background
      for (let screenY = 0; screenY < MAP_HEIGHT; screenY++) {
        for (let screenX = 0; screenX < MAP_WIDTH; screenX++) {
          const wx = screenX + offsetX;
          const wy = screenY + offsetY;
          const terrain = getTerrainAt(wx, wy);
          if (terrain) {
            display.draw(screenX, screenY, terrain.ch, terrain.fg, terrain.bg);
          } else {
            display.draw(screenX, screenY, ' ', '#0d0d0d', '#0d0d0d');
          }
        }
      }

      // Layer 2: paths connecting zones
      drawPaths(display, zoneTiles, offsetX, offsetY);

      // Layer 3: zone tiles (3×3 halo + center symbol)
      for (const tile of zoneTiles) {
        const [fg] = tileColor(tile);
        const dimFg = fg + '44';
        const dimBg = '#111111';

        for (let dy = -1; dy <= 1; dy++) {
          for (let dx = -1; dx <= 1; dx++) {
            if (dx === 0 && dy === 0) continue;
            const sx = tile.x + dx - offsetX;
            const sy = tile.y + dy - offsetY;
            if (sx >= 0 && sx < MAP_WIDTH && sy >= 0 && sy < MAP_HEIGHT) {
              display.draw(sx, sy, '\u00b7', dimFg, dimBg);
            }
          }
        }

        const screenX = tile.x - offsetX;
        const screenY = tile.y - offsetY;
        if (screenX >= 0 && screenX < MAP_WIDTH && screenY >= 0 && screenY < MAP_HEIGHT) {
          display.draw(screenX, screenY, tile.asciiSymbol, fg, '#0d0d0d');
        }
      }

      // Layer 4: player
      const playerScreenX = playerX - offsetX;
      const playerScreenY = playerY - offsetY;
      if (playerScreenX >= 0 && playerScreenX < MAP_WIDTH &&
          playerScreenY >= 0 && playerScreenY < MAP_HEIGHT) {
        const playerFg = blinkRef.current ? '#ffffff' : '#cccccc';
        display.draw(playerScreenX, playerScreenY, '@', playerFg, '#0d0d0d');
      }
    };

    const timer = setInterval(render, 100);
    render();
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
