using HistoryEngine.Core;
using HistoryEngine.Entities;

namespace HistoryEngine.World;

/// <summary>
/// The settlement at the other end of every live trade route, per settlement, surveyed once.
/// </summary>
/// <remarks>
/// <para><see cref="TradeRoutes.From"/> walks the whole route table, which is right for the odd
/// question and quadratic for a yearly pass that asks it of every settlement several times over —
/// the craft supply each trade stands on does exactly that (#253). This is the same answer built in
/// one walk.</para>
///
/// <para>Unlike the travel graph in <see cref="Itineraries"/> it keeps every live route, including
/// one whose far end has emptied, because <see cref="TradeRoutes.Degree"/> counts those and the
/// supply gate reads its degree. Indexed by <see cref="EntityId.Index"/>, which the settlement table
/// keeps dense, so there is no hash container to depend on the order of; each list keeps the route
/// table's own order.</para>
/// </remarks>
public sealed class RouteNeighbours
{
    private static readonly IReadOnlyList<EntityId> None = Array.Empty<EntityId>();

    private readonly List<EntityId>?[] _others;

    private RouteNeighbours(int settlements) => _others = new List<EntityId>?[settlements];

    public static RouteNeighbours Survey(WorldState world)
    {
        var survey = new RouteNeighbours(world.Settlements.Count);
        foreach (TradeRoute route in world.ActiveTradeRoutes())
        {
            survey.Add(route.SettlementAId, route.SettlementBId);
            survey.Add(route.SettlementBId, route.SettlementAId);
        }

        return survey;
    }

    /// <summary>The far end of each live route touching this settlement, in route order.</summary>
    public IReadOnlyList<EntityId> From(EntityId settlementId)
    {
        int index = settlementId.Index;
        return index >= 0 && index < _others.Length ? _others[index] ?? None : None;
    }

    private void Add(EntityId from, EntityId to)
    {
        int index = from.Index;
        if (index < 0 || index >= _others.Length) return;
        (_others[index] ??= new List<EntityId>()).Add(to);
    }
}
