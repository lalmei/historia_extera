using HistoryEngine.Core;
using HistoryEngine.Events;
using HistoryEngine.Serialization;
using HistoryEngine.World;
using Xunit;

namespace HistoryEngine.Tests;

/// <summary>
/// Covers the property the viewer's map depends on: <b>who held what, in any year, is
/// replayable from the event log alone</b>.
/// </summary>
/// <remarks>
/// The export ships each region's <i>final</i> owner and nothing else, so a map that shows the
/// world as it stood in year 140 has to rebuild ownership by replaying the chronicle. That only
/// works if every transfer of land is in the log — including the two that are easy to forget,
/// because neither goes through the expansion system: the homeland a realm claims at its
/// founding, and the provinces a realm releases when it ends.
///
/// <para>Asserted here rather than in the viewer because it is the engine's promise. A system
/// added in a later milestone that moves a border without recording it would leave the viewer
/// drawing a border that never existed, and the failure would surface as a map that looks
/// plausible — the worst kind.</para>
/// </remarks>
public sealed class TerritoryTests
{
    [Theory]
    [InlineData(42UL)]
    [InlineData(7UL)]
    [InlineData(99UL)]
    public void EventLogReplaysToTheFinalMap(ulong seed)
    {
        WorldExport export = HistoryRun.Execute(TestWorlds.Standard(seed)).ToExport();

        IReadOnlyDictionary<EntityId, EntityId> replayed = ReplayTo(export, export.Meta.EndYear);

        foreach (ExportRegion region in export.Regions)
        {
            EntityId? owner = replayed.TryGetValue(region.Id, out EntityId held) ? held : null;

            Assert.Equal(region.Owner, owner);
        }
    }

    /// <summary>
    /// Every realm claims its homeland in the year it is founded.
    /// </summary>
    /// <remarks>
    /// Without this the replay starts each realm with no land at all and only catches up once it
    /// expands, which on a slow-growing realm is decades of a blank capital sitting in nobody's
    /// territory.
    /// </remarks>
    [Fact]
    public void EveryRealmClaimsItsHomelandAtItsFounding()
    {
        WorldExport export = HistoryRun.Execute(TestWorlds.Standard()).ToExport();

        foreach (ExportCivilization civilization in export.Civilizations)
        {
            IReadOnlyDictionary<EntityId, EntityId> atFounding =
                ReplayTo(export, civilization.FoundedYear);

            Assert.Contains(atFounding, entry => entry.Value == civilization.Id);
        }
    }

    /// <summary>
    /// A fallen realm holds nothing, in any year after it ended.
    /// </summary>
    /// <remarks>
    /// The case the release events exist for. A realm that ends releases its land in the same
    /// year, and a replay that missed those events would leave a dead realm's colour on the map
    /// for the rest of the run — including over regions a neighbour has since claimed for itself,
    /// which is how the omission would first be noticed.
    /// </remarks>
    [Fact]
    public void NoLandIsHeldByARealmThatHasEnded()
    {
        WorldExport export = HistoryRun.Execute(
            TestWorlds.Standard() with { Years = 800 }).ToExport();

        var endedIn = new Dictionary<EntityId, int>();
        foreach (ExportCivilization civilization in export.Civilizations)
        {
            if (civilization.EndedYear is int ended) endedIn[civilization.Id] = ended;
        }

        Assert.NotEmpty(endedIn);

        var owners = new Dictionary<EntityId, EntityId>();
        int year = export.Meta.StartYear;

        foreach (ExportEvent entry in export.Events)
        {
            // Checked on each year boundary rather than each event: within the year a realm falls,
            // the ending is written before the provinces it gives up, and that ordering is
            // deliberate.
            if (entry.Year != year)
            {
                AssertNoDeadLandlords(owners, endedIn, year);
                year = entry.Year;
            }

            Apply(owners, entry);
        }

        AssertNoDeadLandlords(owners, endedIn, year);
    }

    private static void AssertNoDeadLandlords(
        Dictionary<EntityId, EntityId> owners,
        Dictionary<EntityId, int> endedIn,
        int year)
    {
        foreach ((EntityId regionId, EntityId ownerId) in owners)
        {
            if (endedIn.TryGetValue(ownerId, out int ended) && ended <= year)
            {
                Assert.Fail(
                    $"{regionId} was still held by {ownerId} in year {year}, " +
                    $"but that realm ended in {ended}.");
            }
        }
    }

    /// <summary>Ownership as it stood at the end of <paramref name="year"/>.</summary>
    private static IReadOnlyDictionary<EntityId, EntityId> ReplayTo(WorldExport export, int year)
    {
        var owners = new Dictionary<EntityId, EntityId>();

        foreach (ExportEvent entry in export.Events)
        {
            if (entry.Year > year) break;

            Apply(owners, entry);
        }

        return owners;
    }

    private static void Apply(Dictionary<EntityId, EntityId> owners, ExportEvent entry)
    {
        switch (entry.Kind)
        {
            case EventKind.RegionClaimed:
            case EventKind.RegionCeded:
                if (entry.Subject is EntityId claimed && entry.Object is EntityId claimant)
                {
                    owners[claimed] = claimant;
                }

                break;

            case EventKind.RegionReleased:
                if (entry.Subject is EntityId released) owners.Remove(released);

                break;
        }
    }
}
