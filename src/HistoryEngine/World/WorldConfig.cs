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

    public TerrainSettings Terrain { get; init; } = new();

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
            Append(nameof(RegionSize), RegionSize);
            Append(nameof(TerrainStride), TerrainStride);
            Append(nameof(HydrologyStride), HydrologyStride);
            Append(nameof(InitialCivilizations), InitialCivilizations);

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
    /// Number of fields folded into <see cref="ConfigHash"/>. Guards against a new
    /// simulation-affecting field being added without extending the hash.
    /// </summary>
    public const int HashedFieldCount = 19;

    public void Validate()
    {
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

        if (InitialCivilizations < 0)
        {
            throw new InvalidOperationException("InitialCivilizations cannot be negative.");
        }
    }
}
