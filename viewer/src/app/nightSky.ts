import type { ExportGalaxy } from './types.ts';

/** Astra's all-sky glow size, scaled down so a page paint stays under a frame or two. */
export const SKY_WIDTH = 480;
export const SKY_HEIGHT = 240;
const STEPS = 40;
const MAX_DISTANCE_KPC = 24;
const SOLAR_NEIGHBORHOOD_KPC = 8;
const ARM_HALF_WIDTH = 0.22;
const ARM_CREST = 1.7;
const INTERARM = 0.72;
const HALO_DENSITY = 0.002;
const BG = { r: 18, g: 16, b: 28 };

const LF_DENSITY = [
  2.0e-9, 8.0e-9, 3.0e-8, 1.1e-7, 4.0e-7, 1.6e-6, 6.0e-6, 2.2e-5, 6.5e-5, 1.4e-4, 2.6e-4, 4.2e-4,
  4.6e-4, 4.4e-4, 5.0e-4, 6.2e-4, 8.2e-4, 1.2e-3, 1.8e-3, 2.6e-3, 3.2e-3, 3.0e-3, 2.0e-3, 1.0e-3,
];
const LF_MIN_MV = -7;
const LF_NORM = 5;
const LIMITING_MAG = 6.5;
const STAR_BUDGET = 2200;
const SIGHT_LINES = 96;
const RADIAL_CELLS = 48;
const EXTINCTION = 0.29;

export interface NightSkyStar {
  x: number;
  y: number;
  mag: number;
  bv: number;
}

export interface NightSky {
  glow: Uint8ClampedArray;
  stars: NightSkyStar[];
  caption: string;
}

export function renderNightSky(galaxy: ExportGalaxy, seed: number): NightSky {
  const glow = renderGlow(galaxy);
  const stars = sampleStars(galaxy, seed);
  const caption = galaxy.morphology === 'Elliptical'
    ? 'Night sky from this world (galactic coordinates). Unresolved glow is the background; resolved stars sit on top. No thin disk: the old spheroid brightens toward the nucleus at longitude 0°.'
    : 'Night sky from this world (galactic coordinates). Unresolved glow is the background; resolved stars sit on top. The bright band is this galaxy\'s disk; longitude 0° is the nucleus.';
  return { glow, stars, caption };
}

function renderGlow(galaxy: ExportGalaxy): Uint8ClampedArray {
  const frame = observerFrame(galaxy);
  const ds = MAX_DISTANCE_KPC / STEPS;
  const intensities = new Float64Array(SKY_WIDTH * SKY_HEIGHT);
  let maxI = 1e-6;

  for (let y = 0; y < SKY_HEIGHT; y++) {
    const b = Math.PI / 2 - ((y + 0.5) / SKY_HEIGHT) * Math.PI;
    for (let x = 0; x < SKY_WIDTH; x++) {
      const l = ((x + 0.5) / SKY_WIDTH) * 2 * Math.PI - Math.PI;
      const dir = direction(frame, l, b);
      let intensity = 0;
      let transmittance = 1;
      for (let step = 1; step <= STEPS; step++) {
        const p = pointAt(frame, dir, step * ds);
        intensity += stellarDensityAt(galaxy, p.x, p.y, p.z) * transmittance * ds;
        transmittance *= Math.exp(-dustDensityAt(galaxy, p.x, p.y, p.z) * ds * 0.22);
        if (transmittance < 0.01) break;
      }
      intensities[y * SKY_WIDTH + x] = intensity;
      if (intensity > maxI) maxI = intensity;
    }
  }

  const rgb = new Uint8ClampedArray(SKY_WIDTH * SKY_HEIGHT * 4);
  const logMax = Math.log(1 + maxI);
  for (let i = 0; i < intensities.length; i++) {
    const t = Math.min(1, Math.log(1 + intensities[i]) / logMax);
    const glow = t * t;
    const o = i * 4;
    rgb[o] = Math.min(255, BG.r + glow * 255 * 1.05);
    rgb[o + 1] = Math.min(255, BG.g + glow * 220);
    rgb[o + 2] = Math.min(255, BG.b + glow * 170);
    rgb[o + 3] = 255;
  }
  return rgb;
}

