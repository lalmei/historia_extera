import { dynastyOf, figureOf, settlementOf, type DecodedRaster, type World } from './store.ts';
import type { Standing } from './timeline.ts';
import {
  FLAG_COAST,
  FLAG_RIVER,
  SITE_LABELS,
  type Dynasty,
  type EntityId,
  type ExportRiver,
  type ExportWorld,
  type Settlement,
  type SiteCharacter,
} from './types.ts';

/** Site characters whose reason for being is water, and so belong on the harbour overlay. */
export const WATER_SITES: readonly SiteCharacter[] = [
  'Harbour',
  'Coastal',
  'Estuary',
  'Riverside',
  'Confluence',
];

const SEA_SITES: ReadonlySet<SiteCharacter> = new Set(['Harbour', 'Coastal', 'Estuary']);

/** Site characters that explain a town's place without being water. */
export const LANDMARK_SITES: readonly SiteCharacter[] = ['Mine', 'Pass'];

export type WaterKind = 'sea' | 'river';

export interface WaterMark {
  settlement: Settlement;
  /** Map space 0–100, already nudged into the water. */
  mx: number;
  my: number;
  water: WaterKind;
}

export interface LandmarkMark {
  settlement: Settlement;
  mx: number;
  my: number;
  site: 'Mine' | 'Pass';
}

export type HouseRole = 'seat' | 'home' | 'residence';

export interface HouseMark {
  house: Dynasty;
  civId: EntityId;
  role: HouseRole;
  settlement: Settlement;
  mx: number;
  my: number;
  /** Living members of the house recorded at this town in the selected year. */
  living: number;
}

export interface HouseOnMap {
  house: Dynasty;
  civId: EntityId;
  seat?: Settlement;
  home?: Settlement;
  marks: HouseMark[];
  /** Seat to ancestral home, when those are two different places. */
  link?: { from: Settlement; to: Settlement };
}

/**
 * Map-space percent along one axis. The same transform the map SVG uses, so a mark computed
 * here lands on the settlement the canvas already drew.
 */
export function toMap(value: number, axis: 'x' | 'z', world: ExportWorld): number {
  return axis === 'x'
    ? ((value - world.minX) / world.width) * 100
    : ((value - world.minZ) / world.height) * 100;
}

/**
 * Anchors and river-landings for towns that were sited for water.
 *
 * The icon sits in the water, not on the town: a harbour that reads as a mark on the city
 * hides the very fact that made it a harbour. Direction comes from the raster (sea, lake,
 * coast, river flag) and, failing that, from the nearest river reach.
 */
export function waterMarks(
  standing: Standing[],
  world: ExportWorld,
  raster: DecodedRaster,
): WaterMark[] {
  const marks: WaterMark[] = [];

  for (const entry of standing) {
    const settlement = entry.settlement;
    if (!WATER_SITES.includes(settlement.site)) continue;

    const preferSea = SEA_SITES.has(settlement.site);
    const offset = waterward(settlement, world, raster, preferSea ? 'sea' : 'river');

    marks.push({
      settlement,
      mx: toMap(settlement.x, 'x', world) + offset.dx,
      my: toMap(settlement.z, 'z', world) + offset.dy,
      water: preferSea ? 'sea' : 'river',
    });
  }

  return marks;
}

export function landmarkMarks(standing: Standing[], world: ExportWorld): LandmarkMark[] {
  const marks: LandmarkMark[] = [];

  for (const entry of standing) {
    const site = entry.settlement.site;
    if (site !== 'Mine' && site !== 'Pass') continue;

    // Off the dot, up and to the right, so a mine never paints over the town it explains.
    marks.push({
      settlement: entry.settlement,
      mx: toMap(entry.settlement.x, 'x', world) + 1.15,
      my: toMap(entry.settlement.z, 'z', world) - 0.95,
      site,
    });
  }

  return marks;
}

/**
 * Year walls were raised, by settlement.
 *
 * Fortification is a chronicle event, not a final-state flag: drawing every walled town as
 * walled from its founding would put ramparts on hamlets that had not yet earned them.
 * Walls that later fell are still shown from the year they went up — the engine does not
 * record the year they came down, and hiding them entirely would erase the years they stood.
 */
export function fortifiedFromYear(events: { kind: string; year: number; subject?: EntityId }[]): Map<EntityId, number> {
  const years = new Map<EntityId, number>();

  for (const event of events) {
    if (event.kind !== 'SettlementFortified' || !event.subject) continue;
    const previous = years.get(event.subject);
    if (previous === undefined || event.year < previous) years.set(event.subject, event.year);
  }

  return years;
}

export function wallsStanding(
  standing: Standing[],
  year: number,
  raised: Map<EntityId, number>,
): Standing[] {
  return standing.filter((entry) => {
    const from = raised.get(entry.settlement.id);
    if (from !== undefined) return from <= year;
    // Older files, or a town the chronicle never announced: trust the final flag only at
    // the end of the run, never as a claim about the past.
    return entry.settlement.isFortified && year >= entry.settlement.foundedYear;
  });
}

