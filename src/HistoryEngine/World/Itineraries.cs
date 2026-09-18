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

    /// <summary>
    /// The cheapest reach in one-way travel days from one settlement to every other, in a single
    /// search.
    /// </summary>
    /// <remarks>
    /// For a caller that needs to price many candidates against the same origin — the levy in
    /// <see cref="Campaigns.NoteSoldiers"/> weighs every soldier of a belligerent realm against
    /// distance from one battle — asking <see cref="Find"/> once per candidate would run a fresh
    /// Dijkstra per candidate per battle. A single-source search already answers the cost to every
    /// settlement it can reach; this is that search, exposed instead of thrown away after one
    /// answer is read out of it.
    /// </remarks>
    internal static RouteGraph.SettlementReach ReachFrom(WorldState world, EntityId sourceId, int year)
    {
        RouteGraph graph = world.RouteGraphFor(year);
        if (!world.Settlements.Contains(sourceId) || !world.Settlements[sourceId].IsActive)
        {
            return graph.ReachFrom(EntityId.None);
        }

        return graph.ReachFrom(sourceId);
    }

    /// <summary>
    /// The straight-line one-way cost between two settlements at plain overland pace, with no
    /// road built — the same fallback an ordinary journey already falls back to when no itinerary
    /// was found.
    /// </summary>
    /// <remarks>
    /// <para><b>A discount on travelling, not a precondition for it.</b> A route path is cheaper
    /// than open ground, never the only way across it — an army, unlike a caravan, does not need a
    /// merchant to have run the way first. <see cref="Systems.TravelSystem.DurationDays(WorldState,
    /// EntityId, EntityId, Itinerary?, int)"/> already treats a null itinerary this way for an
    /// ordinary journey rather than declaring the trip impossible; this is the one-way version of
    /// that same idea, for a caller — the campaign levy — that prices a candidate before any
    /// itinerary exists to ask about.</para>
    ///
    /// <para>Not a second formula invented beside the graph's own. <see cref="TravelPace.
    /// OneWayDays"/> is exactly what <see cref="RouteGraph"/> already charges an unroaded leg
    /// between two settlements a route directly joins; calling it here on the straight line between
    /// two settlements no route joins at all is the same expression at the one edge the graph does
    /// not have.</para>
    /// </remarks>
    internal static int OverlandOneWayDays(WorldState world, EntityId aId, EntityId bId)
    {
        if (!world.Settlements.Contains(aId) || !world.Settlements.Contains(bId)) return 1;

        Settlement a = world.Settlements[aId];
        Settlement b = world.Settlements[bId];
        double distance = world.Distance(a.X, a.Z, b.X, b.Z);

        return TravelPace.OneWayDays(distance, world.Config.UnitsPerTravelDay, TradeRouteMode.Overland, null);
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

        // Stops as soon as the destination itself is settled: a single-target search does not
        // need the cost to every other settlement, only the guarantee that this one is final.
        while (frontier.TryDequeue(out int node, out _))
        {
            if (settled[node]) continue;
            settled[node] = true;
            if (node == destination) break;

            Relax(node, cost, viaNode, viaEdge, settled, frontier, n);
        }

        return ReconstructPath(source, destination, cost, viaNode, viaEdge);
    }

    /// <summary>
    /// Dijkstra from one settlement to every other it can reach, kept as a reusable
    /// <see cref="SettlementReach"/> rather than collapsed to one answer.
    /// </summary>
    /// <remarks>
    /// No early exit: unlike <see cref="Search"/>, every settlement's final cost is wanted, not
    /// just one. A source outside the graph (an unset or inactive settlement, index &lt; 0) settles
    /// nothing and is reported as unable to reach anywhere, which is the same answer <see
    /// cref="Search"/> gives for such a source via <see cref="Itineraries.Find"/>'s own guards.
    /// </remarks>
    internal SettlementReach ReachFrom(EntityId sourceId)
    {
        int n = _settlementCount;
        int source = sourceId.Index;

        var cost = new int[n];
        var viaNode = new int[n];
        var viaEdge = new int[n];

        for (int i = 0; i < n; i++)
        {
            cost[i] = int.MaxValue;
            viaNode[i] = -1;
            viaEdge[i] = -1;
        }

        if (source >= 0 && source < n)
        {
            cost[source] = 0;

            var settled = new bool[n];
            var frontier = new PriorityQueue<int, long>();
            frontier.Enqueue(source, source);

            while (frontier.TryDequeue(out int node, out _))
            {
                if (settled[node]) continue;
                settled[node] = true;

                Relax(node, cost, viaNode, viaEdge, settled, frontier, n);
            }
        }

        return new SettlementReach(this, source, cost, viaNode, viaEdge);
    }

    private void Relax(
        int node,
        int[] cost,
        int[] viaNode,
        int[] viaEdge,
        bool[] settled,
        PriorityQueue<int, long> frontier,
        int settlementCount)
    {
        for (int e = _adjacencyStart[node]; e < _adjacencyStart[node + 1]; e++)
        {
            int next = _edgeTarget[e];
            if (settled[next]) continue;

            int relaxed = cost[node] + _edgeCost[e];
            if (relaxed >= cost[next]) continue;

            cost[next] = relaxed;
            viaNode[next] = node;
            viaEdge[next] = e;
            frontier.Enqueue(next, ((long)relaxed * settlementCount) + next);
        }
    }

    /// <summary>Walks a Dijkstra result's predecessor arrays back into an itinerary, or null.</summary>
    private Itinerary? ReconstructPath(
        int source, int destination, int[] cost, int[] viaNode, int[] viaEdge)
    {
        if (destination < 0 || destination >= _settlementCount) return null;
        if (destination == source) return null;
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

    /// <summary>
    /// One settlement's Dijkstra distances to every other, kept so many candidates can be priced
    /// against the same origin without a second search.
    /// </summary>
    /// <remarks>
    /// A thin read-only view over the arrays <see cref="RouteGraph.ReachFrom"/> built: nothing here
    /// re-walks the graph. <see cref="To"/> reconstructs a path from the same predecessor arrays
    /// <see cref="CostTo"/> already reads its price from, so a caller that wants both the number and
    /// the way pays for the search once.
    /// </remarks>
    internal sealed class SettlementReach
    {
        private readonly RouteGraph _graph;
        private readonly int _source;
        private readonly int[] _cost;
        private readonly int[] _viaNode;
        private readonly int[] _viaEdge;

        internal SettlementReach(
            RouteGraph graph, int source, int[] cost, int[] viaNode, int[] viaEdge)
        {
            _graph = graph;
            _source = source;
            _cost = cost;
            _viaNode = viaNode;
            _viaEdge = viaEdge;
        }

        /// <summary>
        /// One-way travel days from the search's origin to this settlement, or null when the
        /// active network does not reach it (or it does not exist).
        /// </summary>
        internal int? CostTo(EntityId settlementId)
        {
            int index = settlementId.Index;
            if (index < 0 || index >= _cost.Length) return null;

            int cost = _cost[index];
            return cost == int.MaxValue ? null : cost;
        }

        /// <summary>
        /// The itinerary from the search's origin to this settlement, built from the same Dijkstra
        /// pass <see cref="CostTo"/> reads, or null exactly when <see cref="CostTo"/> would be null.
        /// </summary>
        internal Itinerary? To(EntityId settlementId) =>
            _graph.ReconstructPath(_source, settlementId.Index, _cost, _viaNode, _viaEdge);
    }
}
