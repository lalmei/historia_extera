using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.Terrain;
using HistoryEngine.World;
using Xunit;

namespace HistoryEngine.Tests;

public sealed class HarvestModelTests
{
    [Fact]
    public void SameRegionAndYearAlwaysGivesTheSameQuality()
    {
        var model = new HarvestModel(4242);
        Region region = SampleRegion();

        for (int year = 1; year <= 50; year++)
        {
            Assert.Equal(model.QualityAt(region, year), model.QualityAt(region, year));
        }
    }

    [Fact]
    public void QualityStaysInUnitInterval()
    {
        var model = new HarvestModel(7);

        foreach (Region region in ManyRegions(40))
        {
            for (int year = 1; year <= 200; year += 7)
            {
                Assert.InRange(model.QualityAt(region, year), 0.0, 1.0);
            }
        }
    }

    /// <summary>
    /// Poor years must actually occur, at a rate that matters but does not dominate.
    /// </summary>
    /// <remarks>
    /// The whole lifecycle depends on this. An earlier harvest field, rescaled straight from
    /// normalised noise, put almost every year near the middle of the range — famine-grade years
    /// were so rare that nothing ever declined and the abandonment path was dead code that still
    /// passed its unit tests.
    /// </remarks>
    [Fact]
    public void FamineGradeYearsAreNeitherAbsentNorTheNorm()
    {
        var model = new HarvestModel(11);
        int famines = 0;
        int total = 0;

        foreach (Region region in ManyRegions(60))
        {
            for (int year = 1; year <= 300; year += 2)
            {
                if (HarvestModel.IsFamine(model.QualityAt(region, year))) famines++;
                total++;
            }
        }

        double share = famines / (double)total;
        Assert.InRange(share, 0.03, 0.35);
    }

    /// <summary>
    /// A poor year must tend to be followed by another, and shared with the neighbours.
    /// </summary>
    /// <remarks>
    /// Independent per-region-per-year rolls would average out: every settlement would have
    /// mediocre years forever and none would suffer a run bad enough to kill it. Correlation is what
    /// turns scattered bad luck into the multi-year regional failure that empties a village.
    /// </remarks>
    [Fact]
    public void PoorYearsClusterInTimeAndSpace()
    {
        var model = new HarvestModel(3);
        Region here = SampleRegion(1000, 1000);
        Region nextDoor = SampleRegion(1128, 1000);

        double sameRegionDrift = 0.0;
        double unrelatedDrift = 0.0;

        for (int year = 1; year <= 240; year++)
        {
            sameRegionDrift += Math.Abs(model.QualityAt(here, year) - model.QualityAt(here, year + 1));
            unrelatedDrift += Math.Abs(model.QualityAt(here, year) - model.QualityAt(here, year + 137));
        }

        Assert.True(
            sameRegionDrift < unrelatedDrift,
            "Consecutive years are no more alike than distant ones, so harvests are uncorrelated in time.");

        double neighbourGap = 0.0;
        double distantGap = 0.0;
        Region farAway = SampleRegion(3600, 3600);

        for (int year = 1; year <= 240; year++)
        {
            neighbourGap += Math.Abs(model.QualityAt(here, year) - model.QualityAt(nextDoor, year));
            distantGap += Math.Abs(model.QualityAt(here, year) - model.QualityAt(farAway, year));
        }

        Assert.True(
            neighbourGap < distantGap,
            "Adjacent regions are no more alike than distant ones, so harvests are uncorrelated in space.");
    }

    private static Region SampleRegion(int x = 512, int z = 512) =>
        new(
            EntityId.Region(0),
            new TerrainBounds(x, z, 128, 128),
            Biome.Grassland,
            fertility: 0.5,
            meanHeight: 200,
            rainfall: 0.5,
            temperature: 14,
            geologicActivity: 0.2,
            isLand: true,
            hasRiver: false,
            isCoastal: false);

    private static IEnumerable<Region> ManyRegions(int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return SampleRegion(((i * 293) % 4096), ((i * 617) % 4096));
        }
    }
}

