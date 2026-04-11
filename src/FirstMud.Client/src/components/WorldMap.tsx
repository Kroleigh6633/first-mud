import { useEffect, useRef } from 'react';
import * as ROT from 'rot-js';
import type { WorldStateSnapshot } from '../types/game';

interface Props {
  worldState: WorldStateSnapshot | null;
}

const MAP_WIDTH = 40;
const MAP_HEIGHT = 20;

// Static terrain for the Aeldran starting area (40 wide x 40 tall world grid)
// Symbols: . road/field, # forest, ^ hills, ~ water, M mountain, * ruins, ! POI
const WORLD_TERRAIN: string[] = [
  'MMMMMMMM^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^',
  'MMMMMM^^^^^^^^^^####################^^^^',
  'MMMM^^^^^^^^^^^^#####################^^^',
  'MM^^^^^^##########...................####',
  '^^^^^^^###########...!...............####',
  '^^^^^^############....*.................#',
  '^^^^^^###########......*.................',
  '^^^^^############.......!..............~~',
  '################.................~~~~.~~~',
  '################...............~~~~~~~~~',
  '###############.................~~~~~~~~',
  '##############......!.........~~~~~~~~~~',
  '#############......*..........~~~~~~~~~~',
  '############.................~~~~~~~~~~~',
  '###########...............~~~~~~~~~~~~~~',
  '##########................~~~~~~~~~~~~~~',
  '#########..!............~~~~~~~~~~~~~~~~',
  '########...*..........~~~~~~~~~~~~~~~~~~',
  '########..!*............~~~~~~~~~~~~~~~~',
  '######...............~~~~~~~~~~~~~~~~~~~',
  '#####..............~~~~~~~~~~~~~~~~~~~~~',
  '####.............~~~~~~~~~~~~~~~~~~~~~~~',
  '###..!.........~~~~~~~~~~~~~~~~~~~~~~~~~',
  '##.............~~~~~~~~~~~~~~~~~~~~~~~~~',
  '#..............~~~~~~~~~~~~~~~~~~~~~~~~~',
  '................~~~~~~~~~~~~~~~~~~~~~~~~',
  '.................~~~~~~~~~~~~~~~~~~~~~~~',
  '..................~~~~~~~~~~~~~~~~~~~~~~',
  '.....................~~~~~~~~~~~~~~~~~~~',
  '........................~~~~~~~~~~~~~~~~',
  '.............................~~~~~~~~~~~',
  '..............................~~~~~~~~~~',
  '...............................~~~~~~~~~',
  '................................~~~~~~~~',
  '.....^..........................~~~~~~~~',
  '....^^^..........................~~~~~~~',
  '...^^^^^..........................~~~~~~',
  '..^^^^^^^..........................~~~~~',
  '.^^^^^^^^^..........................~~~~',
  '^^^^^^^^^^...........................~~~',
];

type TerrainChar = '.' | '#' | '^' | '~' | 'M' | '*' | '!';

const TERRAIN_COLORS: Record<TerrainChar, [string, string]> = {
  '.': ['#2a4a1a', '#0d0d0d'],
  '#': ['#1a5a1a', '#0d0d0d'],
  '^': ['#8a7a20', '#0d0d0d'],
  '~': ['#1a3a8a', '#0d0d0d'],
  'M': ['#666666', '#0d0d0d'],
  '*': ['#555544', '#0d0d0d'],
  '!': ['#aaaa00', '#0d0d0d'],
};

function getTerrainAt(worldX: number, worldY: number): TerrainChar {
  if (worldY < 0 || worldY >= WORLD_TERRAIN.length) return '.';
  const row = WORLD_TERRAIN[worldY];
  if (worldX < 0 || worldX >= row.length) return '.';
  const ch = row[worldX] as TerrainChar;
  return ch in TERRAIN_COLORS ? ch : '.';
}

export default function WorldMap({ worldState }: Props) {
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

      const playerX = worldState?.player?.x ?? 5;
      const playerY = worldState?.player?.y ?? 5;

      // Viewport offset: centre on player
      const offsetX = playerX - Math.floor(MAP_WIDTH / 2);
      const offsetY = playerY - Math.floor(MAP_HEIGHT / 2);

      // Draw terrain
      for (let screenY = 0; screenY < MAP_HEIGHT; screenY++) {
        for (let screenX = 0; screenX < MAP_WIDTH; screenX++) {
          const worldX = screenX + offsetX;
          const worldY = screenY + offsetY;
          const ch = getTerrainAt(worldX, worldY);
          const [fg, bg] = TERRAIN_COLORS[ch];
          display.draw(screenX, screenY, ch, fg, bg);
        }
      }

      // Draw AI players
      if (worldState) {
        for (const ai of worldState.aiPlayers) {
          // AI players don't have x/y in the type — skip unless we have coords
          // We'll leave them unrendered for now since AiPlayerState has no position
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
        const playerChar = blinkRef.current ? '@' : '@';
        const playerFg = blinkRef.current ? '#ffffff' : '#cccccc';
        display.draw(playerScreenX, playerScreenY, playerChar, playerFg, '#0d0d0d');
      }
    };

    const timer = setInterval(render, 100);
    render(); // immediate first draw
    return () => clearInterval(timer);
  }, [worldState]);

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
