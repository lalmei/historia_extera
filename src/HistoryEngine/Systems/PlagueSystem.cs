using System.Globalization;
using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.World;

namespace HistoryEngine.Systems;

/// <summary>
/// Epidemics: where they start, how far they travel, and what they cost.
/// </summary>
/// <remarks>
/// <para><b>A plague is the first thing in this engine that kills across a whole region at once
/// without anybody deciding to.</b> War is chosen, famine follows a bad harvest that follows the
/// land, and old age comes for one person at a time. Pestilence is the model's only source of
/// sudden, undeserved, correlated loss — a realm can lose a third of three towns and a king in
/// four years having done nothing at all — and that is what it is here for. It is also the one
/// mechanism that reaches the succession machinery sideways: a plague year kills rulers of every
/// age, which is how a throne passes to a child.</para>
///
/// <para><b>Ignition scales with urban exposure.</b> Epidemics need density and movement, so only
/// towns and cities can start one and each contributes according to its population and the live
/// traffic on its trade routes. A world of hamlets in its first century has almost none; the same
/// world at three hundred years has one every few decades, with the busiest cities the likeliest
/// origins. That gets the shape right for free — the plague years arrive once there is something
/// to lose.</para>
///
/// <para><b>Arrival is recorded, presence is not.</b> One event when it reaches a place, with
/// that year's toll, and one when the whole thing burns out. Writing a line for every infected
/// town every year read as a loop rather than as a plague, which is the same lesson the sacking
/// rule learned in M6.</para>
///
/// <para>Samples no terrain: spread is decided by distance between settlements the simulation
/// already knows the coordinates of.</para>
/// </remarks>
public sealed class PlagueSystem : ISystem, IEpisodic
{
    /// <summary>Yearly ignition hazard contributed by one unit of urban exposure.</summary>
    private const double IgnitionPerExposure = 0.0018;

    /// <summary>
    /// Ceiling on the yearly chance, however large the world grows.
    /// </summary>
    /// <remarks>
    /// <para><b>This constant used to decide the answer by itself.</b> At 0.030 against a soft cap,
    /// a world with eight qualifying towns already sat at half the ceiling and a mature one sat at
    /// 93% of it, so the elaborate exposure model above governed the first century and nothing
    /// afterwards. The measurement is unambiguous: over five thousand-year runs whose worlds ranged
    /// from 51 settlements to 301, and whose living populations ranged from 138,000 to 1.6 million,
    /// the number of outbreaks was 22, 21, 20, 26 and 28. A world six times the size of another had
    /// the same plague history, which is the precise opposite of what the remarks above this claim
    /// and what the system was built to do.</para>
    ///
    /// <para>At 0.14 the cap goes back to being a backstop against a runaway rather than the
    /// operative term. A young world of a few towns runs at about 1% a year and a dense, connected,
    /// mature one at about 6%, so urban exposure now moves plague frequency across a sixfold range
    /// instead of a 1.9-fold one, and a world that never urbanises never gets its plague years.</para>
    /// </remarks>
    private const double MaxIgnitionChance = 0.14;

    /// <summary>
    /// How many plagues may run at once.
    /// </summary>
    /// <remarks>
    /// So a second can arrive while the first is still burning — which happens — without the world
    /// ever being in a state where every settlement is infected by something and population never
    /// recovers between them. Raised from two with the ceiling above: at the frequency this now
    /// produces, two was binding often enough to become a second hidden rate limit, which is the
    /// same failure the ceiling had.
    /// </remarks>
    private const int MaxConcurrentOutbreaks = 3;

    /// <summary>A plague needs a town to start in. Below this it is an unremarkable bad year.</summary>
    private const int MinimumSeatOfInfection = 900;

    /// <summary>How far a plague can reach in one jump, in world units.</summary>
    private const double SpreadRange = 950.0;

