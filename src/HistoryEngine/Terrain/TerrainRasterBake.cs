using System.Text.Json;
using System.Text.Json.Serialization;

namespace HistoryEngine.Terrain;

/// <summary>
/// Writes any <see cref="ITerrainSampler"/> out as a raster set.
/// </summary>
/// <remarks>
/// <para><b>Why the engine can export the format it consumes.</b> The raster backend's claim is
/// that terrain can come from somewhere else entirely and the simulation will not notice. Testing
/// that claim against a downloaded map proves it for one map, on one machine, as long as someone
/// keeps the file. Baking Phase 1's own noise world into rasters and reading it back proves it
/// against terrain the test suite can generate from a seed — the same world twice, reached by two
/// completely different routes, and any drift between them is the abstraction leaking.</para>
///
/// <para>It also gives anyone pointing a real generator at the engine a reference set to compare
/// against when their manifest does not load.</para>
///
/// <para><b>Not a simulation cost.</b> Baking samples the raw backend directly rather than going
/// through <see cref="TerrainAtlas"/>, because it is offline tooling that runs instead of a
/// history rather than as part of one. Routing it through the atlas would put a quarter of a
/// million points into the cache the sample budget measures and make the budget meaningless.</para>
/// </remarks>
public static class TerrainRasterBake
{
    /// <summary>Default resolution per axis. On a 4096-unit world that is 8 units per pixel.</summary>
    public const int DefaultResolution = 512;

    private static readonly JsonSerializerOptions ManifestOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Writes a manifest and one PGM per measured field into <paramref name="directory"/>.</summary>
    /// <remarks>
    /// <para><b>Only what the source measures is written.</b> A sampler that declares nothing but
    /// <see cref="TerrainCapabilities.Height"/> still answers every field — the absent ones are
    /// modelled from elevation and latitude. Serialising those answers as planes would turn a
    /// model into a measurement: the reloaded set would declare capabilities its source never
    /// claimed, and the one backend in the project that honestly reports a partial hand would
    /// stop being able to round-trip. A plane is a record of what was measured, so a field the
    /// source cannot measure has no file.</para>
    ///
    /// <para><b>The format covers a square world at the origin.</b> The manifest carries a single
    /// <c>worldSize</c>, so a rectangular or offset sampler is rejected rather than silently
    /// reloaded with its Z extent stretched and its origin discarded.</para>
    /// </remarks>
    /// <returns>The path of the manifest written.</returns>
    public static string Write(ITerrainSampler sampler, string directory, int resolution = DefaultResolution)
    {
        if (resolution < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(resolution), resolution, "Resolution must be at least 2.");
        }

        TerrainBounds bounds = sampler.Bounds;
        if (bounds.MinX != 0 || bounds.MinZ != 0 || bounds.Width != bounds.Height)
        {
            throw new ArgumentException(
                $"The raster format covers a square world at the origin, and this sampler covers "
                + $"{bounds.Width}x{bounds.Height} at ({bounds.MinX}, {bounds.MinZ}). A manifest "
                + "carries one 'worldSize', so baking this would reload as a different shape in a "
                + "different place — quietly, and only visibly as terrain that no longer matches.",
                nameof(sampler));
        }

        TerrainSample[] samples = SampleGrid(sampler, resolution);

        Directory.CreateDirectory(directory);

        var manifest = new TerrainRasterManifest
        {
            WorldSize = bounds.Width,
            Height = WriteHeight(samples, resolution, directory),
            Temperature = Measured(sampler, TerrainCapabilities.Temperature)
                ? WritePlane(samples, resolution, directory, "temperature.pgm", s => s.Temperature)
                : null,
            Rainfall = Measured(sampler, TerrainCapabilities.Rainfall)
                ? WritePlane(samples, resolution, directory, "rainfall.pgm", s => s.Rainfall)
                : null,
            Geology = Measured(sampler, TerrainCapabilities.GeologicActivity)
                ? WritePlane(samples, resolution, directory, "geology.pgm", s => s.GeologicActivity)
                : null,
            Forest = Measured(sampler, TerrainCapabilities.ForestDensity)
                ? WritePlane(samples, resolution, directory, "forest.pgm", s => s.ForestDensity)
                : null,
            Shrub = Measured(sampler, TerrainCapabilities.ShrubDensity)
                ? WritePlane(samples, resolution, directory, "shrub.pgm", s => s.ShrubDensity)
                : null,
            Water = Measured(sampler, TerrainCapabilities.Lakes)
                ? WriteWaterMask(samples, resolution, directory)
                : null,
        };

        string manifestPath = Path.Combine(directory, "terrain.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, ManifestOptions));

        return manifestPath;
    }

