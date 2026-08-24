/**
 * Mirrors HistoryEngine.Serialization.WorldExport.
 *
 * Hand-written rather than generated, for now. If the two drift, the viewer breaks
 * loudly on load (SCHEMA_VERSION mismatch) rather than quietly misrendering — and
 * generating these from the C# model is a small job worth doing once the schema
 * stops moving.
 */

export const SCHEMA_VERSION = 33;

/**
 * Whether an event carries the history or merely records a life.
 *
 * Better than three quarters of a run's events are ordinary births, deaths, marriages
 * and consort appointments, which is why this exists: the wars and schisms were never
 * missing from the chronicle, they were outnumbered four to one.
 */
export type Significance = 'Notable' | 'Routine';

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
  series: Series[];
  indices: ExportIndices;
  narration: Record<string, string>;
}

/** How a measure should be read: a headcount, or a dial in [0, 1]. */
export type MeasureUnit = 'Count' | 'Fraction';

/**
 * One measure of one entity, sampled once a year.
 *
 * The entity records elsewhere in this file report where the run ended — a realm's population,
 * its weariness, the values it was last governed by. None of them can say whether it got there
 * by growing steadily or by being halved twice and clawing its way back, which is usually the
 * interesting half. These carry the shape.
 *
 * Self-describing on purpose: `group` says which measures belong on one set of charts and
 * `unit` says what the axis means, so the viewer can plot a measure it has never heard of.
 */
