using System.Security.Cryptography;
using System.Text.Json;
using HistoryEngine.Core;

namespace HistoryEngine.Terrain;

/// <summary>One plane in a terrain raster set, as the manifest declares it.</summary>
/// <remarks>
/// <see cref="Min"/> and <see cref="Max"/> are what the raster's darkest and brightest values
/// mean in the field's own units. They are not optional decoration: a PGM knows only that its
/// samples run 0..65535, so without this the same file is a mountain range or a mudflat
/// depending on who reads it.
/// </remarks>
public sealed record RasterLayerSpec
{
    /// <summary>Path to the PGM, relative to the manifest.</summary>
    public string File { get; init; } = string.Empty;

    /// <summary>What raster value 0 means, in the field's units.</summary>
    public double? Min { get; init; }

    /// <summary>What the raster's full-scale value means, in the field's units.</summary>
    public double? Max { get; init; }

    /// <summary>
    /// Height only: the normalised raster value that is exactly sea level.
    /// </summary>
    /// <remarks>
    /// The one number that makes a foreign heightmap usable. Generators put their shoreline
    /// wherever they like — Azgaar's Fantasy Map Generator calls it 20 on a 0..100 scale — and
    /// <see cref="TerrainSample.Height"/> is contractually metres relative to a sea level of
    /// exactly zero. Naming the threshold lets the loader honour that contract by construction
    /// rather than hoping the linear range happens to cross zero in the right place.
    /// </remarks>
    public double? SeaLevel { get; init; }
}

/// <summary>
/// The JSON document that describes a terrain raster set.
/// </summary>
/// <remarks>
/// Only <see cref="Height"/> is required, which is the point — see
/// <see cref="RasterTerrainSampler"/> for what happens to the fields nobody supplied.
/// </remarks>
public sealed record TerrainRasterManifest
{
    /// <summary>Side length of the square world the rasters cover, in world units.</summary>
    public int WorldSize { get; init; }

    public RasterLayerSpec? Height { get; init; }

    public RasterLayerSpec? Temperature { get; init; }

    public RasterLayerSpec? Rainfall { get; init; }

    public RasterLayerSpec? Geology { get; init; }

    public RasterLayerSpec? Forest { get; init; }

    public RasterLayerSpec? Shrub { get; init; }

    /// <summary>Optional inland-water mask. Anything above half scale is a lake.</summary>
    public RasterLayerSpec? Water { get; init; }
}

/// <summary>
/// Phase 2's terrain backend: height and climate read from rasters exported by some other
/// generator.
/// </summary>
/// <remarks>
/// <para><b>What this is for.</b> Phase 2's job is to prove the terrain abstraction survives a
/// backend that was not written for it, on terrain we control, rather than discovering the
/// abstraction is wrong inside Vintage Story. Consuming rasters rather than binding to a
/// generator's codebase makes almost any generator usable — Azgaar's Fantasy Map Generator,
/// WorldEngine, a GIS export, a heightmap someone painted — at the cost of one conversion to
/// PGM. Nothing here knows what produced the pixels.</para>
///
/// <para><b>Sea level is zero by construction.</b> <see cref="TerrainSample.Height"/> is metres
/// relative to sea level, and no backend gets to define its own datum. A foreign heightmap has
/// its shoreline at whatever value its author chose, so the manifest names that value and the
/// two sides of it are scaled separately: below the threshold onto
/// <c>[Min, 0]</c>, above it onto <c>[0, Max]</c>. A single linear range would put the
/// coastline wherever the arithmetic landed, and every coastal settlement in the world with it.</para>
///
/// <para><b>Absent fields are synthesised, and not claimed.</b> Real generators export a
/// heightmap and, if you are lucky, a climate layer or two. Refusing to load anything less
/// would make the raster route useless in practice, so the missing fields are modelled here
/// from latitude and elevation — and deliberately left out of <see cref="Capabilities"/>. That
/// flag set has, until now, never had a backend that declared less than
/// <see cref="TerrainCapabilities.Standard"/>: Phase 1's noise sampler produces every field
/// trivially, which was always the trap the flags existed to guard against. A heightmap-only
/// raster set is the first sampler in this project that honestly declares a partial hand, which
/// is most of what the spike was for.</para>
/// </remarks>
public sealed class RasterTerrainSampler : ITerrainSampler
{
    private static readonly JsonSerializerOptions ManifestOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly RasterGrid _height;
    private readonly double _heightAbyss;
    private readonly double _heightPeak;
    private readonly double _seaLevel;

