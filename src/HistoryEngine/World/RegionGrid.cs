using HistoryEngine.Core;
using HistoryEngine.Terrain;

namespace HistoryEngine.World;

/// <summary>
/// Builds the region grid from a primed <see cref="TerrainAtlas"/>.
/// </summary>
/// <remarks>
/// Region statistics come from <see cref="TerrainAtlas.SampleCoarse"/> — interpolation of
/// the existing lattice — so the entire grid costs zero terrain samples no matter how many
/// regions it contains. This is the pattern every consumer of terrain should follow, and the
/// reason the atlas primes in bulk up front.
/// </remarks>
public static class RegionGrid
{
    /// <summary>How many points per axis are averaged within each region.</summary>
    private const int SubSamplesPerAxis = 4;

    public static List<Region> Build(TerrainAtlas atlas, int regionSize, EntityTable<Region> table)
    {
        if (regionSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(regionSize), regionSize, "Region size must be positive.");
        }

        atlas.EnsurePrimed();
        Hydrology hydrology = atlas.Hydrology;

        TerrainBounds world = atlas.Bounds;
        int columns = (world.Width + regionSize - 1) / regionSize;
        int rows = (world.Height + regionSize - 1) / regionSize;

        var regions = new List<Region>(columns * rows);

        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                var bounds = new TerrainBounds(
                    world.MinX + (column * regionSize),
                    world.MinZ + (row * regionSize),
                    Math.Min(regionSize, world.MaxX - (world.MinX + (column * regionSize))),
                    Math.Min(regionSize, world.MaxZ - (world.MinZ + (row * regionSize))));

                Region region = Summarise(atlas, hydrology, bounds, table.NextId);
                table.Add(region);
                regions.Add(region);
            }
        }

        LinkNeighbours(regions, columns, rows, atlas.EastWestPeriodic);
        return regions;
    }

    private static Region Summarise(
        TerrainAtlas atlas, Hydrology hydrology, TerrainBounds bounds, EntityId id)
    {
        double heightSum = 0.0;
        double fertilitySum = 0.0;
        double rainfallSum = 0.0;
        double temperatureSum = 0.0;
        double geologySum = 0.0;
        int landCount = 0;
        int total = 0;
        bool hasRiver = false;
        bool isCoastal = false;

        int stepX = Math.Max(1, bounds.Width / SubSamplesPerAxis);
        int stepZ = Math.Max(1, bounds.Height / SubSamplesPerAxis);

        for (int z = bounds.MinZ; z < bounds.MaxZ; z += stepZ)
        {
            for (int x = bounds.MinX; x < bounds.MaxX; x += stepX)
            {
                TerrainSample s = atlas.SampleCoarse(x, z);

                heightSum += s.Height;
                rainfallSum += s.Rainfall;
                temperatureSum += s.Temperature;
                geologySum += s.GeologicActivity;
                total++;

                if (!s.IsSubmerged)
                {
                    landCount++;
                    fertilitySum += s.Fertility;
                }

                if (hydrology.IsRiver(x, z)) hasRiver = true;
                if (hydrology.IsCoast(x, z)) isCoastal = true;
            }
        }

        if (total == 0) total = 1;

        double meanHeight = heightSum / total;
        double meanRainfall = rainfallSum / total;
        double meanTemperature = temperatureSum / total;
        double meanGeology = geologySum / total;

        // A region counts as land when most of it is above water. Fertility averages over
        // the land portion only, so a mostly-ocean region is not scored as barren land.
        bool isLand = landCount * 2 > total;
        double meanFertility = landCount > 0 ? fertilitySum / landCount : 0.0;

        // Classify from the region's averages rather than one arbitrary point, so a
        // region's biome reflects the whole patch.
        var representative = new TerrainSample(
            Height: (float)meanHeight,
            Temperature: (float)meanTemperature,
            Rainfall: (float)meanRainfall,
            GeologicActivity: 0f,
            ForestDensity: 0f,
            ShrubDensity: 0f,
            Water: isLand ? WaterKind.None : WaterKind.Ocean);

        Biome biome = BiomeClassifier.Classify(representative);

        return new Region(
            id,
            bounds,
            biome,
            meanFertility,
            meanHeight,
            meanRainfall,
            meanTemperature,
            meanGeology,
            isLand,
            hasRiver && isLand,
            isCoastal && isLand);
    }

    /// <summary>Four-way adjacency, in a fixed order so expansion walks the grid reproducibly.</summary>
    private static void LinkNeighbours(
        List<Region> regions, int columns, int rows, bool eastWestPeriodic)
    {
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                Region region = regions[(row * columns) + column];

                if (row > 0) AddNeighbour(region, regions[((row - 1) * columns) + column]);

                if (column > 0)
                {
                    AddNeighbour(region, regions[(row * columns) + column - 1]);
                }
                else if (eastWestPeriodic && columns > 1)
                {
                    AddNeighbour(region, regions[(row * columns) + columns - 1]);
                }

                if (column < columns - 1)
                {
                    AddNeighbour(region, regions[(row * columns) + column + 1]);
                }
                else if (eastWestPeriodic && columns > 1)
                {
                    AddNeighbour(region, regions[row * columns]);
                }

                if (row < rows - 1) AddNeighbour(region, regions[((row + 1) * columns) + column]);
            }
        }
    }

    private static void AddNeighbour(Region region, Region neighbour)
    {
        if (region.Id != neighbour.Id && !region.AdjacentRegions.Contains(neighbour.Id))
        {
            region.AdjacentRegions.Add(neighbour.Id);
        }
    }
}
