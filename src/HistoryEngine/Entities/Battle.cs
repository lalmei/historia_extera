using HistoryEngine.Core;

namespace HistoryEngine.Entities;

/// <summary>
/// One engagement in a war, named for where it was fought.
/// </summary>
/// <remarks>
/// <para><b>Why battles are entities and truces are not.</b> A battle is the thing a chronicle
/// refers back to — people are remembered for having won one, a house is remembered for having
/// lost its king at one, and the reader wants a page listing who fought and what it cost. A truce
/// is a state of two realms and reads perfectly well as an event.</para>
///
/// <para>A siege is not a separate kind. It is a battle whose <see cref="SettlementId"/> is set,
/// which is what makes the walls count and what puts a town at risk of being sacked. Splitting
/// them would duplicate the whole resolution for the sake of one adjective.</para>
/// </remarks>
public sealed class Battle
{
    public Battle(
        EntityId id,
        string name,
        EntityId warId,
        int year,
        EntityId regionId)
    {
        Id = id;
        Name = name;
        WarId = warId;
        Year = year;
        RegionId = regionId;
    }

    public EntityId Id { get; }

    /// <summary>"Battle of Ormsholmadal", "Second Siege of Ekallatograd" — composed, not generated.</summary>
    public string Name { get; }

    public EntityId WarId { get; }

    public int Year { get; }

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

    /// <summary>The realm that took the field. Not necessarily the war's aggressor.</summary>
    public EntityId AttackerId { get; set; } = EntityId.None;

    public EntityId DefenderId { get; set; } = EntityId.None;

    public EntityId VictorId { get; set; } = EntityId.None;

    /// <summary>The ruler who led in person, or <see cref="EntityId.None"/> if the army went without them.</summary>
    public EntityId AttackerCommanderId { get; set; } = EntityId.None;

    public EntityId DefenderCommanderId { get; set; } = EntityId.None;

    public int AttackerStrength { get; set; }

    public int DefenderStrength { get; set; }

    public int AttackerLosses { get; set; }

    public int DefenderLosses { get; set; }

    /// <summary>True if the victors put the settlement to the sack afterwards.</summary>
    public bool Sacked { get; set; }

    public int TotalLosses => AttackerLosses + DefenderLosses;

    public override string ToString() => $"{Id} {Name} ({Year})";
}
