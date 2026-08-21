using HistoryEngine.Core;
using HistoryEngine.Terrain;
using HistoryEngine.World;
using Xunit;

namespace HistoryEngine.Tests;

/// <summary>
/// Pins the property that makes a derived river network a network.
/// </summary>
/// <remarks>
/// <para>D8 gives a cell its steepest downhill neighbour or nothing at all, and a cell with
/// nothing at all is a sink: water arrives and never leaves. That is not merely a lost cell.
/// Flow accumulates <em>into</em> a sink, so the wettest cells on the map become the pits, and
/// <c>Hydrology</c> names the wettest few percent of the land as rivers. A world full of sinks
/// therefore reports its puddles as its rivers, entirely plausibly, and the only visible symptom
/// is that the network is sparse and comes apart into fragments.</para>
///
/// <para>The Phase 2 terrain trial found exactly that on an external generator's terrain — 26 of
/// 41 river cells were sinks — and found it only because there was a second backend to compare
/// against. Phase 1's value noise is smooth by construction and never produced enough sinks for
/// anyone to look. These tests exist so that the next backend does not have to rediscover it:
/// they assert on terrain designed to be hostile, not on terrain that happens to be kind.</para>
/// </remarks>
public sealed class HydrologyTests
{
    /// <summary>
    /// Every named river leaves the map. No river cell is a dead end.
    /// </summary>
    /// <remarks>
    /// Asserted through <see cref="Hydrology.RiverSegments"/> rather than the flow graph, because
    /// a segment is emitted for a river cell exactly when that cell has somewhere downstream to
    /// send its water. One segment per river node is therefore the same statement as "no river is
    /// a sink", said in terms the export already carries.
    /// </remarks>
    [Fact]
    public void EveryRiverCellDrainsSomewhere()
    {
        WorldConfig config = TestWorlds.Standard();
        var atlas = new TerrainAtlas(
            new ProceduralTerrainSampler(config.Seed, config.Bounds, config.Terrain),
            config.TerrainStride,
            config.HydrologyStride);

        Hydrology water = atlas.Hydrology;

        int nodes = water.RiverNodeCount;
        int segments = 0;
        foreach (Hydrology.RiverSegment _ in water.RiverSegments()) segments++;

        Assert.True(nodes > 0, "This world has no rivers at all, so the assertion below is vacuous.");
        Assert.True(
            segments == nodes,
            $"{nodes - segments} of {nodes} river cells have no downstream neighbour. Flow " +
            "accumulates into a sink, so those cells are named rivers because they are pits — " +
            "check that FillDepressions still runs ahead of ComputeFlowDirections.");
    }

    /// <summary>
    /// The same, on terrain built specifically to defeat it.
    /// </summary>
    /// <remarks>
    /// A conical island with a closed bowl carved into its summit: a genuine depression, entirely
    /// above sea level, whose floor collects every drop that falls inside the rim and has nowhere
    /// to send it. Without a fill the bowl is the wettest point on the island by a wide margin and
    /// is duly reported as its principal river.
    /// </remarks>
    [Fact]
    public void AClosedBasinDoesNotBecomeARiver()
    {
        var sampler = new CraterIsland();
        var atlas = new TerrainAtlas(sampler, 256, 64);

        Hydrology water = atlas.Hydrology;

        int nodes = water.RiverNodeCount;
        int segments = 0;
        foreach (Hydrology.RiverSegment _ in water.RiverSegments()) segments++;

        Assert.True(nodes > 0, "The crater island has no rivers, so this proves nothing.");
        Assert.Equal(nodes, segments);

        Assert.False(
            water.IsRiver(CraterIsland.Centre, CraterIsland.Centre),
            "The floor of a closed basin is reported as a river. Flow is accumulating into the " +
            "pit rather than over its rim.");
    }

    /// <summary>
    /// Filling changes where water goes, not how high the ground is.
    /// </summary>
    /// <remarks>
    /// The spill surface is a drainage construct and must not escape into the terrain: a basin
    /// floor is still at its real elevation for siting, fertility and everything else that asks
    /// the atlas how high the ground is. Sampling the crater floor has to answer with the crater
    /// floor.
    /// </remarks>
    [Fact]
    public void FillingDoesNotRaiseTheGround()
    {
        var sampler = new CraterIsland();
        var atlas = new TerrainAtlas(sampler, 256, 64);

        _ = atlas.Hydrology;

        TerrainSample floor = atlas.SampleExact(CraterIsland.Centre, CraterIsland.Centre);

        Assert.Equal(
            sampler.Sample(CraterIsland.Centre, CraterIsland.Centre).Height, floor.Height);
    }

    /// <summary>
    /// A cone of land with a bowl in its summit, defined analytically.
    /// </summary>
    /// <remarks>
    /// Written out rather than baked from noise so that the depression is unmistakably there and
    /// unmistakably closed. Height falls linearly from the summit to the shoreline and keeps
    /// falling into the sea beyond it, so the island needs no separate ocean mask.
    /// </remarks>
    private sealed class CraterIsland : ITerrainSampler
    {
        public const int Size = 2048;

        public const int Centre = Size / 2;

        private const double Shore = 700.0;

        private const double Rim = 200.0;

        public TerrainBounds Bounds { get; } = TerrainBounds.Square(Size);

        public TerrainCapabilities Capabilities => TerrainCapabilities.Height;

        public TerrainSample Sample(int x, int z)
        {
            double dx = x - Centre;
            double dz = z - Centre;
            double radius = DetMath.Sqrt((dx * dx) + (dz * dz));

            // A cone that crosses zero at the shoreline, minus a bowl inside the rim. The bowl
            // is twice as steep as the cone, so its floor sits well below the rim around it.
            double height = Shore - radius;
            if (radius < Rim) height -= (Rim - radius) * 2.0;

            return new TerrainSample(
                Height: (float)height,
                Temperature: 14f,
                Rainfall: 0.5f,
                GeologicActivity: 0.3f,
                ForestDensity: 0.3f,
                ShrubDensity: 0.3f,
                Water: height < 0.0 ? WaterKind.Ocean : WaterKind.None);
        }
    }
}
