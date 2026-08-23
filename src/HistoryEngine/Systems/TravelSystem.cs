using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.World;

namespace HistoryEngine.Systems;

/// <summary>
/// People leaving home for a year and coming back: trade, visits, pilgrimage, and clergy on the road.
/// </summary>
/// <remarks>
/// <para>Residence is where they live. A journey is a trip. Changing one would make a merchant
/// vanish from their town every year they used a route, and the disasters that reach a residence
/// would miss them for no reason the chronicle could defend.</para>
///
/// <para>One journey per recorded adult per year, after the trade routes of that year exist so a
/// merchant walks a corridor that is actually open. The chronicle marks the trip Routine: the
/// life page wants the itinerary; the world's spine does not.</para>
///
/// <para><b>The road costs something.</b> A journey can be robbed, and it can kill the person who
/// made it. Without that, travel is the one thing in this world with no downside: unrest raises
/// brigandage that bites trade but never bites a traveller, a war closes a border nobody was
/// crossing, and three centuries of pilgrimage pass without a single pilgrim failing to come
/// home. The hazard is deliberately small — the overwhelming majority of trips end at the
/// traveller's own hearth — but it is not zero, and where it lands it lands with a place and a
/// cause the chronicle can name.</para>
///
/// <para><b>This is where a road is finally read.</b> A route that has earned a cut way is safer
/// to travel than open country, and a paved one safer again — bridges instead of fords are most
/// of what the engineering bought. The road also says something its traffic does not: the ratio
/// of the line it had to take to the straight distance between the towns is a measurement of how
/// hard the country in between is, and hard country is dangerous country. Nothing here samples
/// terrain; the path was cut once, years ago, and its length has been sitting on the route ever
/// since.</para>
/// </remarks>
public sealed class TravelSystem : ISystem
{
    /// <summary>
    /// The share of journeys of each kind that goes wrong on ordinary ground, before anything
    /// about this particular year is counted.
    /// </summary>
    /// <remarks>
    /// Ordered by how much company the traveller keeps. A merchant moves with a caravan along a
    /// corridor other merchants use and someone's soldiers nominally patrol; a guest of an allied
    /// court travels at that court's expense and under its protection. A priest sent to preach
    /// among strangers and a pilgrim walking to a shrine have neither, and the pilgrim is often
    /// old on top of it.
    /// </remarks>
    private const double TradeHazard = 0.016;

    private const double VisitHazard = 0.010;

    private const double MissionHazard = 0.026;

    private const double PilgrimageHazard = 0.030;

    /// <summary>How much the lawlessness of the worse end adds on top.</summary>
    /// <remarks>
    /// Keyed to the worse of the two ends rather than the average, for the same reason the trade
    /// system takes the minimum of the two securities: a road is as safe as its unsafest stretch,
    /// and nobody is robbed on average. Weighted heavily enough that a country given over to
    /// brigands roughly triples the risk of crossing it — which is the point of the brigandage
    /// the unrest system raises, and until now it was a number that only ever moved a ledger.
    /// </remarks>
    private const double LawlessnessToll = 0.075;

    /// <summary>What a war between the traveller's realm and the far end adds.</summary>
    /// <remarks>
    /// Missions and visits already refuse to set out into a war. This is for the merchant, whose
    /// route stays open across a border its two realms are fighting over, and for a war declared
    /// after the traveller left.
    /// </remarks>
    private const double WartimeToll = 0.045;

    /// <summary>Distance at which a journey has paid its full distance toll, in world units.</summary>
    /// <remarks>
    /// Roughly the longest trade route the standard worlds sustain. Beyond it the toll stops
    /// growing: past a certain point a journey is simply "far", and the model has nothing finer
    /// to say than that.
    /// </remarks>
    private const double FullReach = 900.0;

    private const double DistanceToll = 0.030;

