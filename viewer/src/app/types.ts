/**
 * Mirrors HistoryEngine.Serialization.WorldExport.
 *
 * Hand-written rather than generated, for now. If the two drift, the viewer breaks
 * loudly on load (SCHEMA_VERSION mismatch) rather than quietly misrendering — and
 * generating these from the C# model is a small job worth doing once the schema
 * stops moving.
 */

export const SCHEMA_VERSION = 9;

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
  | 'rel'
  | 'rte'
  | 'hol';

export interface WorldExport {
  schemaVersion: number;
  meta: ExportMeta;
  world: ExportWorld;
  regions: Region[];
  cultures: Culture[];
  civilizations: Civilization[];
  dynasties: Dynasty[];
  settlements: Settlement[];
  tradeRoutes: TradeRoute[];
  figures: Figure[];
  wars: War[];
  battles: Battle[];
  religions: Religion[];
  holySites: HolySite[];
  artifacts: Artifact[];
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
  learning: number;
  lexicon: Lexicon;
}

/** The six dials, as a culture holds them or as a realm is governed by them. */
export interface Values {
  aggression: number;
  expansionism: number;
  piety: number;
  tradition: number;
  mercantile: number;
  learning: number;
}

/**
 * How a realm's recent past sat on it at the end of the run.
 *
 * Weariness and grievance are separate on purpose and pull opposite ways: being beaten
 * exhausts a realm, being humiliated angers it. Grievance also fades far more slowly.
 */
export interface Fortunes {
  weariness: number;
  calamity: number;
  triumph: number;
  grievance: number;
}

/**
 * One person's own inclinations, on the same dials their culture has.
 *
 * Present on every figure, not only those who governed — the brother who would have been a
 * very different king is exactly what a reader of a family tree wants to be able to see.
 */
export interface Disposition {
  aggression: number;
  expansionism: number;
  piety: number;
  tradition: number;
  mercantile: number;
  learning: number;
  /** How much this person insists on deciding things themselves. */
  centralism: number;
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
  /** The realm's faith: whatever its seat of government follows. */
  stateReligionId?: EntityId;
  rulerSinceYear: number;
  population: number;
  peakPopulation: number;
  /** What the realm had lately been through, as of the last simulated year. */
  fortunes: Fortunes;
  /**
   * The dials it was actually governed by in that year: its culture moved toward whoever
   * governed, then shifted by its recent past. Compare against its culture's own values —
   * the gap is the reign.
   */
  effectiveValues: Values;
  rulerIds: EntityId[];
  settlementIds: EntityId[];
  territoryRegionIds: EntityId[];
  relations: Relation[];
  allies: Alliance[];
}

/**
 * One realm's standing opinion of another, in [-1, 1].
 *
 * Directed: this is what *this* realm thinks, and the other side's entry usually differs.
 * A peace costs the beaten realm far more goodwill than the realm that beat it, and that
 * difference is what sends a loser back for its province a generation later.
 */
export interface Relation {
  civilizationId: EntityId;
  opinion: number;
  /** Set while a peace treaty still forbids war between the two. */
  truceUntilYear?: number;
}

export interface Alliance {
  civilizationId: EntityId;
  sinceYear: number;
}

/** Why a war was declared. Each one is reached by a different route in the engine. */
export type CasusBelli =
  | 'Unknown'
  | 'BorderDispute'
  | 'Conquest'
  | 'DynasticClaim'
  | 'Revanche'
  | 'RelicClaim'
  | 'ReligiousWar';

export const CAUSE_LABELS: Record<CasusBelli, string> = {
  Unknown: 'No stated cause',
  BorderDispute: 'Border dispute',
  Conquest: 'Conquest',
  DynasticClaim: 'Dynastic claim',
  Revanche: 'Revanche',
  RelicClaim: 'Relic claim',
  ReligiousWar: 'Religious war',
};

export type WarOutcome =
  | 'Ongoing'
  | 'AggressorVictory'
  | 'DefenderVictory'
  | 'Stalemate';

