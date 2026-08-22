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
/// How a journey ended. Explicit values — part of the export.
/// </summary>
/// <remarks>
/// Most journeys end the dull way, and that is the point of recording the other two: a chronicle
/// where nobody is ever robbed on the road is a chronicle where the road costs nothing, and a
/// world whose roads cost nothing has no reason to have built any.
/// </remarks>
public enum JourneyOutcome
{
    /// <summary>They got there and came home. The overwhelming majority.</summary>
    Returned = 0,

    /// <summary>Robbed or turned back short of the destination. They lived; the goods may not have.</summary>
    Waylaid = 1,

    /// <summary>They did not come home. The death is recorded where the road was, not where they lived.</summary>
    Lost = 2,
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

    /// <summary>
    /// How it ended. Set once, in the year of the journey, and never revised.
    /// </summary>
    /// <remarks>
    /// Kept on the journey rather than inferred from the traveller's death year, because a person
    /// can die at home in the same year they were robbed a hundred leagues away, and the two facts
    /// are not the same fact.
    /// </remarks>
    public JourneyOutcome Outcome { get; set; } = JourneyOutcome.Returned;
}
