using HistoryEngine.Entities;
using HistoryEngine.Events;
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
}
