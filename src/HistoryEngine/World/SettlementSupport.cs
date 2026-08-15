namespace HistoryEngine.World;

/// <summary>
/// What is feeding a settlement, itemised: the ground, the fields, and the roads.
/// </summary>
/// <remarks>
/// <para>Carrying capacity is one number in the simulation and three questions to a reader. A town
/// of four thousand people might stand on exceptional ground, on six busy trade routes, or on a
/// capital's administration holding together a place the land would never have supported — and
/// those are three different histories that a population figure alone cannot tell apart. This is
/// what lets the export answer <em>why</em>.</para>
///
/// <para>The parts are reported after the year's modifiers have been applied, so they sum to
/// <see cref="Capacity"/> and can be compared directly against each other.</para>
///
/// <para>A snapshot, not a record. It describes the settlement under one year's harvest, one
/// year's traffic and one year's neighbours; nothing stores it between ticks.</para>
/// </remarks>
public readonly record struct SettlementSupport(
    double FromSite,
    double FromLand,
    double FromTrade,
    double LandShare,
    double RouteTraffic)
{
    /// <summary>
    /// The floor under every settlement's capacity.
    /// </summary>
    /// <remarks>
    /// Never zero: a positive floor keeps the logistic growth term finite, and the abandonment
    /// threshold in <see cref="Systems.SettlementLifecycleSystem"/> is what actually ends a
    /// settlement.
    /// </remarks>
    public const double Floor = 40.0;

    /// <summary>How many people the settlement can support, all sources together.</summary>
    public double Capacity => Math.Max(Floor, FromSite + FromLand + FromTrade);

    /// <summary>Which of the three sources is doing the most work.</summary>
    /// <remarks>
    /// Ties go to the land, then the roads, which is the order a reader would assume when two
    /// terms are level.
    /// </remarks>
    public SupportSource Principal =>
        FromLand >= FromTrade && FromLand >= FromSite ? SupportSource.Land
        : FromTrade >= FromSite ? SupportSource.Trade
        : SupportSource.Site;
}

/// <summary>Where the greater part of a settlement's living comes from.</summary>
public enum SupportSource
{
    /// <summary>Its own fields, and its share of them.</summary>
    Land = 0,

    /// <summary>What the roads bring in.</summary>
    Trade = 1,

    /// <summary>The ore, the fishery, the spring — what the site itself yields.</summary>
    Site = 2,
}