    /// <summary>
    /// How sharply a plague's reach falls off as it spreads.
    /// </summary>
    /// <remarks>
    /// <para>The term that makes an epidemic a regional catastrophe rather than a world-ending
    /// one, and it was not optional. Without it the first cut of this system produced five
    /// pandemics in three centuries that each reached about thirty-four settlements — every
    /// inhabited place in the world, five times over. Eighteen settlements were abandoned against
    /// M7's one and the world ended with five cities instead of fifteen: the flavour milestone had
    /// quietly become the dominant force in the model.</para>
    ///
    /// <para>What was missing is that people react. A plague two towns away closes gates, stops
    /// markets and turns travellers back, so each place it reaches makes the next one harder — and
    /// an epidemic burns out having taken a district rather than a continent. Modelled as decay in
    /// the spread chance against how far it has already got, which is one line and does the whole
    /// job.</para>
    /// </remarks>
    private const double QuarantineDecay = 0.30;

    /// <summary>Yearly chance an infected settlement stops being infected.</summary>
    private const double RecoveryChance = 0.5;

    /// <summary>Extra yearly mortality for figures of a realm with the plague in it.</summary>
    private const double CourtMortality = 0.055;

    /// <summary>Loss below which the arrival is not worth a number in the chronicle.</summary>
    private const int NotableLoss = 15;

    public string Name => "plague";

    public Cadence Cadence => Cadence.Annual;

    public void Tick(WorldState world, Stamp now)
    {
        int year = now.Year;

        IRng rng = world.Root.Fork(Name, year);

        // Existing outbreaks are advanced before a new one can ignite, so a plague that starts
        // this year does not also get a year of killing out of the same tick.
        Advance(world, year, rng);
        MaybeIgnite(world, year, rng);
    }

    /// <summary>The kind of scheduled work this system answers for: a plague reaching a town.</summary>
    public DocketKind Handles => DocketKind.Arrival;

    /// <summary>
    /// Lands a plague that has been travelling, in the town it was travelling to.
    /// </summary>
    /// <remarks>
    /// <para>Forked on the destination and the day rather than on either alone. The design's rule
    /// for a scheduled episode is that its dice must not depend on how many other episodes were
    /// queued before it — the subject alone would satisfy that and then hand every arrival this
    /// town ever suffers the same stream, so the day it landed on distinguishes them.</para>
    ///
    /// <para>Three ways an arrival comes to nothing, all of them silent: the passage has already
    /// been landed, the town was abandoned or taken while the carriers were on the road, or the
    /// plague reached it by another road first. None is an error — a plague that arrives somewhere
    /// there is no longer anybody to infect has simply failed, which is a thing plagues do.</para>
    /// </remarks>
    public void Resolve(WorldState world, DocketEntry entry, Stamp now)
    {
        foreach (Outbreak outbreak in world.Outbreaks)
        {
            for (int i = 0; i < outbreak.InTransit.Count; i++)
            {
                Passage passage = outbreak.InTransit[i];
                if (passage.SettlementId != entry.Subject || passage.Due != entry.Due) continue;

                outbreak.InTransit.RemoveAt(i);

                if (!world.Settlements.Contains(passage.SettlementId)) return;
                if (!world.Settlements.Contains(passage.FromId)) return;

                Settlement destination = world.Settlements[passage.SettlementId];
                if (!destination.IsActive || destination.Population <= 0) return;
                if (IsInfected(outbreak, destination.Id)) return;

                IRng rng = world.Root
                    .Fork(Name + "-arrival", destination.Id.ToDiscriminator())
                    .Fork("on", world.Config.Calendar.AbsoluteDay(entry.Due));

                Arrive(
                    world, outbreak, destination, world.Settlements[passage.FromId],
                    entry.Due.Year, rng);

                return;
            }
        }
    }

    // -----------------------------------------------------------------------
    // Running outbreaks
    // -----------------------------------------------------------------------

