using HistoryEngine.Terrain;
using HistoryEngine.World;

namespace HistoryEngine.Serialization;

/// <summary>
/// Builds the map raster written into the export.
/// </summary>
/// <remarks>
/// <para><b>This is a presentation cost, and it is budgeted separately on purpose.</b> A
/// 256×256 raster is 65,536 terrain samples — free against Phase 1's noise sampler, roughly a
/// hundred seconds against Vintage Story's. That is fine, because it is not the simulation:
/// Phase 3 can drop the resolution, build the raster off the main thread, or skip it entirely
/// and let the game render its own map. Keeping it out of the simulation's sample budget is what
/// makes that a free choice later rather than a coupled one.</para>
///
/// <para>Sampling still goes through <see cref="TerrainAtlas"/> rather than around it, so the
/// cost is visible to <see cref="CountingTerrainSampler"/> and the points are cached rather than
/// re-sampled.</para>
/// </remarks>
public static class TerrainRaster
{
    public static ExportRaster Build(WorldState world, int resolution)
    {
        if (resolution <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(resolution), resolution, "Resolution must be positive.");
        }

        TerrainBounds bounds = world.Terrain.Bounds;
        Hydrology hydrology = world.Terrain.Hydrology;

        int stride = Math.Max(1, bounds.Width / resolution);
        int cells = resolution * resolution;

        var heights = new double[cells];
        var biomes = new byte[cells];
        var flags = new byte[cells];

        double minHeight = double.MaxValue;
        double maxHeight = double.MinValue;

        for (int row = 0; row < resolution; row++)
        {
            for (int column = 0; column < resolution; column++)
            {
                int x = bounds.MinX + (column * stride);
                int z = bounds.MinZ + (row * stride);

                TerrainSample sample = world.Terrain.SampleExact(x, z);
                int index = (row * resolution) + column;

                heights[index] = sample.Height;
                biomes[index] = (byte)BiomeClassifier.Classify(sample);

                // River presence is still reported per cell so consumers can query it, but the
                // map view draws the vector network instead — at lattice resolution a flag paints
                // a 256-unit block, which looks like a lake rather than a river.
                byte flag = 0;
                if (hydrology.IsRiver(x, z)) flag |= ExportRaster.FlagRiver;
                if (hydrology.IsCoast(x, z)) flag |= ExportRaster.FlagCoast;
                flags[index] = flag;

                if (sample.Height < minHeight) minHeight = sample.Height;
                if (sample.Height > maxHeight) maxHeight = sample.Height;
            }
        }

        if (minHeight > maxHeight)
        {
            minHeight = 0.0;
            maxHeight = 0.0;
        }

        return new ExportRaster(
            Resolution: resolution,
            MinHeight: minHeight,
            MaxHeight: maxHeight,
            Height: Convert.ToBase64String(Quantise(heights, minHeight, maxHeight)),
            Biome: Convert.ToBase64String(biomes),
            Flags: Convert.ToBase64String(flags));
    }

    /// <summary>
    /// Maps heights onto bytes across the world's actual range.
    /// </summary>
    /// <remarks>
    /// The range is exported alongside the bytes, so the viewer can recover metres to within one
    /// 255th of the full span — ample for shading a map, and it keeps sea level meaningful
    /// regardless of how mountainous a particular world turned out.
    /// </remarks>
    private static byte[] Quantise(double[] values, double min, double max)
    {
        var bytes = new byte[values.Length];
        double span = max - min;

        if (span <= 0.0) return bytes;

        for (int i = 0; i < values.Length; i++)
        {
            double normalised = (values[i] - min) / span;
            int quantised = (int)((normalised * 255.0) + 0.5);
            bytes[i] = (byte)Math.Clamp(quantised, 0, 255);
        }

        return bytes;
    }
}