    /// <summary>What is left of the hazard on a cut track, and on an engineered road.</summary>
    /// <remarks>
    /// The track keeps the traveller out of the worst ground and off the wrong valley; the paved
    /// road adds the bridges, which is what removes the two places a crossing actually drowns
    /// people. Neither makes a road safe — brigands prefer a road, since that is where the goods
    /// are — so the remainder is a discount and not an exemption.
    /// </remarks>
    private const double TrackSafety = 0.80;

    private const double PavedSafety = 0.62;

    /// <summary>The detour ratio at which a road's country starts counting against it.</summary>
    /// <remarks>
    /// A road that runs near the straight line crossed country that put up no argument. One that
    /// runs half as far again crossed country that did, and the ground a road had to go round is
    /// ground a traveller can still fall off. Measured on seed 42: built roads range from 1.00 to
    /// 1.41, so this discriminates rather than labelling every road the same.
    /// </remarks>
    private const double EasyCountry = 1.10;

    private const double HardCountry = 1.45;

    private const double HardCountryToll = 0.40;

    /// <summary>Ceiling on a single journey's hazard, whatever the terms sum to.</summary>
    private const double WorstCase = 0.30;

    /// <summary>The share of mishaps that kills, by land and by water.</summary>
    /// <remarks>
    /// Most robberies leave the robbed alive — a corpse is worth nothing and brings soldiers. A
    /// ship in trouble is a different arithmetic, and its passengers do not get to walk home.
    /// </remarks>
    private const double LandFatality = 0.28;

    private const double WaterFatality = 0.55;

    /// <summary>The age past which a bad road is much more likely to be the end of it.</summary>
    private const int FrailAge = 55;

    private const double FrailFatality = 0.20;

    /// <summary>How often a robbery takes something the traveller personally owned.</summary>
    private const double PlunderChance = 0.35;

    /// <summary>The lawlessness at which a town is named as where the men came from.</summary>
    /// <remarks>
    /// Matched to the level the unrest system considers worth writing a brigandage event about, so
    /// that the chronicle only blames a place it has already told the reader had gone bad.
    /// </remarks>
    private const double BlamedBanditry = 0.30;

    /// <summary>How often a robbery in lawless country is pinned on the town it came out of.</summary>
    private const double BlameChance = 0.6;

    /// <summary>What went wrong, told twice.</summary>
    /// <remarks>
    /// The two halves are not interchangeable and one cannot be derived from the other. The
    /// chronicle says "came to grief on the way to Shche, when the ship was driven aground"; the
    /// obituary a year later says "died, of a wreck off the coast". A single string put in both
    /// slots produces one good line and one that reads "died, of when the ship was driven
    /// aground", which is the sort of thing that survives a demo.
    /// </remarks>
    private readonly record struct Mishap(string Clause, string Death);

    /// <summary>How a journey ends badly on a road somebody keeps.</summary>
    private static readonly Mishap[] RoadMishaps =
    {
        new("set upon on the road", "wounds taken on the road"),
        new("robbed at a crossing", "wounds taken at a crossing"),
        new("taken by armed men on the way", "violence on the road"),
    };

    /// <summary>How one ends badly in country with no road at all.</summary>
    private static readonly Mishap[] WildMishaps =
    {
        new("lost in country nobody keeps", "exposure on the way"),
        new("caught by weather in the hills", "the weather in the hills"),
        new("of fever taken on the way", "a fever taken on the way"),
    };

    /// <summary>How one ends badly at sea.</summary>
    private static readonly Mishap[] WaterMishaps =
    {
        new("in a storm off the coast", "a storm at sea"),
        new("when the ship was driven aground", "a wreck off the coast"),
        new("of thirst after a long calm", "thirst at sea"),
    };

    public string Name => "travel";

    public Cadence Cadence => Cadence.Annual;