public sealed class SettlementLifecycleTests
{
    /// <summary>
    /// The lifecycle must actually run its course, not merely be implemented.
    /// </summary>
    /// <remarks>
    /// <para>The point of Milestone 4, and the test that would have caught how long it took to get
    /// there. Growth, promotion, specialization, famine and decline were all written and all
    /// individually correct while the world stayed in permanent expansion, because populations spent
    /// the whole run climbing toward a ceiling they never reached — so a failed harvest only slowed
    /// growth and nothing could ever be lost.</para>
    ///
    /// <para>Asserting that the outcomes appear, rather than that the code paths exist, is the only
    /// version of this test that has teeth.</para>
    /// </remarks>
    [Fact]
    public void AChronicleContainsGrowthSpecializationAndLoss()
    {
        HistoryRun run = HistoryRun.Execute(TestWorlds.Standard());

        var counts = new Dictionary<EventKind, int>();
        foreach (HistoryEvent entry in run.World.Chronicle.Events)
        {
            counts[entry.Kind] = counts.TryGetValue(entry.Kind, out int n) ? n + 1 : 0 + 1;
        }

        int Count(EventKind kind) => counts.TryGetValue(kind, out int n) ? n : 0;

        Assert.True(Count(EventKind.SettlementPromoted) > 20, "Nothing grew.");
        Assert.True(Count(EventKind.SettlementSpecialized) > 10, "Nothing acquired a character.");
        Assert.True(Count(EventKind.SettlementFamine) > 5, "No harvest ever failed.");
        Assert.True(Count(EventKind.SettlementDeclined) > 0, "Nothing ever shrank a tier.");
    }

    /// <summary>Populations must reach their ceiling well inside a chronicle, not at the end of one.</summary>
    /// <remarks>
    /// The single calibration that made the whole lifecycle reachable. If growth is slow enough that
    /// settlements are still climbing at year 300, headroom stays positive and nothing can decline
    /// regardless of how the climate behaves.
    /// </remarks>
    [Fact]
    public void SettlementsReachTheirCeilingAndThenLiveWithTheHarvest()
    {
        HistoryRun run = HistoryRun.Execute(TestWorlds.Standard());

        int matured = 0;
        int depressed = 0;

        foreach (Settlement settlement in run.World.Settlements)
        {
            if (settlement.FoundedYear > 150) continue;

            matured++;
            if (settlement.Population < settlement.PeakPopulation * 0.75) depressed++;
        }

        Assert.True(matured > 5, "Too few long-lived settlements to judge.");
        Assert.True(
            depressed > 0,
            "No settlement founded in the first half ever fell meaningfully below its peak, so " +
            "populations are still growing rather than living at their ceiling.");
    }

    /// <summary>Specialization must reflect terrain, not be scattered at random.</summary>
    [Fact]
    public void SpecializationsFollowTheirTerrain()
    {
        HistoryRun run = HistoryRun.Execute(TestWorlds.Standard());
        WorldState world = run.World;

        int checked_ = 0;

        foreach (Settlement settlement in world.Settlements)
        {
            Region region = world.Regions[settlement.RegionId];

            switch (settlement.Specialization)
            {
                case SettlementSpecialization.Fishing:
                    Assert.True(
                        region.IsCoastal || world.Terrain.Hydrology.IsCoast(settlement.X, settlement.Z),
                        $"{settlement.Name} fishes but is not on a coast.");
                    checked_++;
                    break;

                case SettlementSpecialization.Mining:
                    Assert.True(
                        region.GeologicActivity >= 0.35,
                        $"{settlement.Name} mines ground scoring {region.GeologicActivity:F2}.");
                    checked_++;
                    break;

                case SettlementSpecialization.Crafts:
                    Assert.True(
                        settlement.Tier >= SettlementTier.Town,
                        $"{settlement.Name} is a craft town at tier {settlement.Tier}.");
                    checked_++;
                    break;
            }
        }

        Assert.True(checked_ > 0, "No terrain-gated specializations were produced to check.");
    }

    [Fact]
    public void HamletsHaveNoCharacterAndLargerPlacesDo()
    {
        HistoryRun run = HistoryRun.Execute(TestWorlds.Standard());

        foreach (Settlement settlement in run.World.Settlements)
        {
            if (settlement.Specialization == SettlementSpecialization.None) continue;

            Assert.True(
                settlement.PeakPopulation >= SettlementTiers.PopulationThresholds[(int)SettlementTier.Village],
                $"{settlement.Name} specialized without ever reaching village size.");
            Assert.NotNull(settlement.SpecializedYear);
        }
    }

    /// <summary>Abandonment must never silently take a large, healthy settlement.</summary>
    [Fact]
    public void OnlyDiminishedSettlementsAreAbandoned()
    {
        HistoryRun run = HistoryRun.Execute(TestWorlds.Standard() with { Years = 800 });

        int abandoned = 0;

        foreach (Settlement settlement in run.World.Settlements)
        {
            if (settlement.IsActive) continue;

            abandoned++;
            Assert.True(
                settlement.Population < settlement.PeakPopulation,
                $"{settlement.Name} was abandoned at its peak population.");
        }

        Assert.True(abandoned > 0, "A run of 800 years abandoned nothing at all.");
    }
}
