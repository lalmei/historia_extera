using System.Globalization;
using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.World;

namespace HistoryEngine.Systems;

/// <summary>
/// Grievance a realm never answered, coming back as lawlessness and revolt.
/// </summary>
/// <remarks>
/// <para><b>This is the consumer the pressure was missing.</b> The engine already measured a
/// settlement's hardship densely — a sack, a lost war, a foreign garrison, a famine all raise the
/// grievance and weariness the fortunes model carries — and until now nothing downstream read any
/// of it. Famine fired more than a hundred times in a three-century run and cost only a population
/// figure; a town could be stormed, occupied and starved and go on paying its taxes as if content.
/// Unrest is where that accumulated pressure finally does something: it turns grievance into
/// brigandage on the roads and, past a threshold, into a rising the realm has to put down.</para>
///
/// <para><b>It reads state, it does not invent it.</b> Every term of the pressure below is
/// something an earlier system already decided this year — grievance and weariness from war and
/// calamity, occupation from a storming, faith from religion, distance from the map. Placed after
/// <see cref="WarSystem"/> so a war's grievance is felt the same year it is earned, and before
/// <see cref="TradeRouteSystem"/> so the brigandage it raises suppresses the very trade that year.
/// Adding it is a behaviour change and moves the fingerprint; that is the point of it.</para>
///
/// <para><b>Two outcomes, because unrest has two speeds.</b> Most discontent never becomes a
/// rising: it festers as <see cref="Settlement.Banditry"/>, a standing tax on the trade through a
/// place that dims on its own once its cause fades. Only real pressure boils over into a revolt,
/// and a revolt resolves at once rather than looping — a crushed one vents the grievance that fed
/// it, exactly the fix occupation was just given, so the same town does not rise every spring.</para>
/// </remarks>
public sealed class UnrestSystem : ISystem
{
    // ---- Pressure: how discontent a place is, in [0, 1] ----
    private const double GrievanceWeight = 0.70;
    private const double WearinessWeight = 0.35;
    private const double CalamityWeight = 0.30;

    /// <summary>A foreign garrison is the sharpest single spur to a rising.</summary>
    private const double OccupiedPressure = 0.45;

    /// <summary>A town that keeps a faith its realm has forsworn, or forsworn its realm's.</summary>
    private const double FaithMismatchPressure = 0.20;

    /// <summary>The frontier is harder to hold than the seat; distance is the whole of the term.</summary>
    private const double DistancePressure = 0.20;

    /// <summary>The seat of government sits under the crown's own hand — but only while the crown holds it.</summary>
    private const double CapitalCalm = 0.20;
    private const double FortifiedCalm = 0.10;

    /// <summary>Distance from the capital at which the frontier term is fully in, in world units.</summary>
    private const double FullDistance = 2600.0;

    // ---- Brigandage: the standing condition ----
    private const double BanditryRetention = 0.80;
    private const double BanditryRise = 0.28;

    /// <summary>Brigandage is written when it rises to this from calm — an eruption, not a hover.</summary>
    private const double BanditryNotable = 0.38;

    /// <summary>Below this a place counts as quiet again, so a fresh outbreak is worth recording.</summary>
    private const double BanditryQuiet = 0.18;

    private const double BrigandageScale = 0.6;

    // ---- Revolt: the rising ----
    private const double RevoltThreshold = 0.45;
    private const double RevoltScale = 0.8;

    /// <summary>A hamlet cannot field a rising the realm has to answer.</summary>
    private const int RevoltMinPopulation = 120;

    /// <summary>Share of the realm mustered against a rising, before weariness and distance.</summary>
    private const double ResponseFraction = 0.16;

    /// <summary>How far a seceding town will look for a rival to pass to, in world units.</summary>
    private const double MaxDefectDistance = 2200.0;

    public string Name => "unrest";

    public Cadence Cadence => Cadence.Annual;

    public void Tick(WorldState world, Stamp now)
    {
        int year = now.Year;
        IRng rng = world.Root.Fork(Name, year);

        foreach (Civilization civilization in world.ActiveCivilizations())
        {
            // Snapshot: a rising that transfers a settlement mutates the realm's own list, and a
            // realm cannot be walked while it is being cut down.
            var settlements = new List<Settlement>(world.ActiveSettlementsOf(civilization));

            foreach (Settlement settlement in settlements)
            {
                if (!settlement.IsActive) continue;

                // The memory of last year's lawlessness dims before this year's is judged, so a
                // place whose cause has passed climbs back to safety on its own.
                settlement.Banditry *= BanditryRetention;

                double pressure = Pressure(world, civilization, settlement);
                if (pressure <= 0.0) continue;

                IRng local = rng.Fork("settlement", settlement.Id.ToDiscriminator());

                Brigandage(world, settlement, pressure, year, local);

                if (settlement.Population >= RevoltMinPopulation
                    && pressure >= RevoltThreshold
                    && local.Chance((pressure - RevoltThreshold) * RevoltScale))
                {
                    Rise(world, civilization, settlement, pressure, year, local);
                }
            }
        }
    }

