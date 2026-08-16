using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Terrain;
using HistoryEngine.World;
using Xunit;

namespace HistoryEngine.Tests;

/// <summary>
/// Siting on real terrain: that the ground decides, and that what the record says about a site is
/// true of the ground it stands on.
/// </summary>
/// <remarks>
/// The failure these guard against is the one M10 was built to fix, and it is a quiet one. Nothing
/// crashes when a score stops discriminating — every test passes, every world generates, and the
/// only symptom is that towns stand in arbitrary places. Before this milestone the siting score
/// spread 0.071 across the 64 candidates of a decision while a single boolean spread 0.184, so the
/// choice was effectively made by one flag quantised four times coarser than the choice itself.
/// </remarks>
public sealed class SiteSelectionTests
{
    private static readonly ulong[] Seeds = { 1, 7, 42, 99, 123, 777, 2024, 31337 };

    /// <summary>Eight neighbours a stride out, for measuring the ground under a point.</summary>
    private static readonly (int, int)[] Around =
        { (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1) };

    /// <summary>
    /// The steepest grade onto a spot, measured at the resolution a siting decision refines to.
    /// </summary>
    private static double GradeUnder(WorldState world, int x, int z)
    {
        const int Step = 16;

        double here = world.Terrain.SampleExact(x, z).Height;
        double steepest = 0.0;

        foreach ((int dx, int dz) in Around)
        {
            TerrainSample other = world.Terrain.SampleExact(x + (dx * Step), z + (dz * Step));
            if (other.IsSubmerged) continue;

            double distance = (dx != 0 && dz != 0) ? DetMath.Sqrt(2.0) * Step : Step;
            double grade = Math.Abs(here - other.Height) / distance;
            if (grade > steepest) steepest = grade;
        }

        return steepest;
    }