    private readonly Layer? _temperature;
    private readonly Layer? _rainfall;
    private readonly Layer? _geology;
    private readonly Layer? _forest;
    private readonly Layer? _shrub;
    private readonly RasterGrid? _water;

    private readonly double _spanX;
    private readonly double _spanZ;

    private RasterTerrainSampler(
        TerrainBounds bounds,
        string digest,
        RasterGrid height,
        double abyss,
        double peak,
        double seaLevel,
        Layer? temperature,
        Layer? rainfall,
        Layer? geology,
        Layer? forest,
        Layer? shrub,
        RasterGrid? water)
    {
        Bounds = bounds;
        Digest = digest;

        _height = height;
        _heightAbyss = abyss;
        _heightPeak = peak;
        _seaLevel = seaLevel;

        _temperature = temperature;
        _rainfall = rainfall;
        _geology = geology;
        _forest = forest;
        _shrub = shrub;
        _water = water;

        // Divide by the addressable span, not the extent: a bounds of width 4096 addresses
        // 0..4095, and the last raster column belongs on the last coordinate that exists.
        _spanX = Math.Max(1, bounds.Width - 1);
        _spanZ = Math.Max(1, bounds.Height - 1);

        TerrainCapabilities capabilities = TerrainCapabilities.Height;
        if (temperature is not null) capabilities |= TerrainCapabilities.Temperature;
        if (rainfall is not null) capabilities |= TerrainCapabilities.Rainfall;
        if (geology is not null) capabilities |= TerrainCapabilities.GeologicActivity;
        if (forest is not null) capabilities |= TerrainCapabilities.ForestDensity;
        if (shrub is not null) capabilities |= TerrainCapabilities.ShrubDensity;
        if (water is not null) capabilities |= TerrainCapabilities.Lakes;

        Capabilities = capabilities;
    }

    public TerrainBounds Bounds { get; }

    /// <summary>
    /// Only the fields a raster actually supplied. Never <see cref="TerrainCapabilities.Rivers"/>:
    /// hydrology derives those from height in every phase.
    /// </summary>
    public TerrainCapabilities Capabilities { get; }

    /// <summary>
    /// Content digest of the manifest and every plane it names.
    /// </summary>
    /// <remarks>
    /// A raster world's history depends on the pixels, and a file path is not the pixels — the
    /// same path holds a different world the moment someone re-exports their map. This travels
    /// into <see cref="World.WorldConfig.TerrainSource"/> and from there into the config hash, so
    /// "identical seed + config produces an identical history" stays a checkable claim rather
    /// than one that quietly stopped covering the terrain.
    /// </remarks>
    public string Digest { get; }

    /// <summary>This backend's identity, in the form <see cref="World.WorldConfig.TerrainSource"/> carries.</summary>
    public string Provenance => "raster:" + Digest;

