import { useEffect, useRef, useCallback } from 'react';
import type { WorldStateSnapshot, ZoneTile, WanderingNpc } from '../types/game';

interface Props {
  worldState: WorldStateSnapshot | null;
  zoneTiles: ZoneTile[];
  wanderingNpcs?: WanderingNpc[];
}

// ─── Tile dimensions ────────────────────────────────────────────────────────
const TILE_W = 64;
const TILE_H = 32;

// ─── Coordinate helpers ──────────────────────────────────────────────────────
function gridToScreen(
  gx: number,
  gy: number,
  vpCx: number,
  vpCy: number,
): [number, number] {
  const sx = (gx - gy) * (TILE_W / 2) + vpCx;
  const sy = (gx + gy) * (TILE_H / 2) + vpCy;
  return [sx, sy];
}

function screenToGrid(
  sx: number,
  sy: number,
  vpCx: number,
  vpCy: number,
): [number, number] {
  const relX = sx - vpCx;
  const relY = sy - vpCy;
  const gx = Math.floor((relX / (TILE_W / 2) + relY / (TILE_H / 2)) / 2);
  const gy = Math.floor((relY / (TILE_H / 2) - relX / (TILE_W / 2)) / 2);
  return [gx, gy];
}

// ─── Terrain hash ────────────────────────────────────────────────────────────
function terrainHash(x: number, y: number): number {
  let h = (x * 374761 + y * 668265) & 0x7fffffff;
  h = ((h >> 16) ^ h) * 0x45d9f3b;
  h = ((h >> 16) ^ h) * 0x45d9f3b;
  return ((h >> 16) ^ h) & 0xff;
}

// ─── Terrain type ────────────────────────────────────────────────────────────
type TerrainKind = 'grass' | 'tree' | 'water' | 'mountain' | 'sand' | 'rock' | 'path';

function getTerrainKind(wx: number, wy: number): TerrainKind {
  const h = terrainHash(wx, wy);
  if (h < 18)  return 'tree';
  if (h < 30)  return 'sand';
  if (h < 50)  return 'grass';
  if (h < 60)  return 'water';
  if (h < 70)  return 'mountain';
  if (h < 80)  return 'rock';
  if (h < 90)  return 'path';
  return 'grass';
}

// ─── Terrain colors ──────────────────────────────────────────────────────────
interface TerrainColors {
  top: string;
  left: string;
  right: string;
}

function grassColor(wx: number, wy: number): string {
  const h = terrainHash(wx * 3 + 7, wy * 5 + 11) & 0x1f; // 0–31 variation
  const base = 0x2d + (h - 16); // center on 0x2d
  const g = Math.max(0x18, Math.min(0x3f, base));
  return `rgb(${0x18},${g},${0x10})`;
}

function getTerrainColors(kind: TerrainKind, wx: number, wy: number): TerrainColors {
  switch (kind) {
    case 'grass':
      return { top: grassColor(wx, wy), left: '#1a3a0e', right: '#223015' };
    case 'tree':
      return { top: '#1a4010', left: '#0e2808', right: '#152e0c' };
    case 'water':
      return { top: '#1a3a5a', left: '#0e2438', right: '#152e48' };
    case 'mountain':
      return { top: '#4a4a4a', left: '#2a2a2a', right: '#383838' };
    case 'sand':
      return { top: '#8a7a3a', left: '#5a4e22', right: '#6e6030' };
    case 'rock':
      return { top: '#3a3a3a', left: '#222222', right: '#2e2e2e' };
    case 'path':
    default:
      return { top: '#2a2a1a', left: '#1a1a0e', right: '#222210' };
  }
}

// ─── Zone colors ─────────────────────────────────────────────────────────────
function zoneGlowColor(tile: ZoneTile): string {
  if (tile.isPortalZone) return '#aa44aa';
  if (tile.dangerLevel <= 3) return '#00aa33';
  if (tile.dangerLevel <= 6) return '#aaaa00';
  return '#aa2200';
}

function zoneSaturatedColor(tile: ZoneTile): TerrainColors {
  if (tile.isPortalZone)     return { top: '#7a2a7a', left: '#4a1a4a', right: '#5e225e' };
  if (tile.dangerLevel <= 3) return { top: '#1a6030', left: '#0e3a1e', right: '#154e26' };
  if (tile.dangerLevel <= 6) return { top: '#6a6010', left: '#3a3408', right: '#4e480e' };
  return { top: '#6a1a0a', left: '#3a0e06', right: '#4e1408' };
}

