using System.Globalization;
using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.World;

namespace HistoryEngine.Systems;

/// <summary>
/// Establishes, measures and closes the commercial links that later roads will serve.
/// </summary>
/// <remarks>
/// <para><b>Routes are economic facts before they are lines on the ground.</b> This system chooses
/// endpoints and a preferred transport corridor from settlement size, specialization, access,
/// distance and politics. It does not pathfind. An overland route is therefore a demand for a
/// future road, not a claim that a road already exists.</para>
///
/// <para><b>Degree is bounded.</b> Sorting all viable pairs by strength and then filling a small
/// capacity per settlement produces a legible network of hubs and spokes instead of a complete
/// graph in which every town trades directly with every other town.</para>
///
/// <para><b>Decline has hysteresis.</b> War and abandonment close a route immediately; ordinary
/// economic weakness must persist for years. That preserves established corridors through one
/// poor harvest and gives the chronicle a route history rather than yearly flicker.</para>
///
/// <para>Samples no terrain. River and coastal access are region facts derived during worldgen.</para>
/// </remarks>
public sealed class TradeRouteSystem : IYearSystem
{
    /// <summary>Longest direct commercial link; longer journeys emerge as chains of routes.</summary>
    public const double MaximumDistance = 1600.0;

    /// <summary>Strength at which a new route is worth establishing.</summary>
    private const double FormationThreshold = 0.50;

    /// <summary>Traffic below this begins a sustained decline.</summary>
    private const double DeclineThreshold = 0.36;

    /// <summary>Traffic above this is notable prosperity.</summary>
    private const double ProsperityThreshold = 0.70;

    /// <summary>Weak years tolerated before an established connection is closed.</summary>
    private const int ClosureDelay = 8;

    /// <summary>
    /// Years between searches for new links. Existing routes are still measured every year;
    /// formation waits at most four years and avoids an all-pairs scan on every tick.
    /// </summary>
    private const int FormationInterval = 5;

    private sealed record Candidate(Settlement A, Settlement B, double Strength);

    public string Name => "trade-routes";

    public void Tick(WorldState world, int year)
    {
        Maintain(world, year);
        if ((year - world.StartYear) % FormationInterval == 0) Establish(world, year);
    }

    private static void Maintain(WorldState world, int year)
    {
        foreach (TradeRoute route in world.ActiveTradeRoutes())
        {
            Settlement a = world.Settlements[route.SettlementAId];
            Settlement b = world.Settlements[route.SettlementBId];

            if (!a.IsActive || !b.IsActive)
            {
                Close(world, route, a, b, year, "after one of its settlements was abandoned");
                continue;
            }

            if (Diplomacy.AtWar(world, a.CivilizationId, b.CivilizationId))
            {
                Close(world, route, a, b, year, "when war closed the way");
                continue;
            }

            double strength = Strength(world, a, b);
            route.Traffic = strength;
            if (strength > route.PeakTraffic) route.PeakTraffic = strength;

            if (strength < DeclineThreshold)
            {
                route.YearsDeclining++;

                if (route.Status != TradeRouteStatus.Declining)
                {
                    route.Status = TradeRouteStatus.Declining;
                    Record(world, year, EventKind.TradeRouteDeclined, route, a, b);
                }

                if (route.YearsDeclining >= ClosureDelay)
                {
                    Close(
                        world,
                        route,
                        a,
                        b,
                        year,
                        $"after {ClosureDelay} years of failing traffic");
                }

                continue;
            }

            route.YearsDeclining = 0;

            if (strength >= ProsperityThreshold)
            {
                if (route.Status != TradeRouteStatus.Prosperous)
                {
                    route.Status = TradeRouteStatus.Prosperous;
                    Record(world, year, EventKind.TradeRouteFlourished, route, a, b);
                }
            }
            else
            {
                route.Status = TradeRouteStatus.Active;
            }
        }
    }

    private static void Establish(WorldState world, int year)
    {
        var candidates = new List<Candidate>();
        var degree = new int[world.Settlements.Count];
        var connected = new bool[world.Settlements.Count, world.Settlements.Count];

        foreach (TradeRoute route in world.ActiveTradeRoutes())
        {
            int a = route.SettlementAId.Index;
            int b = route.SettlementBId.Index;
            degree[a]++;
            degree[b]++;
            connected[a, b] = true;
            connected[b, a] = true;
        }

        for (int i = 0; i < world.Settlements.Count; i++)
        {
            Settlement a = world.Settlements[i];
            if (Capacity(a) == 0) continue;

            for (int j = i + 1; j < world.Settlements.Count; j++)
            {
                Settlement b = world.Settlements[j];
                if (Capacity(b) == 0 || connected[i, j]) continue;

                double strength = Strength(world, a, b);
                if (strength >= FormationThreshold) candidates.Add(new Candidate(a, b, strength));
            }
        }

        candidates.Sort(static (left, right) =>
        {
            int byStrength = right.Strength.CompareTo(left.Strength);
            if (byStrength != 0) return byStrength;

            int byA = left.A.Id.CompareTo(right.A.Id);
            return byA != 0 ? byA : left.B.Id.CompareTo(right.B.Id);
        });

        foreach (Candidate candidate in candidates)
        {
            int a = candidate.A.Id.Index;
            int b = candidate.B.Id.Index;

            if (degree[a] >= Capacity(candidate.A)
                || degree[b] >= Capacity(candidate.B)
                || connected[a, b])
            {
                continue;
            }

            EntityId id = world.TradeRoutes.NextId;
            var route = new TradeRoute(
                id,
                candidate.A.Id,
                candidate.B.Id,
                Mode(world, candidate.A, candidate.B),
                year,
                candidate.Strength)
            {
                Status = candidate.Strength >= ProsperityThreshold
                    ? TradeRouteStatus.Prosperous
                    : TradeRouteStatus.Active,
            };

            world.TradeRoutes.Add(route);
            degree[a]++;
            degree[b]++;
            connected[a, b] = true;
            connected[b, a] = true;

            var data = Chronicle.Data(
                ("mode", ModePhrase(route.Mode)),
                ("traffic", Percent(route.Traffic)));

            world.Chronicle.Record(
                year,
                EventKind.TradeRouteOpened,
                route.Id,
                obj: route.SettlementAId,
                location: route.SettlementBId,
                data: data);
        }
    }