    private static void Advance(WorldState world, int year, IRng rng)
    {
        // Collected first: an outbreak that burns out is removed from the list being walked.
        var finished = new List<Outbreak>();

        foreach (Outbreak outbreak in world.Outbreaks)
        {
            Strike(world, outbreak, year, rng);
            Spread(world, outbreak, year, rng);
            Recover(world, outbreak, rng);
            Cull(world, outbreak, year, rng);

            if (outbreak.IsBurningOut) finished.Add(outbreak);
        }

        foreach (Outbreak outbreak in finished)
        {
            world.Outbreaks.Remove(outbreak);

            var data = Chronicle.Data(
                ("name", outbreak.Name),
                ("dead", outbreak.Dead.ToString(CultureInfo.InvariantCulture)),
                ("reached", outbreak.Reached.ToString(CultureInfo.InvariantCulture)));

            int ran = year - outbreak.StartYear;
            if (ran > 0) data["years"] = Chronicle.Years(ran);

            world.Chronicle.Record(
                year, EventKind.PlagueEnded, outbreak.OriginId, data: data);
        }
    }

    /// <summary>Kills, in every settlement the plague is currently in.</summary>
    private static void Strike(WorldState world, Outbreak outbreak, int year, IRng rng)
    {
        foreach (Infection infection in outbreak.Infected)
        {
            Settlement settlement = world.Settlements[infection.SettlementId];
            if (!settlement.IsActive) continue;

            // The arrival year's toll is written into the arrival event, so it is not counted
            // again here — but every year after it is silent, which is why only the total is
            // reported at the end.
            if (infection.Since == year) continue;

            Kill(world, outbreak, settlement, rng);
        }
    }

    /// <summary>Applies one year of one plague to one settlement, and returns the dead.</summary>
    private static int Kill(WorldState world, Outbreak outbreak, Settlement settlement, IRng rng)
    {
        int before = settlement.Population;
        if (before <= 0) return 0;

        double share = outbreak.Lethality * rng.NextDouble(0.6, 1.35);
        int lost = (int)(before * DetMath.Clamp01(share));
        if (lost <= 0) return 0;

        settlement.Population = Math.Max(0, before - lost);

        Civilization owner = world.Civilizations[settlement.CivilizationId];
        owner.Population = Math.Max(0, owner.Population - lost);

        outbreak.Dead += lost;
        return lost;
    }

    /// <summary>Carries the plague to the next town.</summary>
    private static void Spread(WorldState world, Outbreak outbreak, int year, IRng rng)
    {
        // The sources are fixed before any arrival is added, so a plague cannot chain across five
        // towns within a single year by using its own new infections as fresh sources.
        var sources = new List<EntityId>();
        foreach (Infection infection in outbreak.Infected) sources.Add(infection.SettlementId);

        foreach (EntityId sourceId in sources)
        {
            Settlement source = world.Settlements[sourceId];
            if (!source.IsActive) continue;

            foreach (Settlement candidate in world.Settlements)
            {
                if (!candidate.IsActive || candidate.Id == sourceId) continue;
                if (IsInfected(outbreak, candidate.Id)) continue;
                if (IsOnItsWayTo(outbreak, candidate.Id)) continue;

                double distance = world.Distance(source.X, source.Z, candidate.X, candidate.Z);
                TradeRoute? route = TradeRoutes.Between(world, source.Id, candidate.Id);
                if (distance > SpreadRange && route is null) continue;

                double nearness = 1.0 - DetMath.InverseLerp(0.0, SpreadRange, distance);
                // Counted against what the plague is known to have reached *and* what it is on its
                // way to, because a town closes its gates on the news rather than on the disease.
                // Without the second term the damping arrives too late to do its job: several
                // carriers now set out before any of them lands, so each of them reads a reach that
                // the others have already made out of date, and an outbreak travels further than
                // the same wariness used to allow.
                double spreading = outbreak.Reached + outbreak.InTransit.Count;
                double wary = 1.0 / (1.0 + (spreading * QuarantineDecay));
                double connection = nearness * nearness;

                // An established route can carry infection beyond an ordinary local jump. The
                // route is still only topology — no road geometry is assumed — but its measured
                // traffic is a better account of travellers than distance alone.
                if (route is not null)
                {
                    connection = Math.Max(connection, 0.15 + (route.Traffic * 0.50));
                }

                double chance = outbreak.Virulence * connection * SpreadExposure(candidate) * wary;

                if (!rng.Chance(DetMath.Clamp01(chance))) continue;

                SetOut(world, outbreak, candidate, source, distance);
            }
        }
    }

