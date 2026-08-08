using HistoryEngine.Core;

namespace HistoryEngine.Entities;

/// <summary>
/// A polity: a culture, a territory, a seat of government and a line of rulers.
/// </summary>
/// <remarks>
/// Relations are stored as a <see cref="DetMap{TKey,TValue}"/> keyed by civilization id
/// rather than a plain dictionary, so that iterating a civilization's neighbours — which
/// diplomacy does every year — has a fixed order. With a
/// <see cref="Dictionary{TKey,TValue}"/> here, the order would depend on insertion history,
/// and the whole simulation would drift on any change that reordered civ founding.
/// </remarks>
public sealed class Civilization
{
    public Civilization(
        EntityId id,
        EntityId cultureId,
        string name,
        int foundedYear)
    {
        Id = id;
        CultureId = cultureId;
        Name = name;
        FoundedYear = foundedYear;
        SettlementIds = new List<EntityId>();
        TerritoryRegionIds = new List<EntityId>();
        Relations = new DetMap<EntityId, double>();
        RulerIds = new List<EntityId>();
    }

    public EntityId Id { get; }

    public EntityId CultureId { get; }

    public string Name { get; set; }

    public int FoundedYear { get; }

    /// <summary>Set when the civilization ceases to exist. Its entities remain referenceable.</summary>
    public int? EndedYear { get; set; }

    public bool IsActive => EndedYear is null;

    public EntityId CapitalId { get; set; } = EntityId.None;

    public EntityId CurrentRulerId { get; set; } = EntityId.None;

    /// <summary>The year the sitting ruler took office. Drives fixed-term governments.</summary>
    public int RulerSinceYear { get; set; }

    /// <summary>
    /// The house currently holding the throne.
    /// </summary>
    /// <remarks>
    /// Held on the civilization as well as being readable from the ruler, because succession has to
    /// know whose claim it is resolving during the moment when there is no ruler to ask.
    /// </remarks>
    public EntityId RulingDynastyId { get; set; } = EntityId.None;

    /// <summary>Who governs while the ruler is a minor. <see cref="EntityId.None"/> otherwise.</summary>
    public EntityId RegentId { get; set; } = EntityId.None;

    /// <summary>Every ruler in order of accession. The civilization's spine for the viewer.</summary>
    public List<EntityId> RulerIds { get; }

    public List<EntityId> SettlementIds { get; }

    public List<EntityId> TerritoryRegionIds { get; }

    /// <summary>
    /// Opinion of other civilizations, in [-1, 1]. Negative is hostile.
    /// </summary>
    /// <remarks>Populated from Milestone 6 onward; empty during the Milestone 1 slice.</remarks>
    public DetMap<EntityId, double> Relations { get; }

    /// <summary>Total population across active settlements. Recomputed each tick.</summary>
    public int Population { get; set; }

    public int PeakPopulation { get; set; }

    /// <summary>Number of years this civilization has existed as of <paramref name="year"/>.</summary>
    public int AgeIn(int year) => year - FoundedYear;

    public override string ToString() =>
        $"{Id} {Name} (founded {FoundedYear}, pop {Population})";
}
