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
    Sibling = 1 << 12,
    Friend = 1 << 13,
    Lover = 1 << 14,
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
    public FigureBond(
        EntityId otherId,
        int sinceYear,
        EventKind originEventKind,
        EntityId originEntityId,
        EntityId originLocationId)
    {
        OtherId = otherId;
        SinceYear = sinceYear;
        LastChangedYear = sinceYear;
        OriginEventKind = originEventKind;
        OriginEntityId = originEntityId;
        OriginLocationId = originLocationId;
        LastEventKind = originEventKind;
        LastEntityId = originEntityId;
        LastLocationId = originLocationId;
    }

    public EntityId OtherId { get; }

    public BondKind Kinds { get; set; }

    public int SinceYear { get; }

    public int LastChangedYear { get; set; }

    public BondCause LastCause { get; set; }

    /// <summary>The stable event facts that first made this relationship historical.</summary>
    public EventKind OriginEventKind { get; }

    public EntityId OriginEntityId { get; }

    public EntityId OriginLocationId { get; }

    /// <summary>The stable event facts behind the latest material change.</summary>
    public EventKind LastEventKind { get; set; }

    public EntityId LastEntityId { get; set; }

    public EntityId LastLocationId { get; set; }

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

/// <summary>The direction in which an experience pulls before disposition interprets it.</summary>
public enum MemoryValence
{
    Negative = -1,
    Neutral = 0,
    Positive = 1,
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

    public MemoryValence Valence => Kind switch
    {
        MemoryKind.Bereavement
            or MemoryKind.Injury
            or MemoryKind.Defeat
            or MemoryKind.Humiliation
            or MemoryKind.Rivalry
            or MemoryKind.Betrayal => MemoryValence.Negative,
        MemoryKind.Triumph
            or MemoryKind.Gratitude
            or MemoryKind.Mentorship
            or MemoryKind.Marriage
            or MemoryKind.Parenthood => MemoryValence.Positive,
        _ => MemoryValence.Neutral,
    };

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

/// <summary>How badly a named person was hurt in an engagement.</summary>
public enum InjurySeverity
{
    Minor = 0,
    Serious = 1,
    Grievous = 2,
}

/// <summary>A physical consequence that outlasts the battle that caused it.</summary>
public sealed record FigureInjury(
    EntityId BattleId,
    int Year,
    InjurySeverity Severity,
    int RecoveryYear,
    bool Permanent,
    string Detail)
{
    public bool IsRecovering(int year) => year < RecoveryYear;
}

/// <summary>A goal large enough to give several otherwise isolated events a shared arc.</summary>
public enum UndertakingKind
{
    TradeVenture = 0,
    Pilgrimage = 1,
    MissionaryCircuit = 2,
    Embassy = 3,
    Conspiracy = 4,
}

public enum UndertakingState
{
    Active = 0,
    Succeeded = 1,
    Failed = 2,
    Abandoned = 3,
}

/// <summary>One event-sized step inside a larger undertaking.</summary>
public sealed record UndertakingStep(
    int Year,
    EventKind SourceKind,
    EntityId PlaceId,
    EntityId SubjectId,
    string Outcome);

/// <summary>A persistent personal objective and the causal steps taken toward it.</summary>
public sealed class FigureUndertaking
{
    public FigureUndertaking(
        int id,
        UndertakingKind kind,
        int startYear,
        string objective,
        EntityId targetId,
        EntityId destinationId,
        EntityId viaId,
        int requiredProgress,
        MemoryKind motive)
    {
        Id = id;
        Kind = kind;
        StartYear = startYear;
        Objective = objective;
        TargetId = targetId;
        DestinationId = destinationId;
        ViaId = viaId;
        RequiredProgress = requiredProgress;
        Motive = motive;
        ParticipantIds = new List<EntityId>();
        Steps = new List<UndertakingStep>();
    }

    /// <summary>Stable within the owning figure.</summary>
    public int Id { get; }

    public UndertakingKind Kind { get; }

    public UndertakingState State { get; set; } = UndertakingState.Active;

    public int StartYear { get; }

    public int? EndYear { get; set; }

    public string Objective { get; }

    /// <summary>The person, realm, route or sacred object the goal concerns.</summary>
    public EntityId TargetId { get; }

    public EntityId DestinationId { get; set; }

    public EntityId ViaId { get; set; }

    public int Progress { get; set; }

    public int RequiredProgress { get; }

    public MemoryKind Motive { get; }

    /// <summary>Other people committed to the goal; the owner is implicit.</summary>
    public List<EntityId> ParticipantIds { get; }

    public List<UndertakingStep> Steps { get; }

    /// <summary>Used by conspiracies; zero on public undertakings.</summary>
    public double Secrecy { get; set; }

    /// <summary>Used by conspiracies; how close the undertaking is to its target.</summary>
    public double Access { get; set; }
}
