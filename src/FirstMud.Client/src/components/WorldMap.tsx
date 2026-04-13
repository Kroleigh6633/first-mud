import { useEffect, useRef, useCallback } from 'react';
import type { WorldStateSnapshot, ZoneTile, WanderingNpc, QuestWaypoint, HomesteadBuilding, BuildingType } from '../types/game';
import { getBiome, isPathTile, type BiomeType } from '../utils/biome';

interface Props {
  worldState: WorldStateSnapshot | null;
  zoneTiles: ZoneTile[];
  wanderingNpcs?: WanderingNpc[];
  questWaypoint?: QuestWaypoint | null;
  homesteadBuildings?: HomesteadBuilding[];
}

// ─── Tile dimensions ────────────────────────────────────────────────────────
const TILE_W = 64;
const TILE_H = 32;

// ─── Fog of war visibility radii ────────────────────────────────────────────
const VIS_RADIUS      = 8;   // tiles that count as "currently visible"
const VISITED_MARK_R  = 2;   // radius around player that gets marked as visited

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

// ─── Terrain colors derived from biome ──────────────────────────────────────
interface TerrainColors {
  top: string;
  left: string;
  right: string;
}

function grassVariant(wx: number, wy: number): string {
  // Small deterministic variation in green shade
  let h = (wx * 374761 + wy * 668265) & 0x7fffffff;
  h = ((h >> 16) ^ h) * 0x45d9f3b;
  h = ((h >> 16) ^ h) * 0x45d9f3b;
  const shift = (((h >> 16) ^ h) & 0x1f) - 16; // -16 to +15
  const r = Math.max(0x30, Math.min(0x70, 0x4a + Math.round(shift * 0.3)));
  const g = Math.max(0x60, Math.min(0xaa, 0x8a + Math.round(shift * 0.8)));
  const b = Math.max(0x15, Math.min(0x45, 0x2a + Math.round(shift * 0.2)));
  return `rgb(${r},${g},${b})`;
}

function getBiomeColors(type: BiomeType, wx: number, wy: number): TerrainColors {
  switch (type) {
    case 'water':
      return { top: '#2266aa', left: '#14447a', right: '#1c558e' };
    case 'sand':
      return { top: '#c4a84a', left: '#8a7230', right: '#a68d3c' };
    case 'grassland':
      return { top: grassVariant(wx, wy), left: '#2e5a18', right: '#3a6e20' };
    case 'lightForest':
      return { top: '#2a6a1a', left: '#1a4010', right: '#226018' };
    case 'denseForest':
      return { top: '#1a4e10', left: '#0e2c08', right: '#163c0e' };
    case 'mountain':
      return { top: '#7a7a6a', left: '#4e4e42', right: '#626256' };
    case 'snowMountain':
      return { top: '#c8c8d8', left: '#8a8a9a', right: '#a0a0b0' };
    case 'path':
    default:
      return { top: '#8a7a5a', left: '#5a503c', right: '#6e6248' };
  }
}

// ─── Zone colors ─────────────────────────────────────────────────────────────
function zoneGlowColor(tile: ZoneTile): string {
  if (tile.isPortalZone) return '#cc44ff';
  if (tile.dangerLevel <= 3) return '#66dd44';
  if (tile.dangerLevel <= 6) return '#ffbb22';
  return '#ff3311';
}

function zoneSaturatedColor(tile: ZoneTile): TerrainColors {
  if (tile.isPortalZone)     return { top: '#6a4a7a', left: '#3e2c4a', right: '#523a60' };
  if (tile.dangerLevel <= 3) return { top: '#8a7a5a', left: '#5a503c', right: '#6e6248' };
  if (tile.dangerLevel <= 6) return { top: '#7a6a4a', left: '#4e4430', right: '#60523a' };
  return { top: '#4a3a2a', left: '#2c221a', right: '#3a2e22' };
}

/** Difficulty border color based on (tileDanger - playerLevel), relative scale */
function difficultyBorderColor(zoneDanger: number, playerLevel: number): string {
  const diff = zoneDanger - playerLevel;
  if (diff <= -3) return '#44aa44'; // trivial — bright green
  if (diff === -2) return '#66bb44'; // easy
  if (diff === -1) return '#88cc44'; // comfortable — yellow-green
  if (diff ===  0) return '#cccc44'; // even match — yellow
  if (diff ===  1) return '#ddaa33'; // slightly challenging — gold
  if (diff ===  2) return '#dd7722'; // challenging — orange
  if (diff ===  3) return '#cc4422'; // dangerous — red-orange
  if (diff ===  4) return '#cc2222'; // very dangerous — red
  if (diff ===  5) return '#881111'; // deadly — dark red
  return '#440808';                  // DO NOT ENTER — near-black red
}

/** Border thickness (px multiplier) based on (tileDanger - playerLevel) */
function difficultyBorderWidth(zoneDanger: number, playerLevel: number): number {
  const diff = zoneDanger - playerLevel;
  if (diff <= -3) return 1.0; // trivial
  if (diff <=  0) return 1.4; // easy / even
  if (diff <=  2) return 2.0; // challenging
  if (diff <=  4) return 2.6; // dangerous / very dangerous
  return 3.2;                  // deadly / death zone
}

// ─── Draw helpers ─────────────────────────────────────────────────────────────
function drawIsoDiamond(
  ctx: CanvasRenderingContext2D,
  sx: number,
  sy: number,
  tileW: number,
  tileH: number,
  topColor: string,
  strokeColor = 'rgba(0,0,0,0.18)',
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

/** Draw just the diamond outline (for fog/difficulty borders) */
function strokeIsoDiamond(
  ctx: CanvasRenderingContext2D,
  sx: number,
  sy: number,
  tileW: number,
  tileH: number,
  color: string,
  lineWidth: number,
) {
  ctx.beginPath();
  ctx.moveTo(sx,              sy - tileH / 2);
  ctx.lineTo(sx + tileW / 2, sy);
  ctx.lineTo(sx,              sy + tileH / 2);
  ctx.lineTo(sx - tileW / 2, sy);
  ctx.closePath();
  ctx.strokeStyle = color;
  ctx.lineWidth = lineWidth;
  ctx.stroke();
}

// Rounded canopy tree — multiple overlapping circles + a dark trunk
function drawTreeDecal(ctx: CanvasRenderingContext2D, sx: number, sy: number, dense = false) {
  const baseY = sy - TILE_H / 2;

  // Trunk
  ctx.beginPath();
  ctx.rect(sx - 2, baseY - 2, 4, 8);
  ctx.fillStyle = '#3a2a1a';
  ctx.fill();

  const canopyColor = dense ? '#164a0c' : '#2a6a1a';
  const canopyData: [number, number, number][] = [
    [sx,      baseY - 14, 8],
    [sx - 6,  baseY - 9,  6],
    [sx + 6,  baseY - 9,  6],
  ];
  for (const [cx, cy, r] of canopyData) {
    ctx.beginPath();
    ctx.arc(cx, cy, r, 0, Math.PI * 2);
    ctx.fillStyle = canopyColor;
    ctx.fill();
  }
}

function drawMountainDecal(ctx: CanvasRenderingContext2D, sx: number, sy: number, snowy = false) {
  // Main grey mountain body
  ctx.beginPath();
  ctx.moveTo(sx,              sy - TILE_H / 2 - 14);
  ctx.lineTo(sx + 11,         sy - TILE_H / 2 + 2);
  ctx.lineTo(sx - 11,         sy - TILE_H / 2 + 2);
  ctx.closePath();
  ctx.fillStyle = snowy ? '#9a9aaa' : '#7a7a6a';
  ctx.fill();

  // Snow cap
  ctx.beginPath();
  ctx.moveTo(sx,              sy - TILE_H / 2 - 14);
  ctx.lineTo(sx + 4,          sy - TILE_H / 2 - 6);
  ctx.lineTo(sx - 4,          sy - TILE_H / 2 - 6);
  ctx.closePath();
  ctx.fillStyle = snowy ? '#ffffff' : '#ddddcc';
  ctx.fill();
}

// Small building silhouette drawn on top of zone center diamonds
function drawZoneBuilding(
  ctx: CanvasRenderingContext2D,
  sx: number,
  sy: number,
  wallColor: string,
  roofColor: string,
) {
  const baseY = sy - TILE_H / 2;
  ctx.fillStyle = wallColor;
  ctx.fillRect(sx - 6, baseY - 8, 12, 8);
  ctx.beginPath();
  ctx.moveTo(sx,       baseY - 8);
  ctx.lineTo(sx + 8,   baseY - 2);
  ctx.lineTo(sx - 8,   baseY - 2);
  ctx.closePath();
  ctx.fillStyle = roofColor;
  ctx.fill();
}

// Subtle wave lines across water diamonds
function drawWaterWaves(
  ctx: CanvasRenderingContext2D,
  sx: number,
  sy: number,
  tileW: number,
  tileH: number,
  waterFrame: number,
) {
  ctx.save();
  ctx.beginPath();
  ctx.moveTo(sx,              sy - tileH / 2);
  ctx.lineTo(sx + tileW / 2, sy);
  ctx.lineTo(sx,              sy + tileH / 2);
  ctx.lineTo(sx - tileW / 2, sy);
  ctx.closePath();
  ctx.clip();

  const rippleColor = '#3388cc';
  ctx.strokeStyle = rippleColor;
  ctx.lineWidth = 1;
  ctx.globalAlpha = 0.45;

  for (let i = 0; i < 2; i++) {
    const yOff = (i === 0 ? -tileH * 0.12 : tileH * 0.12);
    const phase = waterFrame * 0.06 + i * Math.PI;
    ctx.beginPath();
    const steps = 12;
    for (let s = 0; s <= steps; s++) {
      const t = s / steps;
      const wx = sx - tileW / 2 + t * tileW;
      const wy = sy + yOff + Math.sin(phase + t * Math.PI * 2) * 1.5;
      if (s === 0) ctx.moveTo(wx, wy);
      else ctx.lineTo(wx, wy);
    }
    ctx.stroke();
  }
  ctx.restore();
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
    case 'Merchant': return '#ddaa33';
    case 'Wanderer': return '#6688bb';
    case 'Scout':    return '#44aa66';
    case 'Hermit':   return '#9966bb';
    case 'Refugee':  return '#cc8844';
    case 'Bard':     return '#cc6699';
    default:         return '#aaaaaa';
  }
}

