using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.Serialization;
using HistoryEngine.World;
using Xunit;

namespace HistoryEngine.Tests;

/// <summary>Covers the engine/viewer contract: the export document itself.</summary>
public sealed class ExportTests
{
    /// <summary>
    /// Serialise, deserialise, serialise again — the bytes must match.
    /// </summary>
    /// <remarks>
    /// Proves the format is lossless and its ordering is intrinsic rather than incidental. A field
    /// the writer emits but the reader drops shows up here immediately.
    ///
    /// <para>Deliberately a round trip through the export DTOs and not a rehydrated
    /// <see cref="WorldState"/>. The export is a read-only artefact — the viewer consumes it and
    /// the engine never reads it back — so a live-state rebuild would be untested code written to
    /// satisfy a test. Save-and-resume fidelity is covered instead by
    /// <see cref="DeterminismTests.SplittingARunDoesNotChangeIt"/>, which exercises the property
    /// that actually matters.</para>
    /// </remarks>
    [Fact]
    public void ExportRoundTripsLosslessly()
    {
        WorldExport original = HistoryRun.Execute(TestWorlds.Small()).ToExport();

        string first = WorldExporter.ToJson(original);
        string second = WorldExporter.ToJson(WorldExporter.FromJson(first));

        Assert.Equal(first, second);
    }

    /// <summary>
    /// Event ids must equal their index in the event list.
    /// </summary>
    /// <remarks>
    /// The export's indices store integers and the viewer uses them to subscript the event array
    /// directly. If ids ever stopped being positions — through a filtered export, or a system that
    /// wrote events out of order — every cross-link in the viewer would silently point at the wrong
    /// event.
    /// </remarks>
    [Fact]
    public void EventIdsAreTheirOwnIndices()
    {
        WorldExport export = HistoryRun.Execute(TestWorlds.Small()).ToExport();

        for (int i = 0; i < export.Events.Count; i++)
        {
            Assert.Equal(i, export.Events[i].Id);
        }
    }

    /// <summary>
    /// Events must be written in non-decreasing <c>(year, day)</c> order.
    /// </summary>
    /// <remarks>
    /// <para>The timeline view and the per-year index both assume it, and it is easy to break by
    /// accident: a system that back-dates an event — recording a ruler's birth in the year they were
    /// born rather than the year they were crowned — would produce a log that jumps backwards.
    /// Cheaper to assert than to discover from a timeline that renders out of sequence.</para>
    ///
    /// <para><b>Now on the day as well, which is where it will actually break.</b> Every day in a
    /// world is currently zero, so this is the same assertion it has always been — but the moment a
    /// system runs on a season, the total order over systems and the calendar can disagree:
    /// <c>succession</c> runs after <c>war</c> in the system list, so a king who died on day 40
    /// would be appended after a battle fought on day 200. Asserting the pair now means that
    /// disagreement is caught by a test that already exists, in the milestone that introduces it,
    /// rather than found in a viewer.</para>
    /// </remarks>
    [Fact]
    public void EventsAreChronological()
    {
        WorldExport export = HistoryRun.Execute(TestWorlds.Standard()).ToExport();

        for (int i = 1; i < export.Events.Count; i++)
        {
            ExportEvent entry = export.Events[i];
            ExportEvent before = export.Events[i - 1];

            bool ordered = entry.Year > before.Year
                           || (entry.Year == before.Year && entry.Day >= before.Day);

            Assert.True(
                ordered,
                $"Event {i} is dated {entry.Year}.{entry.Day}, before event {i - 1} at "
                + $"{before.Year}.{before.Day}. Events must be appended in chronological order.");
        }
    }