    private static bool Measured(ITerrainSampler sampler, TerrainCapabilities field) =>
        sampler.Supports(field);

    /// <summary>
    /// One bulk batch over the whole world, at the raster's own stride.
    /// </summary>
    /// <remarks>
    /// Edges inclusive, so the first and last columns land on the first and last addressable
    /// coordinates — the convention <see cref="RasterGrid"/> reads back.
    /// </remarks>
    private static TerrainSample[] SampleGrid(ITerrainSampler sampler, int resolution)
    {
        TerrainBounds bounds = sampler.Bounds;
        var points = new Point2[resolution * resolution];

        for (int row = 0; row < resolution; row++)
        {
            for (int column = 0; column < resolution; column++)
            {
                int x = bounds.MinX + (int)((long)column * (bounds.Width - 1) / (resolution - 1));
                int z = bounds.MinZ + (int)((long)row * (bounds.Height - 1) / (resolution - 1));

                points[(row * resolution) + column] = bounds.Clamp(x, z);
            }
        }

        var samples = new TerrainSample[points.Length];
        sampler.SampleBatch(points, samples);

        return samples;
    }

    /// <summary>
    /// Writes the height plane and the shoreline value that makes it readable.
    /// </summary>
    /// <remarks>
    /// The sea level written out is wherever zero metres falls in the plane's own range, which is
    /// how a normalised raster is made to honour a datum it cannot represent. Reading it back
    /// inverts the same two-piece map, so the shoreline survives the round trip exactly while
    /// everything else survives it to within one 65,535th of the world's relief.
    /// </remarks>
    private static RasterLayerSpec WriteHeight(
        TerrainSample[] samples, int resolution, string directory)
    {
        double min = double.MaxValue;
        double max = double.MinValue;

        foreach (TerrainSample sample in samples)
        {
            if (sample.Height < min) min = sample.Height;
            if (sample.Height > max) max = sample.Height;
        }

        // A world with no relief at all still has to name a shoreline somewhere.
        if (min >= 0.0) min = -1.0;
        if (max <= 0.0) max = 1.0;

        double span = max - min;
        var values = new float[samples.Length];

        for (int i = 0; i < samples.Length; i++)
        {
            values[i] = (float)((samples[i].Height - min) / span);
        }

        Netpbm.WriteFile(
            Path.Combine(directory, "height.pgm"), new RasterGrid(resolution, resolution, values));

        return new RasterLayerSpec
        {
            File = "height.pgm",
            Min = min,
            Max = max,
            SeaLevel = -min / span,
        };
    }

    private static RasterLayerSpec WritePlane(
        TerrainSample[] samples,
        int resolution,
        string directory,
        string file,
        Func<TerrainSample, float> field)
    {
        double min = double.MaxValue;
        double max = double.MinValue;

        foreach (TerrainSample sample in samples)
        {
            float value = field(sample);
            if (value < min) min = value;
            if (value > max) max = value;
        }

        double span = max - min;
        var values = new float[samples.Length];

        // A constant field quantises to zero everywhere and reads back as its single value,
        // because the manifest carries min and max rather than the plane carrying them.
        if (span > 0.0)
        {
            for (int i = 0; i < samples.Length; i++)
            {
                values[i] = (float)((field(samples[i]) - min) / span);
            }
        }

        Netpbm.WriteFile(
            Path.Combine(directory, file), new RasterGrid(resolution, resolution, values));

        return new RasterLayerSpec { File = file, Min = min, Max = max };
    }

    /// <summary>
    /// Writes the inland-water mask.
    /// </summary>
    /// <remarks>
    /// <para>Ocean is not in the mask: it is implied by negative height, and a backend that
    /// reported it twice could contradict itself.</para>
    ///
    /// <para><b>Written whenever the source declares <see cref="TerrainCapabilities.Lakes"/>,
    /// even if it turned up none.</b> A plane of zeros is not the same statement as a missing
    /// layer — one says there are no lakes here, the other says nobody can tell you. Skipping
    /// the file on a world that happens to be lakeless would quietly downgrade the capability
    /// the reloaded set declares, which is exactly the kind of drift the round trip exists to
    /// detect.</para>
    /// </remarks>
    private static RasterLayerSpec WriteWaterMask(
        TerrainSample[] samples, int resolution, string directory)
    {
        var values = new float[samples.Length];

        for (int i = 0; i < samples.Length; i++)
        {
            if (samples[i].Water == WaterKind.Lake) values[i] = 1f;
        }

        Netpbm.WriteFile(
            Path.Combine(directory, "water.pgm"), new RasterGrid(resolution, resolution, values));

        return new RasterLayerSpec { File = "water.pgm" };
    }
}