export interface Series {
  entity: EntityId;
  metric: string;
  /** Measures sharing a group are drawn together. Empty means it stands alone. */
  group: string;
  unit: MeasureUnit;
  /** The year `values` begins — not necessarily the year the world does. */
  fromYear: number;
  values: number[];
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

export type WorldKind = 'Planet' | 'Moon';

export const WORLD_KIND_LABELS: Record<WorldKind, string> = {
  Planet: 'Planet',
  Moon: 'Moon',
};

export type StarSpectralClass = 'M' | 'K' | 'G' | 'F';

export const STAR_CLASS_LABELS: Record<StarSpectralClass, string> = {
  M: 'M-type (red dwarf)',
  K: 'K-type (orange dwarf)',
  G: 'G-type (Sun-like)',
  F: 'F-type (yellow-white)',
};

export type CompanionRole = 'InnerRocky' | 'ShepherdGiant' | 'OuterIceGiant';

export const COMPANION_ROLE_LABELS: Record<CompanionRole, string> = {
  InnerRocky: 'Inner rocky',
  ShepherdGiant: 'Shepherd giant',
  OuterIceGiant: 'Outer ice giant',
};

export interface ExportCompanionPlanet {
  role: CompanionRole;
  semiMajorAxisAu: number;
  massEarth: number;
  radiusEarth: number;
  orbitalPeriodDays: number;
}

export interface ExportComet {
  index: number;
  perihelionAu: number;
  aphelionAu: number;
  eccentricity: number;
  inclinationDeg: number;
  argumentOfPeriapsisRad: number;
  orbitalPeriodDays: number;
  nucleusRadiusKm: number;
  massEarth: number;
}

export interface ExportSystemMoon {
  index: number;
  orbitalDistanceEarthRadii: number;
  massEarth: number;
  radiusEarth: number;
  dayLengthDays: number;
  habitable: boolean;
}

export interface ExportCosmologyCheck {
  label: string;
  passed: boolean;
  detail: string;
}

export type GalaxyMorphology = 'UnbarredSpiral' | 'BarredSpiral' | 'Elliptical';

export interface ExportGalacticLocation {
  galactocentricRadiusKpc: number;
  azimuthRad: number;
  heightPc: number;
  metallicityFeH: number;
  inSpiralArm: boolean;
  localStellarDensityRelativeToSolar: number;
  supernovaRateRelativeToSolar: number;
}

export interface ExportGalaxy {
  morphology: GalaxyMorphology;
  stellarMassSolar: number;
  diskScaleLengthKpc: number;
  thinDiskScaleHeightPc: number;
  bulgeToDiskMass: number;
  solarAnalogMetallicityFeH: number;
  metallicityGradientDexPerKpc: number;
  metallicityScatterDex: number;
  spiralArmCount: number;
  spiralPitchDeg: number;
  innerHabitableRadiusKpc: number;
  outerHabitableRadiusKpc: number;
  sersicIndex: number;
  axisRatio: number;
  metallicityReferenceRadiusKpc: number;
  location: ExportGalacticLocation;
  canHostIronCore: boolean;
  canHostOres: boolean;
}

/** Host-star and habitable-body physics derived from the seed, plus the host galaxy. */
export interface ExportCosmology {
  galaxy: ExportGalaxy;
  starClass: StarSpectralClass;
  starMassSolar: number;
  starRadiusSolar: number;
  luminositySolar: number;
  starLifespanGyr: number;
  habitableZoneInnerAu: number;
  habitableZoneOuterAu: number;
  orbitalDistanceAu: number;
  orbitalPeriodDays: number;
  worldMassEarth: number;
  worldRadiusEarth: number;
  surfaceGravityG: number;
  escapeVelocityKmS: number;
  bondAlbedo: number;
  greenhouseDeltaC: number;
  equilibriumTempK: number;
  surfaceTempK: number;
  parentGiantMassEarth?: number;
  moonOrbitalDistanceEarthRadii?: number;
  moonDayLengthDays?: number;
  rocheLimitEarthRadii?: number;
  snowLineAu: number;
  companions: ExportCompanionPlanet[];
  moons: ExportSystemMoon[];
  habitableMoonIndex?: number;
  comets: ExportComet[];
  isHabitable: boolean;
  checks: ExportCosmologyCheck[];
}

export interface ExportWorld {
  /** The world's own proper name: the planet, or the moon this history is set on. */
  name: string;
  kind: WorldKind;
  /**
   * How the world is spoken of — "The planet Borion", "The 3rd moon of Endor".
   * Unique to the seed; the seed itself stays on `meta` for reproduction.
   */
  designation: string;
  /** The planet a moon orbits. Absent when the world is itself a planet. */
  parentName?: string;
  /** 1-based index among the parent's moons. Absent for planets. */
  moonIndex?: number;
  /** Star-system physics for this habitable body. */
  cosmology: ExportCosmology;
  minX: number;
  minZ: number;
  width: number;
  height: number;
  regionSize: number;
  terrainStride: number;
  /**
   * Whether the east and west edges are the same meridian.
   *
   * Carried because it cannot be inferred and everything drawn from coordinates is wrong
   * without it: the simulation measures distance the short way round, so two towns either side
   * of the seam are neighbours, and a map that has not been told draws the link between them
   * clean across the world.
   */
  eastWestPeriodic: boolean;
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
 * How a recent past sat on a realm or a place at the end of the run.
 *
 * Weariness and grievance are separate on purpose and pull opposite ways: being beaten
 * exhausts, being humiliated angers. Grievance also fades far more slowly.
 */
export interface Fortunes {
  weariness: number;
  calamity: number;
  triumph: number;
  grievance: number;
}

/**
 * What a figure was before the chronicle started following them.
 *
 * Empty for anyone born into the record — a dynast's origin is their house, a consort's the
 * marriage that brought them in, and both are already recorded in more detail. It carries
 * information only for people an office raised out of the ordinary population, who would
 * otherwise have no life behind them at all.
 */
export type FigureOrigin = 'Unrecorded' | 'Soldiery' | 'Clergy' | 'Townsfolk' | 'Guild' | 'Merchant';

export const ORIGIN_LABELS: Record<FigureOrigin, string> = {
  Unrecorded: '',
  Soldiery: 'Risen from the ranks',
  Clergy: 'Risen through the temple',
  Townsfolk: 'Of the town',
  Guild: 'Risen through a guild',
  Merchant: 'Risen through a merchant house',
};

/**
 * How a recorded person spends their life.
 *
 * Empty (`None`) until majority. Raised notables arrive with the career their office
 * implies; children of a recorded household choose from their disposition.
 */
export type Occupation =
  | 'None'
  | 'Soldiery'
  | 'Clergy'
  | 'Townsfolk'
  | 'Guild'
  | 'Merchant'
  | 'Court'
  | 'Official'
  | 'Scribe';

export const OCCUPATION_LABELS: Record<Occupation, string> = {
  None: 'Not yet of age',
  Soldiery: 'Soldiery',
  Clergy: 'Clergy',
  Townsfolk: 'Of the town',
  Guild: 'Guild',
  Merchant: 'Merchant',
  Court: 'Court',
  Official: 'In office',
  Scribe: 'Scribe',
};

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
  /**
   * Follower at zero, rebel at one. How far they let their culture govern their
   * choices — occupation, and the decisions they make once in office.
   */
  independence: number;
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
  day: number;
  endYear?: number;
  endDay?: number;
  regionId: EntityId;
  settlementId?: EntityId;
  wasSiege: boolean;
  siegeOutcome: SiegeOutcome;
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

export type SiegeOutcome = 'NotSiege' | 'Ongoing' | 'Carried' | 'Relieved' | 'Lifted';

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
  character: FaithCharacter;
  peakSettlements: number;
  settlementIds: EntityId[];
}

export type DeityStructure = 'Monotheistic' | 'Polytheistic' | 'Pantheistic' | 'Animistic';
export type Afterlife = 'None' | 'Ancestral' | 'Judgement' | 'Rebirth' | 'Union';
export type SoulDoctrine = 'MortalBreath' | 'ImmortalSpark' | 'WorldSpirit' | 'Transmigrating';
export type AuthorityType = 'Hierarchical' | 'Decentralized' | 'Monastic';
export type ClergyAdmission = 'Open' | 'MaleOnly' | 'FemaleOnly' | 'Bloodline';
export type WealthPractice = 'Tithes' | 'Landed' | 'Mendicant';
export type DogmaEmphasis = 'Honour' | 'Mercy' | 'Purity' | 'Knowledge' | 'Dominion' | 'Hospitality';
export type PrayerCadence = 'Seasonal' | 'Weekly' | 'Daily';
export type DietaryRule = 'None' | 'Fasting' | 'TabooFlesh' | 'TabooIntoxicants';
export type DressCode = 'None' | 'Modest' | 'ClericalColour' | 'SacredMarks';
export type FestivalSeason = 'Spring' | 'Summer' | 'Autumn' | 'Winter';

export const DEITY_LABELS: Record<DeityStructure, string> = {
  Monotheistic: 'Monotheistic',
  Polytheistic: 'Polytheistic',
  Pantheistic: 'Pantheistic',
  Animistic: 'Animistic',
};

export const AFTERLIFE_LABELS: Record<Afterlife, string> = {
  None: 'No afterlife',
  Ancestral: 'Ancestral',
  Judgement: 'Judged',
  Rebirth: 'Rebirth',
  Union: 'Union with the divine',
};

export const SOUL_LABELS: Record<SoulDoctrine, string> = {
  MortalBreath: 'A mortal breath',
  ImmortalSpark: 'An immortal spark',
  WorldSpirit: 'A world-spirit',
  Transmigrating: 'Transmigrating',
};

export const AUTHORITY_LABELS: Record<AuthorityType, string> = {
  Hierarchical: 'Hierarchical',
  Decentralized: 'Decentralized',
  Monastic: 'Monastic',
};

export const CLERGY_LABELS: Record<ClergyAdmission, string> = {
  Open: 'Open',
  MaleOnly: 'Men only',
  FemaleOnly: 'Women only',
  Bloodline: 'Sacred bloodline',
};

export const WEALTH_LABELS: Record<WealthPractice, string> = {
  Tithes: 'Tithes',
  Landed: 'Landed',
  Mendicant: 'Mendicant',
};

export const DOGMA_LABELS: Record<DogmaEmphasis, string> = {
  Honour: 'Honour',
  Mercy: 'Mercy',
  Purity: 'Purity',
  Knowledge: 'Knowledge',
  Dominion: 'Dominion',
  Hospitality: 'Hospitality',
};

export const PRAYER_LABELS: Record<PrayerCadence, string> = {
  Seasonal: 'Seasonal',
  Weekly: 'Weekly',
  Daily: 'Daily',
};

export const DIET_LABELS: Record<DietaryRule, string> = {
  None: 'None',
  Fasting: 'Fasting',
  TabooFlesh: 'Taboo on flesh',
  TabooIntoxicants: 'Taboo on intoxicants',
};

export const DRESS_LABELS: Record<DressCode, string> = {
  None: 'None',
  Modest: 'Modest dress',
  ClericalColour: 'Clerical colour',
  SacredMarks: 'Sacred marks',
};

export const FESTIVAL_LABELS: Record<FestivalSeason, string> = {
  Spring: 'Spring',
  Summer: 'Summer',
  Autumn: 'Autumn',
  Winter: 'Winter',
};

export interface FaithCharacter {
  deity: DeityStructure;
  afterlife: Afterlife;
  soul: SoulDoctrine;
  authority: AuthorityType;
  clergy: ClergyAdmission;
  celibateClergy: boolean;
  wealth: WealthPractice;
  dogma: DogmaEmphasis;
  prayer: PrayerCadence;
  diet: DietaryRule;
  dress: DressCode;
  festival: FestivalSeason;
  fervour: number;
  zealotry: number;
  tolerance: number;
  schismProneness: number;
  syncretism: number;
}

export type HolySiteKind = 'Shrine' | 'Temple' | 'Church' | 'Monastery' | 'Sanctuary';

export const HOLY_SITE_LABELS: Record<HolySiteKind, string> = {
  Shrine: 'Shrine',
  Temple: 'Temple',
  Church: 'Church',
  Monastery: 'Monastery',
  Sanctuary: 'Sanctuary',
};

export type SacredTradition = 'Nordic' | 'Classical' | 'Steppe' | 'Forest';

export const SACRED_TRADITION_LABELS: Record<SacredTradition, string> = {
  Nordic: 'Nordic & elemental',
  Classical: 'Sun-drenched & classical',
  Steppe: 'Steppe & silk-road',
  Forest: 'Deep forest & river',
};

export type HolySiteDedicationKind =
  | 'God'
  | 'AncientGod'
  | 'NatureSpirit'
  | 'CosmicForce'
  | 'DivineConcept'
  | 'AncestralKing'
  | 'LivingKing'
  | 'Martyr'
  | 'Saint'
  | 'Sage';

export const HOLY_SITE_DEDICATION_LABELS: Record<HolySiteDedicationKind, string> = {
  God: 'God',
  AncientGod: 'Ancient god',
  NatureSpirit: 'Nature spirit',
  CosmicForce: 'Cosmic force',
  DivineConcept: 'Divine concept',
  AncestralKing: 'Ancestral ruler',
  LivingKing: 'Living ruler',
  Martyr: 'Martyr',
  Saint: 'Saint',
  Sage: 'Sage',
};

export type HolySiteScale = 'Small' | 'Medium' | 'Large';

/** How a holy place looks and what is done there, fixed at founding. */
export interface HolySiteDescription {
  tradition: SacredTradition;
  dedicationKind: HolySiteDedicationKind;
  dedication: string;
  style: string;
  atmosphere: string;
  scale: HolySiteScale;
  capacity: string;
  hasStatue: boolean;
  focalPoint: string;
  offering: string;
  dedicateeId?: EntityId;
}

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
  description: HolySiteDescription;
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
  | 'ArtifactHistory'
  | 'Cosmology'
  | 'Dedication'
  | 'RealmChronicle'
  | 'Itinerary';

export const TOME_CONTENT_LABELS: Record<TomeContentKind, string> = {
  Biography: 'Life',
  Campaign: 'Campaign account',
  ReligiousRite: 'Book of rites',
  ReligiousTeaching: 'Religious teaching',
  Annals: 'Local annals',
  ArtifactHistory: 'Artifact history',
  Cosmology: 'Account of the heavens',
  Dedication: 'Dedication',
  RealmChronicle: 'Chronicle of the realm',
  Itinerary: 'Itinerary',
};

export interface TomeSection {
  heading: string;
  text: string;
  /** People, places, wars and other entities named by the passage. */
  references: EntityId[];
  /** Year this passage was entered; later continuations keep earlier ones. */
  year?: number;
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

/** Where an artifact was, who claimed it, from a given year, and how it got there. */
export interface Provenance {
  year: number;
  /** Absent for the entry that records it being lost. */
  settlementId?: EntityId;
  /** Absent while it sat in a treasury, or once lost. */
  ownerId?: EntityId;
  how: string;
}

/**
 * A made thing, and everywhere it has been.
 *
 * Kept at a settlement and often claimed by a person, so it can be sacked with a town,
 * inherited with a throne, or given as a gift — which is what makes `provenance` a way of
 * reading both the map and a line of rulers.
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
  /** The settlement keeping it now. Absent once it is lost. */
  holderId?: EntityId;
  /** The person who claims it now. Absent in a treasury, or once lost. */
  ownerId?: EntityId;
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

/**
 * What a settlement's ground was chosen for.
 *
 * Recorded at founding and never revised: it is a fact about a decision, not about the present.
 * A harbour whose bay silted up over three centuries of chronicle was still founded for the
 * harbour, and that is what explains where it is.
 */
export type SiteCharacter =
  | 'Plain'
  | 'Riverside'
  | 'Confluence'
  | 'Estuary'
  | 'Harbour'
  | 'Coastal'
  | 'Pass'
  | 'Mine';

export const SITE_LABELS: Record<SiteCharacter, string> = {
  Plain: 'Open ground',
  Riverside: 'On the river',
  Confluence: 'At the meeting of two rivers',
  Estuary: 'Where the river meets the sea',
  Harbour: 'On sheltered water',
  Coastal: 'On the coast',
  Pass: 'Astride the pass',
  // A noun phrase rather than a place, like the Plain case in EntityPages: the others answer
  // "built here for" with where the town stands, and this one answers it with what it came for.
  Mine: 'The ore under it',
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
  /** What the ground was chosen for. */
  site: SiteCharacter;
  /**
   * What the last years left here, as of the last simulated year. The same four
   * decaying measures a realm carries; the year-by-year track is in the series.
   */
  fortunes: Fortunes;
  /**
   * What was feeding the place when the chronicle closed. Absent for abandoned settlements,
   * and for any export written before schema 15.
   */
  support?: Support;
}

/** Where the greater part of a settlement's living comes from. */
export type SupportSource = 'Land' | 'Trade' | 'Site';

/**
 * Carrying capacity, itemised — the answer to "why is this place this size".
 *
 * The three parts are in people and sum to `capacity`. `landShare` is how much of the
 * surrounding country the settlement keeps rather than ceding to a larger neighbour, so a
 * small number beside good ground is the whole explanation for a village that never grew.
 */
export interface Support {
  capacity: number;
  fromSite: number;
  fromLand: number;
  fromTrade: number;
  /** 0–1. One means nothing else is near enough to compete for the fields. */
  landShare: number;
  /** Summed live traffic of every trade route reaching the settlement. */
  routeTraffic: number;
  principal: SupportSource;
}

/** The transport corridor a route relies on. Coastal routes are sailed and carry no road. */
export type TradeRouteMode = 'Overland' | 'River' | 'Coastal';

export type TradeRouteStatus = 'Active' | 'Prosperous' | 'Declining' | 'Closed';

/** A worn way, or one engineered with cuttings and bridges. */
export type RoadGrade = 'Track' | 'Paved';

/**
 * The physical way a route takes over the ground.
 *
 * `points` is a flat `[x, z, x, z, …]` run from one settlement to the other, with a vertex only
 * where the way turns — so a road over open country has two points and one threading a range has
 * a dozen. `length` is measured along it and therefore exceeds the straight-line distance by what
 * the ground cost.
 *
 * Present only on land routes whose traffic earned one, which is a minority of the network.
 */
export interface Road {
  grade: RoadGrade;
  /** The year the first way was cut. Survives an upgrade, so it is what a replay reads. */
  builtYear: number;
  /** The year the way was bridged and paved, if it ever was. */
  pavedYear?: number;
  length: number;
  points: number[];
}

/** A durable commercial link, and the road under it once traffic paid for one. */
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
  /** The road, if this link ever earned one. Absent on most routes and on every coastal one. */
  road?: Road;
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

/**
 * The offices a figure can hold.
 *
 * Branch on this, never on `Title.title`. The title text is culture flavour — one realm's
 * Marshal is another's Strategos — and identifying a reign by comparing it worked only
 * while the crown and the regency were the only offices in existence.
 */
export type OfficeKind =
  | 'Ruler'
  | 'Regent'
  | 'Consort'
  | 'Marshal'
  | 'HighPriest'
  | 'Governor';

export const OFFICE_LABELS: Record<OfficeKind, string> = {
  Ruler: 'Ruler',
  Regent: 'Regent',
  Consort: 'Consort',
  Marshal: 'Marshal',
  HighPriest: 'High priest',
  Governor: 'Governor',
};

export interface Title {
  kind: OfficeKind;
  title: string;
  civilizationId: EntityId;
  fromYear: number;
  toYear?: number;
  /** The settlement or faith held over, where the office is over one. */
  scopeId?: EntityId;
  /** Whoever granted it. Absent when the body chose its own. */
  grantedBy?: EntityId;
  /** How they came by it, in prose: "by the king's mandate". */
  claim?: string;
}

export type CampaignRole = 'Commanded' | 'Fought' | 'Ruled' | 'EnduredSiege';

export const CAMPAIGN_ROLE_LABELS: Record<CampaignRole, string> = {
  Commanded: 'Commanded',
  Fought: 'Took the field',
  Ruled: 'Led the realm',
  EnduredSiege: 'Endured the siege',
};

export interface Campaign {
  warId: EntityId;
  /** Absent when they led the realm through a war rather than standing in a battle. */
  battleId?: EntityId;
  /** The realm they stood with. */
  sideId: EntityId;
  year: number;
  role: CampaignRole;
  /** Absent while the war or siege is still open, and after a stalemate. */
  triumphant?: boolean;
}

export type JourneyKind = 'Visit' | 'Trade' | 'Pilgrimage' | 'Mission';

export const JOURNEY_KIND_LABELS: Record<JourneyKind, string> = {
  Visit: 'Visit',
  Trade: 'Trade',
  Pilgrimage: 'Pilgrimage',
  Mission: 'Mission',
};

/** How a journey ended. Most end the dull way; the other two are why the road is worth drawing. */
export type JourneyOutcome = 'Returned' | 'Waylaid' | 'Lost';

export const JOURNEY_OUTCOME_LABELS: Record<JourneyOutcome, string> = {
  Returned: '',
  Waylaid: 'waylaid',
  Lost: 'never returned',
};

export interface Journey {
  kind: JourneyKind;
  year: number;
  fromSettlementId: EntityId;
  toSettlementId: EntityId;
  /** The route, holy site or host realm that made the journey make sense. */
  viaId?: EntityId;
  /** Absent on worlds exported before schema 30, where every journey ended well. */
  outcome?: JourneyOutcome;
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
  /** The faith they hold. Absent if none has reached them. Distinct from their town's. */
  religionId?: EntityId;
  /** The house this figure belongs to by blood. Absent for someone married in from outside. */
  dynastyId?: EntityId;
  birthYear: number;
  deathYear?: number;
  deathCause: DeathCause;
  /** Specific form of the cause when known, such as a named plague or a flood. */
  deathDetail?: string;
  birthSettlementId?: EntityId;
  /**
   * Where they actually live, when that is finer than a realm. Set for a governor, who
   * lives in the town they govern — which is what exposes them to what happens there.
   */
  residenceSettlementId?: EntityId;
  /** What they were before the record began following them. See ORIGIN_LABELS. */
  origin: FigureOrigin;
  /** How they spend their life, once of age. See OCCUPATION_LABELS. */
  occupation: Occupation;
  disposition: Disposition;
  titles: Title[];
  /** Wars and engagements they stood in, in the order they were recorded. */
  campaigns: Campaign[];
  /** Trips they made and returned from. Distinct from where they live. */
  journeys: Journey[];
  motherId?: EntityId;
  fatherId?: EntityId;
  childIds: EntityId[];
  /** Every marriage in order. A widowed figure who remarried keeps both. */
  spouseIds: EntityId[];
}

export interface HistoryEvent {
  id: number;
  year: number;
  /**
   * Day within the year. Zero for everything an annual system records, which is
   * currently everything — read it only where a finer date would actually be shown,
   * and keep filtering and indexing on `year`, which is what they have always used.
   */
  day: number;
  kind: string;
  /**
   * Whether this belongs to the narrative spine or to the vital register.
   *
   * The chronicle hides `Routine` by default and entity pages never do: a person's
   * own page is built from the events that mention them, so suppressing their birth
   * there would leave someone who appears in the world fully grown. It is a display
   * decision about the world's history, not a claim that the fact is unimportant.
   */
  significance: Significance;
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
