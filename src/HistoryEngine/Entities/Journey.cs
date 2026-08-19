using HistoryEngine.Core;

namespace HistoryEngine.Entities;

/// <summary>
/// Why a recorded person left home this year. Explicit values — part of the export.
/// </summary>
public enum JourneyKind
{
    /// <summary>A guest of another friendly realm.</summary>
    Visit = 0,

    /// <summary>Along a standing commercial route.</summary>
    Trade = 1,

    /// <summary>To a holy place of their own faith.</summary>
    Pilgrimage = 2,

    /// <summary>Preaching, carrying scripture, or fetching copies from a monastery.</summary>
    Mission = 3,
}

/// <summary>
/// One journey this person made, from one settlement to another.
/// </summary>
/// <remarks>
/// Residence stays where they live. This is the trip, not a move: a merchant returns, a pilgrim
/// comes home, a priest's circuit is a year on the road. The chronicle indexes the destination;
/// the list on the figure is what a life page walks.
/// </remarks>
public sealed class Journey
{
    public Journey(
        JourneyKind kind,
        int year,
        EntityId fromSettlementId,
        EntityId toSettlementId,
        EntityId viaId)
    {
        Kind = kind;
        Year = year;
        FromSettlementId = fromSettlementId;
        ToSettlementId = toSettlementId;
        ViaId = viaId;
    }

    public JourneyKind Kind { get; }

    public int Year { get; }

    public EntityId FromSettlementId { get; }

    public EntityId ToSettlementId { get; }

    /// <summary>
    /// The route, holy site or host realm that made the journey make sense, or none.
    /// </summary>
    public EntityId ViaId { get; }
}