// ─── Zone building style helpers ─────────────────────────────────────────────
function zoneBuildingColors(tile: ZoneTile): { wall: string; roof: string } {
  if (tile.isPortalZone)     return { wall: '#5a3a6a', roof: '#7a4a8a' };
  if (tile.dangerLevel <= 3) return { wall: '#6a5a4a', roof: '#8a7a5a' };
  if (tile.dangerLevel <= 6) return { wall: '#5a4a34', roof: '#7a6a44' };
  return { wall: '#2a1a12', roof: '#4a2a1a' };
}

// ─── Homestead building draw helpers ─────────────────────────────────────────

function buildingBaseColors(type: BuildingType): { wall: string; roof: string; accent: string } {
  switch (type) {
    case 'Forge':          return { wall: '#5a3020', roof: '#8a4830', accent: '#ff6622' };
    case 'Fletcher':       return { wall: '#3a4a20', roof: '#5a7030', accent: '#88bb44' };
    case 'Tannery':        return { wall: '#5a4030', roof: '#7a5a40', accent: '#cc8855' };
    case 'EnchantingTower':return { wall: '#2a2050', roof: '#4a3a80', accent: '#cc88ff' };
    case 'AlchemistHut':   return { wall: '#203040', roof: '#304a60', accent: '#44ccaa' };
    case 'Stoneworker':    return { wall: '#4a4a4a', roof: '#6a6a6a', accent: '#aaaaaa' };
    case 'Woodworker':     return { wall: '#4a3020', roof: '#6a4a30', accent: '#88cc44' };
    case 'MarketStall':    return { wall: '#5a4a20', roof: '#cc8844', accent: '#ffdd88' };
    case 'Farm':           return { wall: '#3a5020', roof: '#5a7030', accent: '#88dd44' };
    case 'Mine':           return { wall: '#302828', roof: '#484040', accent: '#888888' };
    case 'Barracks':       return { wall: '#3a2020', roof: '#5a3030', accent: '#cc4444' };
    case 'Library':        return { wall: '#2a3040', roof: '#4a5060', accent: '#44aacc' };
    case 'Warehouse':      return { wall: '#404040', roof: '#606060', accent: '#aaaaaa' };
    case 'Hut':            return { wall: '#7a5030', roof: '#a06838', accent: '#ddaa77' };
    default:               return { wall: '#3a3a3a', roof: '#5a5a5a', accent: '#888888' };
  }
}

/**
 * Draw a homestead building centered on its tile's screen position.
 *
 * sx, sy  — screen coords of the tile center (diamond midpoint).
 * tileW, tileH — tile pixel dimensions (already DPR-scaled).
 * Sizes are expressed in tile-relative units so buildings scale with the tile.
 */
