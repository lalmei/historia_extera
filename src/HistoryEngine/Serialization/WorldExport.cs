using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.World;

namespace HistoryEngine.Serialization;

/// <summary>
/// The engine/viewer contract: one self-contained JSON document describing a whole history.
/// </summary>
/// <remarks>
/// <para><b>This file is the interface between the two halves of the project.</b> The engine
/// writes it and never reads it back; the viewer reads it and never writes it. Nothing else
/// crosses the boundary — no shared code, no server, no schema negotiation — which is what lets
/// a .NET engine and a TypeScript viewer evolve independently.</para>
///
/// <para><b>No timestamp.</b> There is deliberately no "generated at" field. The export is a
/// pure function of seed and config, so two runs of the same inputs produce byte-identical
/// files — which is exactly what the golden-hash determinism test asserts. A timestamp would
/// make that test impossible to write, and provenance is already covered by
/// <see cref="ExportMeta.Seed"/> and <see cref="ExportMeta.ConfigHash"/>.</para>
///
/// <para><b>Property declaration order is the file's byte layout</b> — see <see cref="Json"/>.</para>
/// </remarks>
public sealed record WorldExport(
    int SchemaVersion,
    ExportMeta Meta,
    ExportWorld World,
    IReadOnlyList<ExportRegion> Regions,
    IReadOnlyList<ExportCulture> Cultures,
    IReadOnlyList<ExportCivilization> Civilizations,
    IReadOnlyList<ExportDynasty> Dynasties,
    IReadOnlyList<ExportSettlement> Settlements,
    IReadOnlyList<ExportTradeRoute> TradeRoutes,
    IReadOnlyList<ExportFigure> Figures,
    IReadOnlyList<ExportWar> Wars,
    IReadOnlyList<ExportBattle> Battles,
    IReadOnlyList<ExportReligion> Religions,
    IReadOnlyList<ExportHolySite> HolySites,
    IReadOnlyList<ExportArtifact> Artifacts,
    IReadOnlyList<ExportEvent> Events,
    IReadOnlyList<ExportSeries> Series,
    ExportIndices Indices,
    IReadOnlyDictionary<string, string> Narration)
{
    /// <summary>
    /// Bumped on any breaking change to this shape. The viewer checks it and refuses politely
    /// rather than misrendering a file it does not understand.
    /// </summary>
    /// <remarks>
    /// Version 2 added dynasties and the family links on a figure, and replaced the figure's
    /// two-element parent list with named mother and father. Version 3 added wars and battles,
    /// and the relations, alliances and truces on a civilization. Version 5 added the contents and
    /// circulation of a tome, and the particular relic and two faiths named by the new religious
    /// causes of war. Version 6 added persistent trade routes. Version 7 added holy sites as
    /// independent map entities. Version 8 added the exact detail behind a figure's categorical
    /// cause of death. Version 9 added the reign-aware layer: a culture's Learning dial, every
    /// figure's own disposition, and the fortunes and effective values a realm is governed by.
    /// Version 10 added the yearly series: every measure that moves, sampled once a year, so the
    /// viewer can plot what the snapshot fields can only report the end of. Version 11 tells the
    /// viewer whether the world's east and west edges are the same meridian, which it cannot infer
    /// and draws wrong without. Version 12 records what each settlement's ground was chosen for —
    /// a confluence, a river mouth, a sheltered harbour, a pass — which the viewer can only
    /// otherwise guess at by reading coordinates against the map. Version 13 added the composed
    /// description of a holy site: tradition, dedication, fabric, atmosphere, scale, focal point
    /// and offering, written once when the place was founded. Version 14 added a faith's character:
    /// deity structure, cosmology, church, clergy, observance, and the dials besides fervour.
    /// Version 15 added what feeds each standing settlement — its capacity itemised into the site,
    /// its share of the surrounding fields, and what the roads bring — so a reader can tell why a
    /// place is the size it is rather than only how large it is. Version 16 added a figure's own
    /// faith, distinct from the town they live in, so a person can follow a church their
    /// residence no longer does. Version 17 added the day an event fell on, alongside the year it
    /// has always carried — additive on purpose, so the per-year index, the timeline slider and the
    /// territory replay all keep reading exactly what they read before. Version 18 records when a
    /// battle began and ended and how a siege ended, so an investment that lasted into another
    /// season is not flattened back into an instantaneous victory at export. Version 19 marks each
    /// event as narrative spine or vital register, so a chronicle in which three quarters of the
    /// lines are ordinary births and deaths can be read at the grain of its history without any of
    /// those facts being dropped from the log or from the pages of the people they concern.
    /// Version 20 named the world itself: whether the history is set on a planet or a moon, and
    /// a designation unique to the seed — "The planet Borion", "The 3rd moon of Endor" — so a
    /// list of exports can be told apart by something other than a filename and a number.
    /// Version 21 added the four fortunes on each settlement, so a town's own years of weariness
    /// and calamity are a snapshot and a series the same way a realm's already were, rather than
    /// only a population curve and a chronicle of what happened to it.
    /// Version 22 added the host star and habitable-body cosmology: spectral class, mass,
    /// luminosity, habitable-zone edges, orbital year, surface gravity, escape velocity,
    /// equilibrium and surface temperature, and for exomoons the parent giant, Roche limit,
    /// and tidal day length — the physics the seed rolls before history begins.
    /// Version 23 added companion planets: a required shepherd giant beyond the snow line
    /// that clears leftover planetesimals, optional inner rocky and outer ice-giant worlds,
    /// and the asteroid-belt gap those orbits leave.
    /// Version 24 added the parent giant's full moon family (so "the 8th moon" has seven
    /// siblings), the host star's radius, and enough to draw a true size comparison.
    ///     Version 25 dropped the asteroid belt from the exported system and keeps every
    /// satellite of the parent inside the same tidal-day limit as the habitable moon.
    /// Version 26 added a figure's occupation and the independence dial on their
    /// disposition — follower to rebel — so a person raised into the record has a
    /// career behind them and a child of one chooses a life the court can appoint from.
    /// Version 27 added a figure's campaigns: the battles a soldier or general stood in, the
    /// wars a sitting ruler led, and the sieges endured by anyone living in an invested town,
    /// each with whether their side prevailed.
    /// Version 28 added journeys (trade, visits, pilgrimage, clerical missions) and the
    /// occupations that office-holding and letters use: official and scribe.
    /// Version 29 gave the busiest land routes a road: a polyline over the ground, the year it
    /// was cut, the year it was bridged and paved if it ever was, and its length along the way —
    /// so the map can draw where the traffic physically went rather than only who traded with
    /// whom. Absent on every route that never earned one, and on every coastal route, which is
    /// sailed.
    /// Version 30 recorded how a journey ended — home, robbed, or never returned.
    /// Version 31 added the host galaxy: morphology, a habitable annulus, and the observer's
    /// galactocentric site, so the cosmology page can show where the seed sits as well as
    /// what it orbits.
    /// Version 32 added the system's comets — perihelion, aphelion, and a nucleus — so the
    /// cosmology page can put tails on the true-size strip and the full-system map.
    /// Version 33 added a figure's bonds, salient memories, feelings, wounds and undertakings,
    /// so a life page can lead with its causal shape rather than only its raw chronology.
    /// Version 34 added terminal battle fates, trauma, desertion, battle-earned renown and
    /// promotion provenance, plus undertaking sponsors, deadlines and motive provenance.
    /// Version 35 added personal quarrels — cause, escalation, acts and outcome — shared by both
    /// parties, and generalised an injury's cause from a battle to whatever inflicted it.
    /// Version 36 added the sky's true schedule of comet returns and the observations named people
    /// wrote down of them, with the interval their own realm's register let them derive.
    /// Version 37 added what they claimed those sightings meant, the year a measured claim named,
    /// and the verdict the sky returned on it.
    /// Version 38 made conspiracies persistent plots: objective, grounded cause, members and the
    /// tie that recruited each of them, secrecy, suspicion, phase, outcome, and the year any of it
    /// became public — with each act carrying whether it was known when it happened. Undertakings
    /// lost the secrecy and access fields the old inline conspiracy borrowed them for.
    /// Version 39 put a revealed plot on its target's page as well as its conspirators' pages, with
    /// an explicit viewpoint so the same facts can be told without exposing a still-secret plot.
    /// Version 40 added bounded guardianships, mentorship starts and structured backgrounds for
    /// adults raised into the record, plus guardian/ward bonds and formative childhood siege memories.
    /// Version 41 added hardship memories carried by the people who lived through a famine,
    /// plague, sack or disaster. Version 42 added residence histories. Version 43 added journeys
    /// that end in staying and the residence change they cause.
    /// Version 44 added route-derived journey durations and dated returns, so a winter journey is
    /// distinguishable from a short trip made in the same year.
    /// Version 45 links a real holy-site dedicatee to the exact chronicle event whose deed the
    /// dedication quotes; a missing link now means the dedicatee is explicitly legendary.
    /// Version 47 added a figure's military service: the rungs of their realm's army they were
    /// raised to, each with the year, the realm and the name that realm gives the rung.
    /// </remarks>
    public const int CurrentSchemaVersion = 48;
}