    public void Tick(WorldState world, Stamp now)
    {
        int year = now.Year;
        IRng rng = world.Root.Fork(Name, year);

        foreach (Figure figure in world.Figures)
        {
            if (!figure.IsAlive) continue;
            if (figure.AgeIn(year) < Succession.MajorityAge) continue;

            EntityId home = world.ResidenceOf(figure);
            if (home.IsNone) continue;

            IRng person = rng.Fork("figure", figure.Id.ToDiscriminator());
            Consider(world, figure, home, year, person);
        }
    }

    private static void Consider(
        WorldState world, Figure figure, EntityId home, int year, IRng rng)
    {
        if (TryTrade(world, figure, home, year, rng)) return;
        if (TryMission(world, figure, home, year, rng)) return;
        if (TryPilgrimage(world, figure, home, year, rng)) return;
        TryVisit(world, figure, home, year, rng);
    }

    private static bool TryTrade(
        WorldState world, Figure figure, EntityId home, int year, IRng rng)
    {
        if (figure.Occupation != Occupation.Merchant) return false;

        var destinations = new List<TradeRoute>();
        foreach (TradeRoute route in TradeRoutes.From(world, home))
        {
            destinations.Add(route);
        }

        if (destinations.Count == 0) return false;
        if (!rng.Chance(0.28 + (figure.Disposition.Values.Mercantile * 0.22))) return false;

        TradeRoute chosen = rng.Pick(destinations);
        EntityId to = chosen.Other(home);
        if (to.IsNone || to == home) return false;

        // No named target: the destination already is the reason, and "traded to Shche along the
        // Aigionanvos–Shche route" tells a reader nothing the line did not already say.
        Record(world, figure, JourneyKind.Trade, year, home, to, chosen.Id, "on trade");
        return true;
    }

    private static bool TryMission(
        WorldState world, Figure figure, EntityId home, int year, IRng rng)
    {
        bool clergy = figure.Occupation == Occupation.Clergy || figure.Holds(OfficeKind.HighPriest);
        if (!clergy) return false;
        if (figure.ReligionId.IsNone) return false;
        if (!rng.Chance(0.10 + (figure.Disposition.Values.Piety * 0.12))) return false;

        bool copies = rng.Chance(0.40);
        Settlement? destination = copies
            ? PickScriptorium(world, figure, home, rng)
            : PickCoReligionist(world, figure, home, rng);

        if (destination is null) return false;

        // A scribe is sent to a particular house, so the errand carries the monastery that made
        // the town worth the walk. When there is no monastery anywhere to send them to,
        // PickScriptorium has already handed back an ordinary town of the same communion — so the
        // journey is recorded as the thing it actually is, a circuit among co-religionists, rather
        // than as an errand to a library that does not exist.
        HolySite? scriptorium = copies ? Tomes.ScriptoriumAt(world, destination) : null;

        (EntityId via, string purpose) = scriptorium is not null
            ? (scriptorium.Id, "to fetch copies from")
            : (destination.ReligionId, "to preach among");

        Record(world, figure, JourneyKind.Mission, year, home, destination.Id, via, purpose);
        return true;
    }

    private static bool TryPilgrimage(
        WorldState world, Figure figure, EntityId home, int year, IRng rng)
    {
        if (figure.ReligionId.IsNone) return false;
        if (figure.Disposition.Values.Piety < 0.42) return false;
        if (!rng.Chance(0.03 + (figure.Disposition.Values.Piety * 0.08))) return false;

        var sites = new List<HolySite>();
        foreach (HolySite site in world.HolySites)
        {
            if (site.ReligionId != figure.ReligionId) continue;
            if (site.FoundedYear > year) continue;
            if (site.SettlementId.IsNone || site.SettlementId == home) continue;
            if (!world.Settlements.Contains(site.SettlementId)) continue;
            if (!world.Settlements[site.SettlementId].IsActive) continue;

            sites.Add(site);
        }

        if (sites.Count == 0) return false;

        HolySite chosen = rng.Pick(sites);
        Record(
            world, figure, JourneyKind.Pilgrimage, year, home, chosen.SettlementId,
            chosen.Id, "on pilgrimage to");
        return true;
    }

