using HistoryEngine.Core;
using HistoryEngine.Entities;

namespace HistoryEngine.World;

/// <summary>Queries over the world's persistent commercial connections.</summary>
public static class TradeRoutes
{
    /// <summary>The active route directly connecting two settlements, if one exists.</summary>
    public static TradeRoute? Between(WorldState world, EntityId a, EntityId b)
    {
        foreach (TradeRoute route in world.ActiveTradeRoutes())
        {
            if ((route.SettlementAId == a && route.SettlementBId == b)
                || (route.SettlementAId == b && route.SettlementBId == a))
            {
                return route;
            }
        }

        return null;
    }

    /// <summary>Every active route touching a settlement, in route id order.</summary>
    public static IEnumerable<TradeRoute> From(WorldState world, EntityId settlementId)
    {
        foreach (TradeRoute route in world.ActiveTradeRoutes())
        {
            if (route.Connects(settlementId)) yield return route;
        }
    }

    /// <summary>Number of active routes touching a settlement.</summary>
    public static int Degree(WorldState world, EntityId settlementId)
    {
        int degree = 0;
        foreach (TradeRoute route in From(world, settlementId)) degree++;
        return degree;
    }

    /// <summary>Summed live traffic of every active route touching a settlement.</summary>
    /// <remarks>
    /// Walks the whole route table. Anything asking this for every settlement in the same tick
    /// wants <see cref="TrafficBySettlement"/>, which answers all of them in one pass.
    /// </remarks>
    public static double TrafficAt(WorldState world, EntityId settlementId)
    {
        double traffic = 0.0;
        foreach (TradeRoute route in From(world, settlementId)) traffic += route.Traffic;
        return traffic;
    }

    /// <summary>
    /// Live traffic reaching every settlement the network touches, in one pass over the routes.
    /// </summary>
    public static TradeTraffic TrafficBySettlement(WorldState world) => TradeTraffic.Survey(world);
}

/// <summary>
/// How much commercial movement reaches each settlement this year.
/// </summary>
/// <remarks>
/// <para>A lookup table rather than a raw map, and that is the point of the type. The engine bans
/// <see cref="Dictionary{TKey,TValue}"/> inside <c>Systems</c> so that no iteration order can ever
/// reach a history — a rule <c>DeterminismGuardTests</c> enforces by reading the source. This
/// exposes no enumeration at all, so a caller cannot walk it even by accident, and the guard stays
/// intact rather than being waived for a case that happens to be safe today.</para>
///
/// <para>Built once per tick by <see cref="PopulationSystem"/>, because the per-settlement query
/// walks the whole route table and asking it for every settlement every year is quadratic.</para>
/// </remarks>
public sealed class TradeTraffic
{
    private readonly Dictionary<EntityId, double> _bySettlement = new();

    private TradeTraffic()
    {
    }

    internal static TradeTraffic Survey(WorldState world)
    {
        var traffic = new TradeTraffic();

        foreach (TradeRoute route in world.ActiveTradeRoutes())
        {
            traffic.Add(route.SettlementAId, route.Traffic);
            traffic.Add(route.SettlementBId, route.Traffic);
        }

        return traffic;
    }

    /// <summary>Summed live traffic reaching a settlement. Zero if no active route touches it.</summary>
    public double At(EntityId settlementId) =>
        _bySettlement.TryGetValue(settlementId, out double sum) ? sum : 0.0;

    private void Add(EntityId id, double amount) =>
        _bySettlement[id] = At(id) + amount;
}
