using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.Systems;
using HistoryEngine.World;
using Xunit;
using Xunit.Abstractions;

namespace HistoryEngine.Tests;

/// <summary>
/// Recorded people leave home and return: trade, visits, pilgrimage, clerical missions.
/// </summary>
public sealed class TravelTests
{
    private static readonly ulong[] Seeds = { 2, 7, 11, 42, 99 };

    private readonly ITestOutputHelper _output;

    public TravelTests(ITestOutputHelper output) => _output = output;

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
    /// A mishap is still not a move: whoever survived one returned to the home they left.
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

                Assert.Equal(journey.FromSettlementId, journey.ReturnSettlementId);
                Assert.NotEqual(journey.ToSettlementId, journey.ReturnSettlementId);
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

    /// <summary>An engineered surface cannot make the same physical way take longer.</summary>
    [Fact]
    public void APavedRoadIsNotSlowerThanTheTrackItReplaced()
    {
        const double Distance = 240.0;
        const double Pace = 6.0;

        int track = TravelSystem.DurationDays(
            Distance, Pace, TradeRouteMode.Overland, RoadGrade.Track);
        int paved = TravelSystem.DurationDays(
            Distance, Pace, TradeRouteMode.Overland, RoadGrade.Paved);

        Assert.True(paved <= track, $"Paved road took {paved} days; its track took {track}.");
    }

    /// <summary>A built way contributes its stored length; before construction the direct line does.</summary>
    [Fact]
    public void DurationConsumesTheRoadThatExistedAtDeparture()
    {
        WorldState world = WorldBuilder.Create(TestWorlds.Small());
        Settlement from = world.Settlements[0];
        Settlement to = world.Settlements[1];
        double direct = world.Distance(from.X, from.Z, to.X, to.Z);
        TradeRoute route = Route(Cut(RoadGrade.Track, (direct * 2.0) + 20.0));

        int before = TravelSystem.DurationDays(world, from.Id, to.Id, route, year: 10);
        int after = TravelSystem.DurationDays(world, from.Id, to.Id, route, year: 50);

        Assert.True(after > before, $"Road took {after} days; the direct line took {before}.");
    }

    /// <summary>A dated return, rather than the residence field, decides whether someone is home.</summary>
    [Fact]
    public void ATravellerWinteringOverIsNotPresentAtHome()
    {
        WorldState world = WorldBuilder.Create(TestWorlds.Small());
        Civilization realm = world.Civilizations[0];
        Settlement home = world.Settlements[realm.CapitalId];
        Culture culture = world.Cultures[realm.CultureId];
        Figure figure = Houses.NewFigure(
            world, realm, culture, Sex.Female, birthYear: world.StartYear - 30);

        figure.Journeys.Add(new Journey(
            JourneyKind.Trade,
            new Stamp(10, 0),
            home.Id,
            home.Id,
            EntityId.None,
            durationDays: 400,
            expectedReturn: new Stamp(11, 40)));

        Assert.False(world.IsPresentAt(figure, home.Id, new Stamp(10, 359)));
        Assert.False(world.IsPresentAt(figure, home.Id, new Stamp(11, 0)));
        Assert.True(world.IsPresentAt(figure, home.Id, new Stamp(11, 40)));
    }