public sealed record ExportMeta(
    ulong Seed,
    string ConfigHash,
    string SystemOrderHash,
    IReadOnlyList<string> SystemOrder,
    string EngineVersion,
    int NarrationSyntaxVersion,
    int StartYear,
    int EndYear,
    int YearsSimulated,
    int EventCount,
    ExportSampleStats TerrainSampling);

/// <summary>
/// What the run cost in terrain samples, split by purpose.
/// </summary>
/// <remarks>
/// Carried in the export because it is the number that decides whether a given world
/// configuration is viable inside Vintage Story, where each sample costs 1–2ms. Simulation and
/// presentation are reported separately: the simulation figure is the one under test and under
/// budget, while the map raster is a presentation cost that Phase 3 can reduce or skip outright
/// without touching the simulation.
/// </remarks>
public sealed record ExportSampleStats(
    long SimulationSamples,
    long RasterSamples,
    double EstimatedGameSecondsSimulation,
    double EstimatedGameSecondsRaster);

/// <param name="EastWestPeriodic">
/// Whether the east and west edges are the same meridian.
/// </param>
/// <param name="Designation">
/// How the world is spoken of: "The planet Borion", "The 3rd moon of Endor". Unique to the
/// seed, and what a list of histories is labelled by.
/// </param>
/// <param name="Cosmology">
/// Host star and habitable-body parameters derived from the seed before simulation begins.
/// </param>
/// <remarks>
/// <para><see cref="EastWestPeriodic"/> is carried because a viewer cannot infer it and draws the
/// world wrong without it. The simulation already measures distance the short way round, so a
/// trade route between a town at the western edge and one at the eastern edge is a neighbourly
/// link — and a map that has not been told the world wraps draws it as a line clean across the
/// continent, which is the one reading that is certainly false.</para>
///
/// <para>The name fields sit first so a catalog that only reads the file header — cutting at
/// the raster payload — still learns what to call the world. They are flavour derived from the
/// seed, not simulation inputs: two runs of the same seed with different year counts keep the
/// same designation, and the seed itself remains in <see cref="ExportMeta"/> for reproduction.
/// </para>
/// </remarks>
public sealed record ExportWorld(
    string Name,
    WorldKind Kind,
    string Designation,
    string? ParentName,
    int? MoonIndex,
    ExportCosmology Cosmology,
    int MinX,
    int MinZ,
    int Width,
    int Height,
    int RegionSize,
    int TerrainStride,
    bool EastWestPeriodic,
    string Capabilities,
    ExportRaster Raster,
    IReadOnlyList<ExportRiver> Rivers);

/// <summary>
/// Star-system physics for a habitable planet or exomoon, rolled from the seed,
/// plus the host galaxy the system sits in.
/// </summary>
public sealed record ExportCosmology(
    ExportGalaxy Galaxy,
    StarSpectralClass StarClass,
    double StarMassSolar,
    double StarRadiusSolar,
    double LuminositySolar,
    double StarLifespanGyr,
    double HabitableZoneInnerAu,
    double HabitableZoneOuterAu,
    double OrbitalDistanceAu,
    double OrbitalPeriodDays,
    double WorldMassEarth,
    double WorldRadiusEarth,
    double MeanDensityEarth,
    double BulkIronMassFraction,
    double CoreMassFraction,
    double SurfaceGravityG,
    double EscapeVelocityKmS,
    double BondAlbedo,
    double GreenhouseDeltaC,
    double EquilibriumTempK,
    double SurfaceTempK,
    double? ParentGiantMassEarth,
    double? MoonOrbitalDistanceEarthRadii,
    double? MoonDayLengthDays,
    double? RocheLimitEarthRadii,
    double SnowLineAu,
    IReadOnlyList<ExportCompanionPlanet> Companions,
    IReadOnlyList<ExportSystemMoon> Moons,
    int? HabitableMoonIndex,
    IReadOnlyList<ExportSystemMoon> HomeMoons,
    ExportCelestialOrientation Orientation,
    IReadOnlyList<ExportComet> Comets,
    IReadOnlyList<ExportApparition> Apparitions,
    bool IsHabitable,
    IReadOnlyList<ExportCosmologyCheck> Checks);