/**
 * Ruling houses in `year`, with a banner at the throne and a house mark at the ancestral seat.
 *
 * Residences — living members posted out as governors, or still at home — are filled in only
 * for the focused realm. Showing every cadet's town on an unfocused map is a family tree, not
 * a political map, and the two disagree about what belongs on one glance.
 */
export function housesOnMap(world: World, year: number, focus: EntityId | null): HouseOnMap[] {
  const { timeline } = world;
  const houses: HouseOnMap[] = [];
  const seen = new Set<string>();

  for (const civ of timeline.realmsAt(year)) {
    const rulerId = timeline.rulerAt(civ.id, year);
    const ruler = figureOf(world, rulerId);
    const house = dynastyOf(world, ruler?.dynastyId);
    if (!house) continue;

    const key = `${house.id}:${civ.id}`;
    if (seen.has(key)) continue;
    seen.add(key);

    const seat = settlementOf(world, timeline.capitalAt(civ.id, year));
    const home = ancestralHome(world, house);
    const livingByTown = countLiving(world, house, year);
    const detail = focus === civ.id;

    const marks: HouseMark[] = [];

    if (seat) {
      marks.push(
        markAt(world, house, civ.id, 'seat', seat, {
          dx: 0,
          dy: -1.55,
          living: livingByTown.get(seat.id) ?? 0,
        }),
      );
    }

    if (home && home.id !== seat?.id) {
      marks.push(
        markAt(world, house, civ.id, 'home', home, {
          dx: 0,
          dy: 1.4,
          living: livingByTown.get(home.id) ?? 0,
        }),
      );
    }

    if (detail) {
      const claimed = new Set(marks.map((mark) => mark.settlement.id));

      for (const [settlementId, living] of livingByTown) {
        if (claimed.has(settlementId)) continue;
        const settlement = settlementOf(world, settlementId);
        if (!settlement) continue;
        if (settlement.foundedYear > year) continue;
        if (settlement.abandonedYear !== undefined && settlement.abandonedYear <= year) continue;

        marks.push(
          markAt(world, house, civ.id, 'residence', settlement, { dx: 1.35, dy: 0.15, living }),
        );
      }
    }

    houses.push({
      house,
      civId: civ.id,
      seat,
      home,
      marks,
      link: seat && home && seat.id !== home.id ? { from: home, to: seat } : undefined,
    });
  }

  return houses;
}

export function waterLabel(site: SiteCharacter): string {
  return SITE_LABELS[site];
}

function markAt(
  world: World,
  house: Dynasty,
  civId: EntityId,
  role: HouseRole,
  settlement: Settlement,
  at: { dx: number; dy: number; living: number },
): HouseMark {
  return {
    house,
    civId,
    role,
    settlement,
    mx: toMap(settlement.x, 'x', world.export.world) + at.dx,
    my: toMap(settlement.z, 'z', world.export.world) + at.dy,
    living: at.living,
  };
}

function ancestralHome(world: World, house: Dynasty): Settlement | undefined {
  const founder = figureOf(world, house.founderId);
  const born = settlementOf(world, founder?.birthSettlementId);
  if (born) return born;

  if (!house.originCivilizationId) return undefined;
  return settlementOf(
    world,
    world.timeline.capitalAt(house.originCivilizationId, house.foundedYear),
  );
}

function countLiving(world: World, house: Dynasty, year: number): Map<EntityId, number> {
  const counts = new Map<EntityId, number>();

  for (const id of house.memberIds) {
    const figure = figureOf(world, id);
    if (!figure) continue;
    if (figure.birthYear > year) continue;
    if (figure.deathYear !== undefined && figure.deathYear < year) continue;

    // Residence is final-state: the export does not replay where someone lived year by
    // year. Alive-in-this-year is honest; the town is where they lived when the chronicle
    // closed, which is still the right place to look for the family.
    const townId = figure.residenceSettlementId ?? figure.birthSettlementId;
    if (!townId) continue;
    counts.set(townId, (counts.get(townId) ?? 0) + 1);
  }

  return counts;
}

/**
 * A short step from the town into the water it was founded for.
 *
 * Clamped: a harbour whose nearest ocean pixel is a dozen cells away still gets an icon
 * beside the town, not a mark in the middle of the bay. The raster is preferred because it
 * knows where the shoreline actually is; river vectors are the fallback for inland water.
 */
function waterward(
  settlement: Settlement,
  world: ExportWorld,
  raster: DecodedRaster,
  prefer: WaterKind,
): { dx: number; dy: number } {
  const fromRaster = rasterWater(settlement, world, raster, prefer);
  const fromRiver = nearestRiver(settlement, world);

  let dx: number;
  let dy: number;

  if (fromRaster) {
    dx = fromRaster.dx;
    dy = fromRaster.dy;
  } else if (fromRiver) {
    dx = fromRiver.dx;
    dy = fromRiver.dy;
  } else {
    // No water in reach. Nudge off the dot so the icon is still readable, toward the
    // bottom of the map — an arbitrary default rather than a claim about the shore.
    dx = 0;
    dy = 1.6;
  }

  return clampOffset(dx, dy);
}

