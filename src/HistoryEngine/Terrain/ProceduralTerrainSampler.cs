using HistoryEngine.Core;

namespace HistoryEngine.Terrain;

/// <summary>Tunable feature scales for <see cref="ProceduralTerrainSampler"/>, in world units.</summary>
public sealed record TerrainSettings
{
    /// <summary>Wavelength of the land/ocean mask. Larger means fewer, broader continents.</summary>
    public double ContinentScale { get; init; } = 6000.0;

    /// <summary>Wavelength of mountain ridges.</summary>
    public double RidgeScale { get; init; } = 1600.0;

    public double RainfallScale { get; init; } = 2800.0;

    public double TemperatureVarianceScale { get; init; } = 7000.0;

    public double GeologyScale { get; init; } = 5200.0;

    public double LakeScale { get; init; } = 900.0;

    /// <summary>Peak land elevation before ridges are added, in metres.</summary>
    public double BaseLandHeight { get; init; } = 520.0;

    /// <summary>Additional elevation ridges can contribute, in metres.</summary>
    public double RidgeHeight { get; init; } = 2400.0;

    /// <summary>Deepest ocean floor, in metres below sea level.</summary>
    public double OceanDepth { get; init; } = 900.0;

    /// <summary>Mean temperature at the equator and at the poles, in °C.</summary>
    public double EquatorTemperature { get; init; } = 29.0;

    public double PolarTemperature { get; init; } = -17.0;

    /// <summary>Adiabatic lapse rate in °C per metre of elevation.</summary>
    public double LapseRate { get; init; } = 0.0065;
}

/// <summary>
/// Phase 1's placeholder terrain: noise-derived height and climate over an abstract plane.
/// </summary>
/// <remarks>
/// This exists to give the simulation a world with opinions — coasts, mountains, a
/// temperature gradient, somewhere worth founding a city and somewhere not — while
/// costing nothing to sample. It is not trying to be plausible geology; Phase 2 replaces
/// it with a real heightmap-plus-climate pipeline, and Phase 3 with Vintage Story's own
/// worldgen.
///
/// <para>Because sampling here is free, it is the worst possible thing to develop
/// simulation code against without discipline. That is what <see cref="TerrainAtlas"/> and
/// <see cref="CountingTerrainSampler"/> are for: the access pattern is measured from the
/// first milestone, so nothing gets written that only works because terrain happens to be
/// cheap right now.</para>
/// </remarks>
public sealed class ProceduralTerrainSampler : ITerrainSampler
{
    private readonly TerrainSettings _settings;
    private readonly bool _eastWestPeriodic;

    private readonly ulong _continentSeed;
    private readonly ulong _ridgeSeed;
    private readonly ulong _rainSeed;
    private readonly ulong _tempSeed;
    private readonly ulong _geologySeed;
    private readonly ulong _aridSeed;
    private readonly ulong _lakeSeed;

    public ProceduralTerrainSampler(
        ulong seed,
        TerrainBounds bounds,
        TerrainSettings? settings = null,
        bool eastWestPeriodic = false)
    {
        Bounds = bounds;
        _settings = settings ?? new TerrainSettings();
        _eastWestPeriodic = eastWestPeriodic;

        // One derived seed per field, so changing how one field is generated cannot
        // shift any other.
        _continentSeed = Hash.Combine(seed, Hash.OfString("terrain.continent"));
        _ridgeSeed = Hash.Combine(seed, Hash.OfString("terrain.ridge"));
        _rainSeed = Hash.Combine(seed, Hash.OfString("terrain.rainfall"));
        _tempSeed = Hash.Combine(seed, Hash.OfString("terrain.temperature"));
        _geologySeed = Hash.Combine(seed, Hash.OfString("terrain.geology"));
        _aridSeed = Hash.Combine(seed, Hash.OfString("terrain.aridity"));
        _lakeSeed = Hash.Combine(seed, Hash.OfString("terrain.lake"));
    }

    public TerrainBounds Bounds { get; }

    /// <summary>
    /// Everything except rivers. Rivers come from <see cref="Hydrology"/> in every phase,
    /// so no backend is asked to supply them.
    /// </summary>
    public TerrainCapabilities Capabilities => TerrainCapabilities.Standard;

    public TerrainSample Sample(int x, int z)
    {
        double height = HeightAt(x, z);
        double temperature = TemperatureAt(x, z, height);
        double rainfall = RainfallAt(x, z, height, temperature);
        double geology = GeologyAt(x, z);
        WaterKind water = WaterAt(x, z, height);

        // Trees need warmth and water; scrub takes the drier margins that are left over.
        double canopy = DetMath.Clamp01(
            DetMath.InverseLerp(0.18, 0.62, rainfall) *
            DetMath.InverseLerp(-6.0, 6.0, temperature) *
            DetMath.Lerp(1.0, 0.15, DetMath.InverseLerp(1400.0, 2600.0, height)));

        double scrub = water != WaterKind.None
            ? 0.0
            : DetMath.Clamp01(DetMath.InverseLerp(0.06, 0.4, rainfall) * (1.0 - (canopy * 0.75)));

        if (water != WaterKind.None)
        {
            canopy = 0.0;
        }

        return new TerrainSample(
            Height: (float)height,
            Temperature: (float)temperature,
            Rainfall: (float)rainfall,
            GeologicActivity: (float)geology,
            ForestDensity: (float)canopy,
            ShrubDensity: (float)scrub,
            Water: water);
    }

