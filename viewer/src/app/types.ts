/**
 * Mirrors HistoryEngine.Serialization.WorldExport.
 *
 * Hand-written rather than generated, for now. If the two drift, the viewer breaks
 * loudly on load (SCHEMA_VERSION mismatch) rather than quietly misrendering — and
 * generating these from the C# model is a small job worth doing once the schema
 * stops moving.
 */

export const SCHEMA_VERSION = 2;

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
  dynasties: Dynasty[];
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

/** How a house decides who inherits. Each is a different walk of the same family tree. */
export type SuccessionLaw =
  | 'Agnatic'
  | 'MalePreference'
  | 'Absolute'
  | 'Seniority'
  | 'Elective';

export const SUCCESSION_LABELS: Record<SuccessionLaw, string> = {
  Agnatic: 'Male line only',
  MalePreference: 'Male-preference primogeniture',
  Absolute: 'Primogeniture',
  Seniority: 'Seniority — eldest of the house',
  Elective: 'Election',
};

export interface Culture {
  id: EntityId;
  name: string;
  government: GovernmentForm;
  rulerTitle: string;
  successionLaw: SuccessionLaw;
  /** Years a ruler serves before standing down, or 0 if the office is held for life. */
  termYears: number;
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
  rulingDynastyId?: EntityId;
  /** Set only while a minor holds the throne. */
  regentId?: EntityId;
  rulerSinceYear: number;
  population: number;
  peakPopulation: number;
  rulerIds: EntityId[];
  settlementIds: EntityId[];
  territoryRegionIds: EntityId[];
}

/**
 * A ruling house.
 *
 * `memberIds` is blood only — consorts keep whatever house they were born into and are
 * reachable through their spouses. Without that distinction a house can never die out,
 * and dying out is the most interesting thing a house can do.
 */
export interface Dynasty {
  id: EntityId;
  name: string;
  cultureId: EntityId;
  foundedYear: number;
  endedYear?: number;
  founderId: EntityId;
  originCivilizationId?: EntityId;
  rulerIds: EntityId[];
  memberIds: EntityId[];
}

export type SettlementTier = 'Hamlet' | 'Village' | 'Town' | 'City';

export const TIER_ORDER: SettlementTier[] = ['Hamlet', 'Village', 'Town', 'City'];

/** What a settlement is chiefly known for. `None` means it is still a hamlet. */
export type SettlementSpecialization =
  | 'None'
  | 'Farming'
  | 'Pastoral'
  | 'Fishing'
  | 'Mining'
  | 'Trade'
  | 'Crafts'
  | 'Shrine';

export const SPECIALIZATION_LABELS: Record<SettlementSpecialization, string> = {
  None: '—',
  Farming: 'Farming',
  Pastoral: 'Herding',
  Fishing: 'Fishing',
  Mining: 'Mining',
  Trade: 'Trade',
  Crafts: 'Craftwork',
  Shrine: 'Pilgrimage',
};

export interface Settlement {
  id: EntityId;
  name: string;
  civilizationId: EntityId;
  foundedBy?: EntityId;
  regionId: EntityId;
  x: number;
  z: number;
  tier: SettlementTier;
  specialization: SettlementSpecialization;
  specializedYear?: number;
  population: number;
  peakPopulation: number;
  foundedYear: number;
  abandonedYear?: number;
  /** Consecutive years below half the peak population. A dying settlement. */
  yearsDepressed: number;
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
  | 'Execution'
  | 'Childbirth';

export const DEATH_LABELS: Record<DeathCause, string> = {
  Unknown: 'unknown causes',
  OldAge: 'old age',
  Illness: 'illness',
  Battle: 'wounds taken in battle',
  Assassination: 'assassination',
  Accident: 'misadventure',
  Execution: 'execution',
  Childbirth: 'childbed',
};

export type Sex = 'Female' | 'Male';

export interface Title {
  title: string;
  civilizationId: EntityId;
  fromYear: number;
  toYear?: number;
}

/**
 * One person, with enough of the family tree attached to draw it.
 *
 * `name` is the styled name the chronicle uses, regnal numeral included — the engine
 * numbers rulers who share a name with a predecessor in the same realm, because at this
 * milestone's figure counts a line of succession is otherwise unreadable.
 */
export interface Figure {
  id: EntityId;
  name: string;
  sex: Sex;
  civilizationId: EntityId;
  cultureId: EntityId;
  /** The house this figure belongs to by blood. Absent for someone married in from outside. */
  dynastyId?: EntityId;
  birthYear: number;
  deathYear?: number;
  deathCause: DeathCause;
  birthSettlementId?: EntityId;
  titles: Title[];
  motherId?: EntityId;
  fatherId?: EntityId;
  childIds: EntityId[];
  /** Every marriage in order. A widowed figure who remarried keeps both. */
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

export type AnyEntity = Region | Culture | Civilization | Dynasty | Settlement | Figure;

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