function sampleStars(galaxy: ExportGalaxy, seed: number): NightSkyStar[] {
  const rng = mulberry32(mix(seed, galaxy));
  const frame = observerFrame(galaxy);
  const edges = radialEdges();
  const lines = buildSightLines(galaxy, frame, edges);
  const stars: NightSkyStar[] = [];
  let expectedTotal = 0;

  for (let bin = 0; bin < LF_DENSITY.length; bin++) {
    const absMag = LF_MIN_MV + bin;
    const density = LF_DENSITY[bin] * LF_NORM;
    const reach = LIMITING_MAG - absMag;
    const weights = new Float64Array(lines.length);
    const horizons = new Int16Array(lines.length);
    let binWeight = 0;
    for (let i = 0; i < lines.length; i++) {
      const visible = lines[i].horizon(reach);
      horizons[i] = visible;
      binWeight += lines[i].weightThrough(visible);
      weights[i] = binWeight;
    }
    const expected = density * binWeight;
    expectedTotal += expected;
    if (expected <= 0) continue;
    const count = poisson(rng, expected);
    for (let n = 0; n < count; n++) {
      const line = pickWeighted(weights, rng() * binWeight);
      const star = sampleStar(lines[line], horizons[line], absMag, rng);
      if (star) stars.push(star);
    }
  }

  stars.sort((a, b) => a.mag - b.mag);
  if (stars.length > STAR_BUDGET) stars.length = STAR_BUDGET;
  void expectedTotal;
  return stars;
}

type Frame = {
  ox: number;
  oy: number;
  oz: number;
  cx: number;
  cy: number;
  px: number;
  py: number;
};

type Dir = { x: number; y: number; z: number };

function observerFrame(galaxy: ExportGalaxy): Frame {
  const loc = galaxy.location;
  const phi = loc.azimuthRad;
  const radius = loc.galactocentricRadiusKpc;
  return {
    ox: radius * Math.cos(phi),
    oy: radius * Math.sin(phi),
    oz: loc.heightPc / 1000,
    cx: -Math.cos(phi),
    cy: -Math.sin(phi),
    px: -Math.sin(phi),
    py: Math.cos(phi),
  };
}

function direction(frame: Frame, longitude: number, latitude: number): Dir {
  const cosB = Math.cos(latitude);
  const sinB = Math.sin(latitude);
  const cosL = Math.cos(longitude);
  const sinL = Math.sin(longitude);
  return {
    x: cosB * cosL * frame.cx + cosB * sinL * frame.px,
    y: cosB * cosL * frame.cy + cosB * sinL * frame.py,
    z: sinB,
  };
}

function pointAt(frame: Frame, dir: Dir, distanceKpc: number): Dir {
  return {
    x: frame.ox + distanceKpc * dir.x,
    y: frame.oy + distanceKpc * dir.y,
    z: frame.oz + distanceKpc * dir.z,
  };
}

function structuralRadius(galaxy: ExportGalaxy, rKpc: number, heightPc: number): number {
  if (galaxy.morphology !== 'Elliptical') return rKpc;
  const zKpc = heightPc / 1000;
  const flattened = zKpc / Math.max(0.2, galaxy.axisRatio);
  return Math.sqrt(rKpc * rKpc + flattened * flattened);
}

function stellarDensityRelative(galaxy: ExportGalaxy, structural: number, heightPc: number): number {
  if (galaxy.morphology === 'Elliptical') {
    const re = Math.max(0.5, galaxy.diskScaleLengthKpc);
    const n = Math.max(1, galaxy.sersicIndex);
    const b = 1.9992 * n - 0.3271;
    const ratio = Math.max(0.05, structural / re);
    return clamp(Math.exp(-b * (Math.pow(ratio, 1 / n) - 1)), 1e-4, 40);
  }
  const radial = Math.exp(-(structural - SOLAR_NEIGHBORHOOD_KPC) / galaxy.diskScaleLengthKpc);
  const vertical = Math.exp(-Math.abs(heightPc) / galaxy.thinDiskScaleHeightPc);
  return radial * vertical;
}

function haloDensity(sphericalKpc: number): number {
  const scaled = Math.max(1, sphericalKpc / SOLAR_NEIGHBORHOOD_KPC);
  return HALO_DENSITY / (scaled * scaled * scaled);
}

function spiralArmAngle(galaxy: ExportGalaxy, arm: number, radiusKpc: number): number {
  if (galaxy.spiralArmCount <= 0) return 0;
  const pitch = (galaxy.spiralPitchDeg * Math.PI) / 180;
  const logTerm = Math.log(Math.max(0.5, radiusKpc) / SOLAR_NEIGHBORHOOD_KPC);
  const phase = logTerm / Math.tan(Math.max(0.05, pitch));
  return (2 * Math.PI * arm) / galaxy.spiralArmCount + phase;
}

