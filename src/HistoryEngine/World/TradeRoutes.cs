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
}
