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

    /// <summary>
    /// Something seen that was larger than the life it happened to.
    /// </summary>
    /// <remarks>
    /// The first category that does not come from another person or from the state. A comet is not
    /// done to anybody, which is exactly why it is worth having: it is the one formative experience
    /// available to someone the chronicle would otherwise record as having been born and died.
    /// </remarks>
    Wonder = 14,
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

/// <summary>How bright a returning comet was, as anyone standing under it would have graded it.</summary>
/// <remarks>
/// Not a measurement. It is the distinction between a thing a scribe bothers to write down, a thing
/// a court talks about for a season, and a thing a chronicle still mentions a century later.
/// Explicit values — part of the export format.
/// </remarks>
public enum ApparitionGrade
{
    Faint = 0,
    Notable = 1,
    Great = 2,
}

/// <summary>
/// A dated, attributed sighting of something the rolled sky actually did.
/// </summary>
/// <remarks>
/// <para><see cref="PriorYear"/> is what the observer's own realm had on record, not what was true.
/// A scribe knows how long it has been only if the register goes back that far, so a realm that lost
/// its books starts counting again — and the interval a later reader works from is the one this
/// person could actually have derived.</para>
///
/// <para><see cref="RealmId"/> is the realm whose register it went into, recorded here rather than
/// read off the observer later. People change realms — by marriage, by conquest, by a border moving
/// over them — and a book does not follow them when they do. Asking the figure's current realm who
/// wrote something down two centuries ago gives the wrong register, and it is the register the next
/// interval is counted from.</para>
/// </remarks>
public sealed record SkyObservation(
    int CometIndex,
    int Year,
    EntityId RealmId,
    EntityId SettlementId,
    int? PriorYear,
    ApparitionGrade Grade)
{
    /// <summary>Years since this realm last wrote the same body down, where it had.</summary>
    public int? Interval => PriorYear is int prior ? Year - prior : null;
}

/// <summary>The two ways a person can answer the question of what a light in the sky was.</summary>
/// <remarks>
/// Neither is a strawman. The mythic register explains and does not predict, which is not a
/// failure of nerve — it is what an explanation is for when nobody has a register going back far
/// enough to count from. The measured register says something checkable and can therefore be
/// wrong, which is the only advantage it has and the whole of it.
/// </remarks>
public enum ClaimRegister
{
    Mythic = 0,
    Measured = 1,
}

/// <summary>What the sky did about a claim that named a year.</summary>
public enum ClaimVerdict
{
    /// <summary>It named a year that has not come yet.</summary>
    Standing = 0,

    /// <summary>The comet returned in the year it named.</summary>
    Confirmed = 1,

    /// <summary>It did not.</summary>
    Refuted = 2,

    /// <summary>The year it named lies beyond the end of the record.</summary>
    Untested = 3,

    /// <summary>It never named a year. Every mythic claim ends here.</summary>
    NotTestable = 4,
}

/// <summary>
/// What one person said a light in the sky was, and what became of the saying.
/// </summary>
/// <remarks>
/// <para>A claim rests on observations its claimant could actually have read — their own realm's
/// register and nothing else. <see cref="RestsOnYears"/> is that evidence, kept so a reader can see
/// what the person was working from rather than taking the conclusion on trust.</para>
///
/// <para>A measured claim names <see cref="PredictedYear"/>, and the sky settles it. Nothing else
/// does: not the claimant's rank, not their realm's learning, not how pious they were. If a roll
/// could make a prediction come true then this is flavour with extra steps.</para>
/// </remarks>
public sealed class SkyClaim
{
    public SkyClaim(
        int id,
        EntityId claimantId,
        EntityId realmId,
        int cometIndex,
        int year,
        ClaimRegister register,
        string reading)
    {
        Id = id;
        ClaimantId = claimantId;
        RealmId = realmId;
        CometIndex = cometIndex;
        Year = year;
        Register = register;
        Reading = reading;
        RestsOnYears = new List<int>();
    }

    /// <summary>Stable within the claimant.</summary>
    public int Id { get; }

    public EntityId ClaimantId { get; }

