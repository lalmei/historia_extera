using HistoryEngine.Terrain;
using HistoryEngine.World;
using Xunit;

namespace HistoryEngine.Tests;

public sealed class PeriodicWorldTests
{
    [Fact]
    public void ProceduralTerrainMeetsExactlyAtTheEastWestSeam()
    {
        TerrainBounds bounds = TerrainBounds.Square(1024);
        var terrain = new ProceduralTerrainSampler(
            42, bounds, eastWestPeriodic: true);

        for (int z = bounds.MinZ; z < bounds.MaxZ; z += 73)
        {
            Assert.Equal(terrain.Sample(bounds.MinX, z), terrain.Sample(bounds.MaxX, z));
        }
    }

    [Fact]
    public void PeriodicDistancesTakeTheShortWayAcrossTheSeam()
    {
        TerrainBounds bounds = TerrainBounds.Square(1000);

        Assert.Equal(980.0, bounds.Distance(10, 20, 990, 20, eastWestPeriodic: false));
        Assert.Equal(20.0, bounds.Distance(10, 20, 990, 20, eastWestPeriodic: true));
    }

    [Fact]
    public void EdgeRegionsBecomeNeighboursOnlyInAPeriodicWorld()
    {
        WorldState periodic = Build(eastWestPeriodic: true);
        WorldState bounded = Build(eastWestPeriodic: false);

        Region periodicWest = At(periodic, 0, 0);
        Region periodicEast = At(periodic, 768, 0);
        Assert.Contains(periodicEast.Id, periodicWest.AdjacentRegions);
        Assert.Contains(periodicWest.Id, periodicEast.AdjacentRegions);

        Region boundedWest = At(bounded, 0, 0);
        Region boundedEast = At(bounded, 768, 0);
        Assert.DoesNotContain(boundedEast.Id, boundedWest.AdjacentRegions);
        Assert.DoesNotContain(boundedWest.Id, boundedEast.AdjacentRegions);
    }

    [Fact]
    public void TerrainAtlasWrapsExactAndCoarseSamples()
    {
        TerrainBounds bounds = TerrainBounds.Square(1024);
        var atlas = new TerrainAtlas(
            new ProceduralTerrainSampler(9, bounds, eastWestPeriodic: true),
            stride: 256,
            hydrologyStride: 64,
            eastWestPeriodic: true);

        Assert.Equal(atlas.SampleExact(-1, 400), atlas.SampleExact(1023, 400));
        Assert.Equal(atlas.SampleCoarse(-1, 400), atlas.SampleCoarse(1023, 400));

        _ = atlas.SampleGrid(64, out int columns, out int rows);
        Assert.Equal(16, columns);
        Assert.Equal(17, rows);
    }

    [Fact]
    public void HydrologyRecognisesOceanAcrossThePeriodicSeam()
    {
        TerrainBounds bounds = TerrainBounds.Square(256);
        var periodic = new TerrainAtlas(
            new SeamCoastSampler(bounds),
            stride: 64,
            hydrologyStride: 64,
            eastWestPeriodic: true);
        var bounded = new TerrainAtlas(
            new SeamCoastSampler(bounds),
            stride: 64,
            hydrologyStride: 64,
            eastWestPeriodic: false);

        Assert.True(periodic.Hydrology.IsCoast(0, 128));
        Assert.False(bounded.Hydrology.IsCoast(0, 128));
    }

    private static WorldState Build(bool eastWestPeriodic) =>
        WorldBuilder.Create(new WorldConfig
        {
            Seed = 7,
            Years = 0,
            WorldSize = 1024,
            RegionSize = 256,
            TerrainStride = 256,
            HydrologyStride = 64,
            InitialCivilizations = 0,
            EastWestPeriodic = eastWestPeriodic,
        });

    private static Region At(WorldState world, int minX, int minZ) =>
        world.Regions.Single(region => region.Bounds.MinX == minX && region.Bounds.MinZ == minZ);

    private sealed class SeamCoastSampler(TerrainBounds bounds) : ITerrainSampler
    {
        public TerrainBounds Bounds { get; } = bounds;

        public TerrainCapabilities Capabilities => TerrainCapabilities.Height;

        public TerrainSample Sample(int x, int z) => new(
            Height: x >= Bounds.MaxX - 64 ? -10f : 10f,
            Temperature: 10f,
            Rainfall: 0.5f,
            GeologicActivity: 0f,
            ForestDensity: 0f,
            ShrubDensity: 0f,
            Water: x >= Bounds.MaxX - 64 ? WaterKind.Ocean : WaterKind.None);
    }
}
