using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.Systems;
using HistoryEngine.World;
using Xunit;

namespace HistoryEngine.Tests;

/// <summary>The commercial network is persistent history, not a proximity calculation.</summary>
public sealed class TradeRouteTests
{
    [Fact]
    public void AFullRunBuildsAValidPersistentNetwork()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard()).World;

        Assert.NotEmpty(world.TradeRoutes);

        var activePairs = new HashSet<(EntityId A, EntityId B)>();
        int opened = 0;

        foreach (HistoryEvent entry in world.Chronicle.Events)
        {
            if (entry.Kind == EventKind.TradeRouteOpened) opened++;
        }

        Assert.Equal(world.TradeRoutes.Count, opened);

        for (int i = 0; i < world.TradeRoutes.Count; i++)
        {
            TradeRoute route = world.TradeRoutes[i];

            Assert.Equal(EntityId.TradeRoute(i), route.Id);
            Assert.True(route.SettlementAId.CompareTo(route.SettlementBId) < 0);
            Assert.True(world.Settlements.Contains(route.SettlementAId));
            Assert.True(world.Settlements.Contains(route.SettlementBId));
            Assert.InRange(route.Traffic, 0.0, 1.0);
            Assert.InRange(route.PeakTraffic, route.Traffic, 1.0);

            Settlement a = world.Settlements[route.SettlementAId];
            Settlement b = world.Settlements[route.SettlementBId];
            Region regionA = world.Regions[a.RegionId];
            Region regionB = world.Regions[b.RegionId];

            if (route.Mode == TradeRouteMode.Coastal)
            {
                Assert.True(regionA.IsCoastal && regionB.IsCoastal);
            }
            else if (route.Mode == TradeRouteMode.River)
            {
                Assert.True(regionA.HasRiver && regionB.HasRiver);
            }

            if (route.IsActive)
            {
                Assert.True(
                    activePairs.Add((route.SettlementAId, route.SettlementBId)),
                    $"More than one active route connects {route.SettlementAId} and {route.SettlementBId}.");
            }
            else
            {
                Assert.Equal(TradeRouteStatus.Closed, route.Status);
                Assert.NotNull(route.EndedYear);
            }
        }
    }

    [Fact]
    public void AbandoningAnEndpointClosesButDoesNotDeleteTheRoute()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard()).World;
        TradeRoute route = world.ActiveTradeRoutes().First();
        Settlement endpoint = world.Settlements[route.SettlementAId];
        // Pick a non-formation year so this test isolates closure from unrelated new routes.
        int year = world.EndYear + 2;

        endpoint.AbandonedYear = year;
        int count = world.TradeRoutes.Count;

        new TradeRouteSystem().Tick(world, year);

        Assert.Equal(count, world.TradeRoutes.Count);
        Assert.Equal(year, route.EndedYear);
        Assert.Equal(TradeRouteStatus.Closed, route.Status);
        Assert.Contains(
            world.Chronicle.Events,
            entry => entry.Kind == EventKind.TradeRouteClosed && entry.Subject == route.Id);
    }
}