// ─── Draw helpers ─────────────────────────────────────────────────────────────
function drawIsoDiamond(
  ctx: CanvasRenderingContext2D,
  sx: number,
  sy: number,
  tileW: number,
  tileH: number,
  topColor: string,
  strokeColor = 'rgba(0,0,0,0.15)',
) {
  ctx.beginPath();
  ctx.moveTo(sx,              sy - tileH / 2);  // top
  ctx.lineTo(sx + tileW / 2, sy);               // right
  ctx.lineTo(sx,              sy + tileH / 2);  // bottom
  ctx.lineTo(sx - tileW / 2, sy);               // left
  ctx.closePath();
  ctx.fillStyle = topColor;
  ctx.fill();
  ctx.strokeStyle = strokeColor;
  ctx.lineWidth = 0.8;
  ctx.stroke();
}

function drawTreeDecal(ctx: CanvasRenderingContext2D, sx: number, sy: number) {
  // Small triangle "tree" on top of the diamond
  ctx.beginPath();
  ctx.moveTo(sx,              sy - TILE_H / 2 - 10);
  ctx.lineTo(sx + 8,          sy - TILE_H / 2 + 2);
  ctx.lineTo(sx - 8,          sy - TILE_H / 2 + 2);
  ctx.closePath();
  ctx.fillStyle = '#0e2a08';
  ctx.fill();
}

function drawMountainDecal(ctx: CanvasRenderingContext2D, sx: number, sy: number) {
  ctx.beginPath();
  ctx.moveTo(sx,              sy - TILE_H / 2 - 12);
  ctx.lineTo(sx + 10,         sy - TILE_H / 2 + 2);
  ctx.lineTo(sx - 10,         sy - TILE_H / 2 + 2);
  ctx.closePath();
  ctx.fillStyle = '#8a8a8a';
  ctx.fill();
  // Snow cap
  ctx.beginPath();
  ctx.moveTo(sx,              sy - TILE_H / 2 - 12);
  ctx.lineTo(sx + 4,          sy - TILE_H / 2 - 5);
  ctx.lineTo(sx - 4,          sy - TILE_H / 2 - 5);
  ctx.closePath();
  ctx.fillStyle = '#dde';
  ctx.fill();
}

// ─── NPC helpers ─────────────────────────────────────────────────────────────
function npcGlyph(role: string): string {
  switch (role) {
    case 'Merchant': return 'M';
    case 'Wanderer': return 'W';
    case 'Scout':    return 'S';
    case 'Hermit':   return 'H';
    case 'Refugee':  return 'R';
    case 'Bard':     return 'B';
    default:         return 'N';
  }
}

function npcColor(role: string): string {
  switch (role) {
    case 'Merchant': return '#ffcc44';
    case 'Wanderer': return '#aaaaff';
    case 'Scout':    return '#44ffaa';
    case 'Hermit':   return '#cc88ff';
    case 'Refugee':  return '#ff8844';
    case 'Bard':     return '#ff44cc';
    default:         return '#cccccc';
  }
}

// ─── Minimap constants ────────────────────────────────────────────────────────
const MINI_SIZE = 100;
const MINI_DOT  = 3;
const MINI_PAD  = 8;

