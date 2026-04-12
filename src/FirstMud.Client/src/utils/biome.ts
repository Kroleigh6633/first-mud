// ─── Biome utility ────────────────────────────────────────────────────────────
// Deterministic 2D value noise + biome classification used by both
// WorldMap.tsx (rendering) and GameTerminal.tsx (status text).

// ─── Zone influence definitions ──────────────────────────────────────────────
interface ZoneInfluence {
  x: number;
  y: number;
  elevationDelta: number;   // how much this zone shifts elevation (±)
  moistureDelta: number;    // how much this zone shifts moisture (±)
  radius: number;           // tiles of influence
}

// Matches the hand-crafted layout in ZoneGridLayout.cs
const ZONE_INFLUENCES: ZoneInfluence[] = [
  // (1) Caervorn Highlands  (8, 3)  — pushes elevation up
  { x: 8,  y: 3,  elevationDelta: +0.25, moistureDelta: -0.05, radius: 6 },
  // (2) The Thornwood       (13, 5) — pushes moisture up (more trees)
  { x: 13, y: 5,  elevationDelta: 0,     moistureDelta: +0.30, radius: 5 },
  // (3) Portmere Compact    (24,13) — slight wetland
  { x: 24, y: 13, elevationDelta: -0.05, moistureDelta: +0.10, radius: 4 },
  // (4) Gravenmarsh         (28, 9) — mild marsh elevation drop
  { x: 28, y: 9,  elevationDelta: -0.10, moistureDelta: +0.15, radius: 4 },
  // (5) Drowned Coast       (32,15) — pulls elevation low (water)
  { x: 32, y: 15, elevationDelta: -0.30, moistureDelta: +0.20, radius: 6 },
  // (6) Ashen Reach         (34, 4) — drains moisture (barren)
  { x: 34, y: 4,  elevationDelta: 0,     moistureDelta: -0.35, radius: 6 },
  // (7) Starting Road       (20,10) — flattens terrain slightly
  { x: 20, y: 10, elevationDelta: -0.05, moistureDelta: 0,     radius: 4 },
  // (8) Gravenhold          (26, 7) — mild highland
  { x: 26, y: 7,  elevationDelta: +0.08, moistureDelta: -0.05, radius: 4 },
  // (9) Maw Borderlands     (36,18) — harsh volcanic / barren
  { x: 36, y: 18, elevationDelta: +0.10, moistureDelta: -0.25, radius: 5 },
];

// ─── Noise internals ─────────────────────────────────────────────────────────

/** Integer hash → 0..1 float, deterministic */
function hashFloat(ix: number, iy: number, seed: number): number {
  // Combine with seed using prime multiplications, keep in 32-bit int range
  let h = (ix * 374761 + iy * 668265 + seed * 1234567) | 0;
  h = Math.imul(h ^ (h >>> 16), 0x45d9f3b);
  h = Math.imul(h ^ (h >>> 16), 0x45d9f3b);
  h = h ^ (h >>> 16);
  return (h & 0x7fffffff) / 0x7fffffff;
}

function lerp(a: number, b: number, t: number): number {
  return a + (b - a) * t;
}

/**
 * Smooth value noise — bilinearly interpolated hash grid.
 * Returns value in [0, 1]. Deterministic: same x/y/seed = same result.
 */
export function noise2D(x: number, y: number, seed: number = 42): number {
  const ix = Math.floor(x);
  const iy = Math.floor(y);
  const fx = x - ix;
  const fy = y - iy;

  // Smooth (Hermite) interpolation factors
  const sx = fx * fx * (3 - 2 * fx);
  const sy = fy * fy * (3 - 2 * fy);

  const n00 = hashFloat(ix,     iy,     seed);
  const n10 = hashFloat(ix + 1, iy,     seed);
  const n01 = hashFloat(ix,     iy + 1, seed);
  const n11 = hashFloat(ix + 1, iy + 1, seed);

  return lerp(lerp(n00, n10, sx), lerp(n01, n11, sx), sy);
}

/** Fractional Brownian Motion — sum several noise octaves for richer detail */
function fbm(x: number, y: number, seed: number, octaves: number, lacunarity = 2.0, gain = 0.5): number {
  let value = 0;
  let amplitude = 1.0;
  let frequency = 1.0;
  let max = 0;
  for (let i = 0; i < octaves; i++) {
    value += amplitude * noise2D(x * frequency, y * frequency, seed + i * 100);
    max += amplitude;
    amplitude *= gain;
    frequency *= lacunarity;
  }
  return value / max;
}

// ─── Public API ──────────────────────────────────────────────────────────────

export type BiomeType = 'water' | 'sand' | 'grassland' | 'lightForest' | 'denseForest' | 'mountain' | 'snowMountain' | 'path';

export interface Biome {
  type: BiomeType;
  name: string;
  /** 1–10 rough danger estimate (used for tooltip + color hints) */
  dangerEstimate: number;
}

/** Cache so per-tile biome is only computed once per viewport */
const biomeCache = new Map<string, Biome>();

/** Clear cache (called when viewport scrolls significantly) */
export function clearBiomeCache(): void {
  biomeCache.clear();
}