    /// <summary>Measures the declared pace against the five-seed panel.</summary>
    [Fact]
    public void JourneyDurationsStayMeasuredAndSomeTripsWinterOver()
    {
        int allJourneys = 0;
        int allWintered = 0;

        foreach (ulong seed in Seeds)
        {
            HistoryRun run = HistoryRun.Execute(TestWorlds.Standard(seed));
            var durations = new List<int>();
            int wintered = 0;

            foreach (Figure figure in run.World.Figures)
            {
                foreach (Journey journey in figure.Journeys)
                {
                    durations.Add(journey.DurationDays);
                    Assert.True(journey.DurationDays >= 1);
                    if (journey.Outcome != JourneyOutcome.Stayed)
                    {
                        Assert.True(journey.DurationDays >= 2);
                    }

                    if (journey.Outcome == JourneyOutcome.Lost)
                    {
                        Assert.Null(journey.ReturnYear);
                        Assert.Null(journey.ReturnDay);
                        continue;
                    }

                    Assert.NotNull(journey.ReturnYear);
                    Assert.NotNull(journey.ReturnDay);
                    if (journey.ReturnYear > journey.Year) wintered++;
                }
            }

            durations.Sort();
            Assert.NotEmpty(durations);
            int median = durations[durations.Count / 2];
            int p90 = durations[(durations.Count * 9) / 10];
            _output.WriteLine(
                $"seed {seed}: {durations.Count} journeys; median {median} days, p90 {p90}, "
                + $"range {durations[0]}–{durations[^1]}; {wintered} wintered");

            allJourneys += durations.Count;
            allWintered += wintered;
        }

        Assert.True(allJourneys > 500, $"Only {allJourneys} journeys reached the duration panel.");
        Assert.True(allWintered > 0, "No journey in the panel ever crossed a year boundary.");
    }

    /// <summary>The duration and dated return survive the serialization boundary.</summary>
    [Fact]
    public void JourneyDurationAppearsInTheExport()
    {
        HistoryRun run = HistoryRun.Execute(TestWorlds.Standard(42));
        var export = run.ToExport();

        foreach (Figure figure in run.World.Figures)
        {
            if (figure.Journeys.Count == 0) continue;

            var exported = Assert.Single(export.Figures, item => item.Id == figure.Id).Journeys[0];
            Journey journey = figure.Journeys[0];
            Assert.Equal(journey.DurationDays, exported.DurationDays);
            Assert.Equal(journey.ReturnYear, exported.ReturnYear);
            Assert.Equal(journey.ReturnDay, exported.ReturnDay);
            return;
        }

        Assert.Fail("No journey was available to test the export.");
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

    /// <summary>
    /// Every journey names the thing it was made for, and the line renders as prose.
    /// </summary>
    /// <remarks>
    /// A journey used to read "travelled to Kaarikkagrad, on pilgrimage" — the destination and a
    /// category, and a merchant's page was thirty of them. The export always held the answer in
    /// <see cref="Journey.ViaId"/>; only the template dropped it. This asserts the fact reaches the
    /// prose for every kind, because a wrong kind prefix in a template is a clause that silently
    /// never renders.
    /// </remarks>
    [Fact]
    public void AJourneyNamesWhatItWasFor()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard(42)).World;

        var seen = new HashSet<JourneyKind>();
        int rendered = 0;

        foreach (HistoryEvent entry in world.Chronicle.Events)
        {
            if (entry.Kind != EventKind.JourneyMade) continue;

            var kind = Enum.Parse<JourneyKind>(entry.DataValue("kind")!);
            seen.Add(kind);

            string prose = Narration.Render(entry, world.NameOf, entry.Subject);
            Assert.EndsWith(".", prose);
            Assert.DoesNotContain("  ", prose);

            // Trade, the wander-years, and a campaign march are the errands with nothing to name:
            // the destination is the reason. A march carries no via at all — the host is not a
            // place or a person the chronicle names separately from the battle it marches to.
            if (kind is JourneyKind.Trade or JourneyKind.Wandering or JourneyKind.Campaign) continue;

            string named = world.NameOf(ViaOf(world, entry));
            Assert.Contains(named, prose);
            rendered++;
        }

