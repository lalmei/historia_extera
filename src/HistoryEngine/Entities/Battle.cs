using HistoryEngine.Core;

namespace HistoryEngine.Entities;

/// <summary>How an invested settlement's siege ended.</summary>
/// <remarks>
/// Explicit values because this is exported. A field battle uses <see cref="NotSiege"/>; an
/// invested settlement begins <see cref="Ongoing"/> and reaches exactly one terminal outcome.
/// </remarks>
public enum SiegeOutcome
{
    NotSiege = 0,
    Ongoing = 1,

    /// <summary>The investing army broke the defence and took the place.</summary>
    Carried = 2,

    /// <summary>A defending force defeated the investing army.</summary>
    Relieved = 3,

    /// <summary>The investment ended without a deciding engagement.</summary>
    Lifted = 4,
}

/// <summary>
/// One engagement in a war, named for where it was fought.
/// </summary>
/// <remarks>
/// <para><b>Why battles are entities and truces are not.</b> A battle is the thing a chronicle
/// refers back to — people are remembered for having won one, a house is remembered for having
/// lost its king at one, and the reader wants a page listing who fought and what it cost. A truce
/// is a state of two realms and reads perfectly well as an event.</para>
///
/// <para>A siege is not a separate entity kind. It is a battle episode whose
/// <see cref="SettlementId"/> is set and whose <see cref="SiegeOutcome"/> begins ongoing. A field
/// battle starts and ends on one stamp; a siege keeps the same participants and committed forces
/// until its scheduled decision, relief, or lifting.</para>
/// </remarks>
public sealed class Battle
{
    public Battle(
        EntityId id,
        string name,
        EntityId warId,
        Stamp startedAt,
        EntityId regionId)
    {
        Id = id;
        Name = name;
        WarId = warId;
        Year = startedAt.Year;
        Day = startedAt.Day;
        RegionId = regionId;
    }

    public EntityId Id { get; }

    /// <summary>"Battle of Ormsholmadal", "Second Siege of Ekallatograd" — composed, not generated.</summary>
    public string Name { get; }

    public EntityId WarId { get; }

    public int Year { get; }

    /// <summary>Day within <see cref="Year"/> on which the engagement began.</summary>
    public int Day { get; }

    /// <summary>When a siege ended. A field battle ends where it began.</summary>
    public int? EndYear { get; set; }

    public int? EndDay { get; set; }

    /// <summary>Where it was fought. Always set: a battle without a place cannot be named.</summary>
    public EntityId RegionId { get; }

    /// <summary>The settlement the fighting was over, if the ground was not empty.</summary>
    /// <remarks>
    /// Set whenever a settlement stood here, siege or not — a field battle outside an unwalled
    /// village is still a battle for that village, and it is the village that gets sacked
    /// afterwards. Deriving "was this a siege" from the presence of a settlement conflated the two
    /// and left a sacking with no record of what had been sacked.
    /// </remarks>
    public EntityId SettlementId { get; set; } = EntityId.None;

    /// <summary>True if the settlement was actually invested rather than merely fought over.</summary>
    public bool IsSiege { get; set; }

    public SiegeOutcome SiegeOutcome { get; set; } = SiegeOutcome.NotSiege;

    public Stamp StartedAt => new(Year, Day);

    public Stamp? EndedAt => EndYear is int year && EndDay is int day ? new Stamp(year, day) : null;

    public bool IsResolved => !IsSiege || SiegeOutcome is not SiegeOutcome.Ongoing;

    /// <summary>The realm that took the field. Not necessarily the war's aggressor.</summary>
    public EntityId AttackerId { get; set; } = EntityId.None;

    public EntityId DefenderId { get; set; } = EntityId.None;

    public EntityId VictorId { get; set; } = EntityId.None;

    /// <summary>The ruler who led in person, or <see cref="EntityId.None"/> if the army went without them.</summary>
    public EntityId AttackerCommanderId { get; set; } = EntityId.None;

    public EntityId DefenderCommanderId { get; set; } = EntityId.None;

    /// <summary>
    /// Named people who were present: commanders, soldiers who took the field, and residents
    /// of an invested town. Not exported of itself — it is how the chronicle indexes the
    /// engagement onto their pages, and the careers live on the figures.
    /// </summary>
    public List<EntityId> WitnessIds { get; } = new();

    public int AttackerStrength { get; set; }

    public int DefenderStrength { get; set; }

    public int AttackerLosses { get; set; }

    public int DefenderLosses { get; set; }

    /// <summary>True if the victors put the settlement to the sack afterwards.</summary>
    public bool Sacked { get; set; }

    public int TotalLosses => AttackerLosses + DefenderLosses;

    public override string ToString() => $"{Id} {Name} ({StartedAt})";
}