/// <summary>
/// One return of a comet the chronicle would carry, in a year, at a brightness.
/// </summary>
/// <remarks>
/// The true schedule, derived from the rolled orbit rather than from anything anyone saw. It is
/// exported beside the observations so a reader — or a later claim — can compare what happened with
/// what was written down, which is the only reason the observations are worth having.
/// </remarks>
public sealed record ExportApparition(int CometIndex, int Year, ApparitionGrade Grade);

public sealed record ExportSystemMoon(
    int Index,
    string Name,
    double OrbitalDistanceEarthRadii,
    double MassEarth,
    double RadiusEarth,
    double DayLengthDays,
    bool Habitable);

public sealed record ExportCompanionPlanet(
    CompanionRole Role,
    string RoleLabel,
    double SemiMajorAxisAu,
    double MassEarth,
    double RadiusEarth,
    double OrbitalPeriodDays,
    ExportGiantAppearance? Appearance,
    IReadOnlyList<ExportSystemMoon> Moons);

/// <summary>
/// How a giant reads from a distance: its tilt, its banding, the storm parked in it, and the ring
/// system lying in its equatorial plane.
/// </summary>
/// <param name="RingBrightnessBoostMagnitudes">
/// What the ring adds to the planet's brightness, as a negative number of magnitudes. Zero when
/// there is no ring, or when the ring is edge-on or too dark to matter.
/// </param>
public sealed record ExportGiantAppearance(
    double ObliquityDeg,
    bool Retrograde,
    double RotationPeriodHours,
    double AscendingNodeDeg,
    int BandCount,
    ExportTint BandLight,
    ExportTint BandDark,
    ExportPlanetStorm? Storm,
    ExportPlanetRing? Ring,
    double RingOpenness,
    double RingBrightnessBoostMagnitudes);

public sealed record ExportPlanetRing(
    double InnerRadiusPlanetRadii,
    double OuterRadiusPlanetRadii,
    double OpticalDepth,
    double DivisionRadiusPlanetRadii,
    RingComposition Composition,
    string CompositionLabel,
    ExportTint Tint);

public sealed record ExportPlanetStorm(
    string Name,
    double LatitudeDeg,
    double LongitudeSpanDeg,
    double LatitudeSpanDeg,
    double AgeYears,
    ExportTint Tint);

/// <summary>A colour in linear 0–1 channels.</summary>
public sealed record ExportTint(double R, double G, double B);

/// <summary>
/// Where the world's spin axis points inside its galaxy, which is what decides whether the band of
/// light wheels overhead each night or lies fixed along the horizon.
/// </summary>
public sealed record ExportCelestialOrientation(
    double PoleGalacticLongitudeRad,
    double PoleGalacticLatitudeRad,
    double RightAscensionOriginRollRad,
    double PoleTiltFromGalacticPoleDeg,
    double GalacticPlaneInclinationDeg);

public sealed record ExportComet(
    int Index,
    double PerihelionAu,
    double AphelionAu,
    double Eccentricity,
    double InclinationDeg,
    double ArgumentOfPeriapsisRad,
    double OrbitalPeriodDays,
    double NucleusRadiusKm,
    double MassEarth);

public sealed record ExportCosmologyCheck(string Label, bool Passed, string Detail);

/// <summary>Host galaxy rolled from the seed: morphology and the observer's site inside it.</summary>
public sealed record ExportGalaxy(
    GalaxyMorphology Morphology,
    double StellarMassSolar,
    double DiskScaleLengthKpc,
    double ThinDiskScaleHeightPc,
    double BulgeToDiskMass,
    double SolarAnalogMetallicityFeH,
    double MetallicityGradientDexPerKpc,
    double MetallicityScatterDex,
    int SpiralArmCount,
    double SpiralPitchDeg,
    double InnerHabitableRadiusKpc,
    double OuterHabitableRadiusKpc,
    double SersicIndex,
    double AxisRatio,
    double MetallicityReferenceRadiusKpc,
    ExportGalacticLocation Location,
    bool CanHostIronCore,
    bool CanHostOres);

public sealed record ExportGalacticLocation(
    double GalactocentricRadiusKpc,
    double AzimuthRad,
    double HeightPc,
    double MetallicityFeH,
    bool InSpiralArm,
    double LocalStellarDensityRelativeToSolar,
    double SupernovaRateRelativeToSolar);

/// <summary>
/// One reach of a river, in world coordinates.
/// </summary>
/// <remarks>
/// Rivers travel as vectors rather than as a raster plane. A per-cell flag rasterises to a block
/// the size of the terrain lattice stride, which renders as a scatter of squares that read as
/// lakes; segments following the flow graph draw as continuous watercourses at any zoom.
/// <see cref="Strength"/> is normalised drainage in [0, 1], for line width.
/// </remarks>
public sealed record ExportRiver(int X1, int Z1, int X2, int Z2, double Strength);

/// <summary>
/// The map view's terrain data, as raw byte planes.
/// </summary>
/// <remarks>
/// Raw normalised bytes rather than an encoded image. A PNG would be smaller, but it would also
/// bake in a colour ramp — and the viewer wants to choose its own, switch it for light and dark
/// themes, and render height, biome and rivers as separate composable layers. Bytes plus the
/// height range they were normalised against let it reconstruct real metres and decide
/// everything else. Base64 costs a third in size and buys a self-contained single file.
///
/// <para>Phase 2 replaces the contents with real generated terrain and changes nothing here: the
/// viewer's map renderer consumes these planes and has no idea what produced them.</para>
/// </remarks>
public sealed record ExportRaster(
    int Resolution,
    double MinHeight,
    double MaxHeight,
    string Height,
    string Biome,
    string Flags)
{
    /// <summary>Bit 0 of a <see cref="Flags"/> byte.</summary>
    public const byte FlagRiver = 1;

    /// <summary>Bit 1 of a <see cref="Flags"/> byte.</summary>
    public const byte FlagCoast = 2;
}

