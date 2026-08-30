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

    /// <summary>
    /// They arrived and did not leave. The destination became home.
    /// </summary>
    /// <remarks>
    /// The third way a trip can fail to end at the traveller's own hearth, and the only one that is
    /// not a misfortune. Until this existed the sole reasons anybody in this world left the town
    /// they were born in were administrative — a marriage, a posting, a recall, an accession, a
    /// regency — so nobody ever emigrated because the trade at the far end was better or because
    /// the shrine they walked to needed a keeper.
    /// </remarks>
    Stayed = 3,
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
        Stamp departed,
        EntityId fromSettlementId,
        EntityId toSettlementId,
        EntityId viaId,
        int durationDays,
        Stamp expectedReturn)
    {
        Kind = kind;
        Year = departed.Year;
        Day = departed.Day;
        FromSettlementId = fromSettlementId;
        ToSettlementId = toSettlementId;
        ViaId = viaId;
        DurationDays = durationDays;
        ReturnSettlementId = fromSettlementId;
        ReturnYear = expectedReturn.Year;
        ReturnDay = expectedReturn.Day;
    }

    public JourneyKind Kind { get; }

    public int Year { get; }

    /// <summary>Day of the year on which they set out.</summary>
    public int Day { get; }

    public EntityId FromSettlementId { get; }

    public EntityId ToSettlementId { get; }

    /// <summary>
    /// The route, holy site or host realm that made the journey make sense, or none.
    /// </summary>
    public EntityId ViaId { get; }

    /// <summary>
    /// Days on the road for the planned outward-and-return itinerary, derived from its actual way.
    /// </summary>
    /// <remarks>
    /// Kept as planned when a mishap prevents the return: it is the route's cost, not a guess at
    /// which mile the mishap occurred on. A journey that ends in staying is the exception — its
    /// itinerary truly ended at the destination, so it keeps the one-way duration actually taken.
    /// </remarks>
    public int DurationDays { get; internal set; }

    /// <summary>Where the traveller came back to, or none when they never came home.</summary>
    /// <remarks>
    /// Kept explicitly because a figure may move years later. Comparing an old journey with their
    /// final residence confuses that later move with what the journey itself did.
    /// </remarks>
    public EntityId ReturnSettlementId { get; set; }

    /// <summary>The dated return or arrival, absent when the traveller was lost.</summary>
    public int? ReturnYear { get; set; }

    public int? ReturnDay { get; set; }

    /// <summary>Whether this journey has the traveller away at <paramref name="when"/>.</summary>
    public bool IsUnderwayAt(Stamp when)
    {
        if (when.Year < Year || (when.Year == Year && when.Day < Day)) return false;
        if (ReturnYear is not int returnYear || ReturnDay is not int returnDay) return true;
        if (when.Year < returnYear) return true;
        return when.Year == returnYear && when.Day < returnDay;
    }

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