    /// <summary>
    /// How exposed a settlement is to whatever the roads bring.
    /// </summary>
    /// <remarks>
    /// Size and trade, because both are traffic. It is what makes a plague travel along the
    /// prosperous coast rather than radiating evenly across the map, and what makes the places
    /// with the most to lose the likeliest to catch it.
    /// </remarks>
    private static double SpreadExposure(Settlement settlement)
    {
        double size = PopulationTraffic(settlement.Population);

        double trade = settlement.Specialization switch
        {
            SettlementSpecialization.Trade => 1.45,
            SettlementSpecialization.Fishing => 1.15,
            SettlementSpecialization.Shrine => 1.25,
            _ => 1.0,
        };

        return size * trade;
    }

    /// <summary>
    /// Continuous crowding proxy which preserves the old tier weights at their thresholds.
    /// </summary>
    /// <remarks>
    /// Tier-only traffic made a settlement of 899 people abruptly less exposed than one of 900,
    /// while cities of 4,000 and 9,000 were identical. Piecewise interpolation keeps the familiar
    /// hamlet, village, town and city calibration without making population stop mattering between
    /// thresholds. Very large cities gain only a modest final premium so size is not counted twice
    /// through both exposure and absolute deaths.
    /// </remarks>
    internal static double PopulationTraffic(int population)
    {
        if (population < 180)
        {
            return DetMath.Lerp(0.28, 0.50, DetMath.InverseLerp(0.0, 180.0, population));
        }

        if (population < 900)
        {
            return DetMath.Lerp(0.50, 0.78, DetMath.InverseLerp(180.0, 900.0, population));
        }

        if (population < 4000)
        {
            return DetMath.Lerp(0.78, 1.0, DetMath.InverseLerp(900.0, 4000.0, population));
        }

        return DetMath.Lerp(1.0, 1.15, DetMath.InverseLerp(4000.0, 8000.0, population));
    }

    /// <summary>Days a carrier takes to cover the first unit of ground, before distance is counted.</summary>
    /// <remarks>
    /// Nothing is next door. Even a neighbouring village is a few days of somebody walking there
    /// with it, and a floor keeps the shortest jumps from resolving in the step that scheduled them
    /// — which would be the old instantaneous model wearing a due date.
    /// </remarks>
    private const int TravelFloor = 6;

    /// <summary>Distance a carrier covers in a day.</summary>
    /// <remarks>
    /// Set so the longest ordinary jump — <see cref="SpreadRange"/>, 950 units — takes about seven
    /// weeks, and a jump between neighbouring towns a week or two. A trade route can carry
    /// infection further than <see cref="SpreadRange"/>, and then it takes proportionately longer:
    /// a plague crossing the map on a merchant's ship arrives seasons after it left, which is the
    /// arithmetic the year could not do.
    /// </remarks>
    private const double TravelPerDay = 22.0;

    /// <summary>Longest a plague can be on the road before it is written off.</summary>
    private const int TravelCeiling = 300;