public sealed record ExportRegion(
    EntityId Id,
    string Name,
    int MinX,
    int MinZ,
    int Width,
    int Height,
    Biome Biome,
    double Fertility,
    double Habitability,
    double MeanHeight,
    bool IsLand,
    bool HasRiver,
    bool IsCoastal,
    EntityId? Owner,
    IReadOnlyList<EntityId> Adjacent);

public sealed record ExportCulture(
    EntityId Id,
    string Name,
    GovernmentForm Government,
    string RulerTitle,
    SuccessionLaw SuccessionLaw,
    int TermYears,
    double Aggression,
    double Expansionism,
    double Piety,
    double Tradition,
    double Mercantile,
    double Learning,
    ExportLexicon Lexicon);

/// <summary>
/// A culture's naming language, described well enough to be inspected.
/// </summary>
/// <remarks>
/// The brief calls for per-culture name lexicons in the export. Shipping the trained Markov
/// tables would be large and useless to a reader; what actually answers the question "why do this
/// culture's names look like that" is the corpus blend, the sound shifts, and a handful of names
/// the language would produce. <see cref="SampleNames"/> are generated from ids outside the
/// world's own range, so showing them cannot collide with a real entity's name.
/// </remarks>
public sealed record ExportLexicon(
    IReadOnlyList<ExportLexiconSource> Sources,
    IReadOnlyList<string> SoundShifts,
    IReadOnlyList<string> SampleNames,
    IReadOnlyList<string> SamplePlaces);

public sealed record ExportLexiconSource(string Family, int Weight);

public sealed record ExportCivilization(
    EntityId Id,
    string Name,
    EntityId CultureId,
    int FoundedYear,
    int? EndedYear,
    EntityId? CapitalId,
    EntityId? CurrentRulerId,
    EntityId? RulingDynastyId,
    EntityId? RegentId,
    EntityId? StateReligionId,
    int RulerSinceYear,
    int Population,
    int PeakPopulation,
    ExportFortunes Fortunes,
    ExportValues EffectiveValues,
    IReadOnlyList<EntityId> RulerIds,
    IReadOnlyList<EntityId> SettlementIds,
    IReadOnlyList<EntityId> TerritoryRegionIds,
    IReadOnlyList<ExportRelation> Relations,
    IReadOnlyList<ExportAlliance> Allies);

/// <summary>
/// The dials a realm was actually governed by in the last simulated year.
/// </summary>
/// <remarks>
/// Its culture's own values, moved toward whoever was governing and then shifted by what the
/// realm had lately been through. Exported beside the culture's values rather than instead of
/// them, because the interesting thing a reader wants is the gap between the two.
/// </remarks>
public sealed record ExportValues(
    double Aggression,
    double Expansionism,
    double Piety,
    double Tradition,
    double Mercantile,
    double Learning);

/// <summary>
/// How a recent past sat on a realm or a place at the end of the run, in four decaying measures.
/// </summary>
/// <remarks>
/// A snapshot of the final year rather than a series. What happened is already in the
/// chronicle event by event; this is the state those events left behind, and it is exported
/// so a reader can see why the last years read as they did. The year-by-year track of the
/// same four measures is in <see cref="WorldExport.Series"/>.
/// </remarks>
public sealed record ExportFortunes(
    double Weariness,
    double Calamity,
    double Triumph,
    double Grievance);

/// <summary>
/// One realm's standing opinion of another, in [-1, 1].
/// </summary>
/// <remarks>
/// <para>A list of pairs rather than an object keyed by id, so the export stays an array of
/// records like everything else in this file and the viewer can sort it however it likes.</para>
///
/// <para>Directed: this is what <em>this</em> realm thinks, and the other side's entry will
/// usually differ. The asymmetry is the model — a peace costs the beaten realm far more goodwill
/// than the one that beat it — and flattening it into a single number per pair would remove the
/// only thing that produces a war of revanche.</para>
/// </remarks>
public sealed record ExportRelation(EntityId CivilizationId, double Opinion, int? TruceUntilYear);

public sealed record ExportAlliance(EntityId CivilizationId, int SinceYear);

/// <summary>
/// A war, its coalitions, and what it cost.
/// </summary>
/// <remarks>
/// <see cref="Name"/> is composed rather than generated — "Second War of Bergajarvi", "War of the
/// Lykos Succession" — because nobody names a war in advance, and every part a chronicle names it
/// after is already an entity in this file that the reader can follow.
/// </remarks>
public sealed record ExportWar(
    EntityId Id,
    string Name,
    CasusBelli Cause,
    EntityId? ClaimedRelicId,
    EntityId? AggressorReligionId,
    EntityId? DefenderReligionId,
    WarOutcome Outcome,
    int StartYear,
    int? EndYear,
    EntityId AggressorId,
    EntityId DefenderId,
    IReadOnlyList<EntityId> Attackers,
    IReadOnlyList<EntityId> Defenders,
    IReadOnlyList<EntityId> BattleIds,
    IReadOnlyList<EntityId> CededRegionIds,
    int AttackerLosses,
    int DefenderLosses);

/// <summary>
/// One engagement, named for where it was fought.
/// </summary>
/// <remarks>
/// A siege is a battle with <see cref="WasSiege"/> set, not a separate entity kind; an unwalled
/// settlement may still be the location of a field battle. Strengths are the forces actually
/// committed rather than either realm's total levy, since the difference between the two is most
/// of why a smaller realm sometimes wins.
/// </remarks>
public sealed record ExportBattle(
    EntityId Id,
    string Name,
    EntityId WarId,
    int Year,
    int Day,
    int? EndYear,
    int? EndDay,
    EntityId RegionId,
    EntityId? SettlementId,
    bool WasSiege,
    SiegeOutcome SiegeOutcome,
    EntityId AttackerId,
    EntityId DefenderId,
    EntityId VictorId,
    EntityId? AttackerCommanderId,
    EntityId? DefenderCommanderId,
    int AttackerStrength,
    int DefenderStrength,
    int AttackerLosses,
    int DefenderLosses,
    bool Sacked);

