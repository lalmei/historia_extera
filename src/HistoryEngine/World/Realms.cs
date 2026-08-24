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
    /// <summary>Every office, so a realm's ending can release all of them in a fixed order.</summary>
    private static readonly OfficeKind[] OfficeKinds =
    {
        OfficeKind.Ruler,
        OfficeKind.Regent,
        OfficeKind.Consort,
        OfficeKind.Marshal,
        OfficeKind.HighPriest,
        OfficeKind.Governor,
    };


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
        // An occupied town already carried this at the storming. An unoccupied one learns it here,
        // when the treaty takes it without a garrison ever having sat on the walls.
        (_, EntityId taken) = MoveRegion(
            world,
            region,
            from,
            to,
            year,
            settlement =>
            {
                if (!settlement.IsOccupied) settlement.Fortunes.LandLost();
            });

        war.CededRegionIds.Add(region.Id);

        // Ground changing hands is the one loss this engine models that a realm does not merely
        // recover from: it answers the taker's own older grievances and opens one on the far side
        // that outlives everyone who remembers the war.
        from.Fortunes.LandLost();
        to.Fortunes.LandTaken();

        // Named as a term of a particular peace rather than as ground that changed colour. The war
        // was already among the references; saying it in the sentence is what connects a cession to
        // the campaign three lines above that won it.
        world.Chronicle.Record(
            year,
            EventKind.RegionCeded,
            region.Id,
            obj: to.Id,
            location: taken,
            extra: new[] { war.Id, from.Id },
            data: Chronicle.Data(("war", war.Name), ("from", from.Name)));
    }

    /// <summary>
    /// Moves a region and everyone standing in it from one realm to another, without a treaty.
    /// </summary>
    /// <remarks>
    /// The ownership half of <see cref="Cede"/> without the war. A defection and a secession both
    /// need this, and writing it twice is how one of them would forget to clear a capital or
    /// release an occupation. Silent on purpose: the caller records the revolt, the founding, or
    /// the usurpation that caused it.
    /// </remarks>
    public static void TransferRegion(
        WorldState world, Region region, Civilization from, Civilization to, int year)
    {
        (List<Settlement> moving, EntityId taken) = MoveRegion(
            world,
            region,
            from,
            to,
            year,
            standing =>
            {
                if (standing.IsOccupied) Warfare.EndOccupation(world, standing, year, ceded: true);
            });

        TransferResidents(world, moving, from, to);
        Recount(world, from);
        Recount(world, to);

        from.Fortunes.LandLost();
        to.Fortunes.LandTaken();

        // Same event a peace uses, without a war to name: the viewer rebuilds borders from the
        // log, and a silent transfer would leave the old colour on the map for the rest of the run.
        world.Chronicle.Record(
            year,
            EventKind.RegionCeded,
            region.Id,
            obj: to.Id,
            location: taken,
            extra: new[] { from.Id },
            data: Chronicle.Data(("from", from.Name)));
    }

    /// <summary>
    /// The ground both cessions share: swaps region ownership and moves every active settlement
    /// standing in it, reporting the largest — the one the chronicle names.
    /// </summary>
    /// <remarks>
    /// <paramref name="onEach"/> runs against each mover before its realm is reassigned — a treaty
    /// marks the loss on the town, a defection ends any occupation on it — which is the one step
    /// the two callers do differently. Collected first, because transferring while walking the list
    /// it is read from would skip entries.
    /// </remarks>
    private static (List<Settlement> Moving, EntityId Taken) MoveRegion(
        WorldState world,
        Region region,
        Civilization from,
        Civilization to,
        int year,
        Action<Settlement> onEach)
    {
        region.Owner = to.Id;
        from.TerritoryRegionIds.Remove(region.Id);
        if (!to.TerritoryRegionIds.Contains(region.Id)) to.TerritoryRegionIds.Add(region.Id);

        var moving = new List<Settlement>();
        foreach (EntityId id in from.SettlementIds)
        {
            Settlement standing = world.Settlements[id];
            if (standing.IsActive && standing.RegionId == region.Id) moving.Add(standing);
        }

        EntityId taken = EntityId.None;
        foreach (Settlement standing in moving)
        {
            if (standing.IsCapital)
            {
                standing.IsCapital = false;
                if (from.CapitalId == standing.Id) from.CapitalId = EntityId.None;
            }

            onEach(standing);

            standing.CivilizationId = to.Id;
            from.SettlementIds.Remove(standing.Id);
            if (!to.SettlementIds.Contains(standing.Id)) to.SettlementIds.Add(standing.Id);

            if (taken.IsNone || standing.Population > world.Settlements[taken].Population)
            {
                taken = standing.Id;
            }
        }

        // Repointed here rather than left for the succession system's yearly repair. Territory
        // moves in a season and succession runs in the year's first one, so a seat lost after it
        // has already passed leaves the realm with no address until the following spring — and a
        // seat lost in the final year leaves it with none at all, which is a realm whose living
        // people are exported as living nowhere.
        if (from.CapitalId.IsNone) Reseat(world, from, year);

        return (moving, taken);
    }

    /// <summary>Gives a realm that has just lost its seat the largest one it still holds.</summary>
    private static void Reseat(WorldState world, Civilization civilization, int year)
    {
        Settlement? replacement = null;
        foreach (Settlement candidate in world.ActiveSettlementsOf(civilization))
        {
            // Largest surviving settlement, id breaking ties — the same rule the succession
            // system uses, so a seat moved here and a seat moved there are the same seat.
            if (replacement is null
                || candidate.Population > replacement.Population
                || (candidate.Population == replacement.Population
                    && candidate.Id.CompareTo(replacement.Id) < 0))
            {
                replacement = candidate;
            }
        }

        if (replacement is null) return;

        replacement.IsCapital = true;
        civilization.CapitalId = replacement.Id;

        // A capital is governed by whoever holds the throne, in person, so the town that has just
        // become one has no governor. The office system drops such a posting on its yearly pass;
        // territory moves in a season, and one moved after that pass has run would leave a
        // governor sitting in a capital until the following spring — or for good, in a final year.
        var seated = new List<Figure>();
        foreach (Figure figure in world.Figures)
        {
            if (!figure.IsAlive) continue;
            if (figure.OpenOffice(OfficeKind.Governor) is not { } posting) continue;
            if (posting.ScopeId != replacement.Id) continue;

            seated.Add(figure);
        }

        foreach (Figure governor in seated)
        {
            Offices.Lapse(world, governor, OfficeKind.Governor, year);
        }
    }

    /// <summary>
    /// A town that won its rising and had no neighbour to join becomes a realm of its own.
    /// </summary>
    /// <remarks>
    /// <para>Shares the parent's culture: these are the same people under a new crown, not a new
    /// folk rolled at worldgen. The breakaway is a civilization from the day it rises, so a truce
    /// with the parent is a truce between realms — the shape a later raid or surprise-attack model
    /// can still read — rather than a third kind of peace invented only for revolts.</para>
    ///
    /// <para>A named founder — a governor, a local adult — is raised into a house if they have
    /// none and crowned here. A cadet keeps theirs. With nobody on the ground, a house is founded
    /// the way an heirless throne already is.</para>
    /// </remarks>
    public static Civilization BreakAway(
        WorldState world,
        Civilization from,
        Settlement seat,
        Figure? founder,
        int year,
        IRng rng)
    {
        Culture culture = world.CultureOf(from);
        EntityId civId = world.Civilizations.NextId;
        var born = new Civilization(
            civId,
            from.CultureId,
            world.Names.ForCivilization(civId, culture),
            year)
        {
            EffectiveValues = from.EffectiveValues,

            // A breakaway is the same people under a new crown: it carries the parent's culture as
            // the centuries have left it, then drifts on its own from there.
            BaseValues = from.BaseValues,
            StateReligionId = seat.ReligionId.IsNone ? from.StateReligionId : seat.ReligionId,
        };

        world.Civilizations.Add(born);

        Region region = world.Regions[seat.RegionId];
        TransferRegion(world, region, from, born, year);

        seat.IsCapital = true;
        born.CapitalId = seat.Id;

        world.Chronicle.Record(
            year, EventKind.CivilizationFounded, born.Id, location: seat.Id);

        Figure ruler = founder
            ?? LocalAdult(world, born, seat, year)
            ?? Houses.FoundDynasty(world, born, culture, year, rng);

        Houses.RaiseHouse(world, born, culture, ruler, year);
        Houses.Enthrone(world, born, culture, ruler, year, "by the rising of " + seat.Name);

        Diplomacy.SwearTruce(from, born, year + rng.NextInt(12, 26));
        Diplomacy.Nudge(from, born, -0.45);
        Diplomacy.Nudge(born, from, -0.30);

        if (IsFinished(world, from))
        {
            Fall(world, from, year, "torn apart by revolt", born.Id);
        }

        return born;
    }

    /// <summary>An adult already living in the breakaway seat, if the rising named nobody.</summary>
    private static Figure? LocalAdult(
        WorldState world, Civilization realm, Settlement seat, int year)
    {
        Figure? best = null;

        foreach (Figure figure in world.Figures)
        {
            if (!figure.IsAlive) continue;
            if (figure.CivilizationId != realm.Id) continue;
            if (figure.ResidenceSettlementId != seat.Id) continue;
            if (figure.AgeIn(year) < Succession.MajorityAge) continue;
            if (Succession.HoldsAThrone(world, figure)) continue;

            if (best is null || figure.Offices.Count > best.Offices.Count)
            {
                best = figure;
            }
        }

        return best;
    }

    /// <summary>
    /// Moves the people who actually live in the transferred towns, and nobody who does not.
    /// </summary>
    /// <remarks>
    /// Residence, not realm: a posting never changes <see cref="Figure.CivilizationId"/>, but a
    /// secession must, or the governor who just won the town is still a subject of the crown they
    /// threw off. The sitting ruler of the parent is left even if they happen to be visiting —
    /// stealing a king by moving a province is how three realms once spent a century governed by a
    /// corpse.
    /// </remarks>
    private static void TransferResidents(
        WorldState world,
        List<Settlement> moving,
        Civilization from,
        Civilization to)
    {
        foreach (Figure figure in world.Figures)
        {
            if (!figure.IsAlive) continue;
            if (figure.CivilizationId != from.Id) continue;
            if (from.CurrentRulerId == figure.Id) continue;
            if (!LivesIn(moving, figure.ResidenceSettlementId)) continue;

            figure.CivilizationId = to.Id;
        }
    }

    private static bool LivesIn(List<Settlement> moving, EntityId residence)
    {
        foreach (Settlement standing in moving)
        {
            if (standing.Id == residence) return true;
        }

        return false;
    }

    /// <summary>Recounts a realm's people from the towns it still holds.</summary>
    public static void Recount(WorldState world, Civilization civilization)
    {
        int population = 0;
        foreach (EntityId id in civilization.SettlementIds)
        {
            Settlement settlement = world.Settlements[id];
            if (settlement.IsActive) population += settlement.Population;
        }

        civilization.Population = population;
        if (population > civilization.PeakPopulation) civilization.PeakPopulation = population;
    }

    /// <summary>
    /// Records against a settlement, and its realm, the people it just lost to something unfightable.
    /// </summary>
    /// <remarks>
    /// Shared by plague, disaster and famine so all three reach fortunes the same way and none of
    /// them has to repeat the guard for a settlement whose owner has already fallen. The town is
    /// measured against its own people, so emptying a place is a catastrophe there even when it
    /// is a bad year to the realm; the realm is still measured against the realm, for the reason
    /// it always was.
    /// </remarks>
    public static void Suffered(WorldState world, Settlement settlement, int lost)
    {
        if (lost <= 0) return;

        // Population has already been reduced by `lost`, so adding it back is the headcount the
        // share should be taken against — including the case the place now stands empty, which
        // would otherwise refuse to record a calamity at all.
        settlement.Fortunes.Suffered(lost, settlement.Population + lost);

        if (!world.Civilizations.Contains(settlement.CivilizationId)) return;

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
            if (ruler.IsAlive)
            {
                ruler.EndOffice(OfficeKind.Ruler, year);
                Occupations.Sync(world, ruler, year);
            }
            civilization.CurrentRulerId = EntityId.None;
        }

        // And so does everyone who held an office under them. The office system releases seats
        // only for realms that still stand, so without this a fallen realm's marshal and governors
        // keep their posts for the rest of the run — the same silent, permanent shape as the regent
        // who was recorded as governing for three centuries after he died, and found the same way:
        // by an invariant test noticing a figure holding an office of a realm they no longer live
        // in. Nothing narrates it; the realm's own ending already says what happened.
        foreach (Figure figure in world.Figures)
        {
            if (!figure.IsAlive) continue;

            foreach (OfficeKind kind in OfficeKinds)
            {
                OfficeHolding? held = figure.OpenOffice(kind);
                if (held is not null && held.CivilizationId == civilization.Id)
                {
                    Offices.Lapse(world, figure, kind, year);
                }
            }
        }

        civilization.RegentId = EntityId.None;

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