    /// <summary>The realm whose register it was made from, and whose argument it becomes.</summary>
    public EntityId RealmId { get; }

    public int CometIndex { get; }

    public int Year { get; }

    public ClaimRegister Register { get; }

    /// <summary>What they said it was, in the words their own world would have used.</summary>
    public string Reading { get; }

    /// <summary>The sightings they had to work from, earliest first.</summary>
    public List<int> RestsOnYears { get; }

    /// <summary>The period they derived. Zero on a mythic claim.</summary>
    public int IntervalYears { get; set; }

    /// <summary>The year they said it would come back. Absent on a mythic claim.</summary>
    public int? PredictedYear { get; set; }

    public ClaimVerdict Verdict { get; set; }

    public int? SettledYear { get; set; }

    /// <summary>Whether its author was alive to hear the answer.</summary>
    public bool ClaimantSawTheAnswer { get; set; }
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

/// <summary>A physical consequence that outlasts the engagement that caused it.</summary>
/// <remarks>
/// <see cref="CauseId"/> is a battle when a battle did it and the other party when a quarrel did.
/// Duels reuse this record rather than growing a second wound model, so "cannot ride this year"
/// means the same thing however the wound was got, and one recovery rule covers both.
/// </remarks>
public sealed record FigureInjury(
    EntityId CauseId,
    EventKind SourceKind,
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
    Revenge = 5,
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
        MemoryKind motive,
        EntityId motiveEntityId,
        EventKind motiveSourceKind,
        int deadlineYear,
        EntityId sponsorId = default,
        OfficeKind? requiredOffice = null)
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
        MotiveEntityId = motiveEntityId;
        MotiveSourceKind = motiveSourceKind;
        DeadlineYear = deadlineYear;
        LastProgressYear = startYear;
        SponsorId = sponsorId;
        RequiredOffice = requiredOffice;
        ParticipantIds = new List<EntityId>();
        Steps = new List<UndertakingStep>();
    }

    /// <summary>Stable within the owning figure.</summary>
    public int Id { get; }

    public UndertakingKind Kind { get; }

    public UndertakingState State { get; set; } = UndertakingState.Active;

    public int StartYear { get; }

    public int? EndYear { get; set; }

    /// <summary>Why the arc ended, suitable for the compact life-page summary.</summary>
    public string? Outcome { get; set; }

    public string Objective { get; }

    /// <summary>The person, realm, route or sacred object the goal concerns.</summary>
    public EntityId TargetId { get; }

    public EntityId DestinationId { get; set; }

    public EntityId ViaId { get; set; }

    public int Progress { get; set; }

    public int RequiredProgress { get; }

    public MemoryKind Motive { get; }

    /// <summary>The concrete memory cause: a battle, person, route, relic, or other entity.</summary>
    public EntityId MotiveEntityId { get; }

    public EventKind MotiveSourceKind { get; }

    /// <summary>The last year in which the goal may still make progress.</summary>
    public int DeadlineYear { get; }

    public int LastProgressYear { get; set; }

    /// <summary>The person who backed the goal, if it was not wholly self-directed.</summary>
    public EntityId SponsorId { get; }

    /// <summary>An office whose loss makes this particular goal impossible.</summary>
    public OfficeKind? RequiredOffice { get; }

    /// <summary>Other people committed to the goal; the owner is implicit.</summary>
    public List<EntityId> ParticipantIds { get; }

    public List<UndertakingStep> Steps { get; }

    /// <summary>Used by conspiracies; zero on public undertakings.</summary>
    public double Secrecy { get; set; }

    /// <summary>Used by conspiracies; how close the undertaking is to its target.</summary>
    public double Access { get; set; }
}

/// <summary>The grounded wrong a personal quarrel began from.</summary>
/// <remarks>
/// Every value names an event the world already wrote. There is deliberately no "took a dislike":
/// two people sharing a realm is not a cause, and a quarrel that cannot name its origin is the
/// random tavern brawl this model exists to avoid.
/// </remarks>
public enum DisputeCause
{
    OfficeRevoked = 0,
    SuccessionPassedOver = 1,
    KinMurdered = 2,
    Accusation = 3,
}

