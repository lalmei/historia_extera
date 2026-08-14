using HistoryEngine.Core;

namespace HistoryEngine.Terrain;

/// <summary>An integer world coordinate. The world is a horizontal plane; Y is elevation, not position.</summary>
public readonly record struct Point2(int X, int Z);

/// <summary>What kind of water, if any, is at a location.</summary>
public enum WaterKind
{
    None = 0,
    Ocean = 1,
    Lake = 2,
    River = 3,
}

/// <summary>
/// Which fields a sampler actually measures, as opposed to synthesises or cannot supply.
/// </summary>
/// <remarks>
/// This flag set is what keeps the abstraction honest across three very different
/// backends. Phase 1's noise sampler can produce every field trivially, which is
/// precisely the trap: code written against it would silently assume everything is
/// always available. Vintage Story's terrain sampler does not report rivers at all
/// unless the Watersheds sampler is present, so any logic that hard-requires
/// <see cref="Rivers"/> would work perfectly for two phases and then fail in the game.
///
/// <para>Consumers must check capabilities and degrade, never assume.</para>
/// </remarks>
[Flags]
public enum TerrainCapabilities
{
    None = 0,
    Height = 1 << 0,
    Temperature = 1 << 1,
    Rainfall = 1 << 2,
    GeologicActivity = 1 << 3,
    ForestDensity = 1 << 4,
    ShrubDensity = 1 << 5,

    /// <summary>The sampler can distinguish inland water bodies from ocean.</summary>
    Lakes = 1 << 6,

    /// <summary>
    /// The sampler reports rivers directly. When absent, <see cref="Hydrology"/> derives
    /// them from the height lattice instead — which is the normal case, not a fallback.
    /// </summary>
    Rivers = 1 << 7,

    /// <summary>What a straightforward heightmap-plus-climate backend provides.</summary>
    Standard = Height | Temperature | Rainfall | GeologicActivity | ForestDensity | ShrubDensity | Lakes,
}

/// <summary>
/// Claims about the terrain layer itself rather than about any one backend.
/// </summary>
/// <remarks>
/// A static holder rather than more members on <see cref="TerrainCapabilities"/>: the export
/// carries the flag set as its formatted name, so a composite added to the enum can change how an
/// existing value prints and move the golden digest for no change in behaviour.
/// </remarks>
public static class TerrainFields
{
    /// <summary>
    /// The fields a backend can honestly model from elevation and latitude when no layer supplies
    /// them.
    /// </summary>
    /// <remarks>
    /// <see cref="TerrainCapabilities.Lakes"/> is deliberately absent, and it is the whole point
    /// of naming this set separately from <see cref="TerrainCapabilities.Standard"/>. A depression
    /// in a heightmap is not a lake — whether water stands in it depends on drainage and climate
    /// no bare heightmap carries — so <see cref="RasterTerrainSampler"/> reports ocean below sea
    /// level and nothing above it, rather than inventing one. Counting lakes as modelled would
    /// have a height-only world claim data that was neither measured nor derived, which is the
    /// exact dishonesty the capability flags exist to prevent.
    /// </remarks>
    public const TerrainCapabilities Modelled =
        TerrainCapabilities.Temperature
        | TerrainCapabilities.Rainfall
        | TerrainCapabilities.GeologicActivity
        | TerrainCapabilities.ForestDensity
        | TerrainCapabilities.ShrubDensity;

    /// <summary>What a backend measuring <paramref name="measured"/> supplies by modelling instead.</summary>
    public static TerrainCapabilities ModelledFor(TerrainCapabilities measured) =>
        Modelled & ~measured;
}

/// <summary>The rectangular extent a sampler can answer for, in world units.</summary>
public readonly record struct TerrainBounds(int MinX, int MinZ, int Width, int Height)
{
    public int MaxX => MinX + Width;

    public int MaxZ => MinZ + Height;

    public int CenterX => MinX + (Width / 2);

    public int CenterZ => MinZ + (Height / 2);

    public bool Contains(int x, int z) => x >= MinX && x < MaxX && z >= MinZ && z < MaxZ;

    public static TerrainBounds Square(int size) => new(0, 0, size, size);

    /// <summary>Clamps a coordinate into these bounds.</summary>
    public Point2 Clamp(int x, int z) => new(
        x < MinX ? MinX : x >= MaxX ? MaxX - 1 : x,
        z < MinZ ? MinZ : z >= MaxZ ? MaxZ - 1 : z);
}

/// <summary>
/// Everything terrain can tell the simulation about one point.
/// </summary>
/// <remarks>
/// Fields are raw measurements only. Anything the simulation wants that a real backend
/// cannot measure — habitability, farmland quality, defensibility — is derived, and
/// derived on this side of the interface where the derivation is written once and can
/// be reviewed. If <see cref="Fertility"/> were a sampler-supplied field, each of the
/// three backends would end up inventing its own, and the same world would score
/// differently depending on which phase you were in.
///
/// <para>Heights are metres relative to sea level, so sea level is always exactly zero
/// and no backend gets to define its own datum.</para>
/// </remarks>
public readonly record struct TerrainSample(
    float Height,
    float Temperature,
    float Rainfall,
    float GeologicActivity,
    float ForestDensity,
    float ShrubDensity,
    WaterKind Water)
{
    public bool IsSubmerged => Height < 0f;

    public bool IsWater => Water != WaterKind.None;

    /// <summary>
    /// Derived crop-growing potential in [0, 1].
    /// </summary>
    /// <remarks>
    /// <para>A crude Liebig's-law-of-the-minimum model: rainfall and temperature each gate the
    /// result, so a place is only fertile when both are adequate, and high ground is discounted.
    /// Deliberately simple — it exists so settlement siting has an opinion, and it is expected to
    /// be replaced once Phase 2 supplies real climate.</para>
    ///
    /// <para><b>Unimodal, not plateaued.</b> An earlier version used clamped ramps that each read
    /// 1.0 across the whole comfortable range, so every temperate lowland scored exactly 1.0 and
    /// the measure could not distinguish good farmland from excellent. Everything downstream
    /// inherited that: settled regions clustered at fertility 1.000, carrying capacity was
    /// effectively uniform, and no settlement could ever be marginal enough to fail. Peaking at one
    /// optimum instead gives the whole range something to say.</para>
    /// </remarks>
    public float Fertility
    {
        get
        {
            if (IsSubmerged) return 0f;

            // Wet enough to farm, not so wet it is swamp.
            double moisture = DetMath.Bump(Rainfall, optimum: 0.55, tolerance: 0.31);

            // Temperate. Falls away toward both frost and desert heat.
            double warmth = DetMath.Bump(Temperature, optimum: 14.0, tolerance: 23.0);

            // Lowlands are the best ground regardless of climate.
            double altitude = DetMath.Lerp(1.0, 0.30, DetMath.InverseLerp(0.0, 1800.0, Height));

            return (float)DetMath.Clamp01(moisture * warmth * altitude);
        }
    }
}
