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
    /// Events must be written in non-decreasing year order.
    /// </summary>
    /// <remarks>
    /// The timeline view and the per-year index both assume it, and it is easy to break by accident:
    /// a system that back-dates an event — recording a ruler's birth in the year they were born
    /// rather than the year they were crowned — would produce a log that jumps backwards. Cheaper to
    /// assert than to discover from a timeline that renders out of sequence.
    /// </remarks>
    [Fact]
    public void EventsAreChronological()
    {
        WorldExport export = HistoryRun.Execute(TestWorlds.Standard()).ToExport();

        for (int i = 1; i < export.Events.Count; i++)
        {
            Assert.True(
                export.Events[i].Year >= export.Events[i - 1].Year,
                $"Event {i} is dated {export.Events[i].Year}, before event {i - 1} at " +
                $"{export.Events[i - 1].Year}. Events must be appended in chronological order.");
        }
    }

    /// <summary>Every index entry must resolve, and every referenced entity must exist.</summary>
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