    /// <summary>Loads a raster set from its manifest.</summary>
    public static RasterTerrainSampler Load(string manifestPath)
    {
        string fullPath = Path.GetFullPath(manifestPath);
        byte[] manifestBytes = File.ReadAllBytes(fullPath);

        TerrainRasterManifest manifest =
            JsonSerializer.Deserialize<TerrainRasterManifest>(manifestBytes, ManifestOptions)
            ?? throw new InvalidOperationException($"{manifestPath}: manifest is empty.");

        string directory = Path.GetDirectoryName(fullPath) ?? ".";

        if (manifest.WorldSize <= 0)
        {
            throw new InvalidOperationException(
                $"{manifestPath}: 'worldSize' must be positive — it is the world extent the " +
                "rasters cover, in world units, and nothing in a PGM implies it.");
        }

        RasterLayerSpec heightSpec = manifest.Height
            ?? throw new InvalidOperationException(
                $"{manifestPath}: a 'height' layer is required. Every other field can be " +
                "modelled from elevation and latitude; elevation itself cannot.");

        using IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hasher.AppendData(manifestBytes);

        RasterGrid height = LoadLayer(directory, heightSpec, "height", hasher);

        double seaLevel = heightSpec.SeaLevel
            ?? throw new InvalidOperationException(
                $"{manifestPath}: the height layer needs 'seaLevel' — the normalised raster " +
                "value that is the shoreline. Heights are metres relative to a sea level of " +
                "exactly zero, so this cannot be guessed.");

        if (seaLevel is < 0.0 or > 1.0)
        {
            throw new InvalidOperationException(
                $"{manifestPath}: 'seaLevel' is a normalised raster value in 0..1, got {seaLevel}.");
        }

        double abyss = Require(heightSpec.Min, manifestPath, "height", "min");
        double peak = Require(heightSpec.Max, manifestPath, "height", "max");

        if (abyss >= peak)
        {
            throw new InvalidOperationException(
                $"{manifestPath}: the height layer's 'min' ({abyss}) must be below its 'max' ({peak}).");
        }

        // Temperature is the one field with no defensible default range: 0..1 °C is not a
        // world. Everything else is a normalised density unless the manifest says otherwise.
        Layer? temperature = manifest.Temperature is null
            ? null
            : new Layer(
                LoadLayer(directory, manifest.Temperature, "temperature", hasher),
                Require(manifest.Temperature.Min, manifestPath, "temperature", "min"),
                Require(manifest.Temperature.Max, manifestPath, "temperature", "max"));

        Layer? rainfall = Optional(directory, manifest.Rainfall, "rainfall", hasher);
        Layer? geology = Optional(directory, manifest.Geology, "geology", hasher);
        Layer? forest = Optional(directory, manifest.Forest, "forest", hasher);
        Layer? shrub = Optional(directory, manifest.Shrub, "shrub", hasher);

        RasterGrid? water = manifest.Water is null
            ? null
            : LoadLayer(directory, manifest.Water, "water", hasher);

        string digest = Convert.ToHexString(hasher.GetCurrentHash()).ToLowerInvariant()[..16];

        return new RasterTerrainSampler(
            TerrainBounds.Square(manifest.WorldSize),
            digest,
            height,
            abyss,
            peak,
            seaLevel,
            temperature,
            rainfall,
            geology,
            forest,
            shrub,
            water);
    }

    public TerrainSample Sample(int x, int z)
    {
        Point2 at = Bounds.Clamp(x, z);

        double u = (at.X - Bounds.MinX) / _spanX;
        double v = (at.Z - Bounds.MinZ) / _spanZ;

        double height = HeightAt(u, v);
        double temperature = _temperature?.At(u, v) ?? SynthesiseTemperature(v, height);
        double rainfall = _rainfall?.At(u, v) ?? SynthesiseRainfall(v, temperature, height);

        WaterKind water = height < 0.0
            ? WaterKind.Ocean
            : _water is not null && _water.Nearest(u, v) > 0.5f
                ? WaterKind.Lake
                : WaterKind.None;

        double geology = _geology?.At(u, v) ?? SynthesiseGeology(height);
        double forest = _forest?.At(u, v) ?? SynthesiseForest(rainfall, temperature, height);
        double shrub = _shrub?.At(u, v) ?? SynthesiseShrub(rainfall, forest);

        if (water != WaterKind.None)
        {
            forest = 0.0;
            shrub = 0.0;
        }

        return new TerrainSample(
            Height: (float)height,
            Temperature: (float)temperature,
            Rainfall: (float)DetMath.Clamp01(rainfall),
            GeologicActivity: (float)DetMath.Clamp01(geology),
            ForestDensity: (float)DetMath.Clamp01(forest),
            ShrubDensity: (float)DetMath.Clamp01(shrub),
            Water: water);
    }

    /// <summary>
    /// Height in metres, with the shoreline pinned to exactly zero.
    /// </summary>
    /// <remarks>
    /// Two linear pieces meeting at <c>seaLevel</c>. The join is the contract: a point on the
    /// declared shoreline reads 0.0 metres whatever the generator's own scale was, so
    /// <see cref="TerrainSample.IsSubmerged"/>, the ocean test and every fertility calculation
    /// downstream mean the same thing in Phase 2 as they did in Phase 1.
    /// </remarks>
    private double HeightAt(double u, double v)
    {
        double normalised = _height.Sample(u, v);

        if (normalised >= _seaLevel)
        {
            double above = _seaLevel >= 1.0 ? 0.0 : (normalised - _seaLevel) / (1.0 - _seaLevel);
            return DetMath.Lerp(0.0, _heightPeak, above);
        }

        double below = _seaLevel <= 0.0 ? 1.0 : normalised / _seaLevel;
        return DetMath.Lerp(_heightAbyss, 0.0, below);
    }