    private static bool TryVisit(
        WorldState world, Figure figure, EntityId home, int year, IRng rng)
    {
        if (figure.Occupation is not (Occupation.Court or Occupation.Official)) return false;
        if (!world.Civilizations.Contains(figure.CivilizationId)) return false;

        Civilization realm = world.Civilizations[figure.CivilizationId];
        if (realm.Allies.Count == 0) return false;
        if (!rng.Chance(0.06 + (figure.Disposition.Values.Tradition * 0.04))) return false;

        var hosts = new List<Civilization>();
        foreach (KeyValuePair<EntityId, int> pact in realm.Allies)
        {
            if (!world.Civilizations.Contains(pact.Key)) continue;

            Civilization ally = world.Civilizations[pact.Key];
            if (!ally.IsActive) continue;
            if (Diplomacy.AtWar(world, realm.Id, ally.Id)) continue;
            if (!world.Settlements.Contains(ally.CapitalId)) continue;
            if (!world.Settlements[ally.CapitalId].IsActive) continue;
            if (ally.CapitalId == home) continue;

            hosts.Add(ally);
        }

        if (hosts.Count == 0) return false;

        Civilization host = rng.Pick(hosts);
        Record(
            world, figure, JourneyKind.Visit, year, home, host.CapitalId,
            host.Id, "as a guest of");
        return true;
    }

    private static Settlement? PickScriptorium(
        WorldState world, Figure figure, EntityId home, IRng rng)
    {
        var towns = new List<Settlement>();
        foreach (Settlement settlement in world.Settlements)
        {
            if (!settlement.IsActive || settlement.Id == home) continue;
            if (settlement.ReligionId != figure.ReligionId) continue;
            if (Diplomacy.AtWar(world, figure.CivilizationId, settlement.CivilizationId)) continue;
            if (!Tomes.HasScriptorium(world, settlement)) continue;

            towns.Add(settlement);
        }

        return towns.Count == 0 ? PickCoReligionist(world, figure, home, rng) : rng.Pick(towns);
    }

    private static Settlement? PickCoReligionist(
        WorldState world, Figure figure, EntityId home, IRng rng)
    {
        var towns = new List<Settlement>();
        foreach (Settlement settlement in world.Settlements)
        {
            if (!settlement.IsActive || settlement.Id == home) continue;
            if (settlement.ReligionId != figure.ReligionId) continue;
            if (settlement.CivilizationId == figure.CivilizationId) continue;
            if (Diplomacy.AtWar(world, figure.CivilizationId, settlement.CivilizationId)) continue;

            towns.Add(settlement);
        }

        return towns.Count == 0 ? null : rng.Pick(towns);
    }

    private static void Record(
        WorldState world,
        Figure figure,
        JourneyKind kind,
        int year,
        EntityId from,
        EntityId to,
        EntityId via,
        string purpose)
    {
        var journey = new Journey(kind, year, from, to, via);
        figure.Journeys.Add(journey);

        var extra = new List<EntityId> { from };
        if (!via.IsNone) extra.Add(via);

        world.Chronicle.Record(
            year,
            EventKind.JourneyMade,
            figure.Id,
            location: to,
            extra: extra.ToArray(),
            data: Chronicle.Data(("purpose", purpose), ("kind", kind.ToString())),
            significance: Significance.Routine);

        Resolve(world, figure, journey, year);
    }