    /// <summary>How close a place is to open revolt, in [0, 1].</summary>
    private static double Pressure(WorldState world, Civilization realm, Settlement settlement)
    {
        RealmFortunes f = settlement.Fortunes;

        double pressure = (GrievanceWeight * f.Grievance)
            + (WearinessWeight * f.Weariness)
            + (CalamityWeight * f.Calamity);

        if (settlement.IsOccupied) pressure += OccupiedPressure;

        if (!settlement.ReligionId.IsNone
            && !realm.StateReligionId.IsNone
            && settlement.ReligionId != realm.StateReligionId)
        {
            pressure += FaithMismatchPressure;
        }

        pressure += DistancePressure * DistanceFactor(world, realm, settlement);

        // A garrisoned town is calmed by nothing it holds: an occupied capital's court has fled and
        // an occupied town's walls are manned against its own people, so the two reducers that
        // stand for a garrison on side only apply while the realm actually holds the place.
        if (!settlement.IsOccupied)
        {
            if (settlement.IsCapital) pressure -= CapitalCalm;
            if (settlement.IsFortified) pressure -= FortifiedCalm;
        }

        return DetMath.Clamp01(pressure);
    }

    /// <summary>How far from its capital a settlement sits, as a [0, 1] share of full reach.</summary>
    /// <remarks>
    /// A realm with no seat is unsettling everywhere at once, so its distance term sits at the
    /// midpoint rather than at zero — an interregnum is not calm.
    /// </remarks>
    private static double DistanceFactor(WorldState world, Civilization realm, Settlement settlement)
    {
        if (!world.Settlements.Contains(realm.CapitalId)) return 0.5;

        Settlement capital = world.Settlements[realm.CapitalId];
        if (capital.Id == settlement.Id) return 0.0;

        double distance = world.Distance(settlement.X, settlement.Z, capital.X, capital.Z);
        return DetMath.Clamp01(distance / FullDistance);
    }

    /// <summary>Raises the lawlessness on the roads, and records it the first time it turns bad.</summary>
    private static void Brigandage(
        WorldState world, Settlement settlement, double pressure, int year, IRng rng)
    {
        if (!rng.Chance(pressure * BrigandageScale)) return;

        double before = settlement.Banditry;
        settlement.Banditry = DetMath.Clamp01(before + (BanditryRise * (0.5 + pressure)));

        // Recorded only when it erupts from a quiet country into a lawless one — not every year it
        // lingers, and not each time it flickers across one line. The road being unsafe is a state;
        // a chronicle that reprinted it every spring would bury the risings it builds toward.
        if (before < BanditryQuiet && settlement.Banditry >= BanditryNotable)
        {
            world.Chronicle.Record(
                year,
                EventKind.BrigandageWorsened,
                settlement.Id,
                obj: settlement.CivilizationId,
                location: settlement.RegionId,
                data: Chronicle.Data(("cause", Cause(world, settlement))));
        }
    }

    /// <summary>Resolves a rising the year it breaks out.</summary>
    /// <remarks>
    /// An occupied town rises against the garrison holding it, and everywhere else against its own
    /// realm — the same discontent pointed at whoever is standing on it. The two share their
    /// arithmetic and differ only in who wins what: throwing off a garrison frees the town back to
    /// its owner, while throwing off the realm itself passes it to a neighbour or wrecks it.
    /// </remarks>
    private static void Rise(
        WorldState world,
        Civilization realm,
        Settlement settlement,
        double pressure,
        int year,
        IRng rng)
    {
        bool againstGarrison = settlement.IsOccupied;
        EntityId adversary = againstGarrison ? settlement.OccupierId : realm.Id;

        double rebels = settlement.Population * (0.5 + (0.6 * pressure)) * rng.NextDouble(0.8, 1.2);
        double response = Response(world, realm, settlement, adversary, rng);

        bool crushed = response + rebels <= 0.0
            ? true
            : rng.Chance(response / (response + rebels));

        world.Chronicle.Record(
            year,
            EventKind.RevoltBroke,
            settlement.Id,
            obj: adversary,
            location: settlement.RegionId,
            data: Chronicle.Data(("cause", Cause(world, settlement))));

        if (crushed)
        {
            Crush(world, realm, settlement, adversary, rebels, year, rng);
        }
        else
        {
            Prevail(world, realm, settlement, againstGarrison, rebels, year, rng);
        }
    }