    private double HeightAt(int x, int z)
    {
        double nx = NoiseX(x, _settings.ContinentScale);
        double nz = z / _settings.ContinentScale;

        // Positive is land, negative is sea.
        double continent = Fbm(
            _continentSeed, nx, nz, _settings.ContinentScale, octaves: 6);

        if (continent < 0.0)
        {
            // Shelf near the coast, abyss further out.
            return continent * _settings.OceanDepth;
        }

        double ridge = Ridged(
            _ridgeSeed,
            NoiseX(x, _settings.RidgeScale),
            z / _settings.RidgeScale,
            _settings.RidgeScale,
            octaves: 5);

        // Ridges build only well inland, so coastlines stay walkable instead of
        // becoming cliff walls.
        double interior = DetMath.InverseLerp(0.04, 0.5, continent);

        return (continent * _settings.BaseLandHeight)
             + (DetMath.IntPow(ridge, 3) * _settings.RidgeHeight * interior);
    }

    private double TemperatureAt(int x, int z, double height)
    {
        // Z maps to latitude: the middle of the world is the equator, the edges are poles.
        double halfSpan = Bounds.Height / 2.0;
        double fromEquator = halfSpan <= 0.0
            ? 0.0
            : DetMath.Clamp01(Math.Abs(z - Bounds.CenterZ) / halfSpan);

        double banded = DetMath.Lerp(
            _settings.EquatorTemperature, _settings.PolarTemperature, fromEquator);

        double variance = Fbm(
            _tempSeed,
            NoiseX(x, _settings.TemperatureVarianceScale),
            z / _settings.TemperatureVarianceScale,
            _settings.TemperatureVarianceScale,
            octaves: 3) * 3.5;

        double lapse = height > 0.0 ? height * _settings.LapseRate : 0.0;

        return banded + variance - lapse;
    }

    /// <summary>
    /// Annual rainfall in [0, 1].
    /// </summary>
    /// <remarks>
    /// <para>Two noise fields rather than one: a regional field at
    /// <see cref="TerrainSettings.RainfallScale"/>, plus a much broader one that creates
    /// continent-spanning arid and humid belts. Without the second, rainfall varied only locally and
    /// averaged out — every temperate region came out moderately wet, so the whole world was
    /// farmable and no settlement anywhere could be marginal.</para>
    ///
    /// <para>The earlier version also added a large constant to every warm region, which put a floor
    /// under temperate rainfall and guaranteed hospitable land. Temperature now <em>scales</em>
    /// rainfall instead of adding to it — cold air genuinely carries less moisture — so a cold dry
    /// region is dry rather than merely less wet.</para>
    /// </remarks>
    private double RainfallAt(int x, int z, double height, double temperature)
    {
        if (height < 0.0) return 1.0;

        double regional = Fbm(
            _rainSeed,
            NoiseX(x, _settings.RainfallScale),
            z / _settings.RainfallScale,
            _settings.RainfallScale,
            octaves: 4);

        double beltScale = _settings.RainfallScale * 3.5;
        double belt = Fbm(
            _aridSeed,
            NoiseX(x, beltScale),
            z / beltScale,
            beltScale,
            octaves: 2);

        double wetness = DetMath.Clamp01(0.46 + (regional * 0.42) + (belt * 0.30));

        // Cold air holds little moisture, so this scales rather than offsets.
        double warmth = DetMath.InverseLerp(-24.0, 10.0, temperature);

        return DetMath.Clamp01(wetness * DetMath.Lerp(0.30, 1.0, warmth));
    }

    private double GeologyAt(int x, int z)
    {
        double field = Ridged(
            _geologySeed,
            NoiseX(x, _settings.GeologyScale),
            z / _settings.GeologyScale,
            _settings.GeologyScale,
            octaves: 3);

        return DetMath.Clamp01(field);
    }

    private WaterKind WaterAt(int x, int z, double height)
    {
        if (height < 0.0) return WaterKind.Ocean;

        // Scattered inland basins. A real basin test needs the height lattice, which
        // this sampler deliberately does not have — Hydrology does that work later.
        double basin = Fbm(
            _lakeSeed,
            NoiseX(x, _settings.LakeScale),
            z / _settings.LakeScale,
            _settings.LakeScale,
            octaves: 2);

        bool lowland = height < 420.0;
        return lowland && basin > 0.62 ? WaterKind.Lake : WaterKind.None;
    }

    private double NoiseX(int x, double scale) =>
        _eastWestPeriodic ? (x - Bounds.MinX) / scale : x / scale;

    private double Fbm(
        ulong seed, double x, double z, double scale, int octaves) =>
        _eastWestPeriodic
            ? ValueNoise.FbmPeriodicX(seed, x, z, Bounds.Width / scale, octaves)
            : ValueNoise.Fbm(seed, x, z, octaves);

    private double Ridged(
        ulong seed, double x, double z, double scale, int octaves) =>
        _eastWestPeriodic
            ? ValueNoise.RidgedPeriodicX(seed, x, z, Bounds.Width / scale, octaves)
            : ValueNoise.Ridged(seed, x, z, octaves);
}