/// <summary>
/// A ruling house.
/// </summary>
/// <remarks>
/// <see cref="MemberIds"/> is blood only — consorts are reachable through their spouses and keep
/// whatever house they were born into. Without that distinction a house can never die out, and the
/// most interesting thing a dynasty can do is die out.
/// </remarks>
public sealed record ExportDynasty(
    EntityId Id,
    string Name,
    EntityId CultureId,
    int FoundedYear,
    int? EndedYear,
    EntityId FounderId,
    EntityId? OriginCivilizationId,
    IReadOnlyList<EntityId> RulerIds,
    IReadOnlyList<EntityId> MemberIds);

public sealed record ExportSettlement(
    EntityId Id,
    string Name,
    EntityId CivilizationId,
    EntityId? FoundedBy,
    EntityId RegionId,
    int X,
    int Z,
    SettlementTier Tier,
    SettlementSpecialization Specialization,
    int? SpecializedYear,
    int Population,
    int PeakPopulation,
    int FoundedYear,
    int? AbandonedYear,
    int YearsDepressed,
    bool IsCapital,
    bool IsFortified,
    EntityId? ReligionId,
    int? ConvertedYear,
    SiteCharacter Site,
    ExportFortunes Fortunes,
    ExportSupport? Support);

/// <summary>
/// What was feeding a settlement when the chronicle closed.
/// </summary>
/// <remarks>
/// <para>Present for settlements still standing at the end of the run and absent for abandoned
/// ones, because there is nothing left to feed and the last figures a dying place produced would
/// only mislead.</para>
///
/// <para>Population answers how large somewhere is; this answers why, and they are not the same
/// question. A town of four thousand on exceptional ground, one on six busy trade routes and one
/// held together by a capital's administration read identically from a population figure and are
/// three different histories. The parts are in people and sum to <see cref="Capacity"/>.</para>
///
/// <para><see cref="LandShare"/> is how much of the surrounding country the settlement keeps
/// rather than ceding to a neighbour — one means it stands alone. It is the term that explains the
/// villages: a place can sit on excellent ground and stay small because a city took the fields.
/// </para>
/// </remarks>
public sealed record ExportSupport(
    int Capacity,
    int FromSite,
    int FromLand,
    int FromTrade,
    double LandShare,
    double RouteTraffic,
    SupportSource Principal);

/// <summary>
/// One durable commercial connection: its topology, its economic history, and the way over the
/// ground if its traffic ever earned one.
/// </summary>
/// <remarks>
/// <see cref="Road"/> is absent for most routes and always absent for a coastal one, which is
/// sailed rather than walked. The route is the entity; the road is a fact about how it is served,
/// so a route that gains, upgrades or outlives a road keeps the same id throughout.
/// </remarks>
public sealed record ExportTradeRoute(
    EntityId Id,
    EntityId SettlementAId,
    EntityId SettlementBId,
    TradeRouteMode Mode,
    TradeRouteStatus Status,
    int FoundedYear,
    int? EndedYear,
    double Traffic,
    double PeakTraffic,
    ExportRoad? Road);

/// <summary>
/// The physical way a route takes, as a polyline over the world.
/// </summary>
/// <remarks>
/// <para><see cref="Points"/> is a flat <c>[x, z, x, z, …]</c> run from one settlement to the
/// other, with a vertex only where the way turns — so a road over open country is two points and a
/// road threading a range is a dozen. <see cref="Length"/> is measured along it, so it exceeds the
/// straight-line distance by exactly what the ground cost.</para>
///
/// <para><see cref="BuiltYear"/> survives an upgrade; <see cref="PavedYear"/> is the year the way
/// was engineered, if it ever was. A viewer replaying a year draws the road only from
/// <see cref="BuiltYear"/> onward, the same way territory is replayed from transfers.</para>
///
/// <para><b>One line, not a history of lines.</b> Only the current course is carried: a road paved
/// in year 400 exports the engineered line, and the track it replaced is gone. The two dates say
/// when each grade began, so a reader knows which of them the drawn line belongs to, but a replay
/// of an earlier year shows the later course.</para>
/// </remarks>
public sealed record ExportRoad(
    RoadGrade Grade,
    int BuiltYear,
    int? PavedYear,
    double Length,
    IReadOnlyList<int> Points);

/// <summary>
/// A faith, and the settlements that follow it.
/// </summary>
/// <remarks>
/// <see cref="SettlementIds"/> is the congregation as it stands at the end of the run, so a faith
/// that once held a continent and now holds one valley exports one valley — which is why
/// <see cref="PeakSettlements"/> is carried separately. Its whole rise and fall is replayable
/// from the adoption events, the same way territory is.
/// </remarks>
public sealed record ExportReligion(
    EntityId Id,
    string Name,
    EntityId CultureId,
    EntityId? FounderId,
    EntityId OriginSettlementId,
    EntityId? ParentId,
    int FoundedYear,
    int? EndedYear,
    double Fervour,
    ExportFaithCharacter Character,
    int PeakSettlements,
    IReadOnlyList<EntityId> SettlementIds);

/// <summary>
/// What a faith is: its gods, its church, its rules, and the dials that move it. Fixed at founding.
/// </summary>
public sealed record ExportFaithCharacter(
    DeityStructure Deity,
    Afterlife Afterlife,
    SoulDoctrine Soul,
    AuthorityType Authority,
    ClergyAdmission Clergy,
    bool CelibateClergy,
    WealthPractice Wealth,
    DogmaEmphasis Dogma,
    PrayerCadence Prayer,
    DietaryRule Diet,
    DressCode Dress,
    FestivalSeason Festival,
    double Fervour,
    double Zealotry,
    double Tolerance,
    double SchismProneness,
    double Syncretism);

/// <summary>
/// A house of worship or sacred place. <see cref="SettlementId"/> is present when it stands
/// within a settlement; otherwise its own coordinate is the location.
/// </summary>
public sealed record ExportHolySite(
    EntityId Id,
    string Name,
    HolySiteKind Kind,
    EntityId ReligionId,
    EntityId RegionId,
    EntityId? SettlementId,
    int X,
    int Z,
    int FoundedYear,
    ExportHolySiteDescription Description);