    /// <summary>
    /// Sends the plague toward a town, to arrive when the people carrying it get there.
    /// </summary>
    /// <remarks>
    /// The docket keeps the date and the outbreak keeps the passage, because an entry names one
    /// subject and the subject is the destination. What is scheduled is a fact about the world; the
    /// roll that decided it has already happened, so no dice travel with it.
    /// </remarks>
    private static void SetOut(
        WorldState world, Outbreak outbreak, Settlement to, Settlement from, double distance)
    {
        int days = Math.Clamp(
            TravelFloor + (int)(distance / TravelPerDay), TravelFloor, TravelCeiling);

        var due = new Stamp(world.Now.Year, world.Now.Day + days);

        outbreak.InTransit.Add(new Passage(to.Id, from.Id, due));
        world.Docket.Schedule(due, DocketKind.Arrival, to.Id);
    }

    private static bool IsOnItsWayTo(Outbreak outbreak, EntityId settlementId)
    {
        foreach (Passage passage in outbreak.InTransit)
        {
            if (passage.SettlementId == settlementId) return true;
        }

        return false;
    }

    private static void Arrive(
        WorldState world,
        Outbreak outbreak,
        Settlement settlement,
        Settlement from,
        int year,
        IRng rng)
    {
        outbreak.Infected.Add(new Infection(settlement.Id, year));
        outbreak.Reached++;

        int lost = Kill(world, outbreak, settlement, rng);
        Realms.Suffered(world, settlement, lost);

        var data = Chronicle.Data(("name", outbreak.Name));
        if (lost >= NotableLoss) data["lost"] = lost.ToString(CultureInfo.InvariantCulture);

        world.Chronicle.Record(
            year,
            EventKind.PlagueSpread,
            settlement.Id,
            obj: settlement.CivilizationId,
            location: from.Id,
            data: data);
    }

    private static void Recover(WorldState world, Outbreak outbreak, IRng rng)
    {
        var recovered = new List<Infection>();

        foreach (Infection infection in outbreak.Infected)
        {
            Settlement settlement = world.Settlements[infection.SettlementId];

            // A place with nobody left in it has nothing more to give the plague.
            if (!settlement.IsActive || settlement.Population <= 0 || rng.Chance(RecoveryChance))
            {
                recovered.Add(infection);
            }
        }

        foreach (Infection infection in recovered) outbreak.Infected.Remove(infection);
    }

    /// <summary>
    /// Kills at court, in every realm the plague is currently in.
    /// </summary>
    /// <remarks>
    /// Plague does not respect a title, and this is the whole reason it reaches the rest of the
    /// engine: a ruler carried off at forty leaves a succession that the ordinary mortality curve
    /// would have produced a generation later, or not at all.
    /// </remarks>
    private static void Cull(WorldState world, Outbreak outbreak, int year, IRng rng)
    {
        var afflicted = new List<EntityId>();

        foreach (Infection infection in outbreak.Infected)
        {
            EntityId civId = world.Settlements[infection.SettlementId].CivilizationId;
            if (!afflicted.Contains(civId)) afflicted.Add(civId);
        }

        if (afflicted.Count == 0) return;

        double mortality = CourtMortality * outbreak.Lethality * 4.0;

        // Id order, which is birth order, so who dies does not depend on where the plague is.
        foreach (Figure figure in world.Figures)
        {
            if (!figure.IsAlive) continue;
            if (!afflicted.Contains(figure.CivilizationId)) continue;
            if (figure.AgeIn(year) < 0) continue;

            if (rng.Chance(DetMath.Clamp01(mortality)))
            {
                Houses.Die(
                    world, figure, year, DeathCause.Plague, "the " + outbreak.Name);
            }
        }
    }

    private static bool IsInfected(Outbreak outbreak, EntityId settlementId)
    {
        foreach (Infection infection in outbreak.Infected)
        {
            if (infection.SettlementId == settlementId) return true;
        }

        return false;
    }

    // -----------------------------------------------------------------------
    // Ignition
    // -----------------------------------------------------------------------

