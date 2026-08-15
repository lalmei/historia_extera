using System.Globalization;
using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;

namespace HistoryEngine.World;

/// <summary>
/// What happens to a polity as a whole: land changing hands, and the end of a realm.
/// </summary>
/// <remarks>
/// <para>Shared because a realm can end two entirely different ways and both must leave the world
/// in the same state. The settlement lifecycle ends one whose last village emptied against a
/// failing climate; a war ends one whose last town was taken at a peace table. If each wrote its
/// own ending, one of them would eventually forget to release the ruler's title or the dead
/// realm's territory, and the symptom would be a chronicle that quietly stops rather than an
/// error anyone can find.</para>
///
/// <para><b>Nothing is deleted.</b> A fallen realm keeps its id, its rulers, its settlements and
/// every event that ever named it. It gains an <see cref="Civilization.EndedYear"/> and stops
/// being iterated.</para>
/// </remarks>
public static class Realms
{
    /// <summary>
    /// Moves a region, and anything standing in it, from one realm to another.
    /// </summary>
    /// <remarks>
    /// <para>Territory and settlements move together. Ceding the region alone would leave a town
    /// flying the loser's flag inside the winner's border, feeding the loser's population and
    /// counting against the loser's fall condition — so a realm could be stripped of every region
    /// it held and still not have lost anything.</para>
    ///
    /// <para>A seat of government that changes hands is cleared rather than reassigned. The
    /// succession system repoints a realm with no capital to its largest surviving settlement the
    /// next time it runs, which is exactly the right behaviour and already written.</para>
    /// </remarks>
    public static void Cede(
        WorldState world,
        Region region,
        Civilization from,
        Civilization to,
        int year,
        War war)
    {
        region.Owner = to.Id;
        from.TerritoryRegionIds.Remove(region.Id);
        if (!to.TerritoryRegionIds.Contains(region.Id)) to.TerritoryRegionIds.Add(region.Id);

        EntityId taken = EntityId.None;

        // Collected first: transferring while walking the list it is read from would skip entries.
        var moving = new List<Settlement>();
        foreach (EntityId settlementId in from.SettlementIds)
        {
            Settlement settlement = world.Settlements[settlementId];
            if (settlement.IsActive && settlement.RegionId == region.Id) moving.Add(settlement);
        }

        foreach (Settlement settlement in moving)
        {
            if (settlement.IsCapital)
            {
                settlement.IsCapital = false;
                if (from.CapitalId == settlement.Id) from.CapitalId = EntityId.None;
            }

            settlement.CivilizationId = to.Id;
            from.SettlementIds.Remove(settlement.Id);
            if (!to.SettlementIds.Contains(settlement.Id)) to.SettlementIds.Add(settlement.Id);

            // The largest of several is the one the chronicle names.
            if (taken.IsNone || settlement.Population > world.Settlements[taken].Population)
            {
                taken = settlement.Id;
            }
        }

        war.CededRegionIds.Add(region.Id);

        // Ground changing hands is the one loss this engine models that a realm does not merely
        // recover from: it answers the taker's own older grievances and opens one on the far side
        // that outlives everyone who remembers the war.
        from.Fortunes.LandLost();
        to.Fortunes.LandTaken();

        world.Chronicle.Record(
            year,
            EventKind.RegionCeded,
            region.Id,
            obj: to.Id,
            location: taken,
            extra: new[] { war.Id, from.Id });
    }

    /// <summary>
    /// Records against a settlement's realm the people it just lost to something unfightable.
    /// </summary>
    /// <remarks>
    /// Shared by plague, disaster and famine so all three reach the realm's fortunes the same way
    /// and none of them has to repeat the guard for a settlement whose owner has already fallen.
    /// The severity is worked out against the realm's population rather than the settlement's, so
    /// a thousand dead is a catastrophe to a small realm and a bad year to a large one.
    /// </remarks>
    public static void Suffered(WorldState world, Settlement settlement, int lost)
    {
        if (lost <= 0 || !world.Civilizations.Contains(settlement.CivilizationId)) return;

        Civilization realm = world.Civilizations[settlement.CivilizationId];
        realm.Fortunes.Suffered(lost, realm.Population);
    }

    /// <summary>
    /// Ends a realm.
    /// </summary>
    /// <param name="cause">
    /// How it ended, in prose, or null when there is nothing to add beyond the fact. Written into
    /// the event rather than inferred by the viewer, because the two ways a realm can end read
    /// completely differently and the difference is the interesting part.
    /// </param>
    /// <param name="conqueror">Whoever finished it, if anyone did.</param>
    public static void Fall(
        WorldState world,
        Civilization civilization,
        int year,
        string? cause = null,
        EntityId conqueror = default)
    {
        civilization.EndedYear = year;
        civilization.Population = 0;

        // A ruler without a realm stops ruling, but does not die of it.
        if (!civilization.CurrentRulerId.IsNone)
        {
            Figure ruler = world.Figures[civilization.CurrentRulerId];
            if (ruler.IsAlive) ruler.EndCurrentTitle(year);
            civilization.CurrentRulerId = EntityId.None;
        }

        // Land outlives the realm that held it. Releasing it is what lets a neighbour expand into
        // the vacancy a generation later, which is the whole point of a realm having fallen.
        var released = new List<EntityId>();
        foreach (EntityId regionId in civilization.TerritoryRegionIds)
        {
            Region region = world.Regions[regionId];
            if (region.Owner != civilization.Id) continue;

            region.Owner = EntityId.None;
            released.Add(regionId);
        }

        civilization.TerritoryRegionIds.Clear();

        var data = Chronicle.Data(
            ("years", (year - civilization.FoundedYear).ToString(CultureInfo.InvariantCulture)),
            ("peakPopulation", civilization.PeakPopulation.ToString(CultureInfo.InvariantCulture)));

        if (cause is not null) data["cause"] = cause;

        world.Chronicle.Record(
            year, EventKind.CivilizationFell, civilization.Id, obj: conqueror, data: data);

        // After the ending, because a province is only masterless once the realm is gone — and
        // because a reader wants to hear that a realm fell before hearing what became of its land.
        foreach (EntityId regionId in released)
        {
            world.Chronicle.Record(
                year, EventKind.RegionReleased, regionId, obj: civilization.Id);
        }
    }

    /// <summary>True once a realm has no settlement left standing.</summary>
    public static bool IsFinished(WorldState world, Civilization civilization)
    {
        foreach (EntityId id in civilization.SettlementIds)
        {
            if (world.Settlements[id].IsActive) return false;
        }

        return true;
    }
}