/// <summary>
/// How a holy place looks and what is done there, fixed at founding.
/// </summary>
public sealed record ExportHolySiteDescription(
    SacredTradition Tradition,
    HolySiteDedicationKind DedicationKind,
    string Dedication,
    string Style,
    string Atmosphere,
    HolySiteScale Scale,
    string Capacity,
    bool HasStatue,
    string FocalPoint,
    string Offering,
    EntityId? DedicateeId,
    int? DedicateeEventId);

/// <summary>
/// A made thing, and everywhere it has been.
/// </summary>
/// <remarks>
/// <see cref="Provenance"/> is the point of exporting these at all: an object made in one realm,
/// looted into a second and lost when a third burned the place down carries all three facts, and
/// a viewer can draw the whole journey without replaying anything.
/// </remarks>
public sealed record ExportArtifact(
    EntityId Id,
    string Name,
    ArtifactKind Kind,
    EntityId? CreatorId,
    EntityId OriginSettlementId,
    EntityId? ReligionId,
    ExportTomeContents? TomeContents,
    int CreatedYear,
    EntityId? HolderId,
    EntityId? OwnerId,
    int? LostYear,
    IReadOnlyList<ExportProvenance> Provenance);

/// <summary>The subject and passages fixed inside a written artifact when it was made.</summary>
public sealed record ExportTomeContents(
    TomeContentKind Kind,
    EntityId SubjectId,
    EntityId? ContextId,
    int CopyLimit,
    IReadOnlyList<ExportTomeCopy> Copies,
    IReadOnlyList<ExportTomeSection> Sections);

/// <summary>One settlement copy made from a work already available elsewhere.</summary>
public sealed record ExportTomeCopy(
    int Year,
    EntityId SettlementId,
    EntityId SourceSettlementId);

/// <summary>One passage and the entity links it makes available to a reader.</summary>
public sealed record ExportTomeSection(
    string Heading,
    string Text,
    IReadOnlyList<EntityId> References,
    int Year);

/// <summary>Where an artifact was, from a given year, and how it got there.</summary>
public sealed record ExportProvenance(int Year, EntityId? SettlementId, EntityId? OwnerId, string How);

/// <summary>
/// One person, with enough of the family tree attached to draw it.
/// </summary>
/// <remarks>
/// Mother and father are named rather than listed as parents, because every question asked of them
/// — which house does this child inherit, who is the queen mother — is about a specific one.
/// <see cref="ChildIds"/> is the redundant half of the same links, carried so the viewer can walk a
/// tree downward without indexing the whole figure table first.
/// </remarks>
public sealed record ExportFigure(
    EntityId Id,
    string Name,
    Sex Sex,
    EntityId CivilizationId,
    EntityId CultureId,
    EntityId? ReligionId,
    EntityId? DynastyId,
    int BirthYear,
    int? DeathYear,
    DeathCause DeathCause,
    string? DeathDetail,
    EntityId? BirthSettlementId,
    EntityId? ResidenceSettlementId,
    IReadOnlyList<ExportResidence> Residences,
    FigureOrigin Origin,
    ExportBackground? Background,
    Occupation Occupation,
    ExportDisposition Disposition,
    IReadOnlyList<ExportTitle> Titles,
    IReadOnlyList<ExportRankStep> Service,
    IReadOnlyList<ExportCampaign> Campaigns,
    IReadOnlyList<ExportJourney> Journeys,
    IReadOnlyList<ExportBond> Bonds,
    IReadOnlyList<ExportMemory> Memories,
    ExportFeelings Feelings,
    IReadOnlyList<ExportInjury> Injuries,
    IReadOnlyList<ExportUndertaking> Undertakings,
    IReadOnlyList<ExportDispute> Disputes,
    IReadOnlyList<ExportAffinity> Affinities,
    IReadOnlyList<ExportPlot> Plots,
    IReadOnlyList<ExportGuardianship> Guardianships,
    IReadOnlyList<ExportMentorship> Mentorships,
    IReadOnlyList<ExportObservation> Observations,
    IReadOnlyList<ExportSkyClaim> Claims,
    EntityId? MotherId,
    EntityId? FatherId,
    IReadOnlyList<EntityId> ChildIds,
    IReadOnlyList<EntityId> SpouseIds);

/// <summary>Facts known when an already-grown figure first became part of the chronicle.</summary>
public sealed record ExportBackground(
    int IntroducedYear,
    EntityId OriginSettlementId,
    CareerFamily CareerFamily,
    EntityId? InstitutionId,
    EntityId? SponsorId,
    EntityId? MentorId);

/// <summary>One bounded guardianship, shared by the guardian and ward.</summary>
public sealed record ExportGuardianship(
    EntityId GuardianId,
    EntityId WardId,
    int StartYear,
    int? EndYear,
    GuardianshipEnd End,
    EventKind CauseKind,
    EntityId? CauseEntityId,
    EntityId? LocationId);

/// <summary>A dated mentorship start; relationship strength remains on the ordinary bond.</summary>
public sealed record ExportMentorship(
    EntityId MentorId,
    EntityId ApprenticeId,
    int StartYear,
    CareerFamily CareerFamily,
    EntityId? LocationId);

/// <summary>
/// One office held over a span of years.
/// </summary>
/// <remarks>
/// <see cref="Kind"/> is what a consumer should branch on and <see cref="Title"/> is what it should
/// print. Identifying a reign by its title text works only while the crown and the regency are the
/// only two offices there are; the moment a third exists, every such comparison starts quietly
/// returning the wrong one rather than failing.
/// </remarks>
public sealed record ExportTitle(
    OfficeKind Kind,
    string Title,
    EntityId CivilizationId,
    int FromYear,
    int? ToYear,
    EntityId? ScopeId,
    EntityId? GrantedBy,
    string? Claim);

/// <summary>
/// One rung of an army a person was raised to, and the year they reached it.
/// </summary>
/// <remarks>
/// The current rank is the last entry — a rank is never laid down, so there is no <c>ToYear</c> to
/// carry and no way for the list and a stored current value to disagree. Empty for everybody who
/// never took to arms, which is most of the table.
/// </remarks>
public sealed record ExportRankStep(
    MilitaryRank Rank,
    string Title,
    EntityId CivilizationId,
    int Year,
    string? Claim);