function nearestArmOffset(galaxy: ExportGalaxy, radiusKpc: number, azimuth: number): number {
  let nearest = Infinity;
  for (let arm = 0; arm < galaxy.spiralArmCount; arm++) {
    const delta = principalAbs(azimuth - spiralArmAngle(galaxy, arm, radiusKpc));
    if (delta < nearest) nearest = delta;
  }
  return nearest;
}

function armOverdensity(galaxy: ExportGalaxy, radiusKpc: number, azimuth: number): number {
  if (galaxy.spiralArmCount <= 0) return 1;
  const offset = nearestArmOffset(galaxy, radiusKpc, azimuth);
  const sigma = ARM_HALF_WIDTH / 1.5;
  const crest = Math.exp(-0.5 * (offset / sigma) * (offset / sigma));
  return INTERARM + (ARM_CREST - INTERARM) * crest;
}

function stellarDensityAt(galaxy: ExportGalaxy, x: number, y: number, z: number): number {
  const r = Math.sqrt(x * x + y * y);
  const heightPc = z * 1000;
  const structural = structuralRadius(galaxy, r, heightPc);
  let density = stellarDensityRelative(galaxy, structural, heightPc);
  const spherical = Math.sqrt(r * r + z * z);
  if (galaxy.morphology === 'Elliptical') return density + haloDensity(spherical);
  density *= armOverdensity(galaxy, r, Math.atan2(y, x));
  density += galaxy.bulgeToDiskMass * 6 * Math.exp(-spherical / 0.7);
  return density + haloDensity(spherical);
}

function dustDensityAt(galaxy: ExportGalaxy, x: number, y: number, z: number): number {
  if (galaxy.morphology === 'Elliptical') return 0.03 * stellarDensityAt(galaxy, x, y, z);
  const r = Math.sqrt(x * x + y * y);
  const radial = Math.exp(-(r - SOLAR_NEIGHBORHOOD_KPC) / galaxy.diskScaleLengthKpc);
  const lanes = Math.pow(armOverdensity(galaxy, r, Math.atan2(y, x)), 1.8);
  return 3.4 * Math.max(0.02, radial) * lanes * Math.exp(-Math.abs(z) / 0.12);
}

function radialEdges(): number[] {
  const near = Math.log(0.5);
  const far = Math.log(30000);
  const edges = new Array<number>(RADIAL_CELLS + 1);
  for (let i = 0; i <= RADIAL_CELLS; i++) {
    edges[i] = Math.exp(near + ((far - near) * i) / RADIAL_CELLS);
  }
  return edges;
}

class SightLine {
  constructor(
    readonly lon: number,
    readonly lat: number,
    readonly solid: number,
    readonly edges: number[],
    readonly cumulative: Float64Array,
    readonly extinction: Float64Array,
    readonly modulus: Float64Array,
  ) {}

  horizon(reach: number): number {
    let low = 0;
    let high = this.modulus.length;
    while (low < high) {
      const mid = (low + high) >> 1;
      if (this.modulus[mid] <= reach) low = mid + 1;
      else high = mid;
    }
    return low;
  }

  weightThrough(cells: number): number {
    return this.cumulative[cells];
  }

  pickCell(horizon: number, unit: number): number {
    const target = unit * this.cumulative[horizon];
    let low = 0;
    let high = horizon - 1;
    while (low < high) {
      const mid = (low + high) >> 1;
      if (this.cumulative[mid + 1] < target) low = mid + 1;
      else high = mid;
    }
    return low;
  }
}

function buildSightLines(galaxy: ExportGalaxy, frame: Frame, edges: number[]): SightLine[] {
  const lines: SightLine[] = [];
  const solid = (4 * Math.PI) / SIGHT_LINES;
  const golden = Math.PI * (3 - Math.sqrt(5));
  for (let i = 0; i < SIGHT_LINES; i++) {
    const lat = Math.asin(1 - (2 * (i + 0.5)) / SIGHT_LINES);
    const lon = wrapAngle(golden * i);
    const dir = direction(frame, lon, lat);
    const cumulative = new Float64Array(RADIAL_CELLS + 1);
    const extinction = new Float64Array(RADIAL_CELLS);
    const modulus = new Float64Array(RADIAL_CELLS);
    let dust = 0;
    for (let cell = 0; cell < RADIAL_CELLS; cell++) {
      const inner = edges[cell];
      const outer = edges[cell + 1];
      const mid = 0.5 * (inner + outer);
      const thick = outer - inner;
      const p = pointAt(frame, dir, mid / 1000);
      const stars = stellarDensityAt(galaxy, p.x, p.y, p.z);
      dust += dustDensityAt(galaxy, p.x, p.y, p.z) * (thick / 1000) * EXTINCTION;
      extinction[cell] = dust;
      modulus[cell] = 5 * Math.log10(mid / 10) + dust;
      cumulative[cell + 1] = cumulative[cell] + stars * mid * mid * thick * solid;
    }
    lines.push(new SightLine(lon, lat, solid, edges, cumulative, extinction, modulus));
  }
  return lines;
}