    /// <summary>Every index entry must resolve, and every referenced entity must exist.</summary>
    /// <summary>
    /// Every series must end where the snapshot field beside it does.
    /// </summary>
    /// <remarks>
    /// <para>The export reports a realm's last year twice — once as a field and once as the final
    /// point of its series — and a reader is entitled to the same number in both places. The two
    /// are written by different code at different moments: the observer samples after the systems
    /// have run, the exporter reads the entity once the run is over. This is the assertion that
    /// keeps those two moments the same moment.</para>
    ///
    /// <para>Entities that stopped being sampled are skipped, not excused: a realm that fell in
    /// year 200 has no reading for 201 by design, and asserting against its final field would be
    /// asserting the wrong year rather than testing anything.</para>
    /// </remarks>
    [Fact]
    public void SeriesEndWhereTheSnapshotsDo()
    {
        WorldExport export = HistoryRun.Execute(TestWorlds.Small()).ToExport();

        var last = new Dictionary<(EntityId, string), double>();
        foreach (ExportSeries series in export.Series)
        {
            Assert.NotEmpty(series.Values);
            Assert.InRange(
                series.FromYear + series.Values.Count - 1,
                export.Meta.StartYear,
                export.Meta.EndYear);

            last[(series.Entity, series.Metric)] = series.Values[^1];
        }

        Assert.NotEmpty(last);

        foreach (ExportCivilization civ in export.Civilizations)
        {
            if (civ.EndedYear is not null) continue;

            Assert.Equal(civ.Population, last[(civ.Id, "population")]);
            Assert.Equal(Round(civ.Fortunes.Weariness), last[(civ.Id, "weariness")]);
            Assert.Equal(Round(civ.Fortunes.Grievance), last[(civ.Id, "grievance")]);
            Assert.Equal(Round(civ.EffectiveValues.Aggression), last[(civ.Id, "aggression")]);
            Assert.Equal(Round(civ.EffectiveValues.Learning), last[(civ.Id, "learning")]);
        }

        foreach (ExportSettlement settlement in export.Settlements)
        {
            if (settlement.AbandonedYear is not null) continue;

            Assert.Equal(settlement.Population, last[(settlement.Id, "population")]);
            Assert.Equal(Round(settlement.Fortunes.Weariness), last[(settlement.Id, "weariness")]);
            Assert.Equal(Round(settlement.Fortunes.Calamity), last[(settlement.Id, "calamity")]);
            Assert.Equal(Round(settlement.Fortunes.Triumph), last[(settlement.Id, "triumph")]);
            Assert.Equal(Round(settlement.Fortunes.Grievance), last[(settlement.Id, "grievance")]);
        }

        foreach (ExportTradeRoute route in export.TradeRoutes)
        {
            if (route.EndedYear is not null) continue;

            Assert.Equal(Round(route.Traffic), last[(route.Id, "traffic")]);
        }
    }

    /// <summary>
    /// A settlement's fortunes must actually move, or the tracks are four flat lines dressed as
    /// history.
    /// </summary>
    /// <remarks>
    /// The snapshot-agreement test above would pass if every town's weariness were identically
    /// zero: last year would match the snapshot, both at rest. This is the assertion that sacks,
    /// sieges, plague and cession reach the place they happened to, not only its owner.
    /// </remarks>
    [Fact]
    public void SettlementFortunesMove()
    {
        WorldExport export = HistoryRun.Execute(TestWorlds.Standard()).ToExport();

        var settlements = new HashSet<EntityId>();
        foreach (ExportSettlement settlement in export.Settlements)
        {
            settlements.Add(settlement.Id);
        }

        bool moved = false;
        foreach (ExportSeries series in export.Series)
        {
            if (series.Group != "fortunes") continue;
            if (!settlements.Contains(series.Entity)) continue;

            foreach (double value in series.Values)
            {
                if (value > 0)
                {
                    moved = true;
                    break;
                }
            }

            if (moved) break;
        }

        Assert.True(
            moved,
            "Three centuries left every town's fortunes at rest, so the tracks have nothing to plot.");
    }

    /// <summary>Dials are exported to three decimals; the snapshot fields are not.</summary>
    private static double Round(double value) => Math.Round(value, 3);

