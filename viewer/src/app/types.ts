/**
 * Mirrors HistoryEngine.Serialization.WorldExport.
 *
 * Hand-written rather than generated, for now. If the two drift, the viewer breaks
 * loudly on load (SCHEMA_VERSION mismatch) rather than quietly misrendering — and
 * generating these from the C# model is a small job worth doing once the schema
 * stops moving.
 */

export const SCHEMA_VERSION = 1;

/** `"civ:3"`, `"fig:1204"` — readable, greppable, and directly usable as a route. */
export type EntityId = string;

export type EntityKind =
  | 'cul'
  | 'civ'
  | 'set'
  | 'fig'
  | 'dyn'
  | 'war'
  | 'bat'
  | 'reg'
  | 'art'
  | 'rel';

export interface WorldExport {
  schemaVersion: number;
  meta: ExportMeta;
  world: ExportWorld;
  regions: Region[];
  cultures: Culture[];
  civilizations: Civilization[];
  settlements: Settlement[];
  figures: Figure[];
  events: HistoryEvent[];
  indices: ExportIndices;
  narration: Record<string, string>;
}

export interface ExportMeta {
  seed: number;
  configHash: string;
  systemOrderHash: string;
  systemOrder: string[];
  engineVersion: string;
  narrationSyntaxVersion: number;
  startYear: number;
  endYear: number;
  yearsSimulated: number;
  eventCount: number;
  terrainSampling: {
    simulationSamples: number;
    rasterSamples: number;
    estimatedGameSecondsSimulation: number;
    estimatedGameSecondsRaster: number;
  };
}

export interface ExportWorld {
  minX: number;
  minZ: number;
  width: number;
  height: number;
  regionSize: number;
  terrainStride: number;
  capabilities: string;
  raster: ExportRaster;
  rivers: ExportRiver[];
}

/**
 * One reach of a river, in world coordinates, with normalised drainage for line width.
 *
 * Vectors rather than a raster plane: at the terrain lattice's 256-unit stride a
 * per-cell river flag paints a block that reads as a lake, not a watercourse.
 */
export interface ExportRiver {
  x1: number;
  z1: number;
  x2: number;
  z2: number;
  strength: number;
}

/**
 * Raw byte planes, base64-encoded — not an image.
 *
 * The engine deliberately ships unpainted data so the viewer owns the colour ramp
 * and can theme it, which a baked PNG would prevent.
 */
export interface ExportRaster {
  resolution: number;
  minHeight: number;
  maxHeight: number;
  height: string;
  biome: string;
  flags: string;
}

export const FLAG_RIVER = 1;
export const FLAG_COAST = 2;

export type Biome =
  | 'Ocean'
  | 'Lake'
  | 'Glacier'
  | 'Tundra'
  | 'Taiga'
  | 'TemperateForest'
  | 'Grassland'
  | 'Steppe'
  | 'Desert'
  | 'Savanna'
  | 'TropicalForest'
  | 'Wetland'
  | 'Alpine';

/** Index in this array is the numeric Biome value in the raster's biome plane. */
export const BIOME_ORDER: Biome[] = [
  'Ocean',
  'Lake',
  'Glacier',
  'Tundra',
  'Taiga',
  'TemperateForest',
  'Grassland',
  'Steppe',
  'Desert',
  'Savanna',
  'TropicalForest',
  'Wetland',
  'Alpine',
];

export interface Region {
  id: EntityId;
  name: string;
  minX: number;
  minZ: number;
  width: number;
  height: number;
  biome: Biome;
  fertility: number;
  habitability: number;
  meanHeight: number;
  isLand: boolean;
  hasRiver: boolean;
  isCoastal: boolean;
  owner?: EntityId;
  adjacent: EntityId[];
}

export type GovernmentForm =
  | 'Chiefdom'
  | 'Monarchy'
  | 'Theocracy'
  | 'Oligarchy'
  | 'Republic';

export interface Culture {
  id: EntityId;
  name: string;
  government: GovernmentForm;
  rulerTitle: string;
  aggression: number;
  expansionism: number;
  piety: number;
  tradition: number;
  mercantile: number;
  lexicon: Lexicon;
}

/**
 * A culture's naming language, as described by the engine.
 *
 * The engine ships the corpus blend, sound shifts, and a few example names rather than
 * the trained Markov tables — what answers "why do this culture's names look like
 * that" is the recipe, not the weights.
 */
export interface Lexicon {
  sources: { family: string; weight: number }[];
  soundShifts: string[];
  sampleNames: string[];
  samplePlaces: string[];
}

export interface Civilization {
  id: EntityId;
  name: string;
  cultureId: EntityId;
  foundedYear: number;
  endedYear?: number;
  capitalId?: EntityId;
  currentRulerId?: EntityId;
  population: number;
  peakPopulation: number;
  rulerIds: EntityId[];
  settlementIds: EntityId[];
  territoryRegionIds: EntityId[];
}

export type SettlementTier = 'Hamlet' | 'Village' | 'Town' | 'City';

export const TIER_ORDER: SettlementTier[] = ['Hamlet', 'Village', 'Town', 'City'];

export interface Settlement {
  id: EntityId;
  name: string;
  civilizationId: EntityId;
  foundedBy?: EntityId;
  regionId: EntityId;
  x: number;
  z: number;
  tier: SettlementTier;
  population: number;
  peakPopulation: number;
  foundedYear: number;
  abandonedYear?: number;
  isCapital: boolean;
  isFortified: boolean;
}

export type DeathCause =
  | 'Unknown'
  | 'OldAge'
  | 'Illness'
  | 'Battle'
  | 'Assassination'
  | 'Accident'
  | 'Execution';

export interface Title {
  title: string;
  civilizationId: EntityId;
  fromYear: number;
  toYear?: number;
}

export interface Figure {
  id: EntityId;
  name: string;
  civilizationId: EntityId;
  cultureId: EntityId;
  birthYear: number;
  deathYear?: number;
  deathCause: DeathCause;
  birthSettlementId?: EntityId;
  titles: Title[];
  parentIds: EntityId[];
  spouseIds: EntityId[];
}

export interface HistoryEvent {
  id: number;
  year: number;
  kind: string;
  subject?: EntityId;
  object?: EntityId;
  location?: EntityId;
  extra?: EntityId[];
  data?: Record<string, string>;
}

/**
 * Denormalised lookups computed by the engine.
 *
 * Values are indices into `events`. Without these, every entity page would scan
 * the whole event list on each navigation — fine at a thousand events, visibly
 * slow at the fifty thousand this is built for.
 */
export interface ExportIndices {
  eventsByEntity: Record<EntityId, number[]>;
  eventsByYear: Record<string, number[]>;
  eventCountsByKind: Record<string, number>;
}

export type AnyEntity = Region | Culture | Civilization | Settlement | Figure;

export function kindOf(id: EntityId): EntityKind {
  return id.slice(0, id.indexOf(':')) as EntityKind;
}

export const KIND_LABELS: Record<string, string> = {
  cul: 'Culture',
  civ: 'Civilization',
  set: 'Settlement',
  fig: 'Figure',
  dyn: 'Dynasty',
  war: 'War',
  bat: 'Battle',
  reg: 'Region',
  art: 'Artifact',
  rel: 'Religion',
};