export default function WorldMap({ worldState, zoneTiles, wanderingNpcs = [] }: Props) {
  const canvasRef  = useRef<HTMLCanvasElement>(null);
  const wrapperRef = useRef<HTMLDivElement>(null);

  // Dimensions tracked via ResizeObserver
  const sizeRef = useRef<{ w: number; h: number }>({ w: 0, h: 0 });

  // Animation state
  const rafRef        = useRef<number>(0);
  const blinkRef      = useRef<boolean>(false);
  const waterFrameRef = useRef<number>(0);

  // Hover state
  const hoverRef = useRef<{ gx: number; gy: number } | null>(null);

  // Stable prop refs so the render loop always sees the latest values
  const worldStateRef   = useRef(worldState);
  const zoneTilesRef    = useRef(zoneTiles);
  const wanderingNpcsRef = useRef(wanderingNpcs);
  worldStateRef.current   = worldState;
  zoneTilesRef.current    = zoneTiles;
  wanderingNpcsRef.current = wanderingNpcs;

  // ─── Resize ─────────────────────────────────────────────────────────────────
  const handleResize = useCallback((w: number, h: number) => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const dpr = window.devicePixelRatio || 1;
    canvas.width  = w * dpr;
    canvas.height = h * dpr;
    canvas.style.width  = `${w}px`;
    canvas.style.height = `${h}px`;
    sizeRef.current = { w, h };
  }, []);

  // ─── Mouse events ────────────────────────────────────────────────────────────
  const getGridCoords = useCallback((clientX: number, clientY: number): [number, number] | null => {
    const canvas = canvasRef.current;
    if (!canvas) return null;
    const rect = canvasRef.current!.getBoundingClientRect();
    const { w, h } = sizeRef.current;
    const vpCx = w / 2;
    const vpCy = h / 2;
    const playerX = worldStateRef.current?.player?.x ?? 0;
    const playerY = worldStateRef.current?.player?.y ?? 0;
    const [psx, psy] = gridToScreen(playerX, playerY, vpCx, vpCy);
    const offsetX = vpCx - psx;
    const offsetY = vpCy - psy;
    const sx = clientX - rect.left - offsetX;
    const sy = clientY - rect.top  - offsetY;
    return screenToGrid(sx, sy, vpCx, vpCy);
  }, []);

  // ─── Main render ─────────────────────────────────────────────────────────────
  const render = useCallback(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const dpr = window.devicePixelRatio || 1;
    const { w, h } = sizeRef.current;
    if (w === 0 || h === 0) return;

    const vpCx = (w / 2) * dpr;
    const vpCy = (h / 2) * dpr;
    const tileW = TILE_W * dpr;
    const tileH = TILE_H * dpr;

    const playerX = worldStateRef.current?.player?.x ?? 0;
    const playerY = worldStateRef.current?.player?.y ?? 0;

    // Camera offset: player always at viewport center
    const [psx, psy] = gridToScreen(playerX, playerY, vpCx, vpCy);
    const camOffX = vpCx - psx;
    const camOffY = vpCy - psy;

    // How many tiles fit on screen (add generous margin)
    const halfW = Math.ceil(w / TILE_W) + 4;
    const halfH = Math.ceil(h / TILE_H) + 8;

    // Collect all tile grid positions in visible range
    interface TileEntry {
      gx: number;
      gy: number;
      sx: number;
      sy: number;
      zone?: ZoneTile;
    }

    const entries: TileEntry[] = [];

    for (let gx = playerX - halfW; gx <= playerX + halfW; gx++) {
      for (let gy = playerY - halfH; gy <= playerY + halfH; gy++) {
        const [sx, sy] = gridToScreen(gx, gy, vpCx, vpCy);
        const asx = sx + camOffX;
        const asy = sy + camOffY;
        // Cull tiles that are fully off-screen
        if (asx + tileW / 2 < 0 || asx - tileW / 2 > w * dpr) continue;
        if (asy + tileH / 2 < 0 || asy - tileH / 2 > h * dpr) continue;
        entries.push({ gx, gy, sx: asx, sy: asy });
      }
    }

    // Painter's algorithm: sort by (gx + gy) asc, then gy asc
    entries.sort((a, b) => {
      const sa = a.gx + a.gy;
      const sb = b.gx + b.gy;
      if (sa !== sb) return sa - sb;
      return a.gy - b.gy;
    });

    // Build zone lookup: (x,y) → ZoneTile (including 3×3 area)
    const zoneCenterMap = new Map<string, ZoneTile>();
    const zoneHaloMap   = new Map<string, ZoneTile>();
    for (const tile of zoneTilesRef.current) {
      zoneCenterMap.set(`${tile.x},${tile.y}`, tile);
      for (let dx = -1; dx <= 1; dx++) {
        for (let dy = -1; dy <= 1; dy++) {
          if (dx === 0 && dy === 0) continue;
          zoneHaloMap.set(`${tile.x + dx},${tile.y + dy}`, tile);
        }
      }
    }

    // Build NPC lookup: (x,y) → WanderingNpc[]
    const npcMap = new Map<string, WanderingNpc[]>();
    for (const npc of wanderingNpcsRef.current) {
      const key = `${npc.x},${npc.y}`;
      if (!npcMap.has(key)) npcMap.set(key, []);
      npcMap.get(key)!.push(npc);
    }

    // Clear
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    ctx.fillStyle = '#0a0a0a';
    ctx.fillRect(0, 0, canvas.width, canvas.height);

    const hover = hoverRef.current;
    const waterShift = (Math.sin(waterFrameRef.current * 0.04) * 0.1); // subtle hue shift

    // ── Draw terrain + zone tiles ──────────────────────────────────────────────
    for (const entry of entries) {
      const { gx, gy, sx, sy } = entry;
      const key = `${gx},${gy}`;
      const centerTile = zoneCenterMap.get(key);
      const haloTile   = zoneHaloMap.get(key);
      const isHovered  = hover?.gx === gx && hover?.gy === gy;

      // Determine terrain
      const kind = getTerrainKind(gx, gy);

      if (centerTile) {
        // Zone center tile — saturated color
        const colors = zoneSaturatedColor(centerTile);
        const glow   = zoneGlowColor(centerTile);
        drawIsoDiamond(ctx, sx, sy, tileW, tileH, colors.top, glow);
      } else if (haloTile) {
        // Zone halo — terrain with tinted glow border
        const colors = getTerrainColors(kind, gx, gy);
        const glow   = zoneGlowColor(haloTile) + '66';
        drawIsoDiamond(ctx, sx, sy, tileW, tileH, colors.top, glow);
      } else if (kind === 'water') {
        // Animated water
        const base = 0x1a + Math.round(waterShift * 16);
        const col  = `rgb(${base},${0x3a},${0x5a})`;
        drawIsoDiamond(ctx, sx, sy, tileW, tileH, col);
      } else {
        const colors = getTerrainColors(kind, gx, gy);
        drawIsoDiamond(ctx, sx, sy, tileW, tileH, colors.top);
      }

      // Hover highlight
      if (isHovered) {
        ctx.beginPath();
        ctx.moveTo(sx,              sy - tileH / 2);
        ctx.lineTo(sx + tileW / 2, sy);
        ctx.lineTo(sx,              sy + tileH / 2);
        ctx.lineTo(sx - tileW / 2, sy);
        ctx.closePath();
        ctx.strokeStyle = 'rgba(255,255,200,0.55)';
        ctx.lineWidth = 1.5 * dpr;
        ctx.stroke();
      }

      // Terrain decals
      if (!centerTile) {
        if (kind === 'tree')     drawTreeDecal(ctx, sx, sy);
        if (kind === 'mountain') drawMountainDecal(ctx, sx, sy);
      }

      // Zone center symbol
      if (centerTile) {
        const glow = zoneGlowColor(centerTile);
        ctx.font = `bold ${10 * dpr}px monospace`;
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        ctx.fillStyle = glow;
        ctx.shadowColor = glow;
        ctx.shadowBlur = 6 * dpr;
        ctx.fillText(centerTile.asciiSymbol, sx, sy);
        ctx.shadowBlur = 0;
      }

      // NPCs on this tile
      const npcs = npcMap.get(key);
      if (npcs) {
        for (let i = 0; i < npcs.length; i++) {
          const npc = npcs[i];
          const color = npcColor(npc.role);
          const letter = npcGlyph(npc.role);
          const offsetI = (i - (npcs.length - 1) / 2) * 8 * dpr;

          // Body dot
          ctx.beginPath();
          ctx.arc(sx + offsetI, sy - tileH / 2 - 4 * dpr, 4 * dpr, 0, Math.PI * 2);
          ctx.fillStyle = color;
          ctx.fill();

          // Letter label
          ctx.font = `bold ${8 * dpr}px monospace`;
          ctx.textAlign = 'center';
          ctx.textBaseline = 'bottom';
          ctx.fillStyle = color;
          ctx.fillText(letter, sx + offsetI, sy - tileH / 2 - 10 * dpr);
        }
      }

      // Player
      if (gx === playerX && gy === playerY) {
        const headColor = blinkRef.current ? '#ffffff' : '#cccccc';
        const bodyColor = blinkRef.current ? '#dddddd' : '#aaaaaa';

        // Body (triangle)
        const bx = sx;
        const by = sy - tileH / 2;
        ctx.beginPath();
        ctx.moveTo(bx, by - 2 * dpr);
        ctx.lineTo(bx + 5 * dpr, by + 10 * dpr);
        ctx.lineTo(bx - 5 * dpr, by + 10 * dpr);
        ctx.closePath();
        ctx.fillStyle = bodyColor;
        ctx.fill();

        // Head (circle)
        ctx.beginPath();
        ctx.arc(bx, by - 5 * dpr, 4 * dpr, 0, Math.PI * 2);
        ctx.fillStyle = headColor;
        ctx.fill();
      }
    }

    // ── Minimap ────────────────────────────────────────────────────────────────
    const mmSize   = MINI_SIZE * dpr;
    const mmPad    = MINI_PAD  * dpr;
    const mmX      = canvas.width - mmSize - mmPad;
    const mmY      = mmPad;

    ctx.save();
    ctx.fillStyle = 'rgba(0,0,0,0.65)';
    ctx.fillRect(mmX, mmY, mmSize, mmSize);
    ctx.strokeStyle = '#333';
    ctx.lineWidth   = 1;
    ctx.strokeRect(mmX, mmY, mmSize, mmSize);

    if (zoneTilesRef.current.length > 0) {
      // Determine world bounds from zone tiles
      let minX = Infinity, maxX = -Infinity, minY = Infinity, maxY = -Infinity;
      for (const t of zoneTilesRef.current) {
        if (t.x < minX) minX = t.x;
        if (t.x > maxX) maxX = t.x;
        if (t.y < minY) minY = t.y;
        if (t.y > maxY) maxY = t.y;
      }
      const rangeX = Math.max(maxX - minX, 1);
      const rangeY = Math.max(maxY - minY, 1);

      for (const t of zoneTilesRef.current) {
        const mx = mmX + ((t.x - minX) / rangeX) * (mmSize - MINI_DOT * dpr);
        const my = mmY + ((t.y - minY) / rangeY) * (mmSize - MINI_DOT * dpr);
        ctx.fillStyle = zoneGlowColor(t);
        ctx.fillRect(mx, my, MINI_DOT * dpr, MINI_DOT * dpr);
      }

      // Player dot (blinking)
      const px = mmX + ((playerX - minX) / rangeX) * (mmSize - MINI_DOT * dpr);
      const py = mmY + ((playerY - minY) / rangeY) * (mmSize - MINI_DOT * dpr);
      if (blinkRef.current) {
        ctx.fillStyle = '#ffffff';
        ctx.fillRect(px - dpr, py - dpr, (MINI_DOT + 2) * dpr, (MINI_DOT + 2) * dpr);
      }
    }

    ctx.restore();
  }, []);

  // ─── Animation loop ──────────────────────────────────────────────────────────
  useEffect(() => {
    let frameCount  = 0;
    let blinkCount  = 0;

    const loop = () => {
      frameCount++;
      waterFrameRef.current = frameCount;

      // Blink every ~30 frames (≈500ms at 60fps)
      blinkCount++;
      if (blinkCount >= 30) {
        blinkRef.current = !blinkRef.current;
        blinkCount = 0;
      }

      render();
      rafRef.current = requestAnimationFrame(loop);
    };

    rafRef.current = requestAnimationFrame(loop);
    return () => cancelAnimationFrame(rafRef.current);
  }, [render]);

  // ─── ResizeObserver ──────────────────────────────────────────────────────────
  useEffect(() => {
    const wrapper = wrapperRef.current;
    if (!wrapper) return;

    const ro = new ResizeObserver(entries => {
      for (const entry of entries) {
        const { width, height } = entry.contentRect;
        handleResize(Math.floor(width), Math.floor(height));
      }
    });

    ro.observe(wrapper);

    // Initial size
    const rect = wrapper.getBoundingClientRect();
    handleResize(Math.floor(rect.width), Math.floor(rect.height));

    return () => ro.disconnect();
  }, [handleResize]);

  // ─── Mouse handlers ──────────────────────────────────────────────────────────
  const onMouseMove = useCallback((e: React.MouseEvent<HTMLCanvasElement>) => {
    const coords = getGridCoords(e.clientX, e.clientY);
    if (coords) {
      hoverRef.current = { gx: coords[0], gy: coords[1] };
    } else {
      hoverRef.current = null;
    }
  }, [getGridCoords]);

  const onMouseLeave = useCallback(() => {
    hoverRef.current = null;
  }, []);

  return (
    <div
      ref={wrapperRef}
      style={{
        width: '100%',
        height: '100%',
        background: '#0a0a0a',
        overflow: 'hidden',
        userSelect: 'none',
        position: 'relative',
      }}
    >
      <canvas
        ref={canvasRef}
        onMouseMove={onMouseMove}
        onMouseLeave={onMouseLeave}
        style={{ display: 'block', cursor: 'crosshair' }}
      />
    </div>
  );
}