function sampleStar(
  line: SightLine,
  horizon: number,
  absMag: number,
  rng: () => number,
): NightSkyStar | null {
  if (horizon <= 0) return null;
  const cell = line.pickCell(horizon, rng());
  const distance = line.edges[cell] + (line.edges[cell + 1] - line.edges[cell]) * rng();
  const mag = absMag + 5 * Math.log10(distance / 10) + line.extinction[cell];
  if (mag > LIMITING_MAG) return null;
  const patch = Math.sqrt(line.solid / Math.PI);
  const offset = patch * Math.sqrt(rng());
  const angle = 2 * Math.PI * rng();
  const lat = clamp(line.lat + offset * Math.sin(angle), -Math.PI / 2 + 1e-6, Math.PI / 2 - 1e-6);
  const lon = wrapAngle(line.lon + (offset * Math.cos(angle)) / Math.max(0.15, Math.cos(lat)));
  const x = ((lon + Math.PI) / (2 * Math.PI)) * SKY_WIDTH;
  const y = ((Math.PI / 2 - lat) / Math.PI) * SKY_HEIGHT;
  return { x, y, mag, bv: colorIndex(absMag, rng) };
}

function colorIndex(absMag: number, rng: () => number): number {
  let base: number;
  if (absMag < 1) base = rng() < 0.45 ? 1.45 : -0.12;
  else if (absMag < 2) base = 0.05;
  else if (absMag < 3) base = 0.25;
  else if (absMag < 4) base = 0.45;
  else if (absMag < 5) base = 0.62;
  else if (absMag < 6) base = 0.75;
  else if (absMag < 7) base = 0.92;
  else if (absMag < 9) base = 1.2;
  else base = 1.5;
  return clamp(base + gauss(rng) * 0.06, -0.35, 2);
}

function pickWeighted(cumulative: Float64Array, target: number): number {
  let low = 0;
  let high = cumulative.length - 1;
  while (low < high) {
    const mid = (low + high) >> 1;
    if (cumulative[mid] < target) low = mid + 1;
    else high = mid;
  }
  return low;
}

function poisson(rng: () => number, lambda: number): number {
  if (lambda <= 0) return 0;
  if (lambda > 30) return Math.max(0, Math.round(lambda + gauss(rng) * Math.sqrt(lambda)));
  const limit = Math.exp(-lambda);
  let n = 0;
  let p = 1;
  do {
    n++;
    p *= rng();
  } while (p > limit);
  return n - 1;
}

function gauss(rng: () => number): number {
  let s = 0;
  for (let i = 0; i < 12; i++) s += rng();
  return s - 6;
}

function mulberry32(seed: number): () => number {
  let a = seed >>> 0;
  return () => {
    a += 0x6d2b79f5;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

function mix(seed: number, galaxy: ExportGalaxy): number {
  const loc = galaxy.location;
  return (
    (seed >>> 0) ^
    Math.imul(Math.round(loc.galactocentricRadiusKpc * 1000), 0x9e3779b9) ^
    Math.imul(Math.round(loc.heightPc), 0x85ebca6b) ^
    Math.imul(Math.round(loc.azimuthRad * 10000), 0xc2b2ae35)
  );
}

function principalAbs(radians: number): number {
  const twoPi = 2 * Math.PI;
  let wrapped = radians - twoPi * Math.floor((radians + Math.PI) / twoPi);
  if (wrapped < 0) wrapped = -wrapped;
  return wrapped;
}

function wrapAngle(radians: number): number {
  const twoPi = 2 * Math.PI;
  let wrapped = radians % twoPi;
  if (wrapped > Math.PI) wrapped -= twoPi;
  if (wrapped < -Math.PI) wrapped += twoPi;
  return wrapped;
}

function clamp(value: number, min: number, max: number): number {
  return value < min ? min : value > max ? max : value;
}
