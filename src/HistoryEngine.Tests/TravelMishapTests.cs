using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.Systems;
using HistoryEngine.World;
using Xunit;

namespace HistoryEngine.Tests;

/// <summary>
/// Which leg of a multi-hop itinerary a road mishap is charged to, and how that leg's own facts —
/// its route, its surface, the towns either side of it — flow into the chronicle.
/// </summary>
/// <remarks>
/// Built on hand-placed settlements and routes, the same reason <see cref="ItineraryTests"/> is:
/// a mishap's hazard roll is small, so these scenarios sweep many travellers and years to find the
/// unlucky ones rather than trying to predict a single draw.
/// </remarks>
public sealed class TravelMishapTests
{
    private const int Year = 50;

    /// <summary>A multi-hop mishap is charged to one of the itinerary's own routes, never to none.</summary>
    [Fact]
    public void MultiHopMishapAttachesToAnItineraryRouteId()
    {
        WorldState world = NewWorld();
        Settlement a = AddSettlement(world, "A", 0, 0);
        Settlement b = AddSettlement(world, "B", 1500, 0);
        Settlement c = AddSettlement(world, "C", 3000, 0);
        TradeRoute ab = AddRoute(world, a, b, TradeRouteMode.Overland);
        TradeRoute bc = AddRoute(world, b, c, TradeRouteMode.Overland);

        Itinerary? itinerary = Itineraries.Find(world, a.Id, c.Id, Year);
        Assert.NotNull(itinerary);
        Assert.Equal(2, itinerary!.RouteIds.Count);

        (HistoryEvent found, _) = FirstMishap(world, a.Id, c.Id, itinerary, JourneyKind.Pilgrimage);

        Assert.NotNull(found.Extra);
        Assert.Equal(2, found.Extra!.Count);
        EntityId routeId = found.Extra[1];
        Assert.Contains(routeId, itinerary.RouteIds);

        // The settlement named alongside the route is the leg's own nearer end, not always the
        // journey's origin — the itinerary's second entry pins that down for the two-leg case.
        EntityId settlementId = found.Extra[0];
        Assert.True(settlementId == a.Id || settlementId == b.Id);
        Assert.True(
            (routeId == ab.Id && settlementId == a.Id) || (routeId == bc.Id && settlementId == b.Id));
    }

    /// <summary>
    /// With no itinerary at all — the destination unreachable over the network — a mishap still
    /// reads as wilderness and attaches only to the origin, exactly as before the network could be
    /// searched past one hop.
    /// </summary>
    [Fact]
    public void NoItineraryStillDrawsWildMishapsAttachedToTheOrigin()
    {
        WorldState world = NewWorld();
        Settlement a = AddSettlement(world, "A", 0, 0);
        Settlement isolated = AddSettlement(world, "Isolated", 5000, 5000);

        Assert.Null(Itineraries.Find(world, a.Id, isolated.Id, Year));

        (HistoryEvent found, string cause) =
            FirstMishap(world, a.Id, isolated.Id, itinerary: null, JourneyKind.Pilgrimage);

        Assert.Contains(TravelSystem.WildMishaps, mishap => mishap.Clause == cause);
        Assert.NotNull(found.Extra);
        Assert.Equal(new[] { a.Id }, found.Extra);
    }

