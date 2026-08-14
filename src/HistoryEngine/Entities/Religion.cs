using HistoryEngine.Core;

namespace HistoryEngine.Entities;

/// <summary>
/// A faith, held by the settlements that follow it.
/// </summary>
/// <remarks>
/// <para><b>Congregations are settlements, not realms.</b> A state religion would have been far
/// less machinery — one field on <see cref="Civilization"/>, converted by decree — but nothing
/// would ever have happened between the decrees. Holding faith at the settlement level means a
/// faith crosses a border before it crosses a throne, a realm can be religiously divided, and a
/// province taken in war brings its own faith with it. The realm's own faith is then a derived
/// thing: whatever its capital follows.</para>
///
/// <para><b>A faith can die.</b> When the last settlement following it converts away it gains an
/// <see cref="EndedYear"/> and stops being offered as a destination — the same shape as a house
/// dying out, and for the same reason: a system where nothing can end produces a world that only
/// accumulates.</para>
/// </remarks>
public sealed class Religion
{
    public Religion(
        EntityId id,
        string name,
        EntityId cultureId,
        EntityId originSettlementId,
        int foundedYear,
        double fervour)
    {
        Id = id;
        Name = name;
        CultureId = cultureId;
        OriginSettlementId = originSettlementId;
        FoundedYear = foundedYear;
        Fervour = fervour;
        SettlementIds = new List<EntityId>();
    }

    public EntityId Id { get; }

    public string Name { get; }

    /// <summary>The culture whose language named it — the people it arose among.</summary>
    public EntityId CultureId { get; }

    /// <summary>Whoever preached it first, if the chronicle knows.</summary>
    public EntityId FounderId { get; set; } = EntityId.None;

    /// <summary>Where it was first preached. Its holy site, for as long as it stands.</summary>
    public EntityId OriginSettlementId { get; }

    /// <summary>The faith this one broke away from, if it did.</summary>
    public EntityId ParentId { get; set; } = EntityId.None;

    public int FoundedYear { get; }

    public int? EndedYear { get; set; }

    public bool IsActive => EndedYear is null;

    /// <summary>
    /// How hard it presses outwards, in [0, 1].
    /// </summary>
    /// <remarks>
    /// Rolled once at founding from the piety of the culture it arose in, and never revised. It is
    /// what stops every faith spreading at the same rate and therefore what makes one of them the
    /// one that takes a continent — the religious counterpart of a culture's aggression, which M6
    /// showed is where a world gets its character.
    /// </remarks>
    public double Fervour { get; }

    /// <summary>Settlements following it now, in the order they converted.</summary>
    public List<EntityId> SettlementIds { get; }

    /// <summary>The most settlements it ever held at once. Survives its decline.</summary>
    public int PeakSettlements { get; set; }

    public void Gain(EntityId settlementId)
    {
        if (SettlementIds.Contains(settlementId)) return;

        SettlementIds.Add(settlementId);
        if (SettlementIds.Count > PeakSettlements) PeakSettlements = SettlementIds.Count;
    }

    public void Lose(EntityId settlementId) => SettlementIds.Remove(settlementId);

    public override string ToString() => $"{Id} {Name} ({SettlementIds.Count} settlements)";
}