    /// <summary>
    /// Rolls the road against the traveller, and writes what it did to them.
    /// </summary>
    /// <remarks>
    /// Called for every journey and does nothing at all for the great majority. A mishap is
    /// recorded as its own event so that a life page reads "travelled to Shche" in most years and
    /// "came to grief on the way to Shche" in the year it mattered, rather than the reader having
    /// to notice a changed field on an otherwise identical line.
    /// </remarks>
    private static void Resolve(WorldState world, Figure figure, Journey journey, int year)
    {
        // Forked from the root by traveller and year rather than drawn from the stream that chose
        // the destination, so that adding a holy site or a trade route — either of which changes
        // how many draws the choice above costs — cannot decide whether some unrelated traveller
        // in another realm came home that year.
        IRng road = world.Root.Fork("travel.road", figure.Id.ToDiscriminator()).Fork("year", year);

        TradeRoute? corridor = TradeRoutes.Between(
            world, journey.FromSettlementId, journey.ToSettlementId);

        double hazard = Hazard(world, figure, journey, corridor);
        if (!road.Chance(hazard)) return;

        bool afloat = corridor is { Mode: TradeRouteMode.Coastal };
        Mishap mishap = WhatHappened(world, journey, corridor, afloat, road);

        journey.Outcome = JourneyOutcome.Waylaid;

        world.Chronicle.Record(
            year,
            EventKind.JourneyWaylaid,
            figure.Id,
            obj: figure.DynastyId,
            location: journey.ToSettlementId,
            extra: Where(journey, corridor),
            data: Chronicle.Data(("cause", mishap.Clause), ("kind", journey.Kind.ToString())));

        double fatal = afloat ? WaterFatality : LandFatality;
        if (figure.AgeIn(year) >= FrailAge) fatal += FrailFatality;

        if (road.Chance(fatal))
        {
            journey.Outcome = JourneyOutcome.Lost;

            // Indexed on the place they were going as well as on their house: a death on the road
            // to somewhere is part of that somewhere's record of what its roads were like.
            Houses.Die(
                world,
                figure,
                year,
                DeathCause.Accident,
                detail: mishap.Death,
                extra: new[] { journey.ToSettlementId });

            return;
        }

        // Robbed and alive. If they had something of their own worth taking, it is gone — which is
        // the one way an artifact in this world leaves a settlement without an army arriving.
        if (!afloat && road.Chance(PlunderChance))
        {
            Treasures.LoseCarried(world, figure, year, "taken from its keeper on the road", road);
        }
    }

    /// <summary>The share of journeys like this one that go wrong, in [0, <see cref="WorstCase"/>].</summary>
    private static double Hazard(
        WorldState world, Figure figure, Journey journey, TradeRoute? corridor)
    {
        double hazard = journey.Kind switch
        {
            JourneyKind.Trade => TradeHazard,
            JourneyKind.Visit => VisitHazard,
            JourneyKind.Mission => MissionHazard,
            _ => PilgrimageHazard,
        };

        bool afloat = corridor is { Mode: TradeRouteMode.Coastal };

        // Brigandage is a fact about the country either end sits in. A sea passage crosses none of
        // it — pirates are not modelled, and pretending the roads' lawlessness follows a ship out
        // to water would be borrowing a number from a place it does not describe.
        if (!afloat)
        {
            hazard += Lawlessness(world, journey) * LawlessnessToll;
        }

        hazard += Reach(world, journey) * DistanceToll;

        if (Warring(world, figure, journey)) hazard += WartimeToll;

        return DetMath.Clamp01(
            Math.Min(hazard * Ground(corridor, Reach(world, journey) * FullReach, journey.Year),
            WorstCase));
    }

    /// <summary>How lawless the worse of the two ends is, in [0, 1].</summary>
    private static double Lawlessness(WorldState world, Journey journey)
    {
        double worst = 0.0;

        foreach (EntityId end in new[] { journey.FromSettlementId, journey.ToSettlementId })
        {
            if (!world.Settlements.Contains(end)) continue;

            worst = Math.Max(worst, world.Settlements[end].Banditry);
        }

        return DetMath.Clamp01(worst);
    }

