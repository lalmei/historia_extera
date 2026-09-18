using HistoryEngine.Core;
using HistoryEngine.Entities;

namespace HistoryEngine.World;

/// <summary>
/// Finds the cheapest way across the active trade-route network, for a journey that a single
/// corridor does not reach.
/// </summary>
/// <remarks>
/// <para><b>Why this exists beside <see cref="TradeRoutes.Between"/>.</b> That query answers "is
/// there a route straight from A to B", which is all a trading trip along a standing corridor ever
/// asked. A pilgrimage, a mission, or a diplomatic visit has no such standing relationship with its
/// destination — it wants the cheapest way there over however many routes that takes, the way a
/// real traveller would ask a merchant "how do I get to Shche" rather than "is there a route to
/// Shche".</para>
///
/// <para><b>Costed in one-way travel days, not distance.</b> A leg's cost is
/// <see cref="TravelPace.OneWayDays"/> for its actual surface — a paved leg is cheaper than a
/// track of the same length, and a track is cheaper than open country. Searching on raw distance
/// would route a pilgrim around a paved valley and over an unpaved ridge that happened to be
/// shorter, which is not the way anyone who could read the road would go.</para>
/// </remarks>
public static class Itineraries
{
    /// <summary>The cheapest itinerary from one settlement to another, or null if none exists.</summary>
    public static Itinerary? Find(
        WorldState world, EntityId sourceId, EntityId destinationId, int year)
    {
        if (sourceId == destinationId) return null;
        if (!world.Settlements.Contains(sourceId) || !world.Settlements.Contains(destinationId))
        {
            return null;
        }

        if (!world.Settlements[sourceId].IsActive || !world.Settlements[destinationId].IsActive)
        {
            return null;
        }

        return world.RouteGraphFor(year).Search(sourceId, destinationId);
    }
}

/// <summary>The pure travel-pace arithmetic shared by a whole trip and by one leg of it.</summary>
/// <remarks>
/// Factored out so a leg's one-way cost and <see cref="Systems.TravelSystem"/>'s round-trip
/// duration are the same expression evaluated once each, rather than one being derived from the
/// other by halving or doubling. A round trip floors at two days and a one-way leg at one; doubling
/// a floored one-way answer or halving a floored round-trip answer both give the wrong number right
/// at that floor, which is exactly where a short hop between neighbouring towns lives.
/// </remarks>
internal static class TravelPace
{
    internal static double Pace(double unitsPerTravelDay, TradeRouteMode mode, RoadGrade? grade)
    {
        double modePace = mode switch
        {
            TradeRouteMode.Coastal => 1.70,
            TradeRouteMode.River => 1.25,
            _ => 1.0,
        };
        double surfacePace = grade == RoadGrade.Paved ? 1.25 : 1.0;

        return unitsPerTravelDay * modePace * surfacePace;
    }

    /// <summary>One-way days for a leg of this distance, mode and surface. Floors at one day.</summary>
    internal static int OneWayDays(
        double distance, double unitsPerTravelDay, TradeRouteMode mode, RoadGrade? grade)
    {
        double pace = Pace(unitsPerTravelDay, mode, grade);
        return Math.Max(1, (int)Math.Ceiling(Math.Max(0.0, distance) / pace));
    }
}

/// <summary>
/// Adjacency over one year's active trade routes, built once and searched for every journey that
/// year asks for.
/// </summary>
/// <remarks>
/// <para><b>Built once per year, not once per journey.</b> <see cref="Systems.TravelSystem"/>
/// records many journeys in a single tick, and the active route set cannot change between them —
/// routes open and close once a year, before travel runs. Rebuilding this per journey would put a
/// graph construction inside a loop that already runs it once; see <see cref="WorldState.
/// RouteGraphFor"/> for where the one-per-year cache lives.</para>
///
/// <para><b>No dictionary, no hash set.</b> A settlement's <see cref="EntityId.Index"/> is already
/// a dense integer — <see cref="Core.EntityTable{T}"/> guarantees it — so adjacency is a plain CSR
/// layout over settlement-index arrays. Nothing here is keyed by anything but that index, so there
/// is no hash-container iteration order to accidentally depend on.</para>
///
/// <para><b>Dijkstra with a unique frontier minimum.</b> The queue is keyed on
/// <c>cost × settlementCount + settlementIndex</c>, the same packing <see cref="Roadbed"/> uses
/// over grid cells, for the same reason: two candidate paths can share a cost, but they cannot
/// share a key, so the frontier's minimum does not depend on the heap's internal tie-breaking. A
/// settlement is relaxed only on a strictly smaller cost, so the edge that first reaches a
/// settlement at its final cost is the one kept — and because routes are walked in
/// <see cref="WorldState.ActiveTradeRoutes"/> order (founding order, i.e. increasing route id) when
/// this graph is built, "first" among equal-cost edges out of the same settlement is always the
/// lower route id.</para>
/// </remarks>
internal sealed class RouteGraph
{
    private readonly int _settlementCount;
    private readonly int[] _adjacencyStart;
    private readonly int[] _edgeTarget;
    private readonly int[] _edgeCost;
    private readonly EntityId[] _edgeRouteId;