export const OUTCOME_LABELS: Record<WarOutcome, string> = {
  Ongoing: 'Still being fought',
  AggressorVictory: 'Won by the aggressor',
  DefenderVictory: 'Won by the defender',
  Stalemate: 'Fought to exhaustion',
};

/**
 * A war, its coalitions, and what it cost.
 *
 * `name` is composed by the engine from the places and houses it was fought over —
 * "Second War of Bergajarvi", "War of the Lykos Succession" — rather than drawn from a
 * naming language, because nobody names a war in advance.
 */
export interface War {
  id: EntityId;
  name: string;
  cause: CasusBelli;
  /** The particular sacred object sought in a relic claim. */
  claimedRelicId?: EntityId;
  /** The two state faiths when a religious war was declared. */
  aggressorReligionId?: EntityId;
  defenderReligionId?: EntityId;
  outcome: WarOutcome;
  startYear: number;
  endYear?: number;
  aggressorId: EntityId;
  defenderId: EntityId;
  /** Principal first, then whoever answered the call to arms. */
  attackers: EntityId[];
  defenders: EntityId[];
  battleIds: EntityId[];
  cededRegionIds: EntityId[];
  attackerLosses: number;
  defenderLosses: number;
}

/**
 * One engagement, named for where it was fought.
 *
 * A siege is a battle with `wasSiege` set, not a kind of its own. `settlementId` is
 * present whenever a settlement stood on the ground, siege or not — a field battle
 * outside an unwalled village is still a battle for that village.
 */
export interface Battle {
  id: EntityId;
  name: string;
  warId: EntityId;
  year: number;
  regionId: EntityId;
  settlementId?: EntityId;
  wasSiege: boolean;
  attackerId: EntityId;
  defenderId: EntityId;
  victorId: EntityId;
  /** The ruler who led in person, absent if the army went without them. */
  attackerCommanderId?: EntityId;
  defenderCommanderId?: EntityId;
  attackerStrength: number;
  defenderStrength: number;
  attackerLosses: number;
  defenderLosses: number;
  sacked: boolean;
}

/**
 * A faith, and the settlements that follow it.
 *
 * `settlementIds` is the congregation at the end of the run, so a faith that once held a
 * continent and now holds one valley lists one valley — `peakSettlements` is what says so.
 * Its whole rise and fall replays from the adoption events, the same way territory does.
 */
export interface Religion {
  id: EntityId;
  name: string;
  cultureId: EntityId;
  /** Whoever preached it first, if the chronicle knows. */
  founderId?: EntityId;
  originSettlementId: EntityId;
  /** The faith it broke away from, if it did. */
  parentId?: EntityId;
  foundedYear: number;
  endedYear?: number;
  /** How hard it presses outwards, in [0, 1]. */
  fervour: number;
  peakSettlements: number;
  settlementIds: EntityId[];
}

export type HolySiteKind = 'Shrine' | 'Temple' | 'Church' | 'Monastery' | 'Sanctuary';

export const HOLY_SITE_LABELS: Record<HolySiteKind, string> = {
  Shrine: 'Shrine',
  Temple: 'Temple',
  Church: 'Church',
  Monastery: 'Monastery',
  Sanctuary: 'Sanctuary',
};

/** A place of worship, either within a settlement or at an independent map coordinate. */
export interface HolySite {
  id: EntityId;
  name: string;
  kind: HolySiteKind;
  religionId: EntityId;
  regionId: EntityId;
  /** Present only when the site stands inside this settlement. */
  settlementId?: EntityId;
  x: number;
  z: number;
  foundedYear: number;
}

export type ArtifactKind = 'Regalia' | 'Weapon' | 'Relic' | 'Tome' | 'Idol' | 'Jewel';

export const ARTIFACT_LABELS: Record<ArtifactKind, string> = {
  Regalia: 'Regalia',
  Weapon: 'Weapon',
  Relic: 'Relic',
  Tome: 'Book',
  Idol: 'Idol',
  Jewel: 'Jewel',
};

export type TomeContentKind =
  | 'Biography'
  | 'Campaign'
  | 'ReligiousRite'
  | 'ReligiousTeaching'
  | 'Annals'
  | 'ArtifactHistory';

