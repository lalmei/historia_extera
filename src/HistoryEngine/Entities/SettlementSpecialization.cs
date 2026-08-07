namespace HistoryEngine.Entities;

/// <summary>
/// What a settlement is chiefly known for. Explicit values — part of the export format.
/// </summary>
public enum SettlementSpecialization
{
    /// <summary>Not yet established. Hamlets have no character of their own.</summary>
    None = 0,
    Farming = 1,
    Pastoral = 2,
    Fishing = 3,
    Mining = 4,
    Trade = 5,
    Crafts = 6,
    Shrine = 7,
}

/// <summary>
/// How a specialization changes what a settlement can support.
/// </summary>
/// <remarks>
/// <para>These are the numbers that make specialization matter rather than decorate. A fishing
/// town and a farming town on the same ground reach different sizes and fail in different years,
/// which is the whole point: it gives geography a second-order effect on history, not just a
/// first-order one.</para>
///
/// <para><see cref="HarvestSensitivity"/> is the interesting field. A farming town lives and dies
/// by the harvest; a mining town buys its food and mostly does not care, and a shrine town is
/// sustained by pilgrims regardless. So a bad regional decade empties the farms and leaves the
/// mines standing — the sort of asymmetry that makes a chronicle worth reading.</para>
/// </remarks>
public static class Specializations
{
    /// <summary>Multiplier on the fertility-derived component of carrying capacity.</summary>
    public static double FertilityWeight(SettlementSpecialization specialization) => specialization switch
    {
        SettlementSpecialization.Farming => 1.35,
        SettlementSpecialization.Pastoral => 0.70,
        SettlementSpecialization.Fishing => 0.55,
        SettlementSpecialization.Mining => 0.35,
        SettlementSpecialization.Trade => 0.65,
        SettlementSpecialization.Crafts => 0.80,
        SettlementSpecialization.Shrine => 0.60,
        _ => 1.0,
    };

    /// <summary>
    /// Capacity a specialization supports regardless of the land, in people.
    /// </summary>
    /// <remarks>
    /// Low for the subsistence trades and high for the ones that import their food. That asymmetry
    /// is what decides which settlements a bad regional decade can actually kill: farming and
    /// herding villages have almost no floor beneath them, while a mining town or a trading port
    /// stands on its trade rather than on its soil.
    /// </remarks>
    public static double BaseCapacity(SettlementSpecialization specialization) => specialization switch
    {
        SettlementSpecialization.Farming => 90.0,
        SettlementSpecialization.Pastoral => 260.0,
        SettlementSpecialization.Fishing => 1900.0,
        SettlementSpecialization.Mining => 2200.0,
        SettlementSpecialization.Trade => 2600.0,
        SettlementSpecialization.Crafts => 1800.0,
        SettlementSpecialization.Shrine => 1400.0,
        _ => 150.0,
    };

    /// <summary>
    /// How much a poor harvest hurts, from 0 (immune) to 1 (fully exposed).
    /// </summary>
    public static double HarvestSensitivity(SettlementSpecialization specialization) => specialization switch
    {
        SettlementSpecialization.Farming => 1.0,
        SettlementSpecialization.Pastoral => 0.80,
        SettlementSpecialization.Fishing => 0.35,
        SettlementSpecialization.Mining => 0.30,
        SettlementSpecialization.Trade => 0.45,
        SettlementSpecialization.Crafts => 0.55,
        SettlementSpecialization.Shrine => 0.40,
        _ => 0.90,
    };

    /// <summary>
    /// How badly distance from the seat of government hurts, from 0 to 1.
    /// </summary>
    /// <remarks>
    /// A mining or trading settlement depends on the realm reaching it; a farming village feeds
    /// itself and barely notices. This is what makes overextended civilizations shed their
    /// furthest holdings first.
    /// </remarks>
    public static double SupplyDependence(SettlementSpecialization specialization) => specialization switch
    {
        SettlementSpecialization.Farming => 0.15,
        SettlementSpecialization.Pastoral => 0.20,
        SettlementSpecialization.Fishing => 0.30,
        SettlementSpecialization.Mining => 0.85,
        SettlementSpecialization.Trade => 0.75,
        SettlementSpecialization.Crafts => 0.60,
        SettlementSpecialization.Shrine => 0.45,
        _ => 0.25,
    };

    public static string Label(SettlementSpecialization specialization) => specialization switch
    {
        SettlementSpecialization.Farming => "farming",
        SettlementSpecialization.Pastoral => "herding",
        SettlementSpecialization.Fishing => "fishing",
        SettlementSpecialization.Mining => "mining",
        SettlementSpecialization.Trade => "trade",
        SettlementSpecialization.Crafts => "craftwork",
        SettlementSpecialization.Shrine => "pilgrimage",
        _ => "nothing in particular",
    };
}