    /// <summary>
    /// A periodic world must say so in its export.
    /// </summary>
    /// <remarks>
    /// Nothing downstream can infer it, and everything drawn from coordinates is wrong without
    /// it: a link between a town on the western edge and one on the eastern edge is short in the
    /// simulation and a line clean across the map in anything that has not been told the seam
    /// joins.
    /// </remarks>
    [Fact]
    public void TheExportSaysWhetherTheWorldWraps()
    {
        WorldConfig bounded = TestWorlds.Small();

        Assert.False(HistoryRun.Execute(bounded).ToExport().World.EastWestPeriodic);
        Assert.True(
            HistoryRun.Execute(bounded with { EastWestPeriodic = true })
                .ToExport().World.EastWestPeriodic);
    }

    /// <summary>
    /// Every id an event reports as a reference must be an id the event actually mentions.
    /// </summary>
    /// <remarks>
    /// <para>The export used to carry <c>indices.eventsByEntity</c> and this test checked the
    /// index against the events. The index is gone — the reader rebuilds it in one pass, which
    /// measured at 272 ms on a 310,746-event world against the 3.0 s that parsing the file costs —
    /// but the property the index depended on is the same property the rebuild depends on, and it
    /// belongs to <see cref="HistoryEvent.References"/> rather than to the section that used to be
    /// written from it. So the assertion moves to the source instead of leaving with the index.</para>
    ///
    /// <para>If this ever fails, an entity page shows an event that says nothing about it.</para>
    /// </remarks>
    [Fact]
    public void EveryReportedReferenceIsOneTheEventMentions()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Small()).World;

        foreach (HistoryEvent entry in world.Chronicle.Events)
        {
            foreach (EntityId reference in entry.References())
            {
                bool mentions =
                    entry.Subject == reference || entry.Object == reference
                    || entry.Location == reference || (entry.Extra?.Contains(reference) ?? false);

                Assert.True(
                    mentions,
                    $"Event {entry.Id} reports {reference} as a reference, but does not mention it.");
            }
        }
    }

    /// <summary>
    /// No number in the file is written at more precision than it is read at.
    /// </summary>
    /// <remarks>
    /// Twenty-three thousand numbers on the standard world were written at seventeen digits
    /// because that is what round-trips a double's bits. The rule is three decimals, extended
    /// below 0.1 so a small astronomical ratio keeps three significant digits — see
    /// <see cref="RoundedDoubleJsonConverter"/>. Asserted on the text rather than on the DTOs,
    /// because it is a property of the file.
    /// </remarks>
    [Fact]
    public void NumbersAreWrittenAtThePrecisionTheyAreReadAt()
    {
        string json = WorldExporter.ToJson(HistoryRun.Execute(TestWorlds.Small()).ToExport());

        foreach (Match match in Regex.Matches(json, @"-?\d+\.\d+"))
        {
            double value = double.Parse(match.Value, CultureInfo.InvariantCulture);

            Assert.True(
                value == RoundedDoubleJsonConverter.Round(value),
                $"{match.Value} is written at more precision than the export rounds to.");
        }
    }

    /// <summary>Small magnitudes keep three significant digits rather than losing them.</summary>
    /// <remarks>
    /// A flat three decimals would write a 0.0123-Earth-mass moon as 0.012, a two-percent error in
    /// a number the sky view draws. This is the guard on that.
    /// </remarks>
    [Theory]
    [InlineData(0.7269980808848671, 0.727)]
    [InlineData(0.46676677372268294, 0.467)]
    [InlineData(0.1234567, 0.123)]
    [InlineData(0.012345678, 0.0123)]
    [InlineData(0.00012345678, 0.000123)]
    [InlineData(-0.012345678, -0.0123)]
    [InlineData(0, 0)]
    [InlineData(6939827362.938271, 6939827362.938)]
    [InlineData(9.662825689100475e-11, 9.66e-11)]
    [InlineData(1.2345e-20, 1.2345e-20)]
    public void RoundingKeepsThreeSignificantDigits(double value, double expected)
    {
        Assert.Equal(expected, RoundedDoubleJsonConverter.Round(value));

        // Reading a rounded file and writing it again must produce the same bytes.
        Assert.Equal(expected, RoundedDoubleJsonConverter.Round(expected));
    }

    /// <summary>
    /// An empty list or dictionary is left out of the file rather than written.
    /// </summary>
    /// <remarks>
    /// <para>Twenty-five thousand empty arrays on the standard world, across thirty-one keys, for
    /// campaigns nobody marched on and plots nobody kept. A reader treats an absent container as
    /// empty — the viewer's <c>normalizeExport</c> already did, for older exports — so this costs
    /// nothing to read and a great deal to write.</para>
    ///
    /// <para>The world is small, so the assertion is on the text: no <c>[]</c> and no <c>{}</c>
    /// anywhere in it.</para>
    /// </remarks>
    [Fact]
    public void EmptyContainersAreNotWritten()
    {
        string json = WorldExporter.ToJson(HistoryRun.Execute(TestWorlds.Small()).ToExport());

        Assert.DoesNotContain("[]", json);
        Assert.DoesNotContain("{}", json);
    }

    /// <summary>The derived lookups are gone from the file the reader parses.</summary>
    /// <remarks>
    /// Not a shape detail: the section scaled with the chronicle, and everything in it is one
    /// linear pass over events the reader has in memory already. If it comes back, it comes back
    /// with a measurement.
    /// </remarks>
    [Fact]
    public void TheExportCarriesNoDerivedIndices()
    {
        string json = WorldExporter.ToJson(HistoryRun.Execute(TestWorlds.Small()).ToExport());

        Assert.DoesNotContain("\"indices\"", json);
        Assert.DoesNotContain("eventsByEntity", json);
    }

    [Fact]
    public void EveryEntityReferenceInAnEventExists()
    {
        WorldExport export = HistoryRun.Execute(TestWorlds.Small()).ToExport();

        int Count(EntityKind kind) => kind switch
        {
            EntityKind.Culture => export.Cultures.Count,
            EntityKind.Civilization => export.Civilizations.Count,
            EntityKind.Settlement => export.Settlements.Count,
            EntityKind.Figure => export.Figures.Count,
            EntityKind.Dynasty => export.Dynasties.Count,
            EntityKind.Region => export.Regions.Count,
            EntityKind.TradeRoute => export.TradeRoutes.Count,
            EntityKind.Religion => export.Religions.Count,
            EntityKind.Artifact => export.Artifacts.Count,
            EntityKind.War => export.Wars.Count,
            EntityKind.Battle => export.Battles.Count,
            EntityKind.HolySite => export.HolySites.Count,
            _ => -1,
        };

        foreach (ExportEvent entry in export.Events)
        {
            var slots = new List<EntityId?> { entry.Subject, entry.Object, entry.Location };

            // Extra carries a marriage's two houses and a birth's other parent, so it reaches
            // entities no named slot does — and an id that only ever appears there would otherwise
            // be checked by nothing at all.
            if (entry.Extra is not null)
            {
                foreach (EntityId id in entry.Extra) slots.Add(id);
            }

            foreach (EntityId? slot in slots)
            {
                if (slot is null) continue;

                int available = Count(slot.Value.Kind);
                Assert.True(available >= 0, $"Event {entry.Id} references unknown kind {slot.Value.Kind}");
                Assert.InRange(slot.Value.Index, 0, available - 1);
            }
        }
    }

    /// <summary>The raster's byte planes must be the length its resolution implies.</summary>
    [Fact]
    public void RasterPlanesAreWellFormed()
    {
        WorldExport export = HistoryRun.Execute(TestWorlds.Small()).ToExport();
        ExportRaster raster = export.World.Raster;

        int expected = raster.Resolution * raster.Resolution;

        Assert.Equal(expected, Convert.FromBase64String(raster.Height).Length);
        Assert.Equal(expected, Convert.FromBase64String(raster.Biome).Length);
        Assert.Equal(expected, Convert.FromBase64String(raster.Flags).Length);
        Assert.True(raster.MaxHeight >= raster.MinHeight);
    }

    /// <summary>
    /// The export must carry no wall-clock time.
    /// </summary>
    /// <remarks>
    /// A timestamp anywhere in the document would make byte-identical output impossible and
    /// silently defeat the golden-hash test. Provenance is carried by seed and config hash instead.
    /// </remarks>
    [Fact]
    public void ExportContainsNoTimestamp()
    {
        string json = WorldExporter.ToJson(HistoryRun.Execute(TestWorlds.Small()).ToExport());

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement meta = document.RootElement.GetProperty("meta");

        foreach (JsonProperty property in meta.EnumerateObject())
        {
            Assert.DoesNotContain("generated", property.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("timestamp", property.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("date", property.Name, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void MetaRecordsProvenance()
    {
        WorldConfig config = TestWorlds.Standard(1234);
        WorldExport export = HistoryRun.Execute(config).ToExport();

        Assert.Equal(WorldExport.CurrentSchemaVersion, export.SchemaVersion);
        Assert.Equal(config.Seed, export.Meta.Seed);
        Assert.Equal(config.ConfigHash, export.Meta.ConfigHash);
        Assert.Equal(Narration.SyntaxVersion, export.Meta.NarrationSyntaxVersion);
        Assert.Equal(export.Events.Count, export.Meta.EventCount);
        Assert.NotEmpty(export.Meta.SystemOrder);
    }

    /// <summary>
    /// A journey of more than one route exports the ordered routes and settlements it actually
    /// crossed; a journey with no itinerary at all exports neither.
    /// </summary>
    /// <remarks>
    /// Built on a hand-placed <see cref="Figure"/> rather than a full history run, for the same
    /// reason <c>ItineraryTests</c> builds its own routes: a real run offers no guarantee any
    /// figure ever draws a two-hop trip, and the point here is the export mapping, not the search
    /// that produces an itinerary to map.
    /// </remarks>
    [Fact]
    public void JourneysExportTheirWayAcrossTheRouteNetworkOrNothingAtAll()
    {
        var figure = new Figure(
            EntityId.Figure(0), EntityId.Civilization(0), EntityId.Culture(0), "Traveller",
            Sex.Male, birthYear: 0);

        EntityId townA = EntityId.Settlement(0);
        EntityId townB = EntityId.Settlement(1);
        EntityId townC = EntityId.Settlement(2);
        EntityId routeAb = EntityId.TradeRoute(0);
        EntityId routeBc = EntityId.TradeRoute(1);

        var multiHop = new Itinerary(
            routeIds: new[] { routeAb, routeBc },
            settlementIds: new[] { townA, townB, townC },
            oneWayDays: 10);
        figure.Journeys.Add(new Journey(
            JourneyKind.Trade, new Stamp(10, 0), townA, townC, EntityId.None,
            durationDays: 20, expectedReturn: new Stamp(11, 0), itinerary: multiHop));

        // A journey the search never found a way for at all — the destination is unreached over
        // the network, or the search never ran — carries no itinerary and falls back to the
        // straight line, exactly as every journey did before more than one hop could be searched.
        figure.Journeys.Add(new Journey(
            JourneyKind.Visit, new Stamp(20, 0), townA, townC, EntityId.None,
            durationDays: 6, expectedReturn: new Stamp(21, 0), itinerary: null));

        List<ExportJourney> exported = WorldExporter.BuildJourneys(figure);

        ExportJourney withWay = exported[0];
        Assert.Equal(new[] { routeAb, routeBc }, withWay.RouteIds);
        Assert.Equal(new[] { townA, townB, townC }, withWay.SettlementIds);

        ExportJourney withoutWay = exported[1];
        Assert.Null(withoutWay.RouteIds);
        Assert.Null(withoutWay.SettlementIds);

        // The omission is real at the byte level, not just a null in the DTO: the journey with no
        // itinerary must not carry the keys at all, per the export's empty/absent-container rule.
        string json = JsonSerializer.Serialize(exported, Json.Compact);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement[] elements = document.RootElement.EnumerateArray().ToArray();

        Assert.True(elements[0].TryGetProperty("routeIds", out _));
        Assert.True(elements[0].TryGetProperty("settlementIds", out _));
        Assert.False(elements[1].TryGetProperty("routeIds", out _));
        Assert.False(elements[1].TryGetProperty("settlementIds", out _));
    }
}