    private static void MaybeIgnite(WorldState world, int year, IRng rng)
    {
        if (world.Outbreaks.Count >= MaxConcurrentOutbreaks) return;

        var seats = new List<Settlement>();
        foreach (Settlement settlement in world.Settlements)
        {
            if (settlement.IsActive && settlement.Population >= MinimumSeatOfInfection)
            {
                seats.Add(settlement);
            }
        }

        if (seats.Count == 0) return;

        double totalExposure = 0.0;
        foreach (Settlement seat in seats) totalExposure += IgnitionExposure(world, seat);

        double rawHazard = IgnitionPerExposure * totalExposure;
        double chance = SoftCap(rawHazard, MaxIgnitionChance);
        if (!rng.Chance(chance)) return;

        // The same exposure which creates the world hazard chooses its origin. This keeps the
        // frequency and the geography of disease grounded in one model rather than two proxies.
        double roll = rng.NextDouble(0.0, totalExposure);
        Settlement origin = seats[seats.Count - 1];

        foreach (Settlement seat in seats)
        {
            roll -= IgnitionExposure(world, seat);
            if (roll <= 0.0)
            {
                origin = seat;
                break;
            }
        }

        IRng naming = rng.Fork("plague", origin.Id.ToDiscriminator());

        var outbreak = new Outbreak(
            Plagues.Name(naming),
            origin.Id,
            year,
            lethality: naming.NextDouble(0.07, 0.21),
            virulence: naming.NextDouble(0.12, 0.40));

        outbreak.Infected.Add(new Infection(origin.Id, year));
        outbreak.Reached = 1;
        world.Outbreaks.Add(outbreak);

        int lost = Kill(world, outbreak, origin, rng);

        var data = Chronicle.Data(("name", outbreak.Name));
        if (lost >= NotableLoss) data["lost"] = lost.ToString(CultureInfo.InvariantCulture);

        world.Chronicle.Record(
            year,
            EventKind.PlagueBegan,
            origin.Id,
            obj: origin.CivilizationId,
            location: origin.RegionId,
            data: data);
    }

    /// <summary>Population and live commercial movement which can ignite an outbreak.</summary>
    internal static double IgnitionExposure(WorldState world, Settlement settlement)
    {
        double population = DetMath.Clamp(
            DetMath.Sqrt(settlement.Population / (double)MinimumSeatOfInfection), 1.0, 2.5);

        double routeTraffic = 0.0;
        foreach (TradeRoute route in TradeRoutes.From(world, settlement.Id))
        {
            routeTraffic += route.Traffic;
        }

        double network = 1.0 + (0.15 * Math.Min(routeTraffic, 4.0));
        double specialization = settlement.Specialization switch
        {
            SettlementSpecialization.Trade => 1.15,
            SettlementSpecialization.Fishing => 1.05,
            SettlementSpecialization.Shrine => 1.10,
            _ => 1.0,
        };

        return population * network * specialization;
    }

    /// <summary>
    /// A deterministic soft ceiling: every extra exposure still matters, but by a diminishing
    /// amount as the yearly probability approaches its maximum.
    /// </summary>
    internal static double SoftCap(double raw, double maximum)
    {
        if (raw <= 0.0 || maximum <= 0.0) return 0.0;
        return maximum * raw / (maximum + raw);
    }
}

/// <summary>
/// What a plague was called.
/// </summary>
/// <remarks>
/// Composed in English from what it looked like, not drawn from a culture's naming language.
/// A plague crosses every border it meets, so it is the one thing in the world that no single
/// people gets to name — and "the Speckled Death" is how a chronicle written after the fact
/// refers to it regardless of whose chronicle it is.
/// </remarks>
public static class Plagues
{
    private static readonly string[] Adjectives =
    {
        "Speckled", "Grey", "Red", "Wasting", "Silent", "Bitter", "Coughing", "Black",
        "Summer", "Winter", "Salt", "Weeping",
    };

    private static readonly string[] Nouns =
    {
        "Death", "Fever", "Sickness", "Pestilence", "Mortality", "Plague", "Contagion",
    };

    public static string Name(IRng rng) => rng.Pick(Adjectives) + " " + rng.Pick(Nouns);
}