/**
 * Returns the biome at world tile (x, y).
 * Shared by WorldMap.tsx and GameTerminal.tsx so the displayed name matches the rendered terrain.
 */
export function getBiome(x: number, y: number): Biome {
  const key = `${x},${y}`;
  const cached = biomeCache.get(key);
  if (cached) return cached;

  // ── Base noise channels ──────────────────────────────────────────────────
  // Scale coordinates so the noise features are 20–30 tiles wide
  const elevScale = 0.05;
  const moistScale = 0.08;
  const tempScale  = 0.03;

  let elevation = fbm(x * elevScale, y * elevScale, 42,   3);
  let moisture  = fbm(x * moistScale, y * moistScale, 137, 3);
  // Temperature channel reserved for future color tinting — computed but not yet applied
  fbm(x * tempScale,  y * tempScale,  251, 2);

  // ── Zone influence blending ──────────────────────────────────────────────
  for (const inf of ZONE_INFLUENCES) {
    const dx = x - inf.x;
    const dy = y - inf.y;
    const dist = Math.sqrt(dx * dx + dy * dy);
    if (dist < inf.radius) {
      // Linear fall-off: full influence at dist=0, zero at dist=radius
      const weight = 1 - dist / inf.radius;
      elevation += inf.elevationDelta * weight;
      moisture  += inf.moistureDelta  * weight;
    }
  }

  // Clamp to [0, 1]
  elevation = Math.max(0, Math.min(1, elevation));
  moisture  = Math.max(0, Math.min(1, moisture));

  // ── Biome classification ─────────────────────────────────────────────────
  let type: BiomeType;
  let name: string;
  let dangerEstimate: number;

  if (elevation < 0.25) {
    type = 'water';
    name = 'Murky Waters';
    dangerEstimate = 3;
  } else if (elevation < 0.35 && moisture < 0.3) {
    type = 'sand';
    name = 'Barren Wastes';
    dangerEstimate = 2;
  } else if (elevation > 0.85) {
    type = 'snowMountain';
    name = 'Frozen Peaks';
    dangerEstimate = 9;
  } else if (elevation > 0.72) {
    type = 'mountain';
    name = 'Mountain Pass';
    dangerEstimate = 7;
  } else if (moisture > 0.6 && elevation < 0.72) {
    type = 'denseForest';
    name = 'Dark Forest';
    dangerEstimate = 6;
  } else if (moisture > 0.38) {
    type = 'lightForest';
    name = 'Unnamed Forest';
    dangerEstimate = 4;
  } else if (elevation < 0.4 && moisture > 0.2) {
    type = 'grassland';
    name = 'Riverside';
    dangerEstimate = 2;
  } else {
    type = 'grassland';
    name = 'Open Plains';
    dangerEstimate = 1;
  }

  const biome: Biome = { type, name, dangerEstimate };
  biomeCache.set(key, biome);
  return biome;
}

/**
 * Returns true if the tile at (x,y) should be rendered as a path
 * (connecting nearby zone positions). Paths run between zones that are
 * within 12 tiles of each other along low-elevation routes.
 *
 * Implements simple winding: a slight sinusoidal lateral offset keyed
 * by position so paths look natural rather than ruler-straight.
 */
export function isPathTile(x: number, y: number): boolean {
  // Zone centre coordinates (from ZoneGridLayout.cs)
  const ZONE_CENTRES: [number, number][] = [
    [8, 3], [13, 5], [24, 13], [28, 9], [32, 15],
    [34, 4], [20, 10], [26, 7], [36, 18],
  ];

  // Pairs of zones close enough to connect with a path (≤ 12 tiles apart)
  for (let i = 0; i < ZONE_CENTRES.length; i++) {
    for (let j = i + 1; j < ZONE_CENTRES.length; j++) {
      const [x1, y1] = ZONE_CENTRES[i];
      const [x2, y2] = ZONE_CENTRES[j];
      const zoneDist = Math.sqrt((x2 - x1) ** 2 + (y2 - y1) ** 2);
      if (zoneDist > 12) continue;

      // Check if (x, y) lies close to the segment between these two zones
      // Project point onto segment
      const segLenSq = (x2 - x1) ** 2 + (y2 - y1) ** 2;
      if (segLenSq === 0) continue;
      const t = Math.max(0, Math.min(1, ((x - x1) * (x2 - x1) + (y - y1) * (y2 - y1)) / segLenSq));

      // Add a small winding offset perpendicular to the segment
      // deterministic based on position along segment
      const windSeed = Math.round(t * 20); // integer "step" along path
      const windAmp = 0.6;
      const windOffset = (hashFloat(x1 + windSeed, y1, 999) - 0.5) * 2 * windAmp;

      // Perpendicular direction (normalised)
      const segLen = Math.sqrt(segLenSq);
      const perpX = -(y2 - y1) / segLen;
      const perpY =  (x2 - x1) / segLen;

      // Winded closest point
      const nearX = x1 + t * (x2 - x1) + perpX * windOffset;
      const nearY = y1 + t * (y2 - y1) + perpY * windOffset;

      const dist = Math.sqrt((x - nearX) ** 2 + (y - nearY) ** 2);
      if (dist < 0.6) return true;
    }
  }
  return false;
}
