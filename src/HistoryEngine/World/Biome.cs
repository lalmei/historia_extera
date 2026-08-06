using HistoryEngine.Terrain;

namespace HistoryEngine.World;

/// <summary>
/// Coarse land cover classes. Explicit values — part of the export format.
/// </summary>
public enum Biome
{
    Ocean = 0,
    Lake = 1,
    Glacier = 2,
    Tundra = 3,
    Taiga = 4,
    TemperateForest = 5,
    Grassland = 6,
    Steppe = 7,
    Desert = 8,
    Savanna = 9,
    TropicalForest = 10,
    Wetland = 11,
    Alpine = 12,
}

/// <summary>
/// Classifies a <see cref="TerrainSample"/> into a <see cref="Biome"/>.
/// </summary>
/// <remarks>
/// A simplified Whittaker scheme over temperature and rainfall, with elevation overriding
/// both above the tree line. Pure thresholds, no transcendentals.
///
/// <para>Biome is presentation and flavour — what a region is <em>called</em>, and how the
/// viewer colours it. The simulation scores land on <see cref="TerrainSample.Fertility"/>
/// and the raw fields instead, so that reclassifying biomes cannot silently change where
/// civilizations choose to settle.</para>
/// </remarks>
public static class BiomeClassifier
{
    /// <summary>Elevation above which land is alpine regardless of climate, in metres.</summary>
    private const double TreeLine = 2100.0;

    public static Biome Classify(TerrainSample sample)
    {
        if (sample.Water == WaterKind.Ocean || sample.IsSubmerged) return Biome.Ocean;
        if (sample.Water == WaterKind.Lake) return Biome.Lake;

        if (sample.Height >= TreeLine)
        {
            return sample.Temperature < -8.0 ? Biome.Glacier : Biome.Alpine;
        }

        double t = sample.Temperature;
        double r = sample.Rainfall;

        if (t < -10.0) return Biome.Glacier;
        if (t < -2.0) return Biome.Tundra;

        // Very wet lowland reads as marsh rather than forest.
        if (r > 0.88 && sample.Height < 220.0) return Biome.Wetland;

        if (t < 6.0)
        {
            return r < 0.25 ? Biome.Steppe : Biome.Taiga;
        }

        if (t < 20.0)
        {
            if (r < 0.18) return Biome.Desert;
            if (r < 0.38) return Biome.Steppe;
            if (r < 0.58) return Biome.Grassland;
            return Biome.TemperateForest;
        }

        if (r < 0.20) return Biome.Desert;
        if (r < 0.48) return Biome.Savanna;
        return Biome.TropicalForest;
    }

    /// <summary>Whether a biome can support settled agriculture at all.</summary>
    public static bool IsHabitable(Biome biome) => biome switch
    {
        Biome.Ocean or Biome.Lake or Biome.Glacier or Biome.Alpine => false,
        _ => true,
    };
}
