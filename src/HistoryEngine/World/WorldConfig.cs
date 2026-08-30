using System.Globalization;
using System.Text;
using HistoryEngine.Core;
using HistoryEngine.Terrain;

namespace HistoryEngine.World;

/// <summary>
/// Everything that, together with the seed, determines a history.
/// </summary>
/// <remarks>
/// The determinism contract is "identical seed + config produces identical history", which
/// makes this record half of that contract. <see cref="ConfigHash"/> is exported alongside
/// the seed so a world file can always be traced back to the exact inputs that produced it —
/// without it, a reproduction attempt that silently used a different default is
/// indistinguishable from a determinism bug.
/// </remarks>
public sealed record WorldConfig
{
    /// <summary>Master seed. Every RNG substream in the run derives from this.</summary>
    public ulong Seed { get; init; } = 1;

    /// <summary>Years to simulate.</summary>
    public int Years { get; init; } = 300;

    /// <summary>The year history starts at. Cosmetic, but it appears in every event.</summary>
    public int StartYear { get; init; } = 1;

    /// <summary>Side length of the square world, in world units.</summary>
    public int WorldSize { get; init; } = 4096;

    /// <summary>
    /// Whether the east and west edges meet, making the world cylindrical.
    /// </summary>
    /// <remarks>
    /// North and south remain bounded. This affects terrain, drainage, region adjacency and
    /// every simulation distance, so it is part of the deterministic configuration.
    /// </remarks>
    public bool EastWestPeriodic { get; init; }

    /// <summary>Side length of one region, in world units.</summary>
    public int RegionSize { get; init; } = 128;

    /// <summary>Terrain lattice spacing. Must be a power of two. Drives the sample budget.</summary>
    public int TerrainStride { get; init; } = TerrainAtlas.DefaultStride;

    /// <summary>
    /// Grid spacing for river derivation. Finer than <see cref="TerrainStride"/>.
    /// </summary>
    /// <remarks>
    /// Simulation-affecting, and therefore hashed: rivers feed settlement siting, so changing this
    /// changes where cities are founded.
    /// </remarks>
    public int HydrologyStride { get; init; } = TerrainAtlas.DefaultHydrologyStride;

    /// <summary>How many civilizations are seeded at the start.</summary>
    public int InitialCivilizations { get; init; } = 8;

    /// <summary>
    /// How long a year is, and how many parts it has.
    /// </summary>
    /// <remarks>
    /// Simulation-affecting, and therefore hashed: the calendar decides how many steps a year has
    /// and how far anything gets in one of them. See <see cref="World.Calendar"/> for why 360, and
    /// <see cref="ConfigHash"/> for why it is folded in only when it is not the default.
    /// </remarks>
    public Calendar Calendar { get; init; } = Calendar.Default;

    /// <summary>World units covered in one ordinary overland travel day.</summary>
    /// <remarks>
    /// World units have no implicit real-world scale. Naming the pace here is therefore the only
    /// honest way for a road length to become days: changing it changes who winters away from home,
    /// so it is simulation configuration and belongs in <see cref="ConfigHash"/>.
    /// </remarks>
    public double UnitsPerTravelDay { get; init; } = 12.0;

    public TerrainSettings Terrain { get; init; } = new();

    /// <summary>
    /// Identity of the terrain backend, when it is not Phase 1's procedural one.
    /// </summary>
    /// <remarks>
    /// <para>Empty means the noise sampler, whose entire input is <see cref="Seed"/> and
    /// <see cref="Terrain"/> — both already hashed. A raster backend's input is a set of files,
    /// and a file path is not the pixels: re-export the map and the same path is a different
    /// world. So <see cref="RasterTerrainSampler.Provenance"/> puts a content digest here, and
    /// the determinism contract keeps meaning what it says.</para>
    ///
    /// <para>Set it with <see cref="WithTerrain"/> rather than by hand, which also squares
    /// <see cref="WorldSize"/> with the extent the rasters actually cover.</para>
    /// </remarks>
    public string TerrainSource { get; init; } = string.Empty;

    /// <summary>Points this config at a raster backend, recording both its extent and its identity.</summary>
    public WorldConfig WithTerrain(RasterTerrainSampler sampler) => this with
    {
        WorldSize = sampler.Bounds.Width,
        TerrainSource = sampler.Provenance,
    };

