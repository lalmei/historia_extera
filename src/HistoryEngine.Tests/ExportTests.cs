using System.Text.Json;
using HistoryEngine.Core;
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
        }

        foreach (ExportTradeRoute route in export.TradeRoutes)
        {
            if (route.EndedYear is not null) continue;

            Assert.Equal(Round(route.Traffic), last[(route.Id, "traffic")]);
        }
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

    [Fact]
    public void IndicesAndReferencesResolve()
    {
        WorldExport export = HistoryRun.Execute(TestWorlds.Small()).ToExport();

        foreach (KeyValuePair<string, int[]> entry in export.Indices.EventsByEntity)
        {
            Assert.True(EntityId.TryParse(entry.Key, out EntityId id), $"Bad index key '{entry.Key}'");

            foreach (int eventIndex in entry.Value)
            {
                Assert.InRange(eventIndex, 0, export.Events.Count - 1);

                ExportEvent referenced = export.Events[eventIndex];
                bool mentions =
                    referenced.Subject == id || referenced.Object == id || referenced.Location == id
                    || (referenced.Extra?.Contains(id) ?? false);

                Assert.True(mentions, $"Index claims event {eventIndex} mentions {id}, but it does not.");
            }
        }

        int indexedCount = 0;
        foreach (KeyValuePair<string, int[]> entry in export.Indices.EventsByYear)
        {
            indexedCount += entry.Value.Length;
        }

        Assert.Equal(export.Events.Count, indexedCount);
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
}
