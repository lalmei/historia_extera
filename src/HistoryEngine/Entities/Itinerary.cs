using HistoryEngine.Core;

namespace HistoryEngine.Entities;

/// <summary>
/// The cheapest way found across the active trade-route network between two settlements.
/// </summary>
/// <remarks>
/// <para><b>Lives in <c>Entities</c>, not <c>World</c>.</b> A <see cref="Journey"/> carries one, and
/// the entity layer depends on <c>Core</c> alone — see the same argument made for
/// <see cref="RoadPoint"/>. Everything this type holds is ids and a number; nothing here needs a
/// <c>WorldState</c> to exist or to be read back from an export.</para>
///
/// <para><b>A record of the search's answer, not the search itself.</b> <c>HistoryEngine.World.
/// Itineraries</c> is what walks the route graph; this is what a journey keeps afterward, the same
/// division <see cref="Road"/> makes between <c>Roadbed.Cut</c> and the polyline it hands back.</para>
/// </remarks>
public sealed class Itinerary
{
    public Itinerary(
        IReadOnlyList<EntityId> routeIds, IReadOnlyList<EntityId> settlementIds, int oneWayDays)
    {
        RouteIds = routeIds;
        SettlementIds = settlementIds;
        OneWayDays = oneWayDays;
    }

    /// <summary>The trade routes crossed, in travel order.</summary>
    public IReadOnlyList<EntityId> RouteIds { get; }

    /// <summary>
    /// Every settlement the itinerary passes through, including both endpoints. Always one longer
    /// than <see cref="RouteIds"/>.
    /// </summary>
    public IReadOnlyList<EntityId> SettlementIds { get; }

    /// <summary>The summed one-way cost of the actual legs, in travel days.</summary>
    public int OneWayDays { get; }

    /// <summary>A single leg, straight from origin to destination with nothing in between.</summary>
    public bool IsDirect => RouteIds.Count <= 1;

    /// <summary>The same way, walked from the other end.</summary>
    /// <remarks>
    /// Valid because the route network is undirected: a leg costs the same one-way price crossed
    /// either direction, since <see cref="World.RouteGraph"/> builds an edge each way for every
    /// route it admits. A search made from one end can therefore serve a traveller starting at the
    /// other without the graph being asked again for a distance it already found.
    /// </remarks>
    public Itinerary Reversed()
    {
        int legs = RouteIds.Count;
        var routeIds = new EntityId[legs];
        for (int i = 0; i < legs; i++) routeIds[i] = RouteIds[legs - 1 - i];

        int stops = SettlementIds.Count;
        var settlementIds = new EntityId[stops];
        for (int i = 0; i < stops; i++) settlementIds[i] = SettlementIds[stops - 1 - i];

        return new Itinerary(routeIds, settlementIds, OneWayDays);
    }
}