    /// <summary>
    /// Temperature from latitude and elevation, when no raster supplies it.
    /// </summary>
    /// <remarks>
    /// A latitude band and a dry adiabatic lapse rate — the same shape Phase 1 uses, minus its
    /// noise, because inventing regional variation a raster did not contain would be
    /// fabrication rather than modelling. Polynomial throughout, per the determinism rules.
    /// </remarks>
    private static double SynthesiseTemperature(double v, double height)
    {
        double fromEquator = DetMath.Clamp01(Math.Abs((v * 2.0) - 1.0));
        double banded = DetMath.Lerp(29.0, -17.0, fromEquator);
        double lapse = height > 0.0 ? height * 0.0065 : 0.0;

        return banded - lapse;
    }

    /// <summary>
    /// Rainfall from latitude, temperature and elevation, when no raster supplies it.
    /// </summary>
    /// <remarks>
    /// The textbook cells rather than a constant: wet on the equator, dry through the horse
    /// latitudes, wet again in the temperate belt, dry at the poles. Then scaled by warmth,
    /// because cold air carries little moisture — the same correction Phase 1 needed once a
    /// flat offset turned out to guarantee every temperate region was farmable.
    /// </remarks>
    private static double SynthesiseRainfall(double v, double temperature, double height)
    {
        double fromEquator = DetMath.Clamp01(Math.Abs((v * 2.0) - 1.0));

        double banded = fromEquator switch
        {
            < 0.25 => DetMath.Lerp(0.85, 0.20, DetMath.InverseLerp(0.0, 0.25, fromEquator)),
            < 0.60 => DetMath.Lerp(0.20, 0.65, DetMath.InverseLerp(0.25, 0.60, fromEquator)),
            _ => DetMath.Lerp(0.65, 0.15, DetMath.InverseLerp(0.60, 1.0, fromEquator)),
        };

        double warmth = DetMath.InverseLerp(-24.0, 10.0, temperature);
        double orographic = DetMath.Lerp(1.0, 0.55, DetMath.InverseLerp(900.0, 2600.0, height));

        return DetMath.Clamp01(banded * DetMath.Lerp(0.30, 1.0, warmth) * orographic);
    }

    /// <summary>Mountains are where the crust is active, absent anything better to go on.</summary>
    private static double SynthesiseGeology(double height) =>
        DetMath.Clamp01(DetMath.InverseLerp(200.0, 2200.0, height));

    private static double SynthesiseForest(double rainfall, double temperature, double height) =>
        DetMath.Clamp01(
            DetMath.InverseLerp(0.18, 0.62, rainfall) *
            DetMath.InverseLerp(-6.0, 6.0, temperature) *
            DetMath.Lerp(1.0, 0.15, DetMath.InverseLerp(1400.0, 2600.0, height)));

    private static double SynthesiseShrub(double rainfall, double forest) =>
        DetMath.Clamp01(DetMath.InverseLerp(0.06, 0.4, rainfall) * (1.0 - (forest * 0.75)));

    private static Layer? Optional(
        string directory, RasterLayerSpec? spec, string name, IncrementalHash hasher) =>
        spec is null
            ? null
            : new Layer(LoadLayer(directory, spec, name, hasher), spec.Min ?? 0.0, spec.Max ?? 1.0);

    private static RasterGrid LoadLayer(
        string directory, RasterLayerSpec spec, string name, IncrementalHash hasher)
    {
        if (string.IsNullOrWhiteSpace(spec.File))
        {
            throw new InvalidOperationException($"The '{name}' layer has no 'file'.");
        }

        string path = Path.Combine(directory, spec.File);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"The '{name}' layer names '{spec.File}', which is not next to the manifest.", path);
        }

        byte[] bytes = File.ReadAllBytes(path);
        hasher.AppendData(bytes);

        try
        {
            return Netpbm.Read(bytes);
        }
        catch (FormatException ex)
        {
            throw new FormatException($"{path} ('{name}' layer): {ex.Message}", ex);
        }
    }

    private static double Require(double? value, string manifestPath, string layer, string field) =>
        value ?? throw new InvalidOperationException(
            $"{manifestPath}: the '{layer}' layer needs '{field}' — a raster carries no units of " +
            "its own, so the range it spans has to be declared.");

    /// <summary>A plane plus what its extremes mean in the field's own units.</summary>
    private sealed class Layer
    {
        private readonly RasterGrid _grid;
        private readonly double _min;
        private readonly double _max;

        public Layer(RasterGrid grid, double min, double max)
        {
            _grid = grid;
            _min = min;
            _max = max;
        }

        public double At(double u, double v) => DetMath.Lerp(_min, _max, _grid.Sample(u, v));
    }
}