/// <summary>How far a quarrel has been carried into the open.</summary>
/// <remarks>
/// The ladder is public visibility, not anger. A grudge is felt, an insult is heard, an accusation
/// is laid before someone with authority to judge it, and a challenge asks for satisfaction that
/// only a meeting can give. Each rung is harder to withdraw from than the one below it, which is
/// why de-escalation gets rarer as the quarrel climbs.
/// </remarks>
public enum DisputeStage
{
    Grudge = 0,
    Insult = 1,
    Accusation = 2,
    Challenge = 3,
}

/// <summary>How a quarrel ended, or that it has not.</summary>
public enum DisputeOutcome
{
    Open = 0,

    /// <summary>The two settled it themselves; the bond keeps the scar and loses the grievance.</summary>
    Reconciled = 1,

    /// <summary>A third party with standing imposed terms and both were held to them.</summary>
    Settled = 2,

    /// <summary>They met, and one of them carried a wound away.</summary>
    Wounded = 3,

    /// <summary>They met, and one of them did not walk away from it.</summary>
    Killed = 4,

    /// <summary>Death elsewhere, or distance, ended it without resolving it.</summary>
    Lapsed = 5,
}

/// <summary>One thing that was done in the course of a quarrel.</summary>
public sealed record DisputeAct(
    int Year,
    EventKind SourceKind,
    DisputeStage Stage,
    EntityId ActorId,
    string Detail);

/// <summary>
/// A persistent quarrel between two named people, from its cause to how it ended.
/// </summary>
/// <remarks>
/// <para>One object, held by both parties. A quarrel is a single fact about two lives and storing
/// it twice invites the two copies to disagree about what happened; the viewpoint each page shows
/// is derived at the edge from which of <see cref="OpenerId"/> and <see cref="RivalId"/> is being
/// read, not from a second record.</para>
///
/// <para>The relationship itself stays in the bond. This carries only what a bond cannot: that the
/// grievance is currently being acted on, how far into the open it has been carried, and what
/// finally answered it.</para>
/// </remarks>
public sealed class FigureDispute
{
    public FigureDispute(
        int id,
        EntityId openerId,
        EntityId rivalId,
        int startYear,
        DisputeCause cause,
        EventKind sourceKind,
        EntityId sourceEntityId,
        EntityId placeId)
    {
        Id = id;
        OpenerId = openerId;
        RivalId = rivalId;
        StartYear = startYear;
        LastActionYear = startYear;
        Cause = cause;
        SourceKind = sourceKind;
        SourceEntityId = sourceEntityId;
        PlaceId = placeId;
        Acts = new List<DisputeAct>();
    }

    /// <summary>Stable within the person who opened it.</summary>
    public int Id { get; }

    /// <summary>The aggrieved party: the one the cause was done to.</summary>
    public EntityId OpenerId { get; }

    /// <summary>The party held responsible for the cause.</summary>
    public EntityId RivalId { get; }

    public int StartYear { get; }

    public int? EndYear { get; set; }

    public DisputeCause Cause { get; }

    /// <summary>The event family that caused it, and the entity that event was about.</summary>
    public EventKind SourceKind { get; }

    public EntityId SourceEntityId { get; }

    public EntityId PlaceId { get; set; }

    public DisputeStage Stage { get; set; }

    public int LastActionYear { get; set; }

    public DisputeOutcome Outcome { get; set; }

    /// <summary>Why it ended, in the words the life page prints.</summary>
    public string? Resolution { get; set; }

    /// <summary>The third party who judged or mediated it, when one did.</summary>
    public EntityId ArbiterId { get; set; } = EntityId.None;

    public List<DisputeAct> Acts { get; }

    public bool IsOpen => Outcome == DisputeOutcome.Open;

    /// <summary>The other party, read from whichever side is asking.</summary>
    public EntityId Other(EntityId self) => self == OpenerId ? RivalId : OpenerId;

    public bool Involves(EntityId id) => id == OpenerId || id == RivalId;
}