    private RouteGraph(
        int settlementCount,
        int[] adjacencyStart,
        int[] edgeTarget,
        int[] edgeCost,
        EntityId[] edgeRouteId)
    {
        _settlementCount = settlementCount;
        _adjacencyStart = adjacencyStart;
        _edgeTarget = edgeTarget;
        _edgeCost = edgeCost;
        _edgeRouteId = edgeRouteId;
    }

    internal static RouteGraph Build(WorldState world, int year)
    {
        int n = world.Settlements.Count;

        var fromIndex = new List<int>();
        var toIndex = new List<int>();
        var routeId = new List<EntityId>();
        var cost = new List<int>();

        foreach (TradeRoute route in world.ActiveTradeRoutes())
        {
            EntityId aId = route.SettlementAId;
            EntityId bId = route.SettlementBId;
            if (!world.Settlements.Contains(aId) || !world.Settlements.Contains(bId)) continue;

            Settlement a = world.Settlements[aId];
            Settlement b = world.Settlements[bId];
            if (!a.IsActive || !b.IsActive) continue;

            int legCost = LegCost(world, route, a, b, year);

            fromIndex.Add(aId.Index);
            toIndex.Add(bId.Index);
            routeId.Add(route.Id);
            cost.Add(legCost);

            fromIndex.Add(bId.Index);
            toIndex.Add(aId.Index);
            routeId.Add(route.Id);
            cost.Add(legCost);
        }

        var degree = new int[n];
        for (int e = 0; e < fromIndex.Count; e++) degree[fromIndex[e]]++;

        var adjacencyStart = new int[n + 1];
        for (int i = 0; i < n; i++) adjacencyStart[i + 1] = adjacencyStart[i] + degree[i];

        var edgeTarget = new int[fromIndex.Count];
        var edgeCost = new int[fromIndex.Count];
        var edgeRouteId = new EntityId[fromIndex.Count];
        var cursor = (int[])adjacencyStart.Clone();

        for (int e = 0; e < fromIndex.Count; e++)
        {
            int slot = cursor[fromIndex[e]]++;
            edgeTarget[slot] = toIndex[e];
            edgeCost[slot] = cost[e];
            edgeRouteId[slot] = routeId[e];
        }

        return new RouteGraph(n, adjacencyStart, edgeTarget, edgeCost, edgeRouteId);
    }

    /// <summary>What one leg between two active settlements costs, at this year's surface.</summary>
    private static int LegCost(WorldState world, TradeRoute route, Settlement a, Settlement b, int year)
    {
        double distance = world.Distance(a.X, a.Z, b.X, b.Z);
        RoadGrade? grade = null;

        if (route.Road is Road road && road.BuiltYear <= year)
        {
            distance = road.Length;
            grade = road.PavedYear is int pavedYear && pavedYear <= year
                ? RoadGrade.Paved
                : RoadGrade.Track;
        }

        return TravelPace.OneWayDays(distance, world.Config.UnitsPerTravelDay, route.Mode, grade);
    }

    /// <summary>Dijkstra over the settlement graph, returning the cheapest itinerary or null.</summary>
    internal Itinerary? Search(EntityId sourceId, EntityId destinationId)
    {
        int n = _settlementCount;
        int source = sourceId.Index;
        int destination = destinationId.Index;
        if (source < 0 || source >= n || destination < 0 || destination >= n) return null;

        var cost = new int[n];
        var viaNode = new int[n];
        var viaEdge = new int[n];
        var settled = new bool[n];

        for (int i = 0; i < n; i++)
        {
            cost[i] = int.MaxValue;
            viaNode[i] = -1;
            viaEdge[i] = -1;
        }

        cost[source] = 0;

        var frontier = new PriorityQueue<int, long>();
        frontier.Enqueue(source, source);

        while (frontier.TryDequeue(out int node, out _))
        {
            if (settled[node]) continue;
            settled[node] = true;
            if (node == destination) break;

            for (int e = _adjacencyStart[node]; e < _adjacencyStart[node + 1]; e++)
            {
                int next = _edgeTarget[e];
                if (settled[next]) continue;

                int relaxed = cost[node] + _edgeCost[e];
                if (relaxed >= cost[next]) continue;

                cost[next] = relaxed;
                viaNode[next] = node;
                viaEdge[next] = e;
                frontier.Enqueue(next, ((long)relaxed * n) + next);
            }
        }

        if (cost[destination] == int.MaxValue) return null;

        var settlementIndices = new List<int> { destination };
        var edgeIndices = new List<int>();

        int at = destination;
        while (at != source)
        {
            edgeIndices.Add(viaEdge[at]);
            at = viaNode[at];
            settlementIndices.Add(at);
        }

        settlementIndices.Reverse();
        edgeIndices.Reverse();

        var settlementIds = new EntityId[settlementIndices.Count];
        for (int i = 0; i < settlementIndices.Count; i++)
        {
            settlementIds[i] = EntityId.Settlement(settlementIndices[i]);
        }

        var routeIds = new EntityId[edgeIndices.Count];
        for (int i = 0; i < edgeIndices.Count; i++)
        {
            routeIds[i] = _edgeRouteId[edgeIndices[i]];
        }

        return new Itinerary(routeIds, settlementIds, cost[destination]);
    }
}