export const TOME_CONTENT_LABELS: Record<TomeContentKind, string> = {
  Biography: 'Life',
  Campaign: 'Campaign account',
  ReligiousRite: 'Book of rites',
  ReligiousTeaching: 'Religious teaching',
  Annals: 'Local annals',
  ArtifactHistory: 'Artifact history',
};

export interface TomeSection {
  heading: string;
  text: string;
  /** People, places, wars and other entities named by the passage. */
  references: EntityId[];
}

/** A settlement copy made from an exemplar already circulating elsewhere. */
export interface TomeCopy {
  year: number;
  settlementId: EntityId;
  sourceSettlementId: EntityId;
}

/** Contents fixed when the tome was made; later history never rewrites them. */
export interface TomeContents {
  kind: TomeContentKind;
  subjectId: EntityId;
  /** The war for a campaign account. */
  contextId?: EntityId;
  /** Maximum additional settlement copies chosen when the work was written. */
  copyLimit?: number;
  /** Historical copying records; absent in exports made before circulation was modelled. */
  copies?: TomeCopy[];
  sections: TomeSection[];
}

/** Where an artifact was, from a given year, and how it got there. */
export interface Provenance {
  year: number;
  /** Absent for the entry that records it being lost. */
  settlementId?: EntityId;
  how: string;
}

/**
 * A made thing, and everywhere it has been.
 *
 * Held by settlements rather than people, so it can be sacked, abandoned or ceded along with
 * the place holding it — which is what makes `provenance` a way of reading the map's history
 * rather than an inventory line.
 */
export interface Artifact {
  id: EntityId;
  name: string;
  kind: ArtifactKind;
  creatorId?: EntityId;
  originSettlementId: EntityId;
  /** The faith it is sacred to, for relics and idols. */
  religionId?: EntityId;
  /** Present only for books, codices, chronicles and testaments. */
  tomeContents?: TomeContents;
  createdYear: number;
  /** The settlement holding it now. Absent once it is lost. */
  holderId?: EntityId;
  lostYear?: number;
  provenance: Provenance[];
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
  /** The faith followed here, or absent while the place keeps its own counsel. */
  religionId?: EntityId;
  convertedYear?: number;
}

/** The transport corridor a logical route is most likely to use. It is not path geometry. */
export type TradeRouteMode = 'Overland' | 'River' | 'Coastal';

export type TradeRouteStatus = 'Active' | 'Prosperous' | 'Declining' | 'Closed';

/** A durable commercial link; later roads can realize its overland path. */
export interface TradeRoute {
  id: EntityId;
  settlementAId: EntityId;
  settlementBId: EntityId;
  mode: TradeRouteMode;
  status: TradeRouteStatus;
  foundedYear: number;
  endedYear?: number;
  /** Current traffic in [0, 1]. Closed routes have zero current traffic. */
  traffic: number;
  /** Highest traffic sustained during the route's life. */
  peakTraffic: number;
}

export type DeathCause =
  | 'Unknown'
  | 'OldAge'
  | 'Illness'
  | 'Battle'
  | 'Assassination'
  | 'Accident'
  | 'Execution'
  | 'Childbirth'
  | 'Plague'
  | 'Disaster'
  | 'Poisoning';

export const DEATH_LABELS: Record<DeathCause, string> = {
  Unknown: 'unknown causes',
  OldAge: 'old age',
  Illness: 'illness',
  Battle: 'wounds taken in battle',
  Assassination: 'assassination',
  Accident: 'misadventure',
  Execution: 'execution',
  Childbirth: 'childbed',
  Plague: 'plague',
  Disaster: 'a disaster',
  Poisoning: 'poisoning',
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
  /** Specific form of the cause when known, such as a named plague or a flood. */
  deathDetail?: string;
  birthSettlementId?: EntityId;
  disposition: Disposition;
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

export type AnyEntity =
  | Region
  | Culture
  | Civilization
  | Dynasty
  | Settlement
  | Figure
  | War
  | Battle
  | Religion
  | HolySite
  | Artifact
  | TradeRoute;

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
  rte: 'Trade route',
  hol: 'Holy site',
};