    /// <summary>How far this journey is, as a share of <see cref="FullReach"/>.</summary>
    private static double Reach(WorldState world, Journey journey)
    {
        if (!world.Settlements.Contains(journey.FromSettlementId)) return 0.0;
        if (!world.Settlements.Contains(journey.ToSettlementId)) return 0.0;

        Settlement from = world.Settlements[journey.FromSettlementId];
        Settlement to = world.Settlements[journey.ToSettlementId];

        return DetMath.Clamp01(
            world.Distance(from.X, from.Z, to.X, to.Z) / FullReach);
    }

    /// <summary>True when the traveller's own realm is fighting the one they are travelling into.</summary>
    private static bool Warring(WorldState world, Figure figure, Journey journey)
    {
        if (!world.Settlements.Contains(journey.ToSettlementId)) return false;

        Settlement destination = world.Settlements[journey.ToSettlementId];
        if (destination.IsOccupied) return true;

        return Diplomacy.AtWar(world, figure.CivilizationId, destination.CivilizationId);
    }

    /// <summary>
    /// What the ground between the two towns does to the hazard, as a multiplier.
    /// </summary>
    /// <remarks>
    /// <para>Open country is the baseline, because most journeys in this world cross ground nobody
    /// has ever spent anything on. A cut road is a discount on that; the country the road had to
    /// bend through gives some of the discount back.</para>
    ///
    /// <para>Reading <see cref="Road.Length"/> costs nothing: the path was searched once when the
    /// road was built and the number has been on the route ever since. This is the whole of the
    /// engine's use of road geometry, and it is a use the route's traffic could not have served —
    /// traffic says how much is carried, not how far round the carrying has to go.</para>
    /// </remarks>
    internal static double Ground(TradeRoute? corridor, double direct, int year)
    {
        if (corridor?.Road is not Road road) return 1.0;
        if (road.BuiltYear > year) return 1.0;

        double surface = road.Grade == RoadGrade.Paved ? PavedSafety : TrackSafety;
        if (direct <= 1.0) return surface;

        double detour = road.Length / direct;
        double country = DetMath.Clamp01((detour - EasyCountry) / (HardCountry - EasyCountry));

        return surface * (1.0 + (country * HardCountryToll));
    }

    /// <summary>What the chronicle says happened, in the traveller's own year.</summary>
    private static Mishap WhatHappened(
        WorldState world, Journey journey, TradeRoute? corridor, bool afloat, IRng rng)
    {
        if (afloat) return rng.Pick(WaterMishaps);

        // Named when one end of the road is visibly lawless, because then the chronicle can say
        // where the men came from — and it is the only line in the world that ties a robbery to
        // the town whose grievance produced the robbers. Otherwise the road is simply not safe
        // and nobody knows whose men they were.
        Settlement? lawless = Lawless(world, journey);
        if (lawless is not null && rng.Chance(BlameChance))
        {
            return new Mishap(
                "set upon by brigands out of " + lawless.Name,
                "wounds taken on the road");
        }

        return corridor is null ? rng.Pick(WildMishaps) : rng.Pick(RoadMishaps);
    }

    /// <summary>The lawless end of the road, if either end is lawless enough to be blamed.</summary>
    private static Settlement? Lawless(WorldState world, Journey journey)
    {
        Settlement? worst = null;

        foreach (EntityId end in new[] { journey.FromSettlementId, journey.ToSettlementId })
        {
            if (!world.Settlements.Contains(end)) continue;

            Settlement settlement = world.Settlements[end];
            if (settlement.Banditry < BlamedBanditry) continue;
            if (worst is null || settlement.Banditry > worst.Banditry) worst = settlement;
        }

        return worst;
    }

    /// <summary>The origin and the corridor, so the mishap lands on their pages too.</summary>
    private static EntityId[] Where(Journey journey, TradeRoute? corridor) =>
        corridor is null
            ? new[] { journey.FromSettlementId }
            : new[] { journey.FromSettlementId, corridor.Id };
}
