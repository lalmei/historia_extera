using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Systems;
using HistoryEngine.World;
using Xunit;

namespace HistoryEngine.Tests;

/// <summary>
/// The route graph a journey of more than one hop is actually searched over.
/// </summary>
/// <remarks>
/// Built on hand-placed settlements and routes rather than a full history run, so each scenario
/// controls exactly which legs exist and what they cost — the same reason <c>TravelTests</c> builds
/// its own bare <see cref="TradeRoute"/> for the pure duration and hazard checks.
/// </remarks>
public sealed class ItineraryTests
{
    private const int Year = 50;

    [Fact]
    public void ATwoLegChainIsFoundAndCostsTheSumOfItsLegs()
    {
        WorldState world = NewWorld();
        Settlement a = AddSettlement(world, "A", 0, 0);
        Settlement b = AddSettlement(world, "B", 1200, 0);
        Settlement c = AddSettlement(world, "C", 2400, 0);
        TradeRoute ab = AddRoute(world, a, b);
        TradeRoute bc = AddRoute(world, b, c);

        Itinerary? itinerary = Itineraries.Find(world, a.Id, c.Id, Year);

        Assert.NotNull(itinerary);
        Assert.Equal(new[] { ab.Id, bc.Id }, itinerary!.RouteIds);
        Assert.Equal(new[] { a.Id, b.Id, c.Id }, itinerary.SettlementIds);

        int expected = LegOneWayDays(world, a, b) + LegOneWayDays(world, b, c);
        Assert.Equal(expected, itinerary.OneWayDays);
        Assert.False(itinerary.IsDirect);
    }

    [Fact]
    public void ADirectRouteIsPreferredWhenGenuinelyCheaperThanADetour()
    {
        WorldState world = NewWorld();
        Settlement a = AddSettlement(world, "A", 0, 0);
        Settlement b = AddSettlement(world, "B", 1200, 0);
        Settlement c = AddSettlement(world, "C", 2400, 0);
        AddRoute(world, a, b);
        AddRoute(world, b, c);
        TradeRoute direct = AddRoute(world, a, c, settlementBOverride: null, distanceOverride: 2000);

        Itinerary? itinerary = Itineraries.Find(world, a.Id, c.Id, Year);

        Assert.NotNull(itinerary);
        Assert.True(itinerary!.IsDirect);
        Assert.Equal(direct.Id, Assert.Single(itinerary.RouteIds));
    }

    [Fact]
    public void AnUnreachableDestinationReturnsNullAndDurationFallsBackToTheStraightLine()
    {
        WorldState world = NewWorld();
        Settlement a = AddSettlement(world, "A", 0, 0);
        Settlement isolated = AddSettlement(world, "Isolated", 5000, 5000);

        Itinerary? itinerary = Itineraries.Find(world, a.Id, isolated.Id, Year);
        Assert.Null(itinerary);

        int duration = TravelSystem.DurationDays(world, a.Id, isolated.Id, itinerary, Year);
        double direct = world.Distance(a.X, a.Z, isolated.X, isolated.Z);
        int expected = TravelSystem.DurationDays(
            direct, world.Config.UnitsPerTravelDay, TradeRouteMode.Overland, null);

        Assert.Equal(expected, duration);
    }

    [Fact]
    public void AClosedRouteIsExcludedFromTheSearch()
    {
        WorldState world = NewWorld();
        Settlement a = AddSettlement(world, "A", 0, 0);
        Settlement b = AddSettlement(world, "B", 1200, 0);
        Settlement c = AddSettlement(world, "C", 2400, 0);
        TradeRoute ab = AddRoute(world, a, b);
        AddRoute(world, b, c);
        ab.EndedYear = Year - 1;

        Assert.Null(Itineraries.Find(world, a.Id, c.Id, Year));
    }

    [Fact]
    public void AnAbandonedSettlementIsExcludedFromTheSearch()
    {
        WorldState world = NewWorld();
        Settlement a = AddSettlement(world, "A", 0, 0);
        Settlement b = AddSettlement(world, "B", 1200, 0);
        Settlement c = AddSettlement(world, "C", 2400, 0);
        AddRoute(world, a, b);
        AddRoute(world, b, c);
        b.AbandonedYear = Year - 1;

        Assert.Null(Itineraries.Find(world, a.Id, c.Id, Year));
    }

    /// <summary>The same graph, searched twice, names the same legs in the same order.</summary>
    [Fact]
    public void TheSameSearchReturnsTheIdenticalRouteSequenceEveryTime()
    {
        WorldState world = NewWorld();
        Settlement a = AddSettlement(world, "A", 0, 0);
        Settlement b = AddSettlement(world, "B", 1200, 0);
        Settlement c = AddSettlement(world, "C", 2400, 300);
        Settlement d = AddSettlement(world, "D", 1400, -900);
        AddRoute(world, a, b);
        AddRoute(world, b, c);
        AddRoute(world, a, d);
        AddRoute(world, d, c);

        Itinerary? first = Itineraries.Find(world, a.Id, c.Id, Year);
        Itinerary? second = Itineraries.Find(world, a.Id, c.Id, Year);

        Assert.NotNull(first);
        Assert.Equal(first!.RouteIds, second!.RouteIds);
        Assert.Equal(first.SettlementIds, second.SettlementIds);
        Assert.Equal(first.OneWayDays, second.OneWayDays);
    }

