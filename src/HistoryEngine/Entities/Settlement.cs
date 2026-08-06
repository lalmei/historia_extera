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
    /// <summary>Population at which each tier is reached. Index matches <see cref="SettlementTier"/>.</summary>
    public static readonly int[] PopulationThresholds = { 0, 250, 1200, 6000 };

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

    public int Population { get; set; }

    /// <summary>Highest population ever reached. Survives decline, for the viewer's benefit.</summary>
    public int PeakPopulation { get; set; }

    public SettlementTier Tier { get; set; }

    public bool IsFortified { get; set; }

    /// <summary>True while this settlement is its civilization's seat of government.</summary>
    public bool IsCapital { get; set; }

    public override string ToString() => $"{Id} {Name} ({Tier}, pop {Population})";
}
