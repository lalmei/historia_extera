import { buildTimeline, type Timeline } from './timeline';
import {
  BIOME_ORDER,
  SCHEMA_VERSION,
  kindOf,
  type AnyEntity,
  type Battle,
  type Biome,
  type Civilization,
  type Culture,
  type Dynasty,
  type EntityId,
  type Figure,
  type HistoryEvent,
  type HolySite,
  type Region,
  type Religion,
  type Artifact,
  type Series,
  type Settlement,
  type TradeRoute,
  type War,
  type WorldExport,
} from './types';

/**
 * The loaded world, plus the lookup maps the views need.
 *
 * Built once on load. Every id-to-entity resolution in the app goes through a Map
 * here rather than an array scan, which is what makes cross-link navigation feel
 * instant when a history has tens of thousands of events.
 */
export interface World {
  export: WorldExport;
  byId: Map<EntityId, AnyEntity>;
  eventsFor: (id: EntityId) => HistoryEvent[];
  /** Every yearly measure recorded for one entity, in the order the engine sampled them. */
  seriesFor: (id: EntityId) => Series[];
  nameOf: (id: EntityId) => string;
  raster: DecodedRaster;
  /** The world as it stood in any year, replayed from the chronicle. */
  timeline: Timeline;
  /** One colour per realm, shared by every view that shows more than one. */
  colourOf: (id: EntityId) => string;
}

export interface DecodedRaster {
  resolution: number;
  minHeight: number;
  maxHeight: number;
  height: Uint8Array;
  biome: Uint8Array;
  flags: Uint8Array;
  biomeAt: (index: number) => Biome;
}

export class SchemaMismatchError extends Error {
  constructor(found: number) {
    super(
      `This world file uses schema version ${found}, but the viewer understands ` +
        `version ${SCHEMA_VERSION}. Regenerate it with the current engine.`,
    );
    this.name = 'SchemaMismatchError';
  }
}

export async function loadWorld(url: string): Promise<World> {
  const response = await fetch(url);

  if (!response.ok) {
    throw new Error(
      `Could not load ${url} (${response.status}). Generate a world first:\n` +
        `dotnet run --project src/HistoryEngine.Cli -- --seed 42`,
    );
  }

  const data = (await response.json()) as WorldExport;

  // Fail loudly on a version mismatch rather than misrendering a file we do not
  // understand — a wrong chronicle is worse than a refused one.
  if (data.schemaVersion !== SCHEMA_VERSION) {
    throw new SchemaMismatchError(data.schemaVersion);
  }

  return buildWorld(data);
}

export function buildWorld(data: WorldExport): World {
  const byId = new Map<EntityId, AnyEntity>();

  for (const collection of [
    data.regions,
    data.cultures,
    data.civilizations,
    data.dynasties,
    data.settlements,
    data.tradeRoutes,
    data.figures,
    data.wars,
    data.battles,
    data.religions,
    data.holySites,
    data.artifacts,
  ] as AnyEntity[][]) {
    for (const entity of collection) byId.set(entity.id, entity);
  }

  const eventsFor = (id: EntityId): HistoryEvent[] => {
    const indices = data.indices.eventsByEntity[id];
    if (!indices) return [];
    return indices.map((index) => data.events[index]);
  };

  // Bucketed once on load for the same reason the event index exists: an entity page should
  // cost a lookup, not a scan of every series in the world.
  const seriesByEntity = new Map<EntityId, Series[]>();
  for (const series of data.series ?? []) {
    const existing = seriesByEntity.get(series.entity);
    if (existing) existing.push(series);
    else seriesByEntity.set(series.entity, [series]);
  }

  const nameOf = (id: EntityId): string => {
    const entity = byId.get(id);
    if (entity && 'name' in entity) return entity.name;

    if (entity && kindOf(id) === 'rte') {
      const route = entity as TradeRoute;
      const a = byId.get(route.settlementAId);
      const b = byId.get(route.settlementBId);
      const nameA = a && 'name' in a ? a.name : route.settlementAId;
      const nameB = b && 'name' in b ? b.name : route.settlementBId;
      return `${nameA}–${nameB} route`;
    }

    // Unresolvable ids render as the id itself. A chronicle that throws is harder
    // to debug than one with an odd label in it.
    return id;
  };

  // Evenly spaced around the wheel by the golden angle, so neighbouring realms are unlikely to
  // land on neighbouring hues however many there are. Assigned once here rather than per view,
  // because a realm that is teal on the map and orange in a list is a realm you cannot follow.
  const colours = new Map<EntityId, string>();
  data.civilizations.forEach((civ, index) => {
    colours.set(civ.id, `hsl(${(index * 137.508) % 360} 62% 52%)`);
  });

  return {
    export: data,
    byId,
    eventsFor,
    seriesFor: (id) => seriesByEntity.get(id) ?? [],
    nameOf,
    raster: decodeRaster(data),
    timeline: buildTimeline(data),
    colourOf: (id) => colours.get(id) ?? 'var(--ink-faint)',
  };
}