    /// <summary>
    /// Ground a town cannot be built on stays empty.
    /// </summary>
    /// <remarks>
    /// <para>The headline defect. With no slope term at all — which is what the score had until
    /// M10, because the question was never asked — <b>19.6%</b> of settlements across these seeds
    /// stood on a grade steeper than 1-in-2 and the worst stood on 2:1, a cliff. It is now
    /// <b>5.6%</b>.</para>
    ///
    /// <para>The bound is a ceiling with room, not the measurement: hill towns are real and a
    /// mountainous region should still be settled somewhere, so this asserts that unbuildable
    /// ground is exceptional rather than that it never happens. If a change pushes past this, the
    /// question is whether the slope term still outweighs what pulls against it — chiefly water,
    /// which sits at the bottom of incised valleys and drags candidates onto valley walls.</para>
    /// </remarks>
    [Fact]
    public void SettlementsAvoidGroundTheyCannotBeBuiltOn()
    {
        const double Unbuildable = 0.5;
        const double AllowedShare = 0.12;

        int total = 0;
        int steep = 0;
        double worst = 0.0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Settlement settlement in world.Settlements)
            {
                total++;
                double grade = GradeUnder(world, settlement.X, settlement.Z);
                if (grade > Unbuildable) steep++;
                if (grade > worst) worst = grade;
            }
        }

        double share = steep / (double)total;

        Assert.True(
            share <= AllowedShare,
            $"{share:P1} of {total} settlements stand on a grade steeper than {Unbuildable:P0} "
            + $"(worst {worst:F2}), against {AllowedShare:P0} allowed. Siting has stopped reading "
            + "the ground it is standing on.");
    }

    /// <summary>
    /// What a site is recorded as is true of the terrain under it.
    /// </summary>
    /// <remarks>
    /// The character is written once at founding and read by the viewer as a statement of fact, so
    /// it has to be one. This is the test that stops the label drifting away from the measures that
    /// produced it — the two are computed in the same place today, and a later refactor that
    /// recomputes the character from anything else fails here.
    /// </remarks>
    [Fact]
    public void ASiteNeverLiesAboutItsGround()
    {
        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;
            Hydrology water = world.Terrain.Hydrology;
            Landform land = world.Terrain.Landform;

            // The grid resolves valleys, so "at hand" cannot mean nearer than one cell.
            double atHand = world.Terrain.HydrologyStride;

            foreach (Settlement s in world.Settlements)
            {
                string where = $"{s.Name} (seed {seed}, {s.X},{s.Z})";

                switch (s.Site)
                {
                    case SiteCharacter.Confluence:
                        Assert.True(water.IsConfluence(s.X, s.Z), $"{where} is no confluence.");
                        break;

                    case SiteCharacter.Estuary:
                        Assert.True(water.IsEstuary(s.X, s.Z), $"{where} is no river mouth.");
                        break;

                    case SiteCharacter.Harbour:
                        Assert.True(
                            water.CoastDistance(s.X, s.Z) <= atHand && water.ShelterAt(s.X, s.Z) >= 0.5,
                            $"{where} is recorded as a harbour on water that is neither near nor sheltered.");
                        break;

                    case SiteCharacter.Pass:
                        Assert.True(land.IsPass(s.X, s.Z), $"{where} is no pass.");
                        break;

                    case SiteCharacter.Riverside:
                        Assert.True(
                            water.RiverDistance(s.X, s.Z) <= atHand, $"{where} is not on a river.");
                        break;

                    case SiteCharacter.Coastal:
                        Assert.True(
                            water.CoastDistance(s.X, s.Z) <= atHand, $"{where} is not on the coast.");
                        break;

                    // The one character an errand produces rather than a search, and so the one
                    // that could be written by intent instead of by measurement. A party sent for
                    // ore that ends up sited off the deposit is recorded as ordinary ground.
                    case SiteCharacter.Mine:
                        Assert.True(
                            world.Terrain.SampleExact(s.X, s.Z).GeologicActivity
                                >= Specializations.OreThreshold,
                            $"{where} is recorded as a mine site on ground with no ore under it.");
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Water is worth a distance, not a flag.
    /// </summary>
    /// <remarks>
    /// <para>The defect this milestone exists to fix, pinned directly. A siting decision compares
    /// candidates a quarter of a stride apart, so a measure that answers per grid cell hands
    /// sixteen of them the same number and cannot rank any of them. Asserting that the graded
    /// measures actually vary <em>inside</em> one cell is what stops a later change quietly
    /// reverting them to a nearest-cell read, which would look correct and rank nothing.</para>
    ///
    /// <para>Measured on the distance rather than on <see cref="Hydrology.RiverAccess"/>, which is
    /// flat by design over most of a map: access clamps to zero past the range at which water is
    /// worth walking to, and the median point in these worlds is further from a river than that.
    /// Flat where water is irrelevant is the curve behaving; it is the field underneath it that has
    /// to be continuous everywhere.</para>
    /// </remarks>
    [Fact]
    public void WaterIsGradedWithinASingleGridCell()
    {
        WorldConfig config = TestWorlds.Standard();
        var atlas = new TerrainAtlas(
            new ProceduralTerrainSampler(config.Seed, config.Bounds, config.Terrain),
            config.TerrainStride,
            config.HydrologyStride);

        Hydrology water = atlas.Hydrology;
        int stride = atlas.HydrologyStride;
        int quarter = stride / 4;

        int cellsSeen = 0;
        int cellsThatVary = 0;

        for (int z = stride * 4; z < config.WorldSize - stride; z += stride * 7)
        {
            for (int x = stride * 4; x < config.WorldSize - stride; x += stride * 7)
            {
                cellsSeen++;

                double here = water.RiverDistance(x, z);
                double alongX = water.RiverDistance(x + quarter, z);
                double alongZ = water.RiverDistance(x, z + quarter);

                if (here != alongX || here != alongZ) cellsThatVary++;
            }
        }

        Assert.True(cellsSeen > 0, "The sweep covered no cells.");
        Assert.True(
            cellsThatVary > cellsSeen / 2,
            $"River distance changed inside only {cellsThatVary} of {cellsSeen} grid cells sampled. "
            + "The measure has gone back to answering per cell, which cannot rank the candidates "
            + "of a siting decision — they are a quarter of a cell apart.");
    }

    /// <summary>Water is at distance zero from itself, and the field only grows away from it.</summary>
    [Fact]
    public void DistanceToWaterIsZeroOnTheWater()
    {
        WorldConfig config = TestWorlds.Standard();
        var atlas = new TerrainAtlas(
            new ProceduralTerrainSampler(config.Seed, config.Bounds, config.Terrain),
            config.TerrainStride,
            config.HydrologyStride);

        Hydrology water = atlas.Hydrology;
        int stride = atlas.HydrologyStride;
        int riversFound = 0;

        for (int z = 0; z < config.WorldSize; z += stride)
        {
            for (int x = 0; x < config.WorldSize; x += stride)
            {
                Assert.True(water.RiverDistance(x, z) >= 0.0);
                Assert.True(water.CoastDistance(x, z) >= 0.0);

                if (!water.IsRiver(x, z)) continue;

                riversFound++;
                Assert.Equal(0.0, water.RiverDistance(x, z));
            }
        }

        Assert.True(riversFound > 0, "The world had no rivers to measure from.");
    }

    /// <summary>
    /// The distance field wraps, on a world whose edges are the same meridian.
    /// </summary>
    /// <remarks>
    /// A raster distance transform is normally two scans, forward then backward, and that does not
    /// converge across a seam — a cell's nearest water can lie in the direction the scan has
    /// already passed. Here it is a queue relaxation for exactly this reason, and this is what says
    /// so: a column against the western edge may not be measured as far from water that sits just
    /// across the seam from it.
    /// </remarks>
    [Fact]
    public void DistanceWrapsAcrossTheSeam()
    {
        WorldConfig config = TestWorlds.Standard() with { EastWestPeriodic = true };
        var atlas = new TerrainAtlas(
            new ProceduralTerrainSampler(
                config.Seed, config.Bounds, config.Terrain, eastWestPeriodic: true),
            config.TerrainStride,
            config.HydrologyStride,
            eastWestPeriodic: true);

        Hydrology water = atlas.Hydrology;
        int stride = atlas.HydrologyStride;

        for (int z = 0; z < config.WorldSize; z += stride)
        {
            // The cell just inside the western edge and the one just inside the eastern edge are
            // neighbours on a cylinder, so neither can be much further from water than the other.
            double west = water.CoastDistance(0, z);
            double east = water.CoastDistance(config.WorldSize - stride, z);

            Assert.True(
                Math.Abs(west - east) <= stride * 3.0,
                $"At z={z} the coast is {west:F0} away on one side of the seam and {east:F0} on "
                + "the other. The distance field is not crossing the seam.");
        }
    }

    /// <summary>A pass cuts through country worth going around.</summary>
    /// <remarks>
    /// The gates here were originally on the col itself — that it be high, and steep. Measurement
    /// said otherwise: a col's median height across these seeds is 58–344 m against a base land
    /// height of 520, and its grade is consistently lower than the land around it, because a col is
    /// the low smooth spot you cross at. What has to be formidable is the barrier, not the gap.
    /// </remarks>
    [Fact]
    public void PassesOnlyCutThroughBrokenCountry()
    {
        foreach (ulong seed in new ulong[] { 1, 31337 })
        {
            WorldConfig config = TestWorlds.Standard(seed);
            var atlas = new TerrainAtlas(
                new ProceduralTerrainSampler(config.Seed, config.Bounds, config.Terrain),
                config.TerrainStride,
                config.HydrologyStride);

            Landform land = atlas.Landform;
            int stride = atlas.HydrologyStride;
            int passes = 0;

            for (int z = 0; z < config.WorldSize; z += stride)
            {
                for (int x = 0; x < config.WorldSize; x += stride)
                {
                    if (!land.IsPass(x, z)) continue;

                    passes++;
                    Assert.True(
                        land.RuggednessAt(x, z) > 0.0,
                        $"A pass at ({x},{z}) in seed {seed} cuts through flat country.");
                }
            }

            Assert.True(passes > 0, $"Seed {seed} is mountainous and produced no passes at all.");
        }
    }
}