        Assert.Contains(JourneyKind.Trade, seen);
        Assert.Contains(JourneyKind.Pilgrimage, seen);
        Assert.Contains(JourneyKind.Mission, seen);
        Assert.True(rendered > 40, $"Only {rendered} journeys named their reason.");
    }

    /// <summary>The reason carried on a journey event: the extra that is not where it started.</summary>
    private static EntityId ViaOf(WorldState world, HistoryEvent entry)
    {
        foreach (EntityId id in entry.Extra ?? Array.Empty<EntityId>())
        {
            if (id.Kind != EntityKind.Settlement) return id;
        }

        return EntityId.None;
    }

    private static readonly string[] JourneyUndertakingKinds =
        { "Pilgrimage", "TradeVenture", "MissionaryCircuit", "Embassy" };

    /// <summary>
    /// A pilgrimage, a trade venture, a missionary circuit and an embassy all name their
    /// destination inside <c>{data:objective}</c> — the figure page prints that string with no
    /// separate destination link, so it has to live there. Before this fix the
    /// UndertakingStarted/Completed/Failed templates named the same settlement again in
    /// <c>{location}</c>, producing lines like "undertook an embassy to Shche, bound for Shche."
    /// This checks every one of those events, across several seeds, and fails if the place name
    /// is said more than once anywhere in the rendered line.
    /// </summary>
    [Fact]
    public void UndertakingLifecycleDoesNotNameItsDestinationTwice()
    {
        var seen = new HashSet<string>();
        int checkedCount = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (HistoryEvent entry in world.Chronicle.Events)
            {
                if (entry.Kind is not (EventKind.UndertakingStarted
                    or EventKind.UndertakingCompleted or EventKind.UndertakingFailed))
                {
                    continue;
                }

                string? kind = entry.DataValue("kind");
                if (kind is null || Array.IndexOf(JourneyUndertakingKinds, kind) < 0) continue;
                if (entry.Location.IsNone) continue;

                seen.Add(kind);
                checkedCount++;

                string place = world.NameOf(entry.Location);
                string prose = world.Narrate(entry);
                int mentions = CountOccurrences(prose, place);
                Assert.True(
                    mentions <= 1,
                    $"'{place}' is named {mentions} times in \"{prose}\" ({entry.Kind}, {kind}).");
            }
        }

        Assert.True(checkedCount > 0, "No journey-kind undertaking event was recorded to check.");
        // Not every seed grows every kind of undertaking in a few hundred years; pooling seeds
        // is what earns coverage of all four without pinning the test to one lucky roll.
        Assert.Equal(JourneyUndertakingKinds.Length, seen.Count);
    }

    /// <summary>
    /// A revenge undertaking's objective names an opponent, not a place — "revenge against
    /// Aeda" — so unlike the four journey kinds it carries no destination of its own. Its
    /// UndertakingCompleted/Failed line is the one place the battlefield is said at all, and this
    /// guards against ever suppressing {location} for every undertaking kind rather than only the
    /// four that already say their destination in the objective.
    /// </summary>
    [Fact]
    public void RevengeStillNamesWhereItWasSettled()
    {
        bool found = false;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (HistoryEvent entry in world.Chronicle.Events)
            {
                if (entry.Kind is not (EventKind.UndertakingCompleted or EventKind.UndertakingFailed))
                {
                    continue;
                }

                if (entry.DataValue("kind") != "Revenge") continue;
                if (entry.Location.IsNone) continue;

                string prose = world.Narrate(entry);
                Assert.Contains(world.NameOf(entry.Location), prose);
                found = true;
            }
        }

        Assert.True(found, "No settled revenge undertaking was recorded across the seeds tried.");
    }

    /// <summary>
    /// The second leg of the same embassy reads as a continuation of the first, not as a
    /// different kind of trip — see NextJourney's Embassy case in Undertakings.cs, which was
    /// changed from "on an embassy to" (a phrase that never appeared on the very first leg) to
    /// the same "guest" wording TryVisit used, prefixed with "again".
    /// </summary>
    [Fact]
    public void ARepeatedEmbassyLegReadsAsTheSameKindOfVisitAsTheFirst()
    {
        bool found = false;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (HistoryEvent entry in world.Chronicle.Events)
            {
                if (entry.Kind != EventKind.JourneyMade) continue;
                if (entry.DataValue("purpose") != "again as a guest of") continue;

                string prose = world.Narrate(entry);
                Assert.Contains("again as a guest of", prose);
                Assert.DoesNotContain("embassy", prose);
                found = true;
            }
        }

        Assert.True(found, "No embassy in these seeds ever reached a second leg.");
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        if (string.IsNullOrEmpty(needle)) return 0;

        int count = 0;
        int index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

}