/// <summary>
/// One war or engagement a person stood in.
/// </summary>
/// <param name="BattleId">
/// The engagement, absent when they led the realm through a war rather than standing in a battle.
/// </param>
/// <param name="Triumphant">
/// Whether their side prevailed. Absent while the war or siege is still open, and after a stalemate.
/// </param>
public sealed record ExportCampaign(
    EntityId WarId,
    EntityId? BattleId,
    EntityId SideId,
    int Year,
    CampaignRole Role,
    bool? Triumphant,
    CampaignFate Fate,
    int RenownGained,
    bool Traumatized,
    bool Deserted,
    int? PromotionYear);

/// <summary>
/// One trip a person made and was expected home from. Residence is not this.
/// </summary>
public sealed record ExportJourney(
    JourneyKind Kind,
    int Year,
    int Day,
    EntityId FromSettlementId,
    EntityId ToSettlementId,
    EntityId? ViaId,
    int DurationDays,
    JourneyOutcome Outcome,
    EntityId? ReturnSettlementId,
    int? ReturnYear,
    int? ReturnDay);

/// <summary>
/// One period of living somewhere, with the year it began and what caused it.
/// </summary>
/// <remarks>
/// Enough on its own to answer where a figure lived in any year: the entries are ordered and the
/// last one before the year in question is the address. The final entry is
/// <c>residenceSettlementId</c>, which is retained so a reader that only wants "where are they now"
/// does not have to walk the list.
/// </remarks>
public sealed record ExportResidence(EntityId SettlementId, int FromYear, ResidenceReason Reason);

/// <summary>One directed relationship, with every role it accumulated.</summary>
public sealed record ExportBond(
    EntityId OtherId,
    IReadOnlyList<BondKind> Kinds,
    int SinceYear,
    int LastChangedYear,
    BondCause LastCause,
    EventKind OriginEventKind,
    EntityId? OriginEntityId,
    EntityId? OriginLocationId,
    EventKind LastEventKind,
    EntityId? LastEntityId,
    EntityId? LastLocationId,
    double Affection,
    double Trust,
    double Obligation,
    double Fear,
    double Grievance);

/// <summary>One of the bounded experiences still formative at the end of the export.</summary>
public sealed record ExportMemory(
    MemoryKind Kind,
    MemoryValence Valence,
    int Year,
    int LastReinforcedYear,
    EventKind SourceKind,
    EntityId? AboutId,
    EntityId? LocationId,
    double Intensity,
    bool Active);

public sealed record ExportFeelings(
    double Grief,
    double Fear,
    double Anger,
    double Pride,
    double Loyalty);

/// <param name="CauseId">The battle that did it, or the person who did it in a quarrel.</param>
public sealed record ExportInjury(
    EntityId CauseId,
    EventKind SourceKind,
    int Year,
    InjurySeverity Severity,
    int RecoveryYear,
    bool Permanent,
    string Detail);

public sealed record ExportUndertaking(
    int Id,
    UndertakingKind Kind,
    UndertakingState State,
    int StartYear,
    int? EndYear,
    string? Outcome,
    string Objective,
    EntityId? TargetId,
    EntityId? DestinationId,
    EntityId? ViaId,
    int Progress,
    int RequiredProgress,
    MemoryKind Motive,
    EntityId? MotiveEntityId,
    EventKind MotiveSourceKind,
    int DeadlineYear,
    int LastProgressYear,
    EntityId? SponsorId,
    OfficeKind? RequiredOffice,
    IReadOnlyList<EntityId> ParticipantIds,
    IReadOnlyList<ExportUndertakingStep> Steps);

/// <summary>
/// One sighting a named person wrote down, and what their own realm's register let them derive.
/// </summary>
/// <param name="PriorYear">
/// The last time this realm recorded the same body, where it had. Absent means nobody there had it
/// on record — so this observer could not have known an interval, whatever the true period is.
/// </param>
public sealed record ExportObservation(
    int CometIndex,
    int Year,
    EntityId? RealmId,
    EntityId? SettlementId,
    int? PriorYear,
    int? Interval,
    ApparitionGrade Grade);

/// <summary>
/// What one person said a light in the sky was, and what became of the saying.
/// </summary>
/// <param name="RestsOnYears">
/// The sightings the claimant had to work from. A reader can see what they were reasoning over
/// rather than taking the conclusion on trust.
/// </param>
/// <param name="ClaimantSawTheAnswer">
/// Whether they were alive when the sky settled it. False is the more interesting case and not a
/// rare one: a period long enough to be worth deriving is usually longer than the rest of a life.
/// </param>
public sealed record ExportSkyClaim(
    int Id,
    int CometIndex,
    int Year,
    EntityId? RealmId,
    ClaimRegister Register,
    string Reading,
    IReadOnlyList<int> RestsOnYears,
    int IntervalYears,
    int? PredictedYear,
    ClaimVerdict Verdict,
    int? SettledYear,
    bool ClaimantSawTheAnswer);

public sealed record ExportUndertakingStep(
    int Year,
    EventKind SourceKind,
    EntityId? PlaceId,
    EntityId? SubjectId,
    string Outcome);

/// <summary>
/// One quarrel, written the same way on both parties' pages.
/// </summary>
/// <param name="OtherId">
/// The party this page is not about, so a consumer never has to work out which side it is reading.
/// </param>
/// <param name="Opened">
/// Whether the figure this record hangs on is the aggrieved party. The facts are identical either
/// way; this is what lets a viewer say "was challenged by" rather than "challenged".
/// </param>
public sealed record ExportDispute(
    int Id,
    EntityId OtherId,
    bool Opened,
    DisputeCause Cause,
    EventKind SourceKind,
    EntityId? SourceEntityId,
    EntityId? PlaceId,
    DisputeStage Stage,
    DisputeOutcome Outcome,
    string? Resolution,
    EntityId? ArbiterId,
    int StartYear,
    int? EndYear,
    int LastActionYear,
    IReadOnlyList<ExportDisputeAct> Acts);

public sealed record ExportDisputeAct(
    int Year,
    EventKind SourceKind,
    DisputeStage Stage,
    EntityId? ActorId,
    string Detail);

