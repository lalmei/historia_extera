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
/// <para><b>Two speeds, then two political endings.</b> Most discontent never becomes a
/// rising: it festers as <see cref="Settlement.Banditry"/>, a standing tax on the trade through a
/// place that dims on its own once its cause fades. Only real pressure boils over into a revolt,
/// and a revolt resolves at once rather than looping — a crushed one vents the grievance that fed
/// it, exactly the fix occupation was just given, so the same town does not rise every spring.
/// A rising that wins and has a neighbour defects; one that has none becomes a realm of its own.
/// A governor of a town that is not the seat may instead march on the crown: they take it, or
/// they settle for independence, and those are kept as separate endings so a chronicle can tell
/// a usurper from a secessionist.</para>
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

    /// <summary>Share of the realm mustered to hold the seat against a marching governor.</summary>
    private const double MarchResponseFraction = 0.22;

    /// <summary>How much projecting force toward the capital costs a distant rising.</summary>
    private const double MarchDistancePenalty = 0.45;

    /// <summary>Yearly chance a disgraced rebel governor does not survive the answer.</summary>
    private const double RebelGovernorExecuted = 0.28;

    public string Name => "unrest";

    public Cadence Cadence => Cadence.Annual;

    public void Tick(WorldState world, Stamp now)
    {
        int year = now.Year;
        IRng rng = world.Root.Fork(Name, year);

        // Snapshot: a rising that transfers a settlement mutates the realm's own list, a secession
        // adds a civilization behind us, and neither collection can be walked while it is being
        // cut down or grown.
        var civilizations = new List<Civilization>(world.ActiveCivilizations());

        foreach (Civilization civilization in civilizations)
        {
            if (!civilization.IsActive) continue;

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

                if (TryUsurp(world, civilization, settlement, pressure, year, local))
                {
                    continue;
                }

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
        Figure? leader = againstGarrison ? null : Offices.GovernorOf(world, settlement);

        double rebels = settlement.Population * (0.5 + (0.6 * pressure)) * rng.NextDouble(0.8, 1.2);
        double response = Response(world, realm, settlement, adversary, rng);

        bool crushed = response + rebels <= 0.0
            ? true
            : rng.Chance(response / (response + rebels));

        RecordBroke(world, settlement, adversary, leader, Cause(world, settlement), year);

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

        // A capital that throws off its own crown is a deposition, not a secession: the seat
        // cannot break away from itself. Succession fills the empty throne later the same year.
        if (settlement.IsCapital)
        {
            PopularDeposition(world, realm, settlement, lost, year);
            return;
        }

        // Against its own realm, a winning rising looks for a neighbour to pass to. If one is in
        // reach the region defects to it; if none is, the town becomes a realm of its own rather
        // than holding the walls as a wreck.
        Civilization? refuge = NearestRival(world, realm, settlement);
        if (refuge is not null)
        {
            Realms.TransferRegion(world, world.Regions[settlement.RegionId], realm, refuge, year);
            Record(world, EventKind.RevoltPrevailed, settlement, realm.Id, refuge.Id, lost, year);
            return;
        }

        Figure? founder = Offices.GovernorOf(world, settlement);
        string cause = Cause(world, settlement);
        Civilization born = Realms.BreakAway(world, realm, settlement, founder, year, rng);
        RecordSecession(world, settlement, realm, born, cause, lost, year);
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

    /// <summary>
    /// A governor of a town that is not the seat may march on the crown, answering the realm's
    /// grievance rather than only their own walls.
    /// </summary>
    /// <remarks>
    /// Separate from a leaderless rising on purpose. Wanting the throne and wanting out of the
    /// realm are opposite war aims; mixing them in one roll produced chronicles that could not
    /// tell which had just happened. A march that is put down is the existing crush. A march that
    /// arrives takes the seat. A march that wins in the field but cannot finish — especially from
    /// far off — settles as a breakaway, which is the truce the original rising was missing.
    /// </remarks>
    private static bool TryUsurp(
        WorldState world,
        Civilization realm,
        Settlement settlement,
        double pressure,
        int year,
        IRng rng)
    {
        if (settlement.IsCapital || settlement.IsOccupied) return false;
        if (settlement.Population < RevoltMinPopulation) return false;
        if (!world.Figures.Contains(realm.CurrentRulerId)) return false;

        Figure ruler = world.Figures[realm.CurrentRulerId];
        if (!ruler.IsAlive) return false;

        Figure? governor = Offices.GovernorOf(world, settlement);
        if (governor is null || !governor.IsAlive) return false;

        double heat = RealmHeat(realm);
        if (pressure < 0.32 || heat < 0.22) return false;

        double distance = DistanceFactor(world, realm, settlement);
        double ambition = governor.Disposition.Centralism;
        if (!governor.OpenOffice(OfficeKind.Governor)!.GrantedBy.IsNone) ambition += 0.15;
        if (!governor.DynastyId.IsNone) ambition += 0.10;
        ambition = DetMath.Clamp01(ambition);

        // Close, ambitious, mandated cadets march on the seat. Far internal notables still get
        // the ordinary rising, which may secede if it wins and has no neighbour.
        double chance = (0.45 * pressure) + (0.40 * heat) + (0.20 * ambition);
        chance *= 1.0 - (0.55 * distance);
        if (!rng.Chance(DetMath.Clamp01(chance * 0.55))) return false;

        double rebels = settlement.Population * (0.5 + (0.6 * pressure)) * rng.NextDouble(0.8, 1.2);
        rebels *= 1.0 - (MarchDistancePenalty * distance);

        double response = MarchResponse(world, realm, rng);
        double share = response + rebels <= 0.0 ? 1.0 : response / (response + rebels);

        RecordBroke(
            world, settlement, realm.Id, governor,
            "marching on the seat to settle the realm's grievances", year);

        if (rng.Chance(share))
        {
            Crush(world, realm, settlement, realm.Id, rebels, year, rng);
            PunishGovernor(world, governor, realm, year, rng);
            return true;
        }

        int lost = Kill(world, settlement, rebels * rng.NextDouble(0.1, 0.25), year);
        settlement.Fortunes.Ease(0.6);
        realm.Fortunes.Ease(0.35);

        // Far from the seat, a field victory more often becomes independence than a coronation:
        // the march never quite arrived. Close to it, the governor takes the crown.
        bool settleForIndependence = distance >= 0.42 && rng.Chance(0.35 + (0.40 * distance));
        if (settleForIndependence)
        {
            Civilization born = Realms.BreakAway(world, realm, settlement, governor, year, rng);
            RecordSecession(
                world, settlement, realm, born,
                "after a march that never reached the seat", lost, year);
            return true;
        }

        Usurp(world, realm, settlement, governor, ruler, lost, year, rng);
        return true;
    }

    /// <summary>How discontent the realm itself is, in [0, 1], apart from any one town.</summary>
    private static double RealmHeat(Civilization realm)
    {
        RealmFortunes f = realm.Fortunes;
        return DetMath.Clamp01(
            (GrievanceWeight * f.Grievance)
            + (WearinessWeight * f.Weariness)
            + (CalamityWeight * f.Calamity));
    }

    /// <summary>Strength the crown can put around its own seat, after weariness.</summary>
    private static double MarchResponse(WorldState world, Civilization realm, IRng rng)
    {
        double strength = realm.Population * MarchResponseFraction;
        strength *= 1.0 - (0.5 * realm.Fortunes.Weariness);

        if (world.Settlements.Contains(realm.CapitalId)
            && world.Settlements[realm.CapitalId].IsFortified)
        {
            strength *= 1.3;
        }

        return strength * rng.NextDouble(0.8, 1.2);
    }

    /// <summary>The marching governor takes the throne: the old ruler dies, abdicates, or is deposed.</summary>
    private static void Usurp(
        WorldState world,
        Civilization realm,
        Settlement settlement,
        Figure governor,
        Figure ruler,
        int lost,
        int year,
        IRng rng)
    {
        Culture culture = world.CultureOf(realm);
        string how = VacateThrone(world, realm, culture, ruler, year, rng);

        Houses.RaiseHouse(world, realm, culture, governor, year);
        Houses.Enthrone(
            world, realm, culture, governor, year, "by force of arms after the rising of " + settlement.Name);

        RecordUsurped(world, settlement, realm, governor, how, lost, year);
    }

    /// <summary>How the sitting ruler leaves, in prose the usurping event can finish with.</summary>
    private static string VacateThrone(
        WorldState world,
        Civilization realm,
        Culture culture,
        Figure ruler,
        int year,
        IRng rng)
    {
        string title = ruler.OpenOffice(OfficeKind.Ruler)?.Title ?? culture.RulerTitle;

        double aggression = ruler.Disposition.Values.Aggression;
        bool kill = rng.Chance(0.30 + (0.15 * realm.Fortunes.Grievance));
        bool abdicate = !kill
            && (realm.Fortunes.Weariness >= 0.45 || aggression < 0.38)
            && rng.Chance(0.55);

        if (kill)
        {
            Houses.Die(
                world, ruler, year, DeathCause.Battle, "defending the seat against the rising");
            return "the ruler fell in the fighting";
        }

        ruler.EndOffice(OfficeKind.Ruler, year);
        Occupations.Sync(world, ruler, year);
        realm.CurrentRulerId = EntityId.None;
        realm.RegentId = EntityId.None;

        if (abdicate)
        {
            world.Chronicle.Record(
                year,
                EventKind.RulerAbdicated,
                ruler.Id,
                obj: realm.Id,
                location: realm.CapitalId,
                data: Chronicle.Data(("title", title), ("cause", "in the face of the rising")));
            return "forcing an abdication";
        }

        world.Chronicle.Record(
            year,
            EventKind.RulerDeposed,
            ruler.Id,
            obj: realm.Id,
            location: realm.CapitalId,
            data: Chronicle.Data(("title", title)));
        return "deposing the ruler";
    }

    /// <summary>The people of the seat throw off their own crown. Succession names the next.</summary>
    private static void PopularDeposition(
        WorldState world, Civilization realm, Settlement settlement, int lost, int year)
    {
        if (world.Figures.Contains(realm.CurrentRulerId)
            && world.Figures[realm.CurrentRulerId].IsAlive)
        {
            Figure ruler = world.Figures[realm.CurrentRulerId];
            Culture culture = world.CultureOf(realm);
            string title = ruler.OpenOffice(OfficeKind.Ruler)?.Title ?? culture.RulerTitle;

            ruler.EndOffice(OfficeKind.Ruler, year);
            Occupations.Sync(world, ruler, year);
            realm.CurrentRulerId = EntityId.None;
            realm.RegentId = EntityId.None;

            world.Chronicle.Record(
                year,
                EventKind.RulerDeposed,
                ruler.Id,
                obj: realm.Id,
                location: settlement.Id,
                data: Chronicle.Data(("title", title)));
        }

        realm.Fortunes.Ease(0.45);
        Record(world, EventKind.RevoltPrevailed, settlement, realm.Id, EntityId.None, lost, year);
    }

    /// <summary>A crushed march costs the governor their office, and sometimes their life.</summary>
    private static void PunishGovernor(
        WorldState world, Figure governor, Civilization realm, int year, IRng rng)
    {
        if (!governor.IsAlive) return;

        if (rng.Chance(RebelGovernorExecuted + (0.20 * realm.EffectiveValues.Aggression)))
        {
            Houses.Die(world, governor, year, DeathCause.Execution, "for raising the town");
            return;
        }

        if (governor.Holds(OfficeKind.Governor))
        {
            Offices.Revoke(world, governor, OfficeKind.Governor, "for raising the town", year);
        }
    }

    private static void RecordBroke(
        WorldState world,
        Settlement settlement,
        EntityId adversary,
        Figure? leader,
        string cause,
        int year)
    {
        var data = Chronicle.Data(("cause", cause));
                    if (leader is not null) data["leader"] = leader.FullName;

        world.Chronicle.Record(
            year,
            EventKind.RevoltBroke,
            settlement.Id,
            obj: adversary,
            location: settlement.RegionId,
            extra: leader is null ? null : new[] { leader.Id },
            data: data);
    }

    private static void RecordSecession(
        WorldState world,
        Settlement settlement,
        Civilization from,
        Civilization born,
        string cause,
        int lost,
        int year)
    {
        var data = Chronicle.Data(("cause", cause));
        if (world.Figures.Contains(born.CurrentRulerId))
        {
            data["ruler"] = world.Figures[born.CurrentRulerId].FullName;
        }

        if (lost > 0) data["lost"] = lost.ToString(CultureInfo.InvariantCulture);

        world.Chronicle.Record(
            year,
            EventKind.RevoltSeceded,
            settlement.Id,
            obj: from.Id,
            location: born.Id,
            extra: new[] { born.CurrentRulerId, settlement.RegionId },
            data: data);
    }

    private static void RecordUsurped(
        WorldState world,
        Settlement settlement,
        Civilization realm,
        Figure governor,
        string how,
        int lost,
        int year)
    {
        var data = Chronicle.Data(("how", how));
        if (lost > 0) data["lost"] = lost.ToString(CultureInfo.InvariantCulture);

        world.Chronicle.Record(
            year,
            EventKind.RevoltUsurped,
            settlement.Id,
            obj: realm.Id,
            location: governor.Id,
            extra: new[] { governor.Id },
            data: data);
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