    /// <summary>The rising is put down: it costs lives, and spends the anger that raised it.</summary>
    private static void Crush(
        WorldState world,
        Civilization realm,
        Settlement settlement,
        EntityId adversary,
        double rebels,
        int year,
        IRng rng)
    {
        int lost = Kill(world, settlement, rebels * rng.NextDouble(0.25, 0.5), year);

        // The road quiets for a while and the grievance is half-spent — put down, not answered, so
        // the cause can build it again, but not from where it stood.
        settlement.Banditry *= 0.4;
        settlement.Fortunes.Ease(0.35);

        // Answering a rising wearies the realm that had to march on its own town.
        if (world.Civilizations.Contains(adversary)) world.Civilizations[adversary].Fortunes.LostABattle();

        var data = new DetMap<string, string>();
        if (lost > 0) data["lost"] = lost.ToString(CultureInfo.InvariantCulture);

        world.Chronicle.Record(
            year,
            EventKind.RevoltCrushed,
            settlement.Id,
            obj: adversary,
            location: settlement.RegionId,
            data: data.Count == 0 ? null : data);
    }

    /// <summary>The rising wins: a garrison is thrown off, or a realm is.</summary>
    private static void Prevail(
        WorldState world,
        Civilization realm,
        Settlement settlement,
        bool againstGarrison,
        double rebels,
        int year,
        IRng rng)
    {
        int lost = Kill(world, settlement, rebels * rng.NextDouble(0.1, 0.25), year);
        settlement.Fortunes.Ease(0.6);

        if (againstGarrison)
        {
            // The garrison is driven out and the town is its owner's again — the one relief an
            // occupation can end in that no treaty wrote. The restoration event this raises is the
            // outcome line; a second "threw off" beside "recovered by force of arms" would say it
            // twice, so the rising's success is left for that event to report.
            Warfare.EndOccupation(world, settlement, year, ceded: false, retaken: true);
            return;
        }

        // Against its own realm, a winning rising looks for a neighbour to pass to. If one is in
        // reach the region defects to it; if none is, the town holds itself at the price of its own
        // ruin, walls down and half its trade gone to the roads.
        Civilization? refuge = NearestRival(world, realm, settlement);
        if (refuge is not null)
        {
            Defect(world, realm, settlement, refuge, year);
            Record(world, EventKind.RevoltPrevailed, settlement, realm.Id, refuge.Id, lost, year);
        }
        else
        {
            settlement.IsFortified = false;
            settlement.Banditry = DetMath.Clamp01(settlement.Banditry + 0.4);
            realm.Fortunes.LostABattle();
            Record(world, EventKind.RevoltPrevailed, settlement, realm.Id, EntityId.None, lost, year);
        }
    }

    private static void Record(
        WorldState world,
        EventKind kind,
        Settlement settlement,
        EntityId adversary,
        EntityId refuge,
        int lost,
        int year)
    {
        var data = new DetMap<string, string>();
        if (lost > 0) data["lost"] = lost.ToString(CultureInfo.InvariantCulture);

        world.Chronicle.Record(
            year,
            kind,
            settlement.Id,
            obj: adversary,
            location: refuge,
            data: data.Count == 0 ? null : data);
    }

    /// <summary>Strength a realm can turn on one of its own towns, after weariness and distance.</summary>
    private static double Response(
        WorldState world, Civilization realm, Settlement settlement, EntityId adversary, IRng rng)
    {
        Civilization power = world.Civilizations.Contains(adversary)
            ? world.Civilizations[adversary]
            : realm;

        double strength = power.Population * ResponseFraction;
        strength *= 1.0 - (0.5 * power.Fortunes.Weariness);
        strength *= 1.0 - (0.5 * DistanceFactor(world, realm, settlement));
        if (settlement.IsFortified) strength *= 1.3;

        return strength * rng.NextDouble(0.8, 1.2);
    }