/// <summary>
/// One friendship, written from the side of the figure page carrying it.
/// </summary>
/// <param name="OtherId">
/// The party this page is not about, so a consumer never has to work out which side it is reading.
/// </param>
/// <param name="Sought">
/// Whether the figure this record hangs on is the one who sought the other. The facts are identical
/// either way; this is what lets a page say "sought out" rather than "was sought out by".
/// </param>
/// <param name="BetrayerId">
/// Which of the two turned, where one did. Absent for every other outcome, and the only field that
/// distinguishes the betrayer's page from the betrayed one.
/// </param>
public sealed record ExportAffinity(
    int Id,
    EntityId OtherId,
    bool Sought,
    AffinityOrigin Origin,
    EventKind SourceKind,
    EntityId? SourceEntityId,
    EntityId? PlaceId,
    AffinityStage Stage,
    AffinityOutcome Outcome,
    string? Resolution,
    EntityId? BetrayerId,
    int StartYear,
    int? EndYear,
    int LastActionYear,
    IReadOnlyList<ExportAffinityAct> Acts);

public sealed record ExportAffinityAct(
    int Year,
    EventKind SourceKind,
    AffinityStage Stage,
    EntityId? ActorId,
    string Detail);

/// <summary>
/// One conspiracy, written from the side of the figure page carrying it.
/// </summary>
/// <param name="Viewpoint">
/// Whether the page belongs to the leader, a willing member, or the target. Targets receive only
/// plots that became public; leaders and members retain the retrospective truth they already knew.
/// </param>
/// <param name="PublicYear">
/// The year the world learned of it, absent where it never did. A consumer must use this to
/// separate what a contemporary could have known from what only a later reader has: an abandoned
/// plot has no public year and no event anywhere in the timeline.
/// </param>
public sealed record ExportPlot(
    int Id,
    EntityId LeaderId,
    EntityId TargetId,
    EntityId? RealmId,
    PlotViewpoint Viewpoint,
    PlotObjective Objective,
    PlotCause Cause,
    EventKind SourceKind,
    EntityId? SourceEntityId,
    EntityId? PlaceId,
    PlotPhase Phase,
    PlotOutcome Outcome,
    string? Resolution,
    EntityId? BetrayerId,
    int StartYear,
    int? EndYear,
    int? PublicYear,
    int Progress,
    int RequiredProgress,
    double Secrecy,
    double Suspicion,
    double Access,
    IReadOnlyList<ExportPlotMember> Members,
    IReadOnlyList<ExportPlotAct> Acts);

/// <param name="Witting">
/// False for someone whose access was used without their knowing what it was for. They are named
/// in the retrospective truth and carry no record of the plot on their own page.
/// </param>
public sealed record ExportPlotMember(
    EntityId FigureId,
    int JoinedYear,
    PlotTie Tie,
    bool Witting);

/// <param name="Known">Whether this was public in the year it happened. Most acts are not.</param>
public sealed record ExportPlotAct(
    int Year,
    EventKind SourceKind,
    PlotPhase Phase,
    EntityId? ActorId,
    string Detail,
    bool Known);

/// <summary>
/// One person's own inclinations, on the same dials their culture has.
/// </summary>
/// <remarks>
/// Exported for everyone rather than only for those who governed, because the viewer draws family
/// trees and "the brother who would have been a very different king" is exactly the sort of thing
/// a reader of a chronicle wants to be able to see. <see cref="Independence"/> is the follower–
/// rebel axis: how far they let that culture actually govern their choices.
/// </remarks>
public sealed record ExportDisposition(
    double Aggression,
    double Expansionism,
    double Piety,
    double Tradition,
    double Mercantile,
    double Learning,
    double Centralism,
    /// <summary>Follower at zero, rebel at one. How far they let their culture govern them.</summary>
    double Independence);

/// <param name="Significance">
/// Whether this belongs to the narrative spine or the vital register. The viewer hides
/// <see cref="Events.Significance.Routine"/> from the chronicle by default and keeps it on entity
/// pages, so a person's own history stays complete while the world's stays readable.
/// </param>
public sealed record ExportEvent(
    int Id,
    int Year,
    int Day,
    EventKind Kind,
    Significance Significance,
    EntityId? Subject,
    EntityId? Object,
    EntityId? Location,
    IReadOnlyList<EntityId>? Extra,
    IReadOnlyDictionary<string, string>? Data);

/// <summary>
/// One measure of one entity, sampled once a year.
/// </summary>
/// <remarks>
/// <para>The snapshot fields elsewhere in this file report where a run ended: a realm's
/// population, its weariness, the values it was last governed by. None of them can say whether
/// it got there by growing steadily or by being halved twice and clawing its way back, and that
/// is usually the interesting half. These carry the whole shape.</para>
///
/// <para><b>Self-describing, so a viewer can plot one it has never heard of.</b>
/// <see cref="Group"/> collects measures that belong on one chart and <see cref="Unit"/> says
/// whether the axis is a headcount or a [0, 1] dial — the same contract the narration templates
/// have with event kinds, and it buys the same thing: a measure added in a later milestone draws
/// itself with no viewer change.</para>
///
/// <para><b>Values are rounded to three decimals</b> — a thousandth of a dial nobody can see,
/// against roughly two thirds of the bytes these would otherwise cost. Rounding is deterministic,
/// so the golden-hash test is unaffected.</para>
/// </remarks>
/// <param name="FromYear">The year <paramref name="Values"/> begins. One entry per year from there.</param>
public sealed record ExportSeries(
    EntityId Entity,
    string Metric,
    string Group,
    string Unit,
    int FromYear,
    IReadOnlyList<double> Values);

/// <summary>
/// Denormalised lookups, computed once by the engine.
/// </summary>
/// <remarks>
/// <para>These exist so the viewer never scans the event list. Without
/// <see cref="EventsByEntity"/>, opening a figure's page means a linear pass over every event in
/// the world — fine at a thousand events, visibly slow at the fifty thousand this is designed
/// for, and repeated on every navigation. With it, an entity page is an array lookup, which is
/// what makes cross-link browsing feel instant.</para>
///
/// <para>Values are indices into <see cref="WorldExport.Events"/>, not event objects, so the
/// cost is a few hundred kilobytes of integers rather than a duplicated event list. Event ids
/// are assigned sequentially on append, so an id <em>is</em> its index — asserted by
/// <c>ExportTests</c>.</para>
/// </remarks>
public sealed record ExportIndices(
    IReadOnlyDictionary<string, int[]> EventsByEntity,
    IReadOnlyDictionary<string, int[]> EventsByYear,
    IReadOnlyDictionary<string, int> EventCountsByKind);
