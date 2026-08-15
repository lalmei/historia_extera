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

/// <summary>
/// The architectural and ritual tradition a holy place is built in. Explicit values — part of
/// the export format.
/// </summary>
/// <remarks>
/// Derived from the founding culture's naming language, with the region's climate allowed to
/// colour it. A stave church and a marble colonnade are both "temples" to the systems; this is
/// the part a reader uses to tell them apart.
/// </remarks>
public enum SacredTradition
{
    /// <summary>Stave work, tarred wood, storm and sea, ancestral mounds.</summary>
    Nordic = 0,

    /// <summary>Marble, colonnades, sundials, theocratic light.</summary>
    Classical = 1,

    /// <summary>Turquoise tile, mud-brick forts, wayside shrines on the caravan roads.</summary>
    Steppe = 2,

    /// <summary>Wooden onion domes, marsh pavilions, megaliths in old forest.</summary>
    Forest = 3,
}

/// <summary>Who or what a holy place was raised for. Explicit values — part of the export format.</summary>
public enum HolySiteDedicationKind
{
    God = 0,
    AncientGod = 1,
    NatureSpirit = 2,
    CosmicForce = 3,
    DivineConcept = 4,
    AncestralKing = 5,
    LivingKing = 6,
    Martyr = 7,
    Saint = 8,
    Sage = 9,
}

/// <summary>How large a holy place is to stand in. Explicit values — part of the export format.</summary>
public enum HolySiteScale
{
    Small = 0,
    Medium = 1,
    Large = 2,
}

/// <summary>
/// The appearance and observance of a holy place, composed once when it was founded.
/// </summary>
/// <remarks>
/// Stored rather than reconstructed at export, for the same reason a tome's contents are: a
/// church raised in a fishing village must keep the smell of tar and the iron fire-bowl even
/// after the town has become a city, and two references to the same sanctuary must be worded
/// identically. Invented dedicatees are keyed to the culture's language so they sound like the
/// people who built the place; a real figure is used when the chronicle already has one.
/// </remarks>
public sealed class HolySiteDescription
{
    public HolySiteDescription(
        SacredTradition tradition,
        HolySiteDedicationKind dedicationKind,
        string dedication,
        string style,
        string atmosphere,
        HolySiteScale scale,
        string capacity,
        bool hasStatue,
        string focalPoint,
        string offering,
        EntityId dedicateeId)
    {
        Tradition = tradition;
        DedicationKind = dedicationKind;
        Dedication = dedication;
        Style = style;
        Atmosphere = atmosphere;
        Scale = scale;
        Capacity = capacity;
        HasStatue = hasStatue;
        FocalPoint = focalPoint;
        Offering = offering;
        DedicateeId = dedicateeId.IsNone ? EntityId.None : dedicateeId;
    }

    public SacredTradition Tradition { get; }

    public HolySiteDedicationKind DedicationKind { get; }

    /// <summary>Who it was raised for, in a chronicler's sentence or two.</summary>
    public string Dedication { get; }

    /// <summary>Materials, plan and ornament.</summary>
    public string Style { get; }

    /// <summary>What it is like to stand there.</summary>
    public string Atmosphere { get; }

    public HolySiteScale Scale { get; }

    /// <summary>How the scale is felt: a cleft in the rock, a hall with side rooms.</summary>
    public string Capacity { get; }

    public bool HasStatue { get; }

    /// <summary>The statue, or whatever stands in its place.</summary>
    public string FocalPoint { get; }

    /// <summary>Where offerings are left, and what is left there.</summary>
    public string Offering { get; }

    /// <summary>A recorded figure this place honours, or none when the dedicatee is legendary.</summary>
    public EntityId DedicateeId { get; }
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
        int foundedYear,
        HolySiteDescription description)
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
        Description = description;
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

    /// <summary>How the place looks and what is done there. Fixed at founding.</summary>
    public HolySiteDescription Description { get; }

    public override string ToString() => $"{Id} {Name} ({Kind})";
}
