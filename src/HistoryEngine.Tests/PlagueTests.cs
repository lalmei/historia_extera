using System.Globalization;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.Systems;
using HistoryEngine.World;
using Xunit;

namespace HistoryEngine.Tests;

/// <summary>Regression coverage for population- and trade-driven plague exposure.</summary>
public sealed class PlagueTests
{
    private static readonly ulong[] Seeds = { 42, 621005106, 660010830, 2279983006, 2946839904 };

    [Fact]
    public void PopulationAndLiveRoutesIncreaseIgnitionExposure()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard()).World;
        Settlement hub = FindUrbanHub(world);

        var routes = new List<TradeRoute>();
        foreach (TradeRoute route in TradeRoutes.From(world, hub.Id)) routes.Add(route);
        Assert.NotEmpty(routes);

        foreach (TradeRoute route in routes) route.EndedYear = world.EndYear;

        hub.Population = 900;
        hub.Specialization = SettlementSpecialization.None;
        double smallTown = PlagueSystem.IgnitionExposure(world, hub);

        hub.Population = 4000;
        double city = PlagueSystem.IgnitionExposure(world, hub);

        hub.Specialization = SettlementSpecialization.Trade;
        double tradeCity = PlagueSystem.IgnitionExposure(world, hub);

        foreach (TradeRoute route in routes) route.EndedYear = null;
        double connectedTradeCity = PlagueSystem.IgnitionExposure(world, hub);

        Assert.True(city > smallTown, $"City exposure {city:F3} did not exceed town exposure {smallTown:F3}.");
        Assert.True(
            tradeCity > city,
            $"Trade specialization exposure {tradeCity:F3} did not exceed ordinary city exposure {city:F3}.");
        Assert.True(
            connectedTradeCity > tradeCity,
            $"Live-route exposure {connectedTradeCity:F3} did not exceed disconnected exposure {tradeCity:F3}.");
    }

    [Fact]
    public void PopulationTrafficIsContinuousAcrossTheOldTierThresholds()
    {
        Assert.Equal(0.50, PlagueSystem.PopulationTraffic(180), precision: 10);
        Assert.Equal(0.78, PlagueSystem.PopulationTraffic(900), precision: 10);
        Assert.Equal(1.00, PlagueSystem.PopulationTraffic(4000), precision: 10);

        Assert.True(PlagueSystem.PopulationTraffic(899) < PlagueSystem.PopulationTraffic(900));
        Assert.True(PlagueSystem.PopulationTraffic(3999) < PlagueSystem.PopulationTraffic(4000));
        Assert.True(PlagueSystem.PopulationTraffic(8000) > PlagueSystem.PopulationTraffic(4000));
    }

    [Fact]
    public void IgnitionCeilingIsSoftAndMonotonic()
    {
        const double maximum = 0.03;

        double low = PlagueSystem.SoftCap(0.01, maximum);
        double middle = PlagueSystem.SoftCap(0.02, maximum);
        double high = PlagueSystem.SoftCap(0.03, maximum);

        Assert.True(0.0 < low && low < middle && middle < high && high < maximum);
        Assert.True(middle - low > high - middle, "Extra exposure should have diminishing returns.");
    }

    /// <summary>Several worlds keep the intended frequency, reach and abandonment budget.</summary>
    /// <remarks>
    /// The floor is a density budget, not a pin. Faith geography moves the contact network that
    /// plagues travel, and a tenth of an outbreak per world is not a modelling error.
    /// </remarks>
    [Fact]
    public void PlagueBurdenStaysWithinItsHistoricalBudget()
    {
        int began = 0;
        int ended = 0;
        int reached = 0;
        int abandoned = 0;

        foreach (ulong seed in Seeds)
        {
            HistoryRun run = HistoryRun.Execute(TestWorlds.Standard(seed));

            foreach (Settlement settlement in run.World.Settlements)
            {
                if (!settlement.IsActive) abandoned++;
            }

            foreach (HistoryEvent entry in run.World.Chronicle.Events)
            {
                if (entry.Kind == EventKind.PlagueBegan) began++;
                if (entry.Kind != EventKind.PlagueEnded) continue;

                ended++;
                reached += int.Parse(entry.Data!["reached"], CultureInfo.InvariantCulture);
            }
        }

        double outbreaksPerWorld = began / (double)Seeds.Length;
        double settlementsPerCompletedOutbreak = reached / (double)ended;

        Assert.InRange(outbreaksPerWorld, 3.0, 7.0);
        Assert.InRange(settlementsPerCompletedOutbreak, 2.0, 5.0);
        Assert.InRange(began - ended, 0, Seeds.Length * 2);
        Assert.InRange(abandoned, 0, 5);
    }

    private static Settlement FindUrbanHub(WorldState world)
    {
        foreach (Settlement settlement in world.Settlements)
        {
            if (!settlement.IsActive || settlement.Population < 900) continue;
            if (TradeRoutes.Degree(world, settlement.Id) > 0) return settlement;
        }

        throw new InvalidOperationException("The standard world produced no connected urban settlement.");
    }
}
