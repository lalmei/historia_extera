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
    IReadOnlyList<ExportFigure> Figures,
    IReadOnlyList<ExportEvent> Events,
    ExportIndices Indices,
    IReadOnlyDictionary<string, string> Narration)
{
    /// <summary>
    /// Bumped on any breaking change to this shape. The viewer checks it and refuses politely
    /// rather than misrendering a file it does not understand.
    /// </summary>
    /// <remarks>
    /// Version 2 added dynasties and the family links on a figure, and replaced the figure's
    /// two-element parent list with named mother and father.
    /// </remarks>
    public const int CurrentSchemaVersion = 2;
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

public sealed record ExportWorld(
    int MinX,
    int MinZ,
    int Width,
    int Height,
    int RegionSize,
    int TerrainStride,
    string Capabilities,
    ExportRaster Raster,
    IReadOnlyList<ExportRiver> Rivers);

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
    int RulerSinceYear,
    int Population,
    int PeakPopulation,
    IReadOnlyList<EntityId> RulerIds,
    IReadOnlyList<EntityId> SettlementIds,
    IReadOnlyList<EntityId> TerritoryRegionIds);

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
    bool IsFortified);

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
    EntityId? DynastyId,
    int BirthYear,
    int? DeathYear,
    DeathCause DeathCause,
    EntityId? BirthSettlementId,
    IReadOnlyList<ExportTitle> Titles,
    EntityId? MotherId,
    EntityId? FatherId,
    IReadOnlyList<EntityId> ChildIds,
    IReadOnlyList<EntityId> SpouseIds);

public sealed record ExportTitle(string Title, EntityId CivilizationId, int FromYear, int? ToYear);

public sealed record ExportEvent(
    int Id,
    int Year,
    EventKind Kind,
    EntityId? Subject,
    EntityId? Object,
    EntityId? Location,
    IReadOnlyList<EntityId>? Extra,
    IReadOnlyDictionary<string, string>? Data);

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
