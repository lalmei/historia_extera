using HistoryEngine.Core;

namespace HistoryEngine.Entities;

/// <summary>
/// The grievance a war was declared over. Explicit values — part of the export format.
/// </summary>
/// <remarks>
/// Not decoration. Each one is reached by a different route and produces a different war: a
/// border dispute needs a shared frontier, a dynastic claim needs a marriage between the two
/// ruling houses, a revanche needs territory lost in a previous war, and a relic claim names
/// the sacred object the aggressor means to take. They also settle differently: a successful
/// relic claim yields its object instead of an ordinary province.
/// </remarks>
public enum CasusBelli
{
    Unknown = 0,

    /// <summary>Contested frontier. The default quarrel between neighbours.</summary>
    BorderDispute = 1,

    /// <summary>A strong realm against a weak one, for no reason but that it can.</summary>
    Conquest = 2,

    /// <summary>The aggressor's house has married into the defender's, and now presses the claim.</summary>
    DynasticClaim = 3,

    /// <summary>Retaking land ceded in an earlier war.</summary>
    Revanche = 4,

    /// <summary>Taking a particular sacred relic held by the other realm.</summary>
    RelicClaim = 5,

    /// <summary>A devout realm carrying a fervent faith against a realm of another faith.</summary>
    ReligiousWar = 6,
}

/// <summary>How a war ended. <see cref="Ongoing"/> while it is still being fought.</summary>
public enum WarOutcome
{
    Ongoing = 0,
    AggressorVictory = 1,
    DefenderVictory = 2,

    /// <summary>Both sides exhausted. Nothing changes hands.</summary>
    Stalemate = 3,
}

/// <summary>
/// A war: two coalitions, a grievance, a run of battles, and a settlement.
/// </summary>
/// <remarks>
/// <para>An entity rather than a field on <see cref="Civilization"/> because a war outlives its
/// causes and is referred to long afterwards — "the regions ceded at the end of the Second War of
/// Bergajarvi" is a sentence a chronicle needs to be able to write two centuries later. It also
/// gives every battle somewhere to belong.</para>
///
/// <para><b>Coalitions, not pairs.</b> <see cref="Attackers"/> and <see cref="Defenders"/> each
/// begin with the principal belligerent and gain allies who answer the call. Modelling a war as
/// two civilizations would make an alliance decorative: the only thing a pact can actually do is
/// bring somebody else's army to the field.</para>
/// </remarks>
public sealed class War
{
    public War(
        EntityId id,
        string name,
        EntityId aggressorId,
        EntityId defenderId,
        CasusBelli cause,
        EntityId claimedRelicId,
        EntityId aggressorReligionId,
        EntityId defenderReligionId,
        int startYear)
    {
        Id = id;
        Name = name;
        AggressorId = aggressorId;
        DefenderId = defenderId;
        Cause = cause;
        ClaimedRelicId = claimedRelicId.IsNone ? EntityId.None : claimedRelicId;
        AggressorReligionId = aggressorReligionId.IsNone ? EntityId.None : aggressorReligionId;
        DefenderReligionId = defenderReligionId.IsNone ? EntityId.None : defenderReligionId;
        StartYear = startYear;
        Attackers = new List<EntityId> { aggressorId };
        Defenders = new List<EntityId> { defenderId };
        BattleIds = new List<EntityId>();
        CededRegionIds = new List<EntityId>();
    }

    public EntityId Id { get; }

    /// <summary>Composed from the places and houses it was fought over, not from a naming language.</summary>
    public string Name { get; }

    /// <summary>Who declared it. Always the first entry in <see cref="Attackers"/>.</summary>
    public EntityId AggressorId { get; }

    /// <summary>Who it was declared on. Always the first entry in <see cref="Defenders"/>.</summary>
    public EntityId DefenderId { get; }

    public CasusBelli Cause { get; }

    /// <summary>The particular object sought in a <see cref="CasusBelli.RelicClaim"/>.</summary>
    public EntityId ClaimedRelicId { get; }

    /// <summary>The aggressor's faith when a <see cref="CasusBelli.ReligiousWar"/> began.</summary>
    public EntityId AggressorReligionId { get; }

    /// <summary>The defender's faith when a <see cref="CasusBelli.ReligiousWar"/> began.</summary>
    public EntityId DefenderReligionId { get; }

    public int StartYear { get; }

    public int? EndYear { get; set; }

    public bool IsActive => EndYear is null;

    public WarOutcome Outcome { get; set; } = WarOutcome.Ongoing;

    public List<EntityId> Attackers { get; }

    public List<EntityId> Defenders { get; }

    public List<EntityId> BattleIds { get; }

    /// <summary>Regions that changed hands in the peace, in the order they were ceded.</summary>
    public List<EntityId> CededRegionIds { get; }

    /// <summary>
    /// How the war is going, positive for the attackers. Battles move it; peace reads it.
    /// </summary>
    /// <remarks>
    /// A running total rather than a count of battles won, so one decisive siege can settle a war
    /// that a dozen skirmishes would not. It is also what stops a war ending on the first
    /// engagement: the threshold is several battles' worth of advantage.
    /// </remarks>
    public double Score { get; set; }

    public int AttackerLosses { get; set; }

    public int DefenderLosses { get; set; }

    public int YearsIn(int year) => year - StartYear;

    public bool Involves(EntityId civilizationId) =>
        Attackers.Contains(civilizationId) || Defenders.Contains(civilizationId);

    /// <summary>True if the given realm is fighting on the attacking side.</summary>
    public bool IsAttacker(EntityId civilizationId) => Attackers.Contains(civilizationId);

    /// <summary>The coalition opposing the given realm, or null if it is not a belligerent.</summary>
    public IReadOnlyList<EntityId>? EnemiesOf(EntityId civilizationId)
    {
        if (Attackers.Contains(civilizationId)) return Defenders;
        if (Defenders.Contains(civilizationId)) return Attackers;
        return null;
    }

    public override string ToString() =>
        $"{Id} {Name} ({StartYear}–{(EndYear?.ToString() ?? string.Empty)}, {Outcome})";
}