const OFFSET_MIN = 1.4;
const OFFSET_MAX = 2.2;

function clampOffset(dx: number, dy: number): { dx: number; dy: number } {
  const length = Math.hypot(dx, dy);
  if (length < 1e-6) return { dx: 0, dy: OFFSET_MIN };

  const scale = Math.min(OFFSET_MAX, Math.max(OFFSET_MIN, length)) / length;
  return { dx: dx * scale, dy: dy * scale };
}

function rasterWater(
  settlement: Settlement,
  world: ExportWorld,
  raster: DecodedRaster,
  prefer: WaterKind,
): { dx: number; dy: number } | undefined {
  const { resolution } = raster;
  const col0 = ((settlement.x - world.minX) / world.width) * resolution;
  const row0 = ((settlement.z - world.minZ) / world.height) * resolution;
  const maxRing = Math.min(14, Math.ceil(resolution / 16));

  let best: { dx: number; dy: number; score: number; dist: number } | undefined;

  for (let ring = 1; ring <= maxRing; ring++) {
    for (let dcol = -ring; dcol <= ring; dcol++) {
      for (let drow = -ring; drow <= ring; drow++) {
        if (Math.max(Math.abs(dcol), Math.abs(drow)) !== ring) continue;

        const col = wrapCol(Math.round(col0) + dcol, resolution, world.eastWestPeriodic);
        const row = Math.round(row0) + drow;
        if (col === null || row < 0 || row >= resolution) continue;

        const index = row * resolution + col;
        const biome = raster.biomeAt(index);
        const flags = raster.flags[index];
        const sea = biome === 'Ocean' || biome === 'Lake' || (flags & FLAG_COAST) !== 0;
        const river = (flags & FLAG_RIVER) !== 0;
        if (!sea && !river) continue;

        const preferred = prefer === 'sea' ? sea : river;
        const score = preferred ? 2 : 1;
        // Offset from the unwrapped search step, not the wrapped sample column —
        // a harbour on the east edge has its water a cell west across the seam,
        // which is a short step, not a line across the map.
        const dx = ((Math.round(col0) + dcol + 0.5 - col0) / resolution) * 100;
        const dy = ((Math.round(row0) + drow + 0.5 - row0) / resolution) * 100;
        const dist = Math.hypot(dx, dy);

        if (
          !best ||
          score > best.score ||
          (score === best.score && dist < best.dist)
        ) {
          best = { dx, dy, score, dist };
        }
      }
    }

    // A preferred hit on this ring is close enough; keep walking only when the shore we
    // wanted has not turned up yet.
    if (best && best.score === 2) break;
  }

  return best;
}

function wrapCol(col: number, resolution: number, periodic: boolean): number | null {
  if (col >= 0 && col < resolution) return col;
  if (!periodic) return null;
  return ((col % resolution) + resolution) % resolution;
}

function nearestRiver(
  settlement: Settlement,
  world: ExportWorld,
): { dx: number; dy: number } | undefined {
  if (world.rivers.length === 0) return undefined;

  let best: { x: number; z: number; dist: number } | undefined;

  for (const reach of world.rivers) {
    const point = closestOnReach(settlement.x, settlement.z, reach, world);
    const dx = wrappedWorldDx(settlement.x, point.x, world);
    const dz = point.z - settlement.z;
    const dist = dx * dx + dz * dz;
    if (!best || dist < best.dist) best = { x: point.x, z: point.z, dist };
  }

  if (!best) return undefined;

  return {
    dx: (wrappedWorldDx(settlement.x, best.x, world) / world.width) * 100,
    dy: ((best.z - settlement.z) / world.height) * 100,
  };
}

function closestOnReach(
  px: number,
  pz: number,
  reach: ExportRiver,
  world: ExportWorld,
): { x: number; z: number } {
  const ax = px + wrappedWorldDx(px, reach.x1, world);
  const bx = px + wrappedWorldDx(px, reach.x2, world);
  const abx = bx - ax;
  const abz = reach.z2 - reach.z1;
  const length = abx * abx + abz * abz;
  const t =
    length < 1e-9
      ? 0
      : Math.max(0, Math.min(1, ((px - ax) * abx + (pz - reach.z1) * abz) / length));

  return { x: ax + abx * t, z: reach.z1 + abz * t };
}

function wrappedWorldDx(from: number, to: number, world: ExportWorld): number {
  let dx = to - from;
  if (!world.eastWestPeriodic) return dx;

  const half = world.width / 2;
  if (dx > half) dx -= world.width;
  if (dx < -half) dx += world.width;
  return dx;
}
