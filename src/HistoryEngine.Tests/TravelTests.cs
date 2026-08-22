using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.Systems;
using HistoryEngine.World;
using Xunit;

namespace HistoryEngine.Tests;

/// <summary>
/// Recorded people leave home and return: trade, visits, pilgrimage, clerical missions.
/// </summary>
public sealed class TravelTests
{
    [Fact]
    public void JourneysAreTripsNotMoves()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard(42)).World;
        int journeys = 0;
        int recorded = 0;
        int trade = 0;

        foreach (HistoryEvent entry in world.Chronicle.Events)
        {
            if (entry.Kind != EventKind.JourneyMade) continue;

            recorded++;
            Assert.Equal(Significance.Routine, entry.Significance);
            Assert.False(entry.Location.IsNone);
        }

        foreach (Figure figure in world.Figures)
        {
            int own = 0;
            foreach (HistoryEvent entry in world.Chronicle.Events)
            {
                if (entry.Kind == EventKind.JourneyMade && entry.Subject == figure.Id) own++;
            }

            Assert.Equal(figure.Journeys.Count, own);

            foreach (Journey journey in figure.Journeys)
            {
                journeys++;
                if (journey.Kind == JourneyKind.Trade) trade++;

                Assert.NotEqual(journey.FromSettlementId, journey.ToSettlementId);
                Assert.True(world.Settlements.Contains(journey.FromSettlementId));
                Assert.True(world.Settlements.Contains(journey.ToSettlementId));
            }
        }

        Assert.Equal(journeys, recorded);
        Assert.True(journeys > 40, $"Only {journeys} journeys were recorded.");
        Assert.True(trade > 0, "No merchant travelled a route.");
    }

    /// <summary>
    /// Some journeys do not end well, and the ones that do not are written where they happened.
    /// </summary>
    /// <remarks>
    /// The whole point of the hazard. If this ever passes with zero mishaps the model has been
    /// tuned into decoration, so the count is asserted rather than the mere absence of a crash.
    /// </remarks>
    [Fact]
    public void SomeJourneysEndBadly()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard(42)).World;

        int waylaid = 0;
        int lost = 0;

        foreach (Figure figure in world.Figures)
        {
            foreach (Journey journey in figure.Journeys)
            {
                switch (journey.Outcome)
                {
                    case JourneyOutcome.Waylaid:
                        waylaid++;
                        break;

                    case JourneyOutcome.Lost:
                        lost++;

                        // They died on that journey, in that year, and not of anything the
                        // mortality pass would have given them.
                        Assert.False(figure.IsAlive);
                        Assert.Equal(journey.Year, figure.DeathYear);
                        Assert.Equal(DeathCause.Accident, figure.DeathCause);
                        Assert.NotNull(figure.DeathDetail);
                        break;
                }
            }
        }

        Assert.True(waylaid > 0, "Three centuries of travel and nobody was ever robbed.");
        Assert.True(lost > 0, "Three centuries of travel and nobody ever failed to come home.");

        int recorded = 0;
        foreach (HistoryEvent entry in world.Chronicle.Events)
        {
            if (entry.Kind != EventKind.JourneyWaylaid) continue;

            recorded++;

            // On the spine, unlike the itinerary it interrupts, and placed somewhere.
            Assert.Equal(Significance.Notable, entry.Significance);
            Assert.False(entry.Location.IsNone);
            Assert.True(entry.Data is not null && entry.Data.ContainsKey("cause"));
        }

        Assert.Equal(waylaid + lost, recorded);
    }

    /// <summary>
    /// A mishap is still not a move: whoever survived one is at home at the end of it.
    /// </summary>
    [Fact]
    public void BeingRobbedDoesNotRelocateAnyone()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard(42)).World;

        foreach (Figure figure in world.Figures)
        {
            foreach (Journey journey in figure.Journeys)
            {
                if (journey.Outcome != JourneyOutcome.Waylaid) continue;

                Assert.NotEqual(journey.ToSettlementId, figure.ResidenceSettlementId);
            }
        }
    }

    /// <summary>
    /// A cut road makes a journey safer, a paved one safer again, and a road that had to bend a
    /// long way round gives some of that back.
    /// </summary>
    /// <remarks>
    /// Asserted on the term itself rather than on mishap counts, because roads reach only a few
    /// per cent of journeys in a three-century run and a rate measured over that many trips would
    /// be noise dressed up as a guarantee.
    /// </remarks>
    [Fact]
    public void ARoadIsSaferThanOpenCountryAndHardCountryTakesSomeBack()
    {
        const double Direct = 200.0;

        double open = TravelSystem.Ground(Route(null), Direct, year: 50);
        double track = TravelSystem.Ground(Route(Cut(RoadGrade.Track, Direct)), Direct, 50);
        double paved = TravelSystem.Ground(Route(Cut(RoadGrade.Paved, Direct)), Direct, 50);
        double winding = TravelSystem.Ground(Route(Cut(RoadGrade.Paved, Direct * 1.5)), Direct, 50);

        Assert.Equal(1.0, open);
        Assert.True(track < open, "A cut track was no safer than open country.");
        Assert.True(paved < track, "An engineered road was no safer than a worn one.");
        Assert.True(winding > paved, "A road forced the long way round cost nothing.");
        Assert.True(winding < open, "Hard country made a road worse than no road at all.");

        // A road cannot make a journey safer in the years before it was cut.
        Assert.Equal(1.0, TravelSystem.Ground(Route(Cut(RoadGrade.Paved, Direct)), Direct, 10));
    }

    private static TradeRoute Route(Road? road) =>
        new(
            EntityId.TradeRoute(1),
            EntityId.Settlement(1),
            EntityId.Settlement(2),
            TradeRouteMode.Overland,
            foundedYear: 20,
            traffic: 0.5)
        {
            Road = road,
        };

    private static Road Cut(RoadGrade grade, double length) =>
        new(
            new[] { new RoadPoint(0, 0), new RoadPoint((int)length, 0) },
            grade,
            builtYear: 30,
            pavedYear: grade == RoadGrade.Paved ? 30 : null,
            length: length);
}