    /// <summary>
    /// A journey that goes overland and then along a coastal route can drown only on the coastal
    /// stretch: <c>afloat</c> is a fact about the leg a mishap fell on, not about the journey's
    /// endpoints.
    /// </summary>
    [Fact]
    public void AfloatFollowsTheChosenLegNotTheEndpoints()
    {
        WorldState world = NewWorld();
        Settlement a = AddSettlement(world, "A", 0, 0);
        Settlement b = AddSettlement(world, "B", 1500, 0);
        Settlement c = AddSettlement(world, "C", 3000, 0);
        TradeRoute ab = AddRoute(world, a, b, TradeRouteMode.Overland);
        TradeRoute bc = AddRoute(world, b, c, TradeRouteMode.Coastal);

        Itinerary? itinerary = Itineraries.Find(world, a.Id, c.Id, Year);
        Assert.NotNull(itinerary);
        Assert.Equal(new[] { ab.Id, bc.Id }, itinerary!.RouteIds);

        // TradeRoutes.Between(a, c) finds no single corridor joining the endpoints directly, which
        // is exactly the incoherence being fixed: the stale reading would have called this journey
        // unable to drown at all.
        Assert.Null(TradeRoutes.Between(world, a.Id, c.Id));

        bool sawOverlandMishap = false;
        bool sawCoastalMishap = false;

        for (int figureId = 0; figureId < 4000 && !(sawOverlandMishap && sawCoastalMishap); figureId++)
        {
            Figure figure = AddFigure(world, "Traveller");
            var journey = new Journey(
                JourneyKind.Pilgrimage, Stamp.Opening(Year), a.Id, c.Id, EntityId.None,
                durationDays: 10, expectedReturn: Stamp.Opening(Year), itinerary);

            int before = world.Chronicle.Events.Count;
            TravelSystem.Resolve(world, figure, journey, Year);
            if (world.Chronicle.Events.Count == before) continue;

            HistoryEvent recorded = world.Chronicle.Events[^1];
            EntityId routeId = recorded.Extra![1];
            bool causeIsWater = Array.Exists(
                TravelSystem.WaterMishaps, m => m.Clause == recorded.DataValue("cause"));

            if (routeId == bc.Id)
            {
                sawCoastalMishap = true;
                Assert.True(causeIsWater, "The coastal leg's mishap was not drawn from the water set.");
            }
            else if (routeId == ab.Id)
            {
                sawOverlandMishap = true;
                Assert.False(causeIsWater, "The overland leg's mishap was drawn from the water set.");
            }
        }

        Assert.True(sawOverlandMishap, "Never saw a mishap fall on the overland leg in range.");
        Assert.True(sawCoastalMishap, "Never saw a mishap fall on the coastal leg in range.");
    }

    /// <summary>An intermediate lawless town on a multi-hop route can be blamed for a robbery.</summary>
    [Fact]
    public void AnIntermediateLawlessTownCanBeBlamedForARobbery()
    {
        WorldState world = NewWorld();
        Settlement a = AddSettlement(world, "A", 0, 0);
        Settlement b = AddSettlement(world, "B", 1500, 0);
        Settlement c = AddSettlement(world, "C", 3000, 0);
        AddRoute(world, a, b, TradeRouteMode.Overland);
        AddRoute(world, b, c, TradeRouteMode.Overland);

        // Neither endpoint is lawless; only the town in the middle is. Before this fix, `Lawless`
        // read only the two ends and could never have named B.
        a.Banditry = 0.0;
        b.Banditry = 0.9;
        c.Banditry = 0.0;

        Itinerary? itinerary = Itineraries.Find(world, a.Id, c.Id, Year);
        Assert.NotNull(itinerary);

        bool sawBlame = false;

        for (int figureId = 0; figureId < 4000 && !sawBlame; figureId++)
        {
            Figure figure = AddFigure(world, "Traveller");
            var journey = new Journey(
                JourneyKind.Pilgrimage, Stamp.Opening(Year), a.Id, c.Id, EntityId.None,
                durationDays: 10, expectedReturn: Stamp.Opening(Year), itinerary);

            int before = world.Chronicle.Events.Count;
            TravelSystem.Resolve(world, figure, journey, Year);
            if (world.Chronicle.Events.Count == before) continue;

            HistoryEvent recorded = world.Chronicle.Events[^1];
            string? cause = recorded.DataValue("cause");
            if (cause is not null && cause.Contains("brigands out of B"))
            {
                sawBlame = true;
            }
        }

        Assert.True(sawBlame, "Never saw a robbery blamed on the lawless town in the middle.");
    }

