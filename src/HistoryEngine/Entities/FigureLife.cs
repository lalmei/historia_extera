using HistoryEngine.Core;
using HistoryEngine.Events;

namespace HistoryEngine.Entities;

/// <summary>The durable parts of one person's relationship to another.</summary>
/// <remarks>
/// Flags are intentional. A brother can also become a rival, and an old mentor can later be an
/// enemy; replacing one label with the other would discard the history that makes the later turn
/// interesting. Directional roles come in pairs (<see cref="Parent"/>/<see cref="Child"/>,
/// <see cref="Patron"/>/<see cref="Client"/>) and are kept mirrored by the life-story helpers.
/// </remarks>
[Flags]
public enum BondKind
{
    None = 0,
    Kin = 1 << 0,
    Spouse = 1 << 1,
    Parent = 1 << 2,
    Child = 1 << 3,
    Patron = 1 << 4,
    Client = 1 << 5,
    Mentor = 1 << 6,
    Apprentice = 1 << 7,
    Companion = 1 << 8,
    Rival = 1 << 9,
    Enemy = 1 << 10,
    CoConspirator = 1 << 11,
}

/// <summary>The last material event to alter a bond.</summary>
public enum BondCause
{
    Unknown = 0,
    Kinship = 1,
    Marriage = 2,
    Parenthood = 3,
    Patronage = 4,
    Mentorship = 5,
    SharedCampaign = 6,
    Bereavement = 7,
    Conflict = 8,
    Conspiracy = 9,
    Undertaking = 10,
}

/// <summary>A directed, persistent relationship between two recorded people.</summary>
public sealed class FigureBond
{
    public FigureBond(EntityId otherId, int sinceYear)
    {
        OtherId = otherId;
        SinceYear = sinceYear;
        LastChangedYear = sinceYear;
    }

    public EntityId OtherId { get; }

    public BondKind Kinds { get; set; }

    public int SinceYear { get; }

    public int LastChangedYear { get; set; }

    public BondCause LastCause { get; set; }

    /// <summary>Warmth or dislike, in [-1, 1].</summary>
    public double Affection { get; set; }

    /// <summary>Confidence or suspicion, in [-1, 1].</summary>
    public double Trust { get; set; }

    /// <summary>Debt, loyalty, or duty felt toward the other person, in [0, 1].</summary>
    public double Obligation { get; set; }

    /// <summary>Personal apprehension of the other person, in [0, 1].</summary>
    public double Fear { get; set; }

    /// <summary>An injury or insult still held against the other person, in [0, 1].</summary>
    public double Grievance { get; set; }
}

/// <summary>The kinds of experiences a figure may carry forward into later decisions.</summary>
public enum MemoryKind
{
    Bereavement = 0,
    Injury = 1,
    Triumph = 2,
    Defeat = 3,
    Humiliation = 4,
    Gratitude = 5,
    Mentorship = 6,
    Rivalry = 7,
    Ambition = 8,
    Betrayal = 9,
    Marriage = 10,
    Parenthood = 11,
    Journey = 12,
    Conspiracy = 13,
}

/// <summary>A bounded, causal memory that can influence later behaviour.</summary>
/// <remarks>
/// It stores the source kind rather than a chronicle index. Events in an open step may be sorted
/// before their final ids are assigned; retaining that temporary position would create dangling
/// provenance. Kind, year, person and place remain stable and are enough to explain the memory.
/// </remarks>
public sealed class SalientMemory
{
    public SalientMemory(
        MemoryKind kind,
        int year,
        EventKind sourceKind,
        EntityId aboutId,
        EntityId locationId,
        double intensity)
    {
        Kind = kind;
        Year = year;
        LastReinforcedYear = year;
        SourceKind = sourceKind;
        AboutId = aboutId;
        LocationId = locationId;
        Intensity = intensity;
    }

    public MemoryKind Kind { get; }

    public int Year { get; }

    public int LastReinforcedYear { get; set; }

    public EventKind SourceKind { get; set; }

    public EntityId AboutId { get; }

    public EntityId LocationId { get; set; }

    public double Intensity { get; set; }
}

/// <summary>Feelings derived from the memories still vivid in a life.</summary>
public readonly record struct FeelingState(
    double Grief,
    double Fear,
    double Anger,
    double Pride,
    double Loyalty);