    /// <summary>Economic strength of one direct connection, in [0, 1].</summary>
    private static double Strength(WorldState world, Settlement a, Settlement b)
    {
        if (!a.IsActive || !b.IsActive || Capacity(a) == 0 || Capacity(b) == 0) return 0.0;

        double distance = world.Distance(a.X, a.Z, b.X, b.Z);
        if (distance > MaximumDistance) return 0.0;

        Civilization realmA = world.Civilizations[a.CivilizationId];
        Civilization realmB = world.Civilizations[b.CivilizationId];
        if (!realmA.IsActive || !realmB.IsActive) return 0.0;
        if (Diplomacy.AtWar(world, realmA.Id, realmB.Id)) return 0.0;

        double distanceScore = 1.0 - DetMath.InverseLerp(world.Config.RegionSize, MaximumDistance, distance);
        double size = (TierWeight(a.Tier) + TierWeight(b.Tier)) * 0.5;
        double specialization =
            (SpecializationWeight(a.Specialization) + SpecializationWeight(b.Specialization)) * 0.5;
        double mercantile =
            (world.CultureOf(realmA).Values.Mercantile + world.CultureOf(realmB).Values.Mercantile) * 0.5;
        double politics = realmA.Id == realmB.Id
            ? 1.0
            : DetMath.InverseLerp(
                -0.25,
                0.45,
                (Diplomacy.Relation(realmA, realmB) + Diplomacy.Relation(realmB, realmA)) * 0.5);

        Region regionA = world.Regions[a.RegionId];
        Region regionB = world.Regions[b.RegionId];
        double access = regionA.IsCoastal && regionB.IsCoastal
            ? 1.0
            : regionA.HasRiver && regionB.HasRiver
                ? 0.85
                : regionA.IsCoastal || regionB.IsCoastal || regionA.HasRiver || regionB.HasRiver
                    ? 0.35
                    : 0.0;

        double complement = a.Specialization != b.Specialization ? 1.0 : 0.0;
        double tradeHub = a.Specialization == SettlementSpecialization.Trade
                          || b.Specialization == SettlementSpecialization.Trade
            ? 1.0
            : 0.0;

        return DetMath.Clamp01(
            (distanceScore * 0.25)
            + (size * 0.20)
            + (specialization * 0.14)
            + (mercantile * 0.12)
            + (politics * 0.10)
            + (access * 0.09)
            + (complement * 0.06)
            + (tradeHub * 0.04));
    }

    private static int Capacity(Settlement settlement)
    {
        if (!settlement.IsActive) return 0;

        return settlement.Tier switch
        {
            SettlementTier.City => 5,
            SettlementTier.Town => 3,
            SettlementTier.Village => 1,
            _ => 0,
        };
    }

    private static double TierWeight(SettlementTier tier) => tier switch
    {
        SettlementTier.City => 1.0,
        SettlementTier.Town => 0.65,
        SettlementTier.Village => 0.25,
        _ => 0.0,
    };

    private static double SpecializationWeight(SettlementSpecialization specialization) =>
        specialization switch
        {
            SettlementSpecialization.Trade => 1.0,
            SettlementSpecialization.Crafts => 0.80,
            SettlementSpecialization.Mining => 0.72,
            SettlementSpecialization.Fishing => 0.60,
            SettlementSpecialization.Shrine => 0.52,
            SettlementSpecialization.Farming => 0.34,
            SettlementSpecialization.Pastoral => 0.30,
            _ => 0.10,
        };

    private static TradeRouteMode Mode(WorldState world, Settlement a, Settlement b)
    {
        Region regionA = world.Regions[a.RegionId];
        Region regionB = world.Regions[b.RegionId];

        if (regionA.IsCoastal && regionB.IsCoastal) return TradeRouteMode.Coastal;
        if (regionA.HasRiver && regionB.HasRiver) return TradeRouteMode.River;
        return TradeRouteMode.Overland;
    }

    private static void Close(
        WorldState world,
        TradeRoute route,
        Settlement a,
        Settlement b,
        int year,
        string cause)
    {
        route.EndedYear = year;
        route.Status = TradeRouteStatus.Closed;
        route.Traffic = 0.0;

        Record(
            world,
            year,
            EventKind.TradeRouteClosed,
            route,
            a,
            b,
            Chronicle.Data(("cause", cause)));
    }

    private static void Record(
        WorldState world,
        int year,
        EventKind kind,
        TradeRoute route,
        Settlement a,
        Settlement b,
        DetMap<string, string>? data = null) =>
        world.Chronicle.Record(
            year,
            kind,
            route.Id,
            obj: a.Id,
            location: b.Id,
            data: data ?? Chronicle.Data(("traffic", Percent(route.Traffic))));

    private static string Percent(double traffic) =>
        ((int)(traffic * 100.0)).ToString(CultureInfo.InvariantCulture) + "%";

    private static string ModePhrase(TradeRouteMode mode) => mode switch
    {
        TradeRouteMode.River => "by river",
        TradeRouteMode.Coastal => "along the coast",
        _ => "overland",
    };
}
