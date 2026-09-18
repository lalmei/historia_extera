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
}