function decodeRaster(data: WorldExport): DecodedRaster {
  const { raster } = data.world;

  const height = base64ToBytes(raster.height);
  const biome = base64ToBytes(raster.biome);
  const flags = base64ToBytes(raster.flags);

  return {
    resolution: raster.resolution,
    minHeight: raster.minHeight,
    maxHeight: raster.maxHeight,
    height,
    biome,
    flags,
    biomeAt: (index) => BIOME_ORDER[biome[index]] ?? 'Ocean',
  };
}

function base64ToBytes(encoded: string): Uint8Array {
  const binary = atob(encoded);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  return bytes;
}

// ---------------------------------------------------------------------------
// Typed accessors. The `byId` map is deliberately untyped-by-kind, so these
// narrow at the point of use and keep casts out of the views.
// ---------------------------------------------------------------------------

export function civOf(world: World, id: EntityId | undefined): Civilization | undefined {
  return id && kindOf(id) === 'civ' ? (world.byId.get(id) as Civilization) : undefined;
}

export function settlementOf(world: World, id: EntityId | undefined): Settlement | undefined {
  return id && kindOf(id) === 'set' ? (world.byId.get(id) as Settlement) : undefined;
}

export function tradeRouteOf(world: World, id: EntityId | undefined): TradeRoute | undefined {
  return id && kindOf(id) === 'rte' ? (world.byId.get(id) as TradeRoute) : undefined;
}

/** Every historical route touching one settlement, in founding order. */
export function tradeRoutesOf(world: World, settlementId: EntityId): TradeRoute[] {
  return world.export.tradeRoutes.filter(
    (route) => route.settlementAId === settlementId || route.settlementBId === settlementId,
  );
}

export function figureOf(world: World, id: EntityId | undefined): Figure | undefined {
  return id && kindOf(id) === 'fig' ? (world.byId.get(id) as Figure) : undefined;
}

export function regionOf(world: World, id: EntityId | undefined): Region | undefined {
  return id && kindOf(id) === 'reg' ? (world.byId.get(id) as Region) : undefined;
}

export function cultureOf(world: World, id: EntityId | undefined): Culture | undefined {
  return id && kindOf(id) === 'cul' ? (world.byId.get(id) as Culture) : undefined;
}

export function dynastyOf(world: World, id: EntityId | undefined): Dynasty | undefined {
  return id && kindOf(id) === 'dyn' ? (world.byId.get(id) as Dynasty) : undefined;
}

export function warOf(world: World, id: EntityId | undefined): War | undefined {
  return id && kindOf(id) === 'war' ? (world.byId.get(id) as War) : undefined;
}

/** Every battle of one war, in the order they were fought. */
export function battlesOf(world: World, war: War): Battle[] {
  const found: Battle[] = [];

  for (const id of war.battleIds) {
    const battle = world.byId.get(id) as Battle | undefined;
    if (battle) found.push(battle);
  }

  return found;
}

export function religionOf(world: World, id: EntityId | undefined): Religion | undefined {
  return id && kindOf(id) === 'rel' ? (world.byId.get(id) as Religion) : undefined;
}

export function holySiteOf(world: World, id: EntityId | undefined): HolySite | undefined {
  return id && kindOf(id) === 'hol' ? (world.byId.get(id) as HolySite) : undefined;
}

export function artifactOf(world: World, id: EntityId | undefined): Artifact | undefined {
  return id && kindOf(id) === 'art' ? (world.byId.get(id) as Artifact) : undefined;
}

/** Every artifact one settlement holds now, in the order they were made. */
export function treasuresOf(world: World, settlementId: EntityId): Artifact[] {
  return world.export.artifacts.filter((artifact) => artifact.holderId === settlementId);
}

/** Resolves a list of figure ids, dropping any that do not. */
export function figures(world: World, ids: EntityId[] | undefined): Figure[] {
  if (!ids) return [];

  const found: Figure[] = [];
  for (const id of ids) {
    const figure = figureOf(world, id);
    if (figure) found.push(figure);
  }

  return found;
}