    /// <summary>Removes rebel dead from a settlement and its realm, returning the count.</summary>
    private static int Kill(WorldState world, Settlement settlement, double toll, int year)
    {
        int lost = Math.Min(settlement.Population, Math.Max(0, (int)toll));
        if (lost <= 0) return 0;

        settlement.Population -= lost;
        if (world.Civilizations.Contains(settlement.CivilizationId))
        {
            Civilization owner = world.Civilizations[settlement.CivilizationId];
            owner.Population = Math.Max(0, owner.Population - lost);
        }

        // Reaches the settlement's fortunes as any mass death does, so a rising that guts a town
        // reads on its own record as the calamity it was.
        settlement.Fortunes.Suffered(lost, settlement.Population + lost);
        return lost;
    }

    /// <summary>The nearest active realm a seceding town could pass to, if one is in reach.</summary>
    /// <remarks>
    /// A realm at war with the town's own is preferred over a neutral one at the same distance — a
    /// rising finds its readiest patron in its ruler's enemy — but reach is the hard bound: with no
    /// neighbour close enough, a rising has no one to join and must hold the town itself.
    /// </remarks>
    private static Civilization? NearestRival(WorldState world, Civilization realm, Settlement settlement)
    {
        Civilization? best = null;
        double bestScore = double.MaxValue;

        foreach (Civilization other in world.ActiveCivilizations())
        {
            if (other.Id == realm.Id) continue;

            double nearest = double.MaxValue;
            foreach (Settlement theirs in world.ActiveSettlementsOf(other))
            {
                double distance = world.Distance(settlement.X, settlement.Z, theirs.X, theirs.Z);
                if (distance < nearest) nearest = distance;
            }

            if (nearest > MaxDefectDistance) continue;

            // A quarter off the distance for a realm already at war with the town's ruler, so a
            // patron in the field wins ties against a neutral neighbour a little farther on.
            double score = Diplomacy.AtWar(world, realm.Id, other.Id) ? nearest * 0.75 : nearest;
            if (score < bestScore)
            {
                bestScore = score;
                best = other;
            }
        }

        return best;
    }

    /// <summary>Passes a rebel town and the region under it from one realm to another, in peace.</summary>
    /// <remarks>
    /// The ownership half of <see cref="Realms.Cede"/> without the war: a defection is not a treaty
    /// term, so it writes no cession event of its own — <see cref="Rise"/> records the revolt that
    /// caused it. A seat of government that defects is cleared, and succession repoints the rump the
    /// next time it runs, exactly as a cession leaves it.
    /// </remarks>
    private static void Defect(
        WorldState world, Civilization from, Settlement settlement, Civilization to, int year)
    {
        Region region = world.Regions[settlement.RegionId];
        region.Owner = to.Id;
        from.TerritoryRegionIds.Remove(region.Id);
        if (!to.TerritoryRegionIds.Contains(region.Id)) to.TerritoryRegionIds.Add(region.Id);

        var moving = new List<Settlement>();
        foreach (EntityId id in from.SettlementIds)
        {
            Settlement standing = world.Settlements[id];
            if (standing.IsActive && standing.RegionId == region.Id) moving.Add(standing);
        }

        foreach (Settlement standing in moving)
        {
            if (standing.IsCapital)
            {
                standing.IsCapital = false;
                if (from.CapitalId == standing.Id) from.CapitalId = EntityId.None;
            }

            if (standing.IsOccupied) Warfare.EndOccupation(world, standing, year, ceded: true);

            standing.CivilizationId = to.Id;
            from.SettlementIds.Remove(standing.Id);
            if (!to.SettlementIds.Contains(standing.Id)) to.SettlementIds.Add(standing.Id);
        }

        from.Fortunes.LandLost();
        to.Fortunes.LandTaken();
    }

    /// <summary>The plainest current reason a place is disaffected, for the chronicle.</summary>
    private static string Cause(WorldState world, Settlement settlement)
    {
        if (settlement.IsOccupied) return "chafing under occupation";

        if (world.Civilizations.Contains(settlement.CivilizationId))
        {
            Civilization realm = world.Civilizations[settlement.CivilizationId];
            if (!settlement.ReligionId.IsNone
                && !realm.StateReligionId.IsNone
                && settlement.ReligionId != realm.StateReligionId)
            {
                return "in a quarrel of faith with its rulers";
            }
        }

        RealmFortunes f = settlement.Fortunes;
        if (f.Calamity >= f.Grievance && f.Calamity >= f.Weariness) return "worn down by hard years";
        if (f.Weariness >= f.Grievance) return "sick of a long war";
        return "nursing old grievances";
    }
}