    /// <summary>Resolution per axis of the map raster written to the export.</summary>
    /// <remarks>
    /// Presentation only, and budgeted separately from simulation sampling — see
    /// <c>TerrainRaster</c>.
    /// </remarks>
    public int MapRasterResolution { get; init; } = 256;

    public TerrainBounds Bounds => TerrainBounds.Square(WorldSize);

    /// <summary>
    /// A stable hash of every field that can change the resulting history.
    /// </summary>
    /// <remarks>
    /// Built from an explicitly formatted string rather than reflection or
    /// <see cref="object.GetHashCode"/>: reflection over properties has no guaranteed order,
    /// and string hash codes are randomised per process. Adding a field that affects the
    /// simulation means adding it here too — <c>ConfigHashTests</c> checks that the field
    /// count matches, so a forgotten field fails a test rather than silently producing two
    /// different histories that claim the same provenance.
    /// </remarks>
    public string ConfigHash
    {
        get
        {
            var sb = new StringBuilder();
            void Append(string key, object value) =>
                sb.Append(key).Append('=')
                  .Append(Convert.ToString(value, CultureInfo.InvariantCulture))
                  .Append(';');

            Append(nameof(Years), Years);
            Append(nameof(StartYear), StartYear);
            Append(nameof(WorldSize), WorldSize);
            if (EastWestPeriodic) Append(nameof(EastWestPeriodic), EastWestPeriodic);
            Append(nameof(RegionSize), RegionSize);
            Append(nameof(TerrainStride), TerrainStride);
            Append(nameof(HydrologyStride), HydrologyStride);
            Append(nameof(InitialCivilizations), InitialCivilizations);
            Append(nameof(UnitsPerTravelDay), UnitsPerTravelDay);

            // Appended only when a foreign backend is in play. Not a convenience: an empty
            // source means the procedural sampler, whose inputs are already hashed below, so
            // omitting it leaves every world generated before Phase 2 hashing to the value it
            // has always hashed to. A field that shifted the provenance of files already
            // written, without any history having changed, would be the exact reflex the
            // golden test exists to discourage.
            if (TerrainSource.Length > 0) Append(nameof(TerrainSource), TerrainSource);

            // Appended only when the world does not keep the standard year, for the same reason as
            // the two conditionals above. Every world written before there was a calendar kept one
            // implicitly — the year was the atom and nothing divided it — so hashing 360 days and
            // four seasons unconditionally would move the provenance of files whose histories are
            // identical, which is the reflex the golden test exists to discourage. A world that
            // changes its calendar is a different history and says so.
            if (Calendar != Calendar.Default)
            {
                Append(nameof(Calendar.DaysPerYear), Calendar.DaysPerYear);
                Append(nameof(Calendar.SeasonsPerYear), Calendar.SeasonsPerYear);
            }

            Append(nameof(TerrainSettings.ContinentScale), Terrain.ContinentScale);
            Append(nameof(TerrainSettings.RidgeScale), Terrain.RidgeScale);
            Append(nameof(TerrainSettings.RainfallScale), Terrain.RainfallScale);
            Append(nameof(TerrainSettings.TemperatureVarianceScale), Terrain.TemperatureVarianceScale);
            Append(nameof(TerrainSettings.GeologyScale), Terrain.GeologyScale);
            Append(nameof(TerrainSettings.LakeScale), Terrain.LakeScale);
            Append(nameof(TerrainSettings.BaseLandHeight), Terrain.BaseLandHeight);
            Append(nameof(TerrainSettings.RidgeHeight), Terrain.RidgeHeight);
            Append(nameof(TerrainSettings.OceanDepth), Terrain.OceanDepth);
            Append(nameof(TerrainSettings.EquatorTemperature), Terrain.EquatorTemperature);
            Append(nameof(TerrainSettings.PolarTemperature), Terrain.PolarTemperature);
            Append(nameof(TerrainSettings.LapseRate), Terrain.LapseRate);

            return Hash.OfString(sb.ToString()).ToString("x16", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Number of fields <see cref="ConfigHash"/> can fold in. Guards against a new
    /// simulation-affecting field being added without extending the hash.
    /// </summary>
    /// <remarks>
    /// <para>A ceiling rather than a count: <see cref="TerrainSource"/> contributes only when a
    /// foreign terrain backend is in play, <see cref="EastWestPeriodic"/> only when enabled, and
    /// <see cref="Calendar"/> only when the world does not keep the standard year — preserving
    /// hashes from worlds generated before any of those options existed.</para>
    ///
    /// <para>Fields, not appended keys: <see cref="Calendar"/> is one field carrying two numbers,
    /// and both go in when it goes in.</para>
    /// </remarks>
    public const int HashedFieldCount = 23;

    public void Validate()
    {
        Calendar.Validate();

        if (Years < 0) throw new InvalidOperationException("Years cannot be negative.");
        if (WorldSize <= 0) throw new InvalidOperationException("WorldSize must be positive.");
        if (RegionSize <= 0) throw new InvalidOperationException("RegionSize must be positive.");
        if (RegionSize > WorldSize)
        {
            throw new InvalidOperationException("RegionSize cannot exceed WorldSize.");
        }

        if (TerrainStride <= 0 || (TerrainStride & (TerrainStride - 1)) != 0)
        {
            throw new InvalidOperationException("TerrainStride must be a positive power of two.");
        }

        if (HydrologyStride <= 0)
        {
            throw new InvalidOperationException("HydrologyStride must be positive.");
        }

        if (EastWestPeriodic
            && (WorldSize % RegionSize != 0
                || WorldSize % TerrainStride != 0
                || WorldSize % HydrologyStride != 0))
        {
            throw new InvalidOperationException(
                "A periodic world size must be divisible by RegionSize, TerrainStride, " +
                "and HydrologyStride so the east/west seam aligns on every grid.");
        }

        if (InitialCivilizations < 0)
        {
            throw new InvalidOperationException("InitialCivilizations cannot be negative.");
        }

        if (!double.IsFinite(UnitsPerTravelDay) || UnitsPerTravelDay <= 0.0)
        {
            throw new InvalidOperationException("UnitsPerTravelDay must be finite and positive.");
        }

        int regions = (WorldSize / RegionSize) * (WorldSize / RegionSize);
        if (InitialCivilizations > 0 && regions < RegionsPerCivilization * InitialCivilizations)
        {
            throw new InvalidOperationException(
                $"A world of {WorldSize} units holds {regions} regions, which is too few to seat " +
                $"{InitialCivilizations} civilizations. Allow at least {RegionsPerCivilization} " +
                $"regions each — raise WorldSize to at least " +
                $"{MinimumWorldSize(InitialCivilizations, RegionSize)}, or lower " +
                "InitialCivilizations.");
        }
    }

    /// <summary>
    /// Regions a civilization needs to its name before a world is worth simulating.
    /// </summary>
    /// <remarks>
    /// <para>Homelands are chosen from land that clears <c>MinFoundingHabitability</c>, and most of
    /// a region grid is ocean, mountain or ice. So the grid has to be several times larger than the
    /// civilization count before there is anywhere to put them, and
    /// <see cref="WorldBuilder"/> relaxes its separation constraint rather than failing — by design,
    /// since a mostly-ocean seed legitimately seats fewer. The consequence was that asking for
    /// eight civilizations in a small world returned a world with no civilizations, no settlements
    /// and a one-line chronicle, reported as a successful run.</para>
    ///
    /// <para>Sixteen is where that stops. Measured over seeds 1, 42 and 99 at four, eight and
    /// sixteen civilizations: every configuration at or above sixteen regions each seated the full
    /// count, and every configuration below it came up short — 8 regions each seated six
    /// civilizations of eight, and 2 each seated five. It is a floor against a world that cannot
    /// work, not a promise: a particularly wet seed can still seat fewer, which is why the CLI
    /// reports the shortfall rather than this rejecting it.</para>
    /// </remarks>
    public const int RegionsPerCivilization = 16;

    /// <summary>Smallest world that can seat this many civilizations, rounded up to whole regions.</summary>
    public static int MinimumWorldSize(int civilizations, int regionSize)
    {
        int regions = RegionsPerCivilization * civilizations;

        int perAxis = 1;
        while (perAxis * perAxis < regions) perAxis++;

        return perAxis * regionSize;
    }
}
