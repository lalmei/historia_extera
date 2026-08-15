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
        Allies = new DetMap<EntityId, int>();
        Truces = new DetMap<EntityId, int>();
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

    /// <summary>
    /// The faith of the realm, as its seat of government held it when the year began.
    /// </summary>
    /// <remarks>
    /// Written only by the religion system, which syncs it from the capital once a year. Everything
    /// downstream — diplomacy above all — reads this rather than chasing the capital, so every
    /// judgement made within one year is made against the same answer.
    /// </remarks>
    public EntityId StateReligionId { get; set; } = EntityId.None;

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
    /// <remarks>
    /// <para><b>Directed, not shared.</b> Each realm keeps its own view, and the two rarely agree.
    /// A shared number per pair would be half the state and would lose the asymmetry that drives
    /// the most interesting wars: a peace costs the beaten realm far more goodwill than the realm
    /// that beat it, and that difference is what sends the loser back for its province a
    /// generation later. Symmetric relations cannot express a grudge, only a quarrel.</para>
    ///
    /// <para>Note what this does <em>not</em> claim. The loser is not therefore the colder of the
    /// two — the winner is usually the more aggressive realm and so structurally the colder,
    /// before and after. What is asymmetric is the movement, not the level.</para>
    ///
    /// <para>Entries appear on contact — a shared frontier — and persist afterwards, so two realms
    /// that once fought still remember each other when the border between them has moved away.</para>
    /// </remarks>
    public DetMap<EntityId, double> Relations { get; }

    /// <summary>Standing alliances, keyed by ally, valued by the year the pact was sworn.</summary>
    /// <remarks>
    /// The year is kept because it is the only thing that makes an alliance legible in a chronicle
    /// — "allied these forty years" is what distinguishes a dynastic bond from a marriage of
    /// convenience sworn last spring.
    /// </remarks>
    public DetMap<EntityId, int> Allies { get; }

    /// <summary>
    /// Years through which peace is guaranteed, keyed by the other realm.
    /// </summary>
    /// <remarks>
    /// A settlement imposes a truce, and without one the loser's collapsed relations re-declare the
    /// same war the following spring. Wars then run continuously, every chronicle reads as one
    /// unbroken campaign, and neither exhaustion nor recovery ever appears.
    /// </remarks>
    public DetMap<EntityId, int> Truces { get; }

    /// <summary>
    /// How the realm's recent past sits on it: what it has suffered, lost and won lately.
    /// </summary>
    /// <remarks>
    /// Written by the systems that cause each entry and faded once a year by the crown system,
    /// which runs before anything reads it. This is the third of the three layers a realm's
    /// decisions are made from — after the culture it has and the person governing it.
    /// </remarks>
    public RealmFortunes Fortunes { get; } = new();

    /// <summary>Total population across active settlements. Recomputed each tick.</summary>
    public int Population { get; set; }

    public int PeakPopulation { get; set; }

    /// <summary>Number of years this civilization has existed as of <paramref name="year"/>.</summary>
    public int AgeIn(int year) => year - FoundedYear;

    public override string ToString() =>
        $"{Id} {Name} (founded {FoundedYear}, pop {Population})";
}
