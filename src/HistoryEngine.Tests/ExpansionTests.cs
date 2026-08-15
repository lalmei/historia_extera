using HistoryEngine.Entities;
using HistoryEngine.World;
using Xunit;

namespace HistoryEngine.Tests;

/// <summary>Regression coverage for the long-run density produced by territorial expansion.</summary>
public sealed class ExpansionTests
{
    private static readonly ulong[] Seeds = { 42, 621005106, 660010830, 2279983006, 2946839904 };

    /// <summary>
    /// Expansion should fill a world gradually without turning every habitable region into a town.
    /// </summary>
    /// <remarks>
    /// The ranges deliberately describe a density budget rather than pinning one seed's exact
    /// history. The golden export supplies the exact deterministic check; this test says what the
    /// expansion calibration is meant to accomplish across several different land distributions.
    /// </remarks>
    [Fact]
    public void LargeWorldsStayWithinTheSettlementDensityBudget()
    {
        var at100 = new List<int>();
        var at200 = new List<int>();
        var at300 = new List<int>();

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            at100.Add(ActiveAt(world, 100));
            at200.Add(ActiveAt(world, 200));
            at300.Add(ActiveAt(world, 300));

            int habitableRegions = 0;
            foreach (Region region in world.Regions)
            {
                if (region.Habitability >= 0.15) habitableRegions++;
            }

            double settledShare = at300[^1] / (double)habitableRegions;
            Assert.True(
                settledShare <= 0.30,
                $"Seed {seed} settled {at300[^1]} of {habitableRegions} habitable regions " +
                $"({settledShare:P1}); the year-300 density budget is 30%.");
        }

        Assert.InRange(Median(at100), 14, 24);
        Assert.InRange(Median(at200), 35, 55);
        Assert.InRange(Median(at300), 60, 75);
    }

    private static int ActiveAt(WorldState world, int year)
    {
        int count = 0;
        foreach (Settlement settlement in world.Settlements)
        {
            if (settlement.FoundedYear > year) continue;
            if (settlement.AbandonedYear is not null && settlement.AbandonedYear <= year) continue;
            count++;
        }

        return count;
    }

    private static int Median(List<int> values)
    {
        values.Sort();
        return values[values.Count / 2];
    }
}
