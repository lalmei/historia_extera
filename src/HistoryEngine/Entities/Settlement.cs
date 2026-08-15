using HistoryEngine.Core;

namespace HistoryEngine.Entities;

/// <summary>Settlement size classes. Explicit values — part of the export format.</summary>
public enum SettlementTier
{
    Hamlet = 0,
    Village = 1,
    Town = 2,
    City = 3,
}

public static class SettlementTiers
{
    /// <summary>
    /// Population at which each tier is reached. Index matches <see cref="SettlementTier"/>.
    /// </summary>
    /// <remarks>
    /// Calibrated against the carrying capacities the terrain actually produces, which range from a
    /// few hundred on marginal ground to around eleven thousand on the best. The original ladder
    /// was set against a world whose fertility saturated at 1.0 everywhere; once that was fixed,
    /// a city at six thousand was unreachable outside a handful of regions.
    /// </remarks>
    public static readonly int[] PopulationThresholds = { 0, 180, 900, 4000 };

    /// <summary>The tier a given population qualifies for.</summary>
    public static SettlementTier ForPopulation(int population)
    {
        for (int tier = PopulationThresholds.Length - 1; tier >= 0; tier--)
        {
            if (population >= PopulationThresholds[tier]) return (SettlementTier)tier;
        }

        return SettlementTier.Hamlet;
    }

    public static string Label(SettlementTier tier) => tier switch
    {
        SettlementTier.Hamlet => "hamlet",
        SettlementTier.Village => "village",
        SettlementTier.Town => "town",
        SettlementTier.City => "city",
        _ => "settlement",
    };
}

/// <summary>
/// A settled place, with a real position on the map.
/// </summary>
/// <remarks>
/// Position is an exact world coordinate from the first milestone, even though Phase 1's
/// terrain is a placeholder. Phase 2 renders these on real generated terrain and Phase 3
/// stamps them into Vintage Story's world, and retrofitting coordinates onto a chronicle
/// that only knew about regions would mean regenerating every history ever produced.
///
/// <para>Abandoned settlements are never removed. They keep their id and gain
/// <see cref="AbandonedYear"/>, because a ruined city is a thing history refers to.</para>
/// </remarks>
public sealed class Settlement
{
    public Settlement(
        EntityId id,
        EntityId civilizationId,
        EntityId regionId,
        string name,
        int x,
        int z,
        int foundedYear,
        int population)
    {
        Id = id;
        CivilizationId = civilizationId;
        RegionId = regionId;
        Name = name;
        X = x;
        Z = z;
        FoundedYear = foundedYear;
        Population = population;
        Tier = SettlementTiers.ForPopulation(population);
    }

    public EntityId Id { get; }

    /// <summary>Current owner. Changes hands when territory does.</summary>
    public EntityId CivilizationId { get; set; }

    /// <summary>The civilization that founded it. Never changes.</summary>
    public EntityId FoundedBy { get; init; } = EntityId.None;

    public EntityId RegionId { get; }

    public string Name { get; set; }

    public int X { get; }

    public int Z { get; }

    public int FoundedYear { get; }

    public int? AbandonedYear { get; set; }

    public bool IsActive => AbandonedYear is null;

    /// <summary>
    /// How many people live here now.
    /// </summary>
    /// <remarks>
    /// <para>Writing it maintains <see cref="PeakPopulation"/>, so the two cannot disagree. That is
    /// structural rather than a rule each caller remembers, because a caller did forget: eight
    /// systems change a population and seven of them only ever reduce it, so the one that raises it
    /// outside the growth tick — <see cref="Systems.SettlementLifecycleSystem"/> settling refugees
    /// from an abandoned neighbour — was the only place the invariant could break, and it broke
    /// there. A town that took in refugees in the run's final year was exported holding more people
    /// than it had ever held.</para>
    ///
    /// <para>It was self-correcting in every year but the last, which is what kept it hidden: the
    /// next growth tick would notice and raise the peak. So it surfaced only as a nonsense pair of
    /// numbers on the viewer's settlement page, and briefly as a peak too low for
    /// <see cref="Systems.SettlementLifecycleSystem"/> to judge decline against.</para>
    /// </remarks>
    public int Population
    {
        get => _population;
        set
        {
            _population = value;
            if (value > PeakPopulation) PeakPopulation = value;
        }
    }

    private int _population;

    /// <summary>Highest population ever reached. Survives decline, for the viewer's benefit.</summary>
    public int PeakPopulation { get; set; }

    public SettlementTier Tier { get; set; }

    /// <summary>
    /// What the settlement is chiefly known for. Established once it outgrows a hamlet.
    /// </summary>
    public SettlementSpecialization Specialization { get; set; } = SettlementSpecialization.None;

    /// <summary>The year its character was established, if it has one.</summary>
    public int? SpecializedYear { get; set; }

    /// <summary>
    /// Consecutive years spent below half the population this settlement once held.
    /// </summary>
    /// <remarks>
    /// <para>Abandonment is a decision about a sustained state, not about the direction of travel.
    /// Counting <em>declining</em> years instead — the obvious first attempt — could never work
    /// here, because collapse and recovery are wildly asymmetric: a settlement sheds people at
    /// nearly twenty percent a year when its land fails, then creeps back at under two. It spends
    /// five years falling and forty slowly growing, so a declining-year counter peaked at five and
    /// then decayed, and no settlement was ever abandoned however long the world ran.</para>
    ///
    /// <para>"Has been a shadow of itself for a generation" is the condition that actually
    /// describes a place people give up on, and it accumulates during the long recovery rather
    /// than being erased by it.</para>
    /// </remarks>
    public int YearsDepressed { get; set; }

    public bool IsFortified { get; set; }

    /// <summary>The faith followed here, or none while the place keeps its own counsel.</summary>
    public EntityId ReligionId { get; set; } = EntityId.None;

    /// <summary>The year the current faith took hold, if one has.</summary>
    public int? ConvertedYear { get; set; }

    /// <summary>True while this settlement is its civilization's seat of government.</summary>
    public bool IsCapital { get; set; }

    /// <summary>
    /// What the ground it stands on was chosen for.
    /// </summary>
    /// <remarks>
    /// Set once at founding and never revised, because it is a fact about a decision rather than
    /// about the present: a harbour town whose bay silted up in the chronicle's telling was still
    /// founded for the harbour, and that is what explains where it is.
    /// </remarks>
    public SiteCharacter Site { get; init; } = SiteCharacter.Plain;

    public override string ToString() => $"{Id} {Name} ({Tier}, pop {Population})";
}