function drawHomesteadBuilding(
  ctx: CanvasRenderingContext2D,
  sx: number,
  sy: number,
  tileW: number,
  tileH: number,
  type: BuildingType,
  isConstructed: boolean,
  progress: number,
  waterFrame: number,
) {
  const colors = buildingBaseColors(type);
  // "floor level" — top edge of the isometric tile face, used as the ground line
  const floor = sy - tileH / 2;

  if (!isConstructed) {
    // ── Under construction: isometric wireframe scaffold ──────────────────
    ctx.save();
    ctx.globalAlpha = 0.55 + 0.35 * (progress / 100);

    const hw = tileW * 0.44;   // half-width of scaffold footprint
    const sh = tileH * 1.2;    // total scaffold height above floor

    // Outer outline (dashed)
    ctx.strokeStyle = '#886644';
    ctx.lineWidth = 1.5;
    ctx.setLineDash([4, 3]);
    ctx.strokeRect(sx - hw, floor - sh, hw * 2, sh);

    // Diagonal cross-beams
    ctx.beginPath();
    ctx.moveTo(sx - hw, floor - sh);
    ctx.lineTo(sx + hw, floor);
    ctx.moveTo(sx + hw, floor - sh);
    ctx.lineTo(sx - hw, floor);
    ctx.stroke();
    ctx.setLineDash([]);

    // Progress fill (tinted wall color)
    const fillH = sh * (progress / 100);
    ctx.fillStyle = colors.wall + '88';
    ctx.fillRect(sx - hw, floor - fillH, hw * 2, fillH);

    // "%" label
    ctx.font = `bold ${tileH * 0.5}px monospace`;
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.fillStyle = '#ffcc44';
    ctx.fillText(`${progress}%`, sx, floor - sh * 0.5);

    ctx.restore();
    return;
  }

  // ── Completed buildings ────────────────────────────────────────────────────
  ctx.save();

  switch (type) {

    // ── Forge: dark-red block + peaked roof + right-side chimney + smoke ──
    case 'Forge': {
      const hw  = tileW * 0.42;
      const wh  = tileH * 1.4;   // wall height
      const rh  = tileH * 0.6;   // roof peak extra height
      const roofBase = floor - wh;

      // Wall (left face — darker)
      ctx.fillStyle = '#3d2016';
      ctx.fillRect(sx - hw, roofBase, hw, wh);
      // Wall (right face)
      ctx.fillStyle = colors.wall;
      ctx.fillRect(sx, roofBase, hw, wh);
      // Wall top stripe (front face)
      ctx.fillStyle = '#6a3a28';
      ctx.fillRect(sx - hw, roofBase, hw * 2, tileH * 0.18);

      // Peaked roof
      ctx.beginPath();
      ctx.moveTo(sx - hw, roofBase);
      ctx.lineTo(sx,       roofBase - rh);
      ctx.lineTo(sx + hw,  roofBase);
      ctx.closePath();
      ctx.fillStyle = colors.roof;
      ctx.fill();

      // Chimney (right side)
      const chimneyX = sx + hw * 0.5;
      const chimneyH = tileH * 0.9;
      ctx.fillStyle = '#3a2010';
      ctx.fillRect(chimneyX - tileW * 0.05, roofBase - chimneyH, tileW * 0.1, chimneyH);

      // Animated orange smoke puffs
      ctx.globalAlpha = 0.55 + 0.2 * Math.sin(waterFrame * 0.07);
      const smokePhase = (waterFrame * 0.03) % 1;
      for (let i = 0; i < 3; i++) {
        const t   = ((smokePhase + i * 0.33) % 1);
        const sr  = tileH * (0.12 + t * 0.18);
        const sy2 = roofBase - chimneyH - tileH * 0.3 - t * tileH * 0.7;
        const sx2 = chimneyX + Math.sin(t * Math.PI * 2) * tileW * 0.06;
        ctx.beginPath();
        ctx.arc(sx2, sy2, sr, 0, Math.PI * 2);
        ctx.fillStyle = i === 0 ? '#ff8844' : '#886644';
        ctx.fill();
      }
      ctx.globalAlpha = 1;

      // Anvil silhouette in front
      const ax = sx - hw * 0.3, ay = floor;
      ctx.fillStyle = '#222';
      ctx.fillRect(ax - tileW * 0.08, ay - tileH * 0.28, tileW * 0.16, tileH * 0.1);
      ctx.fillRect(ax - tileW * 0.05, ay - tileH * 0.38, tileW * 0.1,  tileH * 0.1);
      break;
    }

    // ── Warehouse: wide grey block + flat roof + crates ────────────────────
    case 'Warehouse': {
      const hw = tileW * 0.72;   // extra-wide (2x1 tiles)
      const wh = tileH * 1.2;
      const roofBase = floor - wh;

      // Left (dark) face
      ctx.fillStyle = '#2a2a2a';
      ctx.fillRect(sx - hw, roofBase, hw, wh);
      // Right (lighter) face
      ctx.fillStyle = colors.wall;
      ctx.fillRect(sx, roofBase, hw, wh);
      // Flat roof
      ctx.fillStyle = '#555';
      ctx.fillRect(sx - hw, roofBase - tileH * 0.15, hw * 2, tileH * 0.15);

      // Crate outlines on right face
      ctx.strokeStyle = '#666';
      ctx.lineWidth = 1.2;
      for (let ci = 0; ci < 2; ci++) {
        const cx = sx + tileW * (0.08 + ci * 0.35);
        const cy = floor - tileH * 0.3;
        const cs = tileH * 0.35;
        ctx.strokeRect(cx - cs / 2, cy - cs, cs, cs);
        ctx.beginPath();
        ctx.moveTo(cx - cs / 2, cy - cs * 0.5);
        ctx.lineTo(cx + cs / 2, cy - cs * 0.5);
        ctx.stroke();
      }
      // Door (big double-door)
      ctx.fillStyle = '#1a1a1a';
      ctx.fillRect(sx - tileW * 0.12, floor - wh * 0.6, tileW * 0.24, wh * 0.6);
      ctx.strokeStyle = '#444';
      ctx.lineWidth = 1;
      ctx.strokeRect(sx - tileW * 0.12, floor - wh * 0.6, tileW * 0.24, wh * 0.6);
      ctx.beginPath();
      ctx.moveTo(sx, floor - wh * 0.6);
      ctx.lineTo(sx, floor);
      ctx.stroke();
      break;
    }

    // ── MarketStall: open stall with triangular awning + counter ────────────
    case 'MarketStall': {
      const hw = tileW * 0.38;
      const wh = tileH * 0.85;
      const roofBase = floor - wh;

      // Wooden posts at corners
      ctx.fillStyle = '#7a5530';
      for (const px2 of [sx - hw + tileW * 0.04, sx + hw - tileW * 0.04]) {
        ctx.fillRect(px2 - tileW * 0.025, roofBase - tileH * 0.4, tileW * 0.05, wh + tileH * 0.4);
      }

      // Triangular canopy
      ctx.beginPath();
      ctx.moveTo(sx - hw - tileW * 0.05, roofBase);
      ctx.lineTo(sx,                      roofBase - tileH * 0.55);
      ctx.lineTo(sx + hw + tileW * 0.05,  roofBase);
      ctx.closePath();
      ctx.fillStyle = colors.roof;
      ctx.fill();
      // Canopy stripes
      ctx.strokeStyle = colors.accent + 'aa';
      ctx.lineWidth = 2;
      for (let s = 0; s < 3; s++) {
        const t = (s + 1) / 4;
        const stripX = sx - hw + (hw * 2) * t;
        ctx.beginPath();
        ctx.moveTo(stripX, roofBase);
        ctx.lineTo(sx + (stripX - sx) * 0.5, roofBase - tileH * 0.55 * 0.5);
        ctx.stroke();
      }

      // Counter / table
      ctx.fillStyle = '#6a4828';
      ctx.fillRect(sx - hw * 0.8, floor - tileH * 0.45, hw * 1.6, tileH * 0.12);
      ctx.fillRect(sx - hw * 0.8, floor - tileH * 0.45, tileW * 0.04, tileH * 0.45);
      ctx.fillRect(sx + hw * 0.8 - tileW * 0.04, floor - tileH * 0.45, tileW * 0.04, tileH * 0.45);

      // Goods on counter (colorful dots)
      ctx.fillStyle = '#ff9944';
      ctx.beginPath(); ctx.arc(sx - tileW * 0.14, floor - tileH * 0.38, tileH * 0.08, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = '#44cc88';
      ctx.beginPath(); ctx.arc(sx, floor - tileH * 0.38, tileH * 0.08, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = '#cc6644';
      ctx.beginPath(); ctx.arc(sx + tileW * 0.14, floor - tileH * 0.38, tileH * 0.08, 0, Math.PI * 2); ctx.fill();
      break;
    }

    // ── Farm: green crop rows + fence perimeter ─────────────────────────────
    case 'Farm': {
      const extent = tileW * 0.85;   // span most of 2×2 footprint

      // Soil base
      ctx.fillStyle = '#4a3018';
      ctx.fillRect(sx - extent, floor - tileH * 0.22, extent * 2, tileH * 0.22);

      // Crop rows (4 rows of green lines)
      for (let r = 0; r < 4; r++) {
        const rowX = sx - extent + extent * 2 * ((r + 0.5) / 4);
        ctx.fillStyle = r % 2 === 0 ? colors.accent : '#5aaa28';
        ctx.fillRect(rowX - tileW * 0.025, floor - tileH * 0.2, tileW * 0.05, tileH * 0.18);
        // Small leaf bumps
        for (let b = 0; b < 3; b++) {
          const by2 = floor - tileH * (0.05 + b * 0.06);
          ctx.beginPath();
          ctx.arc(rowX + Math.sin(b * 1.8) * tileW * 0.02, by2, tileH * 0.05, 0, Math.PI * 2);
          ctx.fill();
        }
      }

      // Fence perimeter
      ctx.strokeStyle = '#7a5530';
      ctx.lineWidth = 1.5;
      // Top fence (horizontal)
      ctx.beginPath();
      ctx.moveTo(sx - extent, floor - tileH * 0.24);
      ctx.lineTo(sx + extent, floor - tileH * 0.24);
      ctx.stroke();
      // Bottom fence
      ctx.beginPath();
      ctx.moveTo(sx - extent, floor + tileH * 0.04);
      ctx.lineTo(sx + extent, floor + tileH * 0.04);
      ctx.stroke();
      // Fence posts
      for (let p = 0; p <= 4; p++) {
        const px2 = sx - extent + extent * 2 * (p / 4);
        ctx.beginPath();
        ctx.moveTo(px2, floor - tileH * 0.28);
        ctx.lineTo(px2, floor + tileH * 0.08);
        ctx.stroke();
      }

      // Small barn structure in center
      ctx.fillStyle = '#7a4020';
      ctx.fillRect(sx - tileW * 0.16, floor - tileH * 0.85, tileW * 0.32, tileH * 0.65);
      ctx.beginPath();
      ctx.moveTo(sx - tileW * 0.2, floor - tileH * 0.85);
      ctx.lineTo(sx,               floor - tileH * 1.25);
      ctx.lineTo(sx + tileW * 0.2, floor - tileH * 0.85);
      ctx.closePath();
      ctx.fillStyle = '#aa5030';
      ctx.fill();
      break;
    }

    // ── Mine: dark hillside arch + cart tracks + ore pile ──────────────────
    case 'Mine': {
      const hw = tileW * 0.40;
      const wh = tileH * 1.1;
      const roofBase = floor - wh;

      // Hillside body (dark mound)
      ctx.beginPath();
      ctx.ellipse(sx, floor - wh * 0.5, hw * 1.1, wh * 0.7, 0, 0, Math.PI * 2);
      ctx.fillStyle = '#2a2218';
      ctx.fill();
      // Hillside top contour
      ctx.beginPath();
      ctx.moveTo(sx - hw * 1.1, floor);
      ctx.quadraticCurveTo(sx, roofBase - tileH * 0.1, sx + hw * 1.1, floor);
      ctx.fillStyle = '#3a3020';
      ctx.fill();

      // Dark archway entrance
      ctx.fillStyle = '#0a0808';
      ctx.beginPath();
      ctx.arc(sx, floor - tileH * 0.6, tileW * 0.18, Math.PI, 0);
      ctx.rect(sx - tileW * 0.18, floor - tileH * 0.6, tileW * 0.36, tileH * 0.6);
      ctx.fill();
      // Arch frame (timber)
      ctx.strokeStyle = '#5a3a1a';
      ctx.lineWidth = 2.5;
      ctx.beginPath();
      ctx.arc(sx, floor - tileH * 0.6, tileW * 0.18, Math.PI, 0);
      ctx.stroke();
      ctx.strokeRect(sx - tileW * 0.18, floor - tileH * 0.6, tileW * 0.36, tileH * 0.6);

      // Cart tracks leading out (two parallel lines)
      ctx.strokeStyle = '#6a5540';
      ctx.lineWidth = 1.2;
      for (const offset of [-tileW * 0.05, tileW * 0.05]) {
        ctx.beginPath();
        ctx.moveTo(sx + offset, floor - tileH * 0.02);
        ctx.lineTo(sx + offset, floor + tileH * 0.22);
        ctx.stroke();
      }

      // Small ore pile at entrance
      ctx.fillStyle = '#555';
      ctx.beginPath();
      ctx.ellipse(sx + hw * 0.6, floor - tileH * 0.12, tileW * 0.1, tileH * 0.1, 0, 0, Math.PI * 2);
      ctx.fill();
      ctx.fillStyle = '#aaaacc';
      for (let d = 0; d < 3; d++) {
        ctx.beginPath();
        ctx.arc(sx + hw * 0.6 + (d - 1) * tileW * 0.06, floor - tileH * 0.18, tileH * 0.06, 0, Math.PI * 2);
        ctx.fill();
      }
      break;
    }

    // ── Tannery: building + hide racks (vertical poles + rectangles) ────────
    case 'Tannery': {
      const hw = tileW * 0.38;
      const wh = tileH * 1.2;
      const roofBase = floor - wh;

      // Main building
      ctx.fillStyle = '#3d2a18';
      ctx.fillRect(sx - hw, roofBase, hw, wh);
      ctx.fillStyle = colors.wall;
      ctx.fillRect(sx, roofBase, hw, wh);
      // Roof
      ctx.beginPath();
      ctx.moveTo(sx - hw, roofBase);
      ctx.lineTo(sx,       roofBase - tileH * 0.5);
      ctx.lineTo(sx + hw,  roofBase);
      ctx.closePath();
      ctx.fillStyle = colors.roof;
      ctx.fill();

      // Hide racks (3 vertical poles with brown hides)
      for (let r = 0; r < 3; r++) {
        const rx = sx - hw * 1.5 + r * hw;
        const ry = floor - tileH * 0.2;
        // Pole
        ctx.fillStyle = '#5a3a18';
        ctx.fillRect(rx - 1.5, ry - tileH * 1.0, 3, tileH * 1.0);
        // Hide (brown rectangle)
        ctx.fillStyle = colors.accent;
        ctx.globalAlpha = 0.75;
        ctx.fillRect(rx - tileW * 0.065, ry - tileH * 0.9, tileW * 0.13, tileH * 0.55);
        ctx.globalAlpha = 1;
        // Stitch lines on hide
        ctx.strokeStyle = '#7a4a28';
        ctx.lineWidth = 0.8;
        ctx.strokeRect(rx - tileW * 0.065, ry - tileH * 0.9, tileW * 0.13, tileH * 0.55);
      }
      break;
    }

    // ── Barracks: stone building + flag on top ───────────────────────────────
    case 'Barracks': {
      const hw = tileW * 0.46;
      const wh = tileH * 1.5;
      const roofBase = floor - wh;

      // Stone wall texture (left face)
      ctx.fillStyle = '#282020';
      ctx.fillRect(sx - hw, roofBase, hw, wh);
      // Right face
      ctx.fillStyle = colors.wall;
      ctx.fillRect(sx, roofBase, hw, wh);
      // Stone texture — horizontal mortar lines
      ctx.strokeStyle = '#1a1010';
      ctx.lineWidth = 0.8;
      for (let row = 0; row < 4; row++) {
        const ly = roofBase + wh * ((row + 0.5) / 4);
        ctx.beginPath();
        ctx.moveTo(sx - hw, ly);
        ctx.lineTo(sx + hw, ly);
        ctx.stroke();
      }
      // Crenellations on flat top
      ctx.fillStyle = colors.wall;
      for (let c = 0; c < 5; c++) {
        if (c % 2 === 0) {
          const cx = sx - hw + (hw * 2) * (c / 4);
          ctx.fillRect(cx, roofBase - tileH * 0.2, hw * 2 / 4 * 0.7, tileH * 0.2);
        }
      }
      // Battlements top line
      ctx.fillStyle = '#3a2818';
      ctx.fillRect(sx - hw, roofBase, hw * 2, tileH * 0.1);

      // Flagpole
      const fpX = sx + hw * 0.6;
      const fpY = roofBase - tileH * 0.8;
      ctx.fillStyle = '#6a5030';
      ctx.fillRect(fpX - 1.5, fpY, 3, tileH * 0.8);
      // Flag (triangle, animated flap)
      const flap = Math.sin(waterFrame * 0.09) * tileW * 0.04;
      ctx.beginPath();
      ctx.moveTo(fpX, fpY);
      ctx.lineTo(fpX + tileW * 0.22 + flap, fpY + tileH * 0.12);
      ctx.lineTo(fpX, fpY + tileH * 0.24);
      ctx.closePath();
      ctx.fillStyle = colors.accent;
      ctx.fill();

      // Windows
      ctx.fillStyle = '#0a0606';
      for (let w2 = 0; w2 < 2; w2++) {
        const wx2 = sx - hw * 0.6 + w2 * hw * 1.1;
        ctx.fillRect(wx2 - tileW * 0.04, floor - tileH * 0.8, tileW * 0.08, tileH * 0.2);
      }
      break;
    }

    // ── EnchantingTower: tall narrow tower + purple glow ────────────────────
    case 'EnchantingTower': {
      const hw = tileW * 0.22;
      const wh = tileH * 2.2;   // much taller than others
      const roofBase = floor - wh;

      // Narrow tower body
      ctx.fillStyle = '#1a1638';
      ctx.fillRect(sx - hw, roofBase, hw, wh);
      ctx.fillStyle = colors.wall;
      ctx.fillRect(sx, roofBase, hw, wh);
      // Stone band rings
      ctx.strokeStyle = '#3a2870';
      ctx.lineWidth = 1.5;
      for (let ring = 0; ring < 4; ring++) {
        const ry = roofBase + wh * (ring / 4);
        ctx.beginPath();
        ctx.moveTo(sx - hw, ry);
        ctx.lineTo(sx + hw, ry);
        ctx.stroke();
      }
      // Conical peaked roof
      ctx.beginPath();
      ctx.moveTo(sx - hw * 1.3, roofBase);
      ctx.lineTo(sx,             roofBase - tileH * 0.9);
      ctx.lineTo(sx + hw * 1.3,  roofBase);
      ctx.closePath();
      ctx.fillStyle = colors.roof;
      ctx.fill();

      // Glowing orb at top
      const glowPulse = 0.7 + 0.3 * Math.sin(waterFrame * 0.11);
      ctx.beginPath();
      ctx.arc(sx, roofBase - tileH * 0.9, tileH * 0.22 * glowPulse, 0, Math.PI * 2);
      ctx.fillStyle = colors.accent;
      ctx.shadowColor = colors.accent;
      ctx.shadowBlur = 12;
      ctx.globalAlpha = 0.9;
      ctx.fill();
      ctx.shadowBlur = 0;
      ctx.globalAlpha = 1;

      // Rune windows (glowing slits)
      ctx.fillStyle = colors.accent;
      ctx.globalAlpha = 0.5 + 0.3 * glowPulse;
      ctx.fillRect(sx - hw * 0.2, roofBase + wh * 0.2, hw * 0.4, tileH * 0.18);
      ctx.fillRect(sx - hw * 0.2, roofBase + wh * 0.5, hw * 0.4, tileH * 0.18);
      ctx.globalAlpha = 1;
      break;
    }

    // ── AlchemistHut: teal-accent hut + cauldron silhouette ────────────────
    case 'AlchemistHut': {
      const hw = tileW * 0.35;
      const wh = tileH * 1.1;
      const roofBase = floor - wh;

      ctx.fillStyle = '#152028';
      ctx.fillRect(sx - hw, roofBase, hw, wh);
      ctx.fillStyle = colors.wall;
      ctx.fillRect(sx, roofBase, hw, wh);
      // Pitched roof
      ctx.beginPath();
      ctx.moveTo(sx - hw, roofBase);
      ctx.lineTo(sx,       roofBase - tileH * 0.55);
      ctx.lineTo(sx + hw,  roofBase);
      ctx.closePath();
      ctx.fillStyle = colors.roof;
      ctx.fill();

      // Glowing window
      const gp = 0.6 + 0.4 * Math.sin(waterFrame * 0.08);
      ctx.fillStyle = colors.accent;
      ctx.globalAlpha = gp * 0.7;
      ctx.fillRect(sx - hw * 0.25, roofBase + wh * 0.25, hw * 0.5, tileH * 0.22);
      ctx.globalAlpha = 1;

      // Cauldron in front
      ctx.fillStyle = '#222';
      ctx.beginPath();
      ctx.arc(sx, floor - tileH * 0.22, tileH * 0.18, 0, Math.PI * 2);
      ctx.fill();
      ctx.fillStyle = colors.accent + '99';
      ctx.beginPath();
      ctx.arc(sx, floor - tileH * 0.28, tileH * 0.1, 0, Math.PI * 2);
      ctx.fill();
      break;
    }

    // ── Woodworker: warm-brown building + lumber pile ────────────────────────
    case 'Woodworker': {
      const hw = tileW * 0.38;
      const wh = tileH * 1.15;
      const roofBase = floor - wh;

      ctx.fillStyle = '#3a2010';
      ctx.fillRect(sx - hw, roofBase, hw, wh);
      ctx.fillStyle = colors.wall;
      ctx.fillRect(sx, roofBase, hw, wh);
      ctx.beginPath();
      ctx.moveTo(sx - hw, roofBase);
      ctx.lineTo(sx,       roofBase - tileH * 0.5);
      ctx.lineTo(sx + hw,  roofBase);
      ctx.closePath();
      ctx.fillStyle = colors.roof;
      ctx.fill();

      // Lumber pile (stacked log rectangles)
      ctx.fillStyle = '#6a4020';
      for (let l = 0; l < 3; l++) {
        ctx.fillRect(sx + hw * 0.5, floor - tileH * (0.12 + l * 0.2), tileW * 0.25, tileH * 0.16);
        ctx.strokeStyle = '#3a2010';
        ctx.lineWidth = 0.8;
        ctx.strokeRect(sx + hw * 0.5, floor - tileH * (0.12 + l * 0.2), tileW * 0.25, tileH * 0.16);
      }

      // Saw blade icon on wall
      ctx.strokeStyle = colors.accent;
      ctx.lineWidth = 1.5;
      ctx.beginPath();
      ctx.arc(sx - hw * 0.4, roofBase + wh * 0.5, tileH * 0.15, 0, Math.PI * 2);
      ctx.stroke();
      break;
    }

    // ── Hut: small brown dwelling with peaked thatched roof ─────────────────
    case 'Hut': {
      const hw = tileW * 0.26;   // narrower than full buildings
      const wh = tileH * 0.85;   // shorter walls
      const roofBase = floor - wh;

      // Left wall face (darker)
      ctx.fillStyle = '#5a3820';
      ctx.fillRect(sx - hw, roofBase, hw, wh);
      // Right wall face
      ctx.fillStyle = colors.wall;
      ctx.fillRect(sx, roofBase, hw, wh);

      // Thatched roof — wide, low-pitched triangle with layered stripes
      ctx.beginPath();
      ctx.moveTo(sx - hw * 1.15, roofBase);
      ctx.lineTo(sx,              roofBase - tileH * 0.55);
      ctx.lineTo(sx + hw * 1.15,  roofBase);
      ctx.closePath();
      ctx.fillStyle = colors.roof;
      ctx.fill();

      // Thatch texture: horizontal stroke lines across the roof
      ctx.save();
      ctx.beginPath();
      ctx.moveTo(sx - hw * 1.15, roofBase);
      ctx.lineTo(sx,              roofBase - tileH * 0.55);
      ctx.lineTo(sx + hw * 1.15,  roofBase);
      ctx.closePath();
      ctx.clip();
      ctx.strokeStyle = '#7a5020';
      ctx.lineWidth = 0.9;
      for (let i = 1; i <= 4; i++) {
        const ty = roofBase - (tileH * 0.55) * (i / 5);
        const span = hw * 1.15 * (1 - i / 5);
        ctx.beginPath();
        ctx.moveTo(sx - span, ty);
        ctx.lineTo(sx + span, ty);
        ctx.stroke();
      }
      ctx.restore();

      // Small door
      ctx.fillStyle = '#2a1808';
      ctx.fillRect(sx - tileW * 0.04, floor - tileH * 0.42, tileW * 0.08, tileH * 0.42);

      // Tiny window on right face
      ctx.fillStyle = colors.accent;
      ctx.globalAlpha = 0.55;
      ctx.fillRect(sx + hw * 0.35, roofBase + wh * 0.28, hw * 0.3, tileH * 0.16);
      ctx.globalAlpha = 1;
      break;
    }

    // ── Generic fallback: simple house ──────────────────────────────────────
    default: {
      const hw = tileW * 0.36;
      const wh = tileH * 1.1;
      const roofBase = floor - wh;

      ctx.fillStyle = colors.wall;
      ctx.fillRect(sx - hw, roofBase, hw * 2, wh);
      ctx.beginPath();
      ctx.moveTo(sx - hw, roofBase);
      ctx.lineTo(sx,       roofBase - tileH * 0.5);
      ctx.lineTo(sx + hw,  roofBase);
      ctx.closePath();
      ctx.fillStyle = colors.roof;
      ctx.fill();
      // Door
      ctx.fillStyle = '#1a1a1a';
      ctx.fillRect(sx - tileW * 0.06, floor - tileH * 0.5, tileW * 0.12, tileH * 0.5);
      break;
    }
  }

  ctx.restore();
}

// ─── Visited tiles storage ────────────────────────────────────────────────────
function visitedKey(playerId: string): string {
  return `firstmud_visited_${playerId}`;
}

function loadVisited(playerId: string): Set<string> {
  try {
    const raw = localStorage.getItem(visitedKey(playerId));
    if (!raw) return new Set();
    const arr = JSON.parse(raw) as string[];
    return new Set(arr);
  } catch {
    return new Set();
  }
}

function saveVisited(playerId: string, visited: Set<string>): void {
  try {
    // Only persist every N additions to avoid thrashing localStorage
    localStorage.setItem(visitedKey(playerId), JSON.stringify(Array.from(visited)));
  } catch {
    // Quota exceeded — silently ignore
  }
}

// ─── Minimap constants ────────────────────────────────────────────────────────
const MINI_SIZE = 100;
const MINI_DOT  = 3;
const MINI_PAD  = 8;

export default function WorldMap({ worldState, zoneTiles, wanderingNpcs = [], questWaypoint = null, homesteadBuildings = [] }: Props) {
  const canvasRef  = useRef<HTMLCanvasElement>(null);
  const wrapperRef = useRef<HTMLDivElement>(null);

  const sizeRef = useRef<{ w: number; h: number }>({ w: 0, h: 0 });

  // Animation state
  const rafRef        = useRef<number>(0);
  const blinkRef      = useRef<boolean>(false);
  const waterFrameRef = useRef<number>(0);

  // Hover state
  const hoverRef = useRef<{ gx: number; gy: number } | null>(null);

  // Fog of war — visited tile set
  const visitedRef    = useRef<Set<string>>(new Set());
  const playerIdRef   = useRef<string | null>(null);
  const lastSaveFrame = useRef<number>(0);

  // Stable prop refs
  const worldStateRef         = useRef(worldState);
  const zoneTilesRef          = useRef(zoneTiles);
  const wanderingNpcsRef      = useRef(wanderingNpcs);
  const questWaypointRef      = useRef(questWaypoint);
  const homesteadBuildingsRef = useRef(homesteadBuildings);
  worldStateRef.current         = worldState;
  zoneTilesRef.current          = zoneTiles;
  wanderingNpcsRef.current      = wanderingNpcs;
  questWaypointRef.current      = questWaypoint;
  homesteadBuildingsRef.current = homesteadBuildings;

  // ─── Load visited tiles when player ID becomes available ──────────────────
  useEffect(() => {
    const pid = worldState?.player?.id ?? null;
    if (pid && pid !== playerIdRef.current) {
      playerIdRef.current = pid;
      visitedRef.current  = loadVisited(pid);
    }
  }, [worldState?.player?.id]);

  // ─── Mark current position + radius as visited ───────────────────────────
  const markVisited = useCallback((px: number, py: number) => {
    const pid = playerIdRef.current;
    if (!pid) return;
    const set = visitedRef.current;
    let changed = false;
    for (let dx = -VISITED_MARK_R; dx <= VISITED_MARK_R; dx++) {
      for (let dy = -VISITED_MARK_R; dy <= VISITED_MARK_R; dy++) {
        const k = `${px + dx},${py + dy}`;
        if (!set.has(k)) { set.add(k); changed = true; }
      }
    }
    if (changed) {
      // Debounce saves: only write localStorage every 60 frames
      const frame = waterFrameRef.current;
      if (frame - lastSaveFrame.current > 60) {
        saveVisited(pid, set);
        lastSaveFrame.current = frame;
      }
    }
  }, []);

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
    const playerLevel = worldStateRef.current?.player?.level ?? 1;

    // Mark player vicinity as visited
    markVisited(playerX, playerY);

    // Camera offset: player always at viewport center
    const [psx, psy] = gridToScreen(playerX, playerY, vpCx, vpCy);
    const camOffX = vpCx - psx;
    const camOffY = vpCy - psy;

    const halfW = Math.ceil(w / TILE_W) + 4;
    const halfH = Math.ceil(h / TILE_H) + 8;

    interface TileEntry {
      gx: number;
      gy: number;
      sx: number;
      sy: number;
    }

    const entries: TileEntry[] = [];

    for (let gx = playerX - halfW; gx <= playerX + halfW; gx++) {
      for (let gy = playerY - halfH; gy <= playerY + halfH; gy++) {
        const [sx, sy] = gridToScreen(gx, gy, vpCx, vpCy);
        const asx = sx + camOffX;
        const asy = sy + camOffY;
        if (asx + tileW / 2 < 0 || asx - tileW / 2 > w * dpr) continue;
        if (asy + tileH / 2 < 0 || asy - tileH / 2 > h * dpr) continue;
        entries.push({ gx, gy, sx: asx, sy: asy });
      }
    }

    // Painter's algorithm
    entries.sort((a, b) => {
      const sa = a.gx + a.gy;
      const sb = b.gx + b.gy;
      if (sa !== sb) return sa - sb;
      return a.gy - b.gy;
    });

    // Zone lookup maps
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

    // Difficulty border halo: 3×3 around zone centre (including the halo)
    // The difficulty border shows on the zone centre AND its surrounding 3×3
    const zoneDiffMap = new Map<string, ZoneTile>(); // key → zone (for difficulty borders)
    for (const tile of zoneTilesRef.current) {
      for (let dx = -1; dx <= 1; dx++) {
        for (let dy = -1; dy <= 1; dy++) {
          zoneDiffMap.set(`${tile.x + dx},${tile.y + dy}`, tile);
        }
      }
    }

    // NPC lookup
    const npcMap = new Map<string, WanderingNpc[]>();
    for (const npc of wanderingNpcsRef.current) {
      const key = `${npc.x},${npc.y}`;
      if (!npcMap.has(key)) npcMap.set(key, []);
      npcMap.get(key)!.push(npc);
    }

    // Homestead building lookup (grid coords are relative to homestead centre -100,-100)
    const homesteadCx = -100;
    const homesteadCy = -100;
    const buildingMap = new Map<string, HomesteadBuilding>();
    for (const b of homesteadBuildingsRef.current) {
      // Each building occupies a 2×2 footprint centred on (homesteadCx + gridX, homesteadCy + gridY)
      const bx = homesteadCx + b.gridX;
      const by = homesteadCy + b.gridY;
      for (let dx = 0; dx <= 1; dx++) {
        for (let dy = 0; dy <= 1; dy++) {
          buildingMap.set(`${bx + dx},${by + dy}`, b);
        }
      }
    }

    const visited = visitedRef.current;

    // Clear
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    ctx.fillStyle = '#1a1510';
    ctx.fillRect(0, 0, canvas.width, canvas.height);

    const hover = hoverRef.current;
    const waterFrame = waterFrameRef.current;
    const waterShift = (Math.sin(waterFrame * 0.04) * 0.1);

    // Buildings collected during terrain pass, drawn in a second pass (after terrain, before player)
    interface DeferredBuilding {
      sx: number; sy: number; sortKey: number;
      building: HomesteadBuilding; fogAlpha: number;
    }
    const deferredBuildings: DeferredBuilding[] = [];

    // ── Draw terrain + zone tiles ──────────────────────────────────────────────
    for (const entry of entries) {
      const { gx, gy, sx, sy } = entry;
      const key = `${gx},${gy}`;
      const centerTile = zoneCenterMap.get(key);
      const haloTile   = zoneHaloMap.get(key);
      const isHovered  = hover?.gx === gx && hover?.gy === gy;

      // ── Fog of war ─────────────────────────────────────────────────────────
      const distToPlayer = Math.sqrt((gx - playerX) ** 2 + (gy - playerY) ** 2);
      const isVisible    = distToPlayer <= VIS_RADIUS;
      const isVisited    = visited.has(key);

      // Determine per-tile opacity
      let fogAlpha: number;
      if (isVisible) {
        fogAlpha = 1.0; // full brightness
      } else if (isVisited) {
        // Gradient fade at the edge of visibility
        const fadeStart = VIS_RADIUS;
        const fadeEnd   = VIS_RADIUS + 3;
        fogAlpha = distToPlayer <= fadeEnd
          ? 0.65 + 0.35 * (1 - (distToPlayer - fadeStart) / (fadeEnd - fadeStart))
          : 0.65;
      } else {
        // Unvisited — draw terrain dimmed so it looks mysterious, not like a wall
        fogAlpha = 0.38;
      }

      ctx.save();
      ctx.globalAlpha = fogAlpha;

      // ── Biome / terrain ────────────────────────────────────────────────────
      const biome = getBiome(gx, gy);
      const path  = !centerTile && !haloTile && isPathTile(gx, gy);

      // Within 10 tiles of the homestead centre (-100, -100) treat terrain as
      // a cleared grassland so buildings are visible and trees don't clutter the area.
      const distToHomestead = Math.sqrt((gx - homesteadCx) ** 2 + (gy - homesteadCy) ** 2);
      const inHomesteadClearing = distToHomestead <= 10;
      const effectiveBiomeType = inHomesteadClearing && !centerTile && !haloTile
        ? 'grassland'
        : biome.type;

      if (centerTile) {
        const colors = zoneSaturatedColor(centerTile);
        const glow   = zoneGlowColor(centerTile);
        drawIsoDiamond(ctx, sx, sy, tileW, tileH, colors.top, glow);
      } else if (haloTile) {
        const colors = path
          ? getBiomeColors('path', gx, gy)
          : getBiomeColors(effectiveBiomeType, gx, gy);
        const glow   = zoneGlowColor(haloTile) + '66';
        drawIsoDiamond(ctx, sx, sy, tileW, tileH, colors.top, glow);
      } else if (path) {
        const colors = getBiomeColors('path', gx, gy);
        drawIsoDiamond(ctx, sx, sy, tileW, tileH, colors.top);
      } else if (effectiveBiomeType === 'water') {
        const blueBase = 0x22 + Math.round(waterShift * 16);
        const greenVal = Math.round(0x66 + waterShift * 20);
        const col = `rgb(${blueBase},${greenVal},${0xaa})`;
        drawIsoDiamond(ctx, sx, sy, tileW, tileH, col);
        drawWaterWaves(ctx, sx, sy, tileW, tileH, waterFrame);
      } else {
        const colors = getBiomeColors(effectiveBiomeType, gx, gy);
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
        ctx.strokeStyle = 'rgba(255,240,160,0.65)';
        ctx.lineWidth = 1.5 * dpr;
        ctx.stroke();
      }

      // ── Terrain decals ─────────────────────────────────────────────────────
      // Suppress decals inside the homestead clearing (grassland tiles are shown instead)
      // and on any tile occupied by a homestead building.
      const hasBuildingHere = buildingMap.has(key);
      if (!centerTile && !path && !inHomesteadClearing && !hasBuildingHere) {
        if (biome.type === 'denseForest')  drawTreeDecal(ctx, sx, sy, true);
        if (biome.type === 'lightForest')  drawTreeDecal(ctx, sx, sy, false);
        if (biome.type === 'mountain')     drawMountainDecal(ctx, sx, sy, false);
        if (biome.type === 'snowMountain') drawMountainDecal(ctx, sx, sy, true);
      }

      // ── Zone building + glyph ──────────────────────────────────────────────
      if (centerTile) {
        const bldColors = zoneBuildingColors(centerTile);
        drawZoneBuilding(ctx, sx, sy, bldColors.wall, bldColors.roof);

        const glow = zoneGlowColor(centerTile);
        ctx.font = `bold ${10 * dpr}px monospace`;
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        ctx.fillStyle = glow;
        ctx.shadowColor = glow;
        ctx.shadowBlur = 8 * dpr;
        ctx.fillText(centerTile.asciiSymbol, sx, sy);
        ctx.shadowBlur = 0;
      }

      // ── Collect homestead buildings for a deferred second pass ──────────────
      const homesteadBuilding = buildingMap.get(key);
      if (homesteadBuilding && !centerTile) {
        const anchorX = homesteadCx + homesteadBuilding.gridX;
        const anchorY = homesteadCy + homesteadBuilding.gridY;
        if (gx === anchorX && gy === anchorY) {
          // Compute center screen position for a 2×2 footprint:
          // anchor is top-left corner tile; center is midpoint of the 4 tiles.
          // For a 2×2 block the visual center between (ax,ay),(ax+1,ay),(ax,ay+1),(ax+1,ay+1)
          // in isometric is the average of their screen centers.
          const [s00x, s00y] = gridToScreen(anchorX,     anchorY,     vpCx, vpCy);
          const [s10x, s10y] = gridToScreen(anchorX + 1, anchorY,     vpCx, vpCy);
          const [s01x, s01y] = gridToScreen(anchorX,     anchorY + 1, vpCx, vpCy);
          const [s11x, s11y] = gridToScreen(anchorX + 1, anchorY + 1, vpCx, vpCy);
          const bldSx = (s00x + s10x + s01x + s11x) / 4 + camOffX;
          const bldSy = (s00y + s10y + s01y + s11y) / 4 + camOffY;
          deferredBuildings.push({
            sx: bldSx, sy: bldSy,
            sortKey: anchorX + anchorY,
            building: homesteadBuilding,
            fogAlpha,
          });
        }
      }

      // ── Difficulty border (zone 3×3 halo including centre) ─────────────────
      const diffZone = zoneDiffMap.get(key);
      if (diffZone) {
        if (isVisible || isVisited) {
          // Visited / visible: show relative-difficulty color with scaled thickness
          const borderColor = difficultyBorderColor(diffZone.dangerLevel, playerLevel);
          const borderWidth = difficultyBorderWidth(diffZone.dangerLevel, playerLevel);
          strokeIsoDiamond(ctx, sx, sy, tileW, tileH, borderColor, borderWidth * dpr);
        } else {
          // Unvisited: thin dark border — zone is unknown, not threatening
          strokeIsoDiamond(ctx, sx, sy, tileW, tileH, '#222222', 0.8 * dpr);
        }
      }

      // ── Fog overlay for unvisited tiles ────────────────────────────────────
      if (!isVisible && !isVisited) {
        // Check if adjacent to any visited tile for edge-gradient effect
        const adjToVisited =
          visited.has(`${gx - 1},${gy}`) || visited.has(`${gx + 1},${gy}`) ||
          visited.has(`${gx},${gy - 1}`) || visited.has(`${gx},${gy + 1}`) ||
          visited.has(`${gx - 1},${gy - 1}`) || visited.has(`${gx + 1},${gy - 1}`) ||
          visited.has(`${gx - 1},${gy + 1}`) || visited.has(`${gx + 1},${gy + 1}`);

        // Warm grey fog overlay — softer at edges, denser further in
        const fogOverlayAlpha = adjToVisited ? 0.40 : 0.70;

        ctx.globalAlpha = fogOverlayAlpha;
        ctx.beginPath();
        ctx.moveTo(sx,              sy - tileH / 2);
        ctx.lineTo(sx + tileW / 2,  sy);
        ctx.lineTo(sx,              sy + tileH / 2);
        ctx.lineTo(sx - tileW / 2,  sy);
        ctx.closePath();
        ctx.fillStyle = '#3a3530';
        ctx.fill();

        // Scatter subtle "?" hints on non-edge fog tiles to invite exploration
        if (!adjToVisited) {
          // Use tile coords as a stable pseudo-random seed
          const seed = ((gx * 374761393 + gy * 668265263) >>> 0) % 1000;
          if (seed < 80) { // ~8% of deep-fog tiles get a hint
            ctx.globalAlpha = 0.28;
            ctx.font = `bold ${7 * dpr}px monospace`;
            ctx.textAlign = 'center';
            ctx.textBaseline = 'middle';
            ctx.fillStyle = '#c8b89a';
            ctx.fillText('?', sx, sy - tileH * 0.08);
          }
        }

        ctx.globalAlpha = 1.0;
      }

      ctx.restore();

      // ── NPCs on this tile ──────────────────────────────────────────────────
      if (isVisible || isVisited) {
        const npcs = npcMap.get(key);
        if (npcs) {
          ctx.save();
          ctx.globalAlpha = isVisible ? 1.0 : 0.65;
          for (let i = 0; i < npcs.length; i++) {
            const npc = npcs[i];
            const color = npcColor(npc.role);
            const letter = npcGlyph(npc.role);
            const offsetI = (i - (npcs.length - 1) / 2) * 8 * dpr;

            ctx.beginPath();
            ctx.arc(sx + offsetI, sy - tileH / 2 - 4 * dpr, 4 * dpr, 0, Math.PI * 2);
            ctx.fillStyle = color;
            ctx.fill();

            ctx.font = `bold ${8 * dpr}px monospace`;
            ctx.textAlign = 'center';
            ctx.textBaseline = 'bottom';
            ctx.fillStyle = color;
            ctx.fillText(letter, sx + offsetI, sy - tileH / 2 - 10 * dpr);
          }
          ctx.restore();
        }
      }

    }  // end terrain loop

    // ── Homestead buildings pass (after terrain, before player) ───────────────
    if (homesteadBuildingsRef.current.length > 0) {
      console.log(`[WorldMap] homesteadBuildings count=${homesteadBuildingsRef.current.length} deferred=${deferredBuildings.length} playerAt=(${playerX},${playerY})`);
    }
    deferredBuildings.sort((a, b) => a.sortKey - b.sortKey);
    for (const db of deferredBuildings) {
      ctx.save();
      ctx.globalAlpha = db.fogAlpha;
      drawHomesteadBuilding(
        ctx, db.sx, db.sy, tileW, tileH,
        db.building.type,
        db.building.isConstructed,
        db.building.constructionProgress,
        waterFrame,
      );
      ctx.restore();
    }

    // ── Player (drawn above all buildings) ────────────────────────────────────
    {
      const [psx2, psy2] = gridToScreen(playerX, playerY, vpCx, vpCy);
      const bx = psx2 + camOffX;
      const by = psy2 - tileH / 2;
      const headColor = '#ffffff';
      const bodyColor = blinkRef.current ? '#ffcc33' : '#e6b820';

      ctx.beginPath();
      ctx.moveTo(bx, by - 2 * dpr);
      ctx.lineTo(bx + 5 * dpr, by + 10 * dpr);
      ctx.lineTo(bx - 5 * dpr, by + 10 * dpr);
      ctx.closePath();
      ctx.fillStyle = bodyColor;
      ctx.shadowColor = '#ffcc33';
      ctx.shadowBlur = 6 * dpr;
      ctx.fill();
      ctx.shadowBlur = 0;

      ctx.beginPath();
      ctx.arc(bx, by - 5 * dpr, 4 * dpr, 0, Math.PI * 2);
      ctx.fillStyle = headColor;
      ctx.fill();
    }

    // ── Minimap ────────────────────────────────────────────────────────────────
    const mmSize = MINI_SIZE * dpr;
    const mmPad  = MINI_PAD  * dpr;
    const mmX    = canvas.width - mmSize - mmPad;
    const mmY    = mmPad;

    ctx.save();
    ctx.fillStyle = '#2a2218';
    ctx.fillRect(mmX, mmY, mmSize, mmSize);
    ctx.strokeStyle = '#6a5a3a';
    ctx.lineWidth   = 1.5 * dpr;
    ctx.strokeRect(mmX, mmY, mmSize, mmSize);

    if (zoneTilesRef.current.length > 0) {
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
        const mx = mmX + ((t.x - minX) / rangeX) * (mmSize - MINI_DOT * dpr * 2) + MINI_DOT * dpr;
        const my = mmY + ((t.y - minY) / rangeY) * (mmSize - MINI_DOT * dpr * 2) + MINI_DOT * dpr;
        const r = MINI_DOT * dpr * 0.75;
        ctx.beginPath();
        ctx.arc(mx, my, r, 0, Math.PI * 2);
        ctx.fillStyle = zoneGlowColor(t);
        ctx.fill();
        ctx.beginPath();
        ctx.arc(mx - r * 0.3, my - r * 0.3, r * 0.35, 0, Math.PI * 2);
        ctx.fillStyle = 'rgba(255,255,255,0.5)';
        ctx.fill();
      }

      const px = mmX + ((playerX - minX) / rangeX) * (mmSize - MINI_DOT * dpr * 2) + MINI_DOT * dpr;
      const py = mmY + ((playerY - minY) / rangeY) * (mmSize - MINI_DOT * dpr * 2) + MINI_DOT * dpr;
      if (blinkRef.current) {
        ctx.beginPath();
        ctx.arc(px, py, (MINI_DOT + 1) * dpr * 0.75, 0, Math.PI * 2);
        ctx.fillStyle = '#ffcc33';
        ctx.shadowColor = '#ffcc33';
        ctx.shadowBlur = 4 * dpr;
        ctx.fill();
        ctx.shadowBlur = 0;
      } else {
        ctx.beginPath();
        ctx.arc(px, py, MINI_DOT * dpr * 0.75, 0, Math.PI * 2);
        ctx.fillStyle = '#ffcc33';
        ctx.fill();
      }
    }

    ctx.restore();

    // ── Quest waypoint ─────────────────────────────────────────────────────────
    const wp = questWaypointRef.current;
    if (wp) {
      const wpX = wp.targetX;
      const wpY = wp.targetY;
      const [wpSx, wpSy] = gridToScreen(wpX, wpY, vpCx, vpCy);
      const wpAsx = wpSx + camOffX;
      const wpAsy = wpSy + camOffY;

      const pulse = 0.65 + 0.35 * Math.sin(waterFrame * 0.08);
      const wpColor = '#00e5ff';

      // Dotted line from player to waypoint
      const [plSx, plSy] = gridToScreen(playerX, playerY, vpCx, vpCy);
      const plAsx = plSx + camOffX;
      const plAsy = plSy + camOffY;

      ctx.save();
      ctx.globalAlpha = 0.45 * pulse;
      ctx.setLineDash([4 * dpr, 6 * dpr]);
      ctx.strokeStyle = wpColor;
      ctx.lineWidth = 1.5 * dpr;
      ctx.beginPath();
      ctx.moveTo(plAsx, plAsy - tileH / 4);
      ctx.lineTo(wpAsx, wpAsy - tileH / 4);
      ctx.stroke();
      ctx.setLineDash([]);
      ctx.restore();

      // Pulsing glow ring
      ctx.save();
      ctx.globalAlpha = 0.25 * pulse;
      ctx.beginPath();
      ctx.arc(wpAsx, wpAsy - tileH / 4, 16 * dpr * pulse, 0, Math.PI * 2);
      ctx.fillStyle = wpColor;
      ctx.fill();
      ctx.restore();

      // Diamond marker
      ctx.save();
      ctx.globalAlpha = 0.85 * pulse;
      const dmS = 8 * dpr;
      ctx.beginPath();
      ctx.moveTo(wpAsx,        wpAsy - tileH / 4 - dmS); // top
      ctx.lineTo(wpAsx + dmS,  wpAsy - tileH / 4);       // right
      ctx.lineTo(wpAsx,        wpAsy - tileH / 4 + dmS); // bottom
      ctx.lineTo(wpAsx - dmS,  wpAsy - tileH / 4);       // left
      ctx.closePath();
      ctx.fillStyle = wpColor;
      ctx.shadowColor = wpColor;
      ctx.shadowBlur = 10 * dpr;
      ctx.fill();
      ctx.shadowBlur = 0;
      ctx.strokeStyle = '#ffffff';
      ctx.lineWidth = 1 * dpr;
      ctx.stroke();
      ctx.restore();

      // Quest name label above the diamond
      ctx.save();
      ctx.globalAlpha = 0.9;
      ctx.font = `bold ${9 * dpr}px monospace`;
      ctx.textAlign = 'center';
      ctx.textBaseline = 'bottom';
      ctx.shadowColor = '#000000';
      ctx.shadowBlur = 4 * dpr;
      ctx.fillStyle = wpColor;
      const labelText = wp.questTitle.length > 22 ? wp.questTitle.slice(0, 19) + '...' : wp.questTitle;
      ctx.fillText(labelText, wpAsx, wpAsy - tileH / 4 - dmS - 4 * dpr);
      ctx.shadowBlur = 0;
      ctx.restore();

      // Also draw waypoint dot on minimap
      if (zoneTilesRef.current.length > 0) {
        let minX2 = Infinity, maxX2 = -Infinity, minY2 = Infinity, maxY2 = -Infinity;
        for (const t of zoneTilesRef.current) {
          if (t.x < minX2) minX2 = t.x;
          if (t.x > maxX2) maxX2 = t.x;
          if (t.y < minY2) minY2 = t.y;
          if (t.y > maxY2) maxY2 = t.y;
        }
        const rangeX2 = Math.max(maxX2 - minX2, 1);
        const rangeY2 = Math.max(maxY2 - minY2, 1);
        const mmSize2 = MINI_SIZE * dpr;
        const mmPad2  = MINI_PAD  * dpr;
        const mmX2    = canvas.width - mmSize2 - mmPad2;
        const mmY2    = mmPad2;
        const wpMx = mmX2 + ((wpX - minX2) / rangeX2) * (mmSize2 - MINI_DOT * dpr * 2) + MINI_DOT * dpr;
        const wpMy = mmY2 + ((wpY - minY2) / rangeY2) * (mmSize2 - MINI_DOT * dpr * 2) + MINI_DOT * dpr;
        ctx.save();
        ctx.globalAlpha = 0.85 * pulse;
        ctx.beginPath();
        ctx.arc(wpMx, wpMy, (MINI_DOT + 1) * dpr * 0.75, 0, Math.PI * 2);
        ctx.fillStyle = wpColor;
        ctx.shadowColor = wpColor;
        ctx.shadowBlur = 4 * dpr;
        ctx.fill();
        ctx.shadowBlur = 0;
        ctx.restore();
      }
    }
  }, [markVisited]);

  // ─── Animation loop ──────────────────────────────────────────────────────────
  useEffect(() => {
    let frameCount = 0;
    let blinkCount = 0;

    const loop = () => {
      frameCount++;
      waterFrameRef.current = frameCount;

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
        background: '#1a1510',
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
