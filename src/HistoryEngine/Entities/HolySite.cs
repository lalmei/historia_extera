using HistoryEngine.Core;

namespace HistoryEngine.Entities;

/// <summary>The form a holy place takes. Explicit values — part of the export format.</summary>
public enum HolySiteKind
{
    Shrine = 0,
    Temple = 1,
    Church = 2,
    Monastery = 3,
    Sanctuary = 4,
}

/// <summary>A lasting place of worship, either inside a settlement or standing on its own.</summary>
/// <remarks>
/// <para><b>Not every holy place is a settlement.</b> A church inside a town shares the town's
/// coordinate and carries its <see cref="SettlementId"/>. A shrine on a mountain or a monastery
/// beyond the walls has no settlement id and keeps an exact coordinate of its own. Treating both
/// as the same entity lets a faith's sacred geography survive conquest, conversion and the
/// abandonment of the nearest town without inventing a population for an isolated sanctuary.</para>
///
/// <para>Sites are not deleted when their faith fades. A ruined temple is still a place the map
/// and chronicle can refer to, for the same reason an abandoned city remains a settlement.</para>
/// </remarks>
public sealed class HolySite
{
    public HolySite(
        EntityId id,
        string name,
        HolySiteKind kind,
        EntityId religionId,
        EntityId regionId,
        EntityId settlementId,
        int x,
        int z,
        int foundedYear)
    {
        Id = id;
        Name = name;
        Kind = kind;
        ReligionId = religionId;
        RegionId = regionId;
        SettlementId = settlementId.IsNone ? EntityId.None : settlementId;
        X = x;
        Z = z;
        FoundedYear = foundedYear;
    }

    public EntityId Id { get; }

    public string Name { get; }

    public HolySiteKind Kind { get; }

    /// <summary>The faith for which this place was established. Never changes.</summary>
    public EntityId ReligionId { get; }

    public EntityId RegionId { get; }

    /// <summary>The enclosing settlement, or none when this is an independent map location.</summary>
    public EntityId SettlementId { get; }

    public bool IsWithinSettlement => !SettlementId.IsNone;

    public int X { get; }

    public int Z { get; }

    public int FoundedYear { get; }

    public override string ToString() => $"{Id} {Name} ({Kind})";
}
