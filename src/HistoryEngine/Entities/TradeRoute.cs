using HistoryEngine.Core;

namespace HistoryEngine.Entities;

/// <summary>
/// The transport a trade route principally relies on before a physical path is modelled.
/// Explicit values — part of the export format.
/// </summary>
public enum TradeRouteMode
{
    Overland = 0,
    River = 1,
    Coastal = 2,
}

/// <summary>The route's economic state at the end of the simulated year.</summary>
public enum TradeRouteStatus
{
    Active = 0,
    Prosperous = 1,
    Declining = 2,
    Closed = 3,
}

/// <summary>
/// A durable commercial connection between two settlements.
/// </summary>
/// <remarks>
/// <para><b>Topology, not geometry.</b> The endpoints say which places exchange people, goods
/// and ideas. A later road system can attach a physical path to an overland route without
/// changing its identity or losing the years before a road was built.</para>
///
/// <para>Endpoints are stored in id order. That makes a route an undirected pair with one
/// canonical representation, so creation cannot produce both A–B and B–A.</para>
///
/// <para>Closed routes remain in the table. A route can therefore be read as history, and the
/// same settlements can establish a new route later without rewriting the old one.</para>
/// </remarks>
public sealed class TradeRoute
{
    public TradeRoute(
        EntityId id,
        EntityId settlementAId,
        EntityId settlementBId,
        TradeRouteMode mode,
        int foundedYear,
        double traffic)
    {
        if (settlementBId.CompareTo(settlementAId) < 0)
        {
            (settlementAId, settlementBId) = (settlementBId, settlementAId);
        }

        Id = id;
        SettlementAId = settlementAId;
        SettlementBId = settlementBId;
        Mode = mode;
        FoundedYear = foundedYear;
        Traffic = DetMath.Clamp01(traffic);
        PeakTraffic = Traffic;
        Status = TradeRouteStatus.Active;
    }

    public EntityId Id { get; }

    public EntityId SettlementAId { get; }

    public EntityId SettlementBId { get; }

    /// <summary>
    /// The route's likely transport corridor. This is not a path: river and coast mean both
    /// endpoints have that access, while overland is the work a later road network must realize.
    /// </summary>
    public TradeRouteMode Mode { get; }

    public int FoundedYear { get; }

    public int? EndedYear { get; set; }

    public bool IsActive => EndedYear is null;

    public TradeRouteStatus Status { get; set; }

    /// <summary>Current traffic in [0, 1], recomputed from the settlements and their realms.</summary>
    public double Traffic { get; set; }

    /// <summary>Highest traffic the route has sustained.</summary>
    public double PeakTraffic { get; set; }

    /// <summary>Consecutive years below the level that can sustain a route.</summary>
    public int YearsDeclining { get; set; }

    /// <summary>
    /// The way over the ground, once traffic has earned one. Null for most routes.
    /// </summary>
    /// <remarks>
    /// The geometry hangs off the topology rather than replacing it. A route with no road is still
    /// a route — demand exists before anyone spends on it — and a route that closes keeps the road
    /// it had, because an abandoned road is exactly what the ground would show.
    /// </remarks>
    public Road? Road { get; set; }

    /// <summary>Whether the ground between the endpoints has already been searched for a way.</summary>
    /// <remarks>
    /// Endpoints never move, so the search has one answer for the life of the route and a failed
    /// one is as final as a successful one. Without this, a pair that trades across a strait would
    /// pay for a graph search every year it stayed busy — cost scaling with years, which is the one
    /// thing the terrain discipline exists to prevent.
    /// </remarks>
    public bool RoadSurveyed { get; set; }

    public bool Connects(EntityId settlementId) =>
        SettlementAId == settlementId || SettlementBId == settlementId;

    public EntityId Other(EntityId settlementId) => settlementId == SettlementAId
        ? SettlementBId
        : settlementId == SettlementBId
            ? SettlementAId
            : EntityId.None;
}