    /// <summary>
    /// Two ways to Carthagi cost exactly the same. The search always resolves the tie the same
    /// way, toward the settlement created first — never toward whichever the heap visited first.
    /// </summary>
    [Fact]
    public void EqualCostPathsBreakTheTieOnSettlementIdEveryTime()
    {
        WorldState world = NewWorld();
        Settlement a = AddSettlement(world, "A", 0, 0);

        // b1 is created (and so indexed) before b2. Both legs to and from each are identical in
        // length, so the two routes through them cost exactly the same and only creation order can
        // tell them apart.
        Settlement b1 = AddSettlement(world, "B1", 0, 1200);
        Settlement b2 = AddSettlement(world, "B2", 1200, 0);
        Settlement c = AddSettlement(world, "C", 1200, 1200);

        TradeRoute ab1 = AddRoute(world, a, b1);
        TradeRoute b1c = AddRoute(world, b1, c);
        AddRoute(world, a, b2);
        AddRoute(world, b2, c);

        for (int attempt = 0; attempt < 5; attempt++)
        {
            // A fresh graph each time, so the result is the search's own tie-break and not an
            // artifact of a cache built once and merely read back.
            Itinerary? itinerary = RouteGraph.Build(world, Year).Search(a.Id, c.Id);

            Assert.NotNull(itinerary);
            Assert.Equal(new[] { ab1.Id, b1c.Id }, itinerary!.RouteIds);
            Assert.Equal(new[] { a.Id, b1.Id, c.Id }, itinerary.SettlementIds);
        }
    }

    /// <summary>The route graph is rebuilt once per year and reused, not once per journey.</summary>
    [Fact]
    public void TheRouteGraphIsCachedPerYear()
    {
        WorldState world = NewWorld();
        Settlement a = AddSettlement(world, "A", 0, 0);
        Settlement b = AddSettlement(world, "B", 1200, 0);
        AddRoute(world, a, b);

        RouteGraph first = world.RouteGraphFor(Year);
        RouteGraph second = world.RouteGraphFor(Year);
        RouteGraph third = world.RouteGraphFor(Year + 1);

        Assert.Same(first, second);
        Assert.NotSame(first, third);
    }

    /// <summary>Duration and hazard read the whole itinerary, not just its first leg.</summary>
    [Fact]
    public void MultiHopDurationAndGroundReadTheWholeItinerary()
    {
        WorldState world = NewWorld();
        Settlement a = AddSettlement(world, "A", 0, 0);
        Settlement b = AddSettlement(world, "B", 1200, 0);
        Settlement c = AddSettlement(world, "C", 2400, 0);
        TradeRoute ab = AddRoute(world, a, b);
        TradeRoute bc = AddRoute(world, b, c);
        ab.Road = Cut(RoadGrade.Paved, 1200);
        bc.Road = Cut(RoadGrade.Track, 1200);

        Itinerary? itinerary = Itineraries.Find(world, a.Id, c.Id, Year);
        Assert.NotNull(itinerary);

        int duration = TravelSystem.DurationDays(world, a.Id, c.Id, itinerary, Year);
        Assert.Equal(Math.Max(2, itinerary!.OneWayDays * 2), duration);

        double direct = world.Distance(a.X, a.Z, c.X, c.Z);
        double ground = TravelSystem.Ground(world, itinerary, direct, Year);
        double open = TravelSystem.Ground(world, null, direct, Year);
        double pavedAlone = TravelSystem.Ground(ab, 1200.0, Year);
        double trackAlone = TravelSystem.Ground(bc, 1200.0, Year);

        Assert.True(ground < open, "A two-leg road was no safer than open country.");

        // Half paved, half track: strictly between what either leg alone would give, since a paved
        // stretch cannot make the track beside it any safer and the track cannot slow the paved
        // stretch down.
        Assert.True(ground > pavedAlone, "The track leg contributed nothing to the mix.");
        Assert.True(ground < trackAlone, "The paved leg contributed nothing to the mix.");
    }

    private static int LegOneWayDays(WorldState world, Settlement from, Settlement to)
    {
        double distance = world.Distance(from.X, from.Z, to.X, to.Z);
        return TravelPace.OneWayDays(distance, world.Config.UnitsPerTravelDay, TradeRouteMode.Overland, null);
    }

    private static WorldState NewWorld() => WorldBuilder.Create(TestWorlds.Small());

    private static Settlement AddSettlement(WorldState world, string name, int x, int z)
    {
        var settlement = new Settlement(
            world.Settlements.NextId,
            world.Civilizations[0].Id,
            world.Regions[0].Id,
            name,
            x,
            z,
            foundedYear: world.StartYear,
            population: 500);

        world.Settlements.Add(settlement);
        return settlement;
    }

    private static TradeRoute AddRoute(
        WorldState world,
        Settlement a,
        Settlement b,
        Settlement? settlementBOverride = null,
        double? distanceOverride = null)
    {
        EntityId bId = settlementBOverride?.Id ?? b.Id;
        var route = new TradeRoute(
            world.TradeRoutes.NextId, a.Id, bId, TradeRouteMode.Overland, foundedYear: world.StartYear, traffic: 0.5);
        world.TradeRoutes.Add(route);

        // distanceOverride exists only to build a direct route shorter than the towns' real
        // positions would give, without having to place a fourth settlement pair just for one test.
        if (distanceOverride is double distance)
        {
            route.Road = Cut(RoadGrade.Track, distance);
        }

        return route;
    }

    private static Road Cut(RoadGrade grade, double length) =>
        new(
            new[] { new RoadPoint(0, 0), new RoadPoint((int)length, 0) },
            grade,
            builtYear: 1,
            pavedYear: grade == RoadGrade.Paved ? 1 : null,
            length: length);
}