    /// <summary>
    /// The leg-choice substream is independent of the traveller's own hazard/fatality/plunder
    /// stream: forking and drawing from it does not move a single draw on <c>travel.road</c>.
    /// </summary>
    [Fact]
    public void LegChoiceDoesNotPerturbTheRoadStream()
    {
        WorldState world = NewWorld();
        Figure figure = AddFigure(world, "Traveller");

        // The exact fork Resolve makes for its hazard/fatality/plunder draws.
        IRng roadAlone = world.Root.Fork("travel.road", figure.Id.ToDiscriminator()).Fork("year", Year);
        double[] untouched = { roadAlone.NextDouble(), roadAlone.NextDouble(), roadAlone.NextDouble() };

        // The same fork, but only after the leg-choice substream was forked and drawn from first —
        // mirroring what Resolve now does for a journey with an itinerary.
        IRng legChoice =
            world.Root.Fork("travel.mishap-leg", figure.Id.ToDiscriminator()).Fork("year", Year);
        var weights = new[] { 1.0, 2.0, 3.0 };
        legChoice.PickWeighted(new[] { 0, 1, 2 }, i => weights[i]);

        IRng roadAfter = world.Root.Fork("travel.road", figure.Id.ToDiscriminator()).Fork("year", Year);
        double[] afterLegDraw = { roadAfter.NextDouble(), roadAfter.NextDouble(), roadAfter.NextDouble() };

        Assert.Equal(untouched, afterLegDraw);
    }

    /// <summary>The same journey, resolved on two freshly built but identical worlds, picks the same leg.</summary>
    [Fact]
    public void TheSameJourneyResolvedTwicePicksTheSameLeg()
    {
        (HistoryEvent firstEvent, string firstCause) = ResolveOnAFreshChain();
        (HistoryEvent secondEvent, string secondCause) = ResolveOnAFreshChain();

        Assert.Equal(firstEvent.Extra, secondEvent.Extra);
        Assert.Equal(firstCause, secondCause);
    }

    /// <summary>Builds the same two-leg chain and resolves the same journey once, for the determinism check.</summary>
    private static (HistoryEvent, string) ResolveOnAFreshChain()
    {
        WorldState world = NewWorld();
        Settlement a = AddSettlement(world, "A", 0, 0);
        Settlement b = AddSettlement(world, "B", 1500, 0);
        Settlement c = AddSettlement(world, "C", 3000, 0);
        AddRoute(world, a, b, TradeRouteMode.Overland);
        AddRoute(world, b, c, TradeRouteMode.Overland);

        Itinerary? itinerary = Itineraries.Find(world, a.Id, c.Id, Year);
        Assert.NotNull(itinerary);

        (HistoryEvent found, string cause) = FirstMishap(world, a.Id, c.Id, itinerary, JourneyKind.Pilgrimage);
        return (found, cause);
    }

    /// <summary>Sweeps travellers until one of them comes to grief, and returns that chronicle entry.</summary>
    private static (HistoryEvent, string) FirstMishap(
        WorldState world, EntityId fromId, EntityId toId, Itinerary? itinerary, JourneyKind kind)
    {
        for (int figureId = 0; figureId < 4000; figureId++)
        {
            Figure figure = AddFigure(world, "Traveller");
            var journey = new Journey(
                kind, Stamp.Opening(Year), fromId, toId, EntityId.None,
                durationDays: 10, expectedReturn: Stamp.Opening(Year), itinerary);

            int before = world.Chronicle.Events.Count;
            TravelSystem.Resolve(world, figure, journey, Year);
            if (world.Chronicle.Events.Count > before)
            {
                HistoryEvent recorded = world.Chronicle.Events[^1];
                return (recorded, recorded.DataValue("cause") ?? string.Empty);
            }
        }

        throw new InvalidOperationException("No mishap occurred in range — widen the sweep.");
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

    private static TradeRoute AddRoute(WorldState world, Settlement a, Settlement b, TradeRouteMode mode)
    {
        var route = new TradeRoute(
            world.TradeRoutes.NextId, a.Id, b.Id, mode, foundedYear: world.StartYear, traffic: 0.5);
        world.TradeRoutes.Add(route);
        return route;
    }

    private static Figure AddFigure(WorldState world, string name)
    {
        var figure = new Figure(
            world.Figures.NextId,
            world.Civilizations[0].Id,
            world.Civilizations[0].CultureId,
            name,
            Sex.Male,
            birthYear: world.StartYear - 30)
        {
            ResidenceSettlementId = world.Settlements[0].Id,
        };

        world.Figures.Add(figure);
        return figure;
    }
}
