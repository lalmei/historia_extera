using HistoryEngine.Core;

namespace HistoryEngine.Entities;

/// <summary>
/// A ruling house: a line of rulers related by descent.
/// </summary>
/// <remarks>
/// <para>Before Milestone 5 a succession produced a fully-grown adult from nowhere, so a
/// civilization's rulers were an unrelated sequence of names. A dynasty is what makes that sequence
/// a <em>line</em>, and what makes its end an event worth recording — a house dying out and being
/// replaced is one of the more interesting things a chronicle can contain, and it can only happen
/// once rulers have relatives.</para>
///
/// <para><b><see cref="MemberIds"/> is blood only.</b> Consorts married into the house are not
/// members of it; they are reachable through <see cref="Figure.SpouseIds"/> and keep their own
/// <see cref="Figure.DynastyId"/>. Folding them in would be the more intuitive reading of
/// "the house" and would quietly break the thing dynasties exist for — a house whose members
/// include everyone who ever married one can never die out.</para>
///
/// <para>Dynasties outlive their civilizations' individual rulers and can outlive a civilization
/// itself, which is why they are entities with ids rather than a field on
/// <see cref="Civilization"/>. A house can also hold more than one throne over its life, so
/// <see cref="RulerIds"/> spans civilizations.</para>
/// </remarks>
public sealed class Dynasty
{
    public Dynasty(
        EntityId id,
        EntityId cultureId,
        string name,
        int foundedYear,
        EntityId founderId,
        EntityId originCivilizationId)
    {
        Id = id;
        CultureId = cultureId;
        Name = name;
        FoundedYear = foundedYear;
        FounderId = founderId;
        OriginCivilizationId = originCivilizationId;
        MemberIds = new List<EntityId> { founderId };
        RulerIds = new List<EntityId>();
    }

    public EntityId Id { get; }

    public EntityId CultureId { get; }

    public string Name { get; }

    public int FoundedYear { get; }

    /// <summary>Set when the last member of the house dies without an heir.</summary>
    public int? EndedYear { get; set; }

    public bool IsExtinct => EndedYear is not null;

    public EntityId FounderId { get; }

    /// <summary>The realm the house first rose in. It may end up ruling another, or none.</summary>
    public EntityId OriginCivilizationId { get; }

    /// <summary>Everyone descended from the founder, in id order — which is also birth order.</summary>
    public List<EntityId> MemberIds { get; }

    /// <summary>Every member who has held a throne, in order of accession.</summary>
    public List<EntityId> RulerIds { get; }

    public override string ToString() =>
        $"{Id} {Name} ({MemberIds.Count} members, {RulerIds.Count} rulers)";
}
