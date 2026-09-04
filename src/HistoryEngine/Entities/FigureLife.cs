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
    Guardian = 1 << 15,
    Ward = 1 << 16,
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
    Guardianship = 11,

    /// <summary>A friendship that reached the rung where the two declared it one.</summary>
    Friendship = 12,

    /// <summary>A friend turned on the other, which is a bond change and not a bond ending.</summary>
    Betrayal = 13,
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

    /// <summary>A siege endured while still young enough for it to shape later choices.</summary>
    Siege = 15,

    /// <summary>
    /// A famine, plague, sack or calamity lived through in the town it fell on.
    /// </summary>
    /// <remarks>
    /// The second category that is not done to you by another person, and the one that reaches the
    /// people the other fifteen cannot. Triumph, humiliation and gratitude all sit downstream of
    /// the state doing something to you, so a scribe who never held an office could hold no memory
    /// but a domestic one — while living through four plague years that the chronicle recorded and
    /// her page did not.
    /// </remarks>
    Hardship = 16,

    /// <summary>
    /// A friendship somebody came to rely on.
    /// </summary>
    /// <remarks>
    /// The counterpart <see cref="Rivalry"/> already had. Every affiliative tie in the engine used
    /// to be either a fact of birth or a fact of office, so the only relationships a page could
    /// show as formative were ones nobody chose; this is the one a person did.
    /// </remarks>
    Friendship = 17,
}

/// <summary>How a wound was got, which decides only how it is described.</summary>
/// <remarks>
/// Not a severity, a risk or a lifecycle — those are shared by every wound in the engine and must
/// stay shared. This selects the vocabulary, so that a person pulled out of a collapsed building is
/// not recorded as having taken a spear through the chest.
/// </remarks>
public enum InjuryCause
{
    /// <summary>A weapon, in a battle, a storming or a duel.</summary>
    Violence = 0,

    /// <summary>The world itself: fire, water, falling stone.</summary>
    Calamity = 1,
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
        MemoryKind.Siege or MemoryKind.Hardship => MemoryValence.Negative,
        MemoryKind.Triumph
            or MemoryKind.Gratitude
            or MemoryKind.Mentorship
            or MemoryKind.Friendship
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

/// <summary>The broad working tradition through which a person learned or became known.</summary>
public enum CareerFamily
{
    Arms = 0,
    Faith = 1,
    TradeCraft = 2,
    LettersOffice = 3,
}

/// <summary>Why a guardianship stopped being an active duty.</summary>
public enum GuardianshipEnd
{
    Ongoing = 0,
    Majority = 1,
    GuardianDied = 2,
    WardDied = 3,
}

/// <summary>A bounded guardianship shared by the adult and the child it protected.</summary>
public sealed class FigureGuardianship
{
    public FigureGuardianship(
        EntityId guardianId,
        EntityId wardId,
        int startYear,
        EventKind causeKind,
        EntityId causeEntityId,
        EntityId locationId)
    {
        GuardianId = guardianId;
        WardId = wardId;
        StartYear = startYear;
        CauseKind = causeKind;
        CauseEntityId = causeEntityId;
        LocationId = locationId;
    }

    public EntityId GuardianId { get; }

    public EntityId WardId { get; }

    public int StartYear { get; }

    public int? EndYear { get; set; }

    public GuardianshipEnd End { get; set; }

    public EventKind CauseKind { get; }

    public EntityId CauseEntityId { get; }

    public EntityId LocationId { get; }

    public bool IsActive => End == GuardianshipEnd.Ongoing;
}

/// <summary>Grounded facts known when an already-grown person first enters the record.</summary>
public sealed class FigureBackground
{
    public FigureBackground(
        int introducedYear,
        EntityId originSettlementId,
        CareerFamily careerFamily)
    {
        IntroducedYear = introducedYear;
        OriginSettlementId = originSettlementId;
        CareerFamily = careerFamily;
    }

    public int IntroducedYear { get; }

    public EntityId OriginSettlementId { get; }

    public CareerFamily CareerFamily { get; }

    /// <summary>The army, faith, town, guild, or court through which they became known.</summary>
    public EntityId InstitutionId { get; set; }

    /// <summary>A named backer where the creation path actually had one.</summary>
    public EntityId SponsorId { get; set; }

    /// <summary>A named teacher where the creation path actually had one.</summary>
    public EntityId MentorId { get; set; }
}

/// <summary>A mentorship start, shared by the teacher and apprentice without a second affinity.</summary>
public sealed record FigureMentorship(
    EntityId MentorId,
    EntityId ApprenticeId,
    int StartYear,
    CareerFamily CareerFamily,
    EntityId LocationId);

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

    // 4 was Conspiracy, before a plot became a record of its own with members, phases, secrecy
    // and an outcome an undertaking has nowhere to put. See <see cref="FigurePlot"/>.
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

    /// <summary>
    /// A post they had a claim on, given to somebody standing beside them.
    /// </summary>
    /// <remarks>
    /// The first wrong in the engine between two people of comparable standing. The other four are
    /// all vertical — a crown and the man it dismissed, an heir and the claimant he beat, a court
    /// and the hand it named — which left peers with nothing to fall out over, and so left the
    /// friendship model's betrayal reachable in five worlds out of forty.
    /// </remarks>
    PassedOverForOffice = 4,
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

/// <summary>
/// How far two people have gone in relying on each other.
/// </summary>
/// <remarks>
/// The ladder is what has actually been risked, which is the affiliative counterpart of the
/// quarrel ladder's public visibility. Two people are known to each other, then one has done the
/// other a good turn, then something has been entrusted that could have been withheld, and then
/// the tie is one both of them would name. Each rung costs more to walk back than the one below
/// it, and none may be skipped, so a friendship in the export always carries the years it took.
/// </remarks>
public enum AffinityStage
{
    /// <summary>They are known to each other, and nothing more has been asked or given.</summary>
    Acquaintance = 0,

    /// <summary>One of them did the other a good turn.</summary>
    Kindness = 1,

    /// <summary>Something was entrusted that could have been kept back.</summary>
    Confidence = 2,

    /// <summary>A tie both of them would name, and the rung a betrayal has something to betray.</summary>
    Friendship = 3,
}

/// <summary>
/// What put the two of them within reach of each other to begin with.
/// </summary>
/// <remarks>
/// Contact is a precondition rather than a roll, so every affinity names the circumstance that
/// made it possible. Sharing a realm is not one of them: that is the mistake the quarrel model
/// refused, and it produces a great deal of friendship that means nothing.
/// </remarks>
public enum AffinityOrigin
{
    /// <summary>They lived in the same town in the year it began.</summary>
    SharedResidence = 0,

    /// <summary>Comrades from the same battle line, taken further than comradeship.</summary>
    SharedCampaign = 1,

    /// <summary>An existing tie of office or teaching that warmed into something chosen.</summary>
    SharedService = 2,
}

/// <summary>How a friendship ended, or that it has not.</summary>
public enum AffinityOutcome
{
    /// <summary>Still standing. At <see cref="AffinityStage.Friendship"/> this is the good ending.</summary>
    Open = 0,

    /// <summary>Nothing was done about it for long enough that it stopped being one.</summary>
    Cooled = 1,

    /// <summary>One of them ended up in another realm, and it did not survive the distance.</summary>
    Parted = 2,

    /// <summary>One of them turned on the other.</summary>
    Betrayed = 3,

    /// <summary>A death ended it while it still stood.</summary>
    Lapsed = 4,
}

/// <summary>One thing that was done in the course of a friendship.</summary>
public sealed record AffinityAct(
    int Year,
    EventKind SourceKind,
    AffinityStage Stage,
    EntityId ActorId,
    string Detail);

/// <summary>
/// A friendship between two named people, from the contact that allowed it to how it ended.
/// </summary>
/// <remarks>
/// <para>One object, held by both parties, for the reason a <see cref="FigureDispute"/> is: it is a
/// single fact about two lives, and two copies would come to disagree about it. The
/// <see cref="OpenerId"/> is the one who sought the other, which is the only asymmetry in it.</para>
///
/// <para>One record per pair, ever. A friendship that cooled and was struck up again twenty years
/// later would be a second record of the same two people, and the reading a page wants — how long
/// these two have known each other — is the one that would then be wrong.</para>
/// </remarks>
public sealed class FigureAffinity
{
    public FigureAffinity(
        int id,
        EntityId openerId,
        EntityId friendId,
        int startYear,
        AffinityOrigin origin,
        EventKind sourceKind,
        EntityId sourceEntityId,
        EntityId placeId)
    {
        Id = id;
        OpenerId = openerId;
        FriendId = friendId;
        StartYear = startYear;
        LastActionYear = startYear;
        Origin = origin;
        SourceKind = sourceKind;
        SourceEntityId = sourceEntityId;
        PlaceId = placeId;
        Acts = new List<AffinityAct>();
    }

    /// <summary>Stable within the person who sought the other.</summary>
    public int Id { get; }

    /// <summary>The one who sought the other out.</summary>
    public EntityId OpenerId { get; }

    public EntityId FriendId { get; }

    public int StartYear { get; }

    public int? EndYear { get; set; }

    public AffinityOrigin Origin { get; }

    /// <summary>The event family the contact came from, and the entity that event was about.</summary>
    public EventKind SourceKind { get; }

    public EntityId SourceEntityId { get; }

    public EntityId PlaceId { get; set; }

    public AffinityStage Stage { get; set; }

    public int LastActionYear { get; set; }

    public AffinityOutcome Outcome { get; set; }

    /// <summary>How it ended, in the words the life page prints.</summary>
    public string? Resolution { get; set; }

    /// <summary>Which of the two turned, where one did.</summary>
    public EntityId BetrayerId { get; set; } = EntityId.None;

    public List<AffinityAct> Acts { get; }

    public bool IsOpen => Outcome == AffinityOutcome.Open;

    /// <summary>The other party, read from whichever side is asking.</summary>
    public EntityId Other(EntityId self) => self == OpenerId ? FriendId : OpenerId;

    public bool Involves(EntityId id) => id == OpenerId || id == FriendId;
}

/// <summary>What a plot is trying to do to the person it names.</summary>
/// <remarks>
/// Two, because two are enough to prove the lifecycle carries an objective rather than assuming
/// one. Killing a ruler and unseating a ruler need different backing, run different risks and end
/// the same world state differently — a corpse and a succession, or a deposed man who is still
/// alive to be executed, exiled, or restored. Anything further reuses this lifecycle rather than
/// widening it.
/// </remarks>
public enum PlotObjective
{
    Assassinate = 0,
    Depose = 1,
}

/// <summary>The recorded wrong or claim a plot began from.</summary>
/// <remarks>
/// Every value names something the world already wrote down about this person. There is no
/// "ambitious courtier": a plot that cannot say which year and which event it came from is the
/// annual assassination roll this model replaced.
/// </remarks>
public enum PlotCause
{
    SuccessionPassedOver = 0,
    OfficeRevoked = 1,
    KinMurdered = 2,

    /// <summary>A quarrel with someone rank forbade them to call out. See <see cref="FigureDispute"/>.</summary>
    QuarrelBeyondReach = 3,
}

/// <summary>How far a plot has got, in the order a plot has to get there.</summary>
/// <remarks>
/// Not a difficulty ladder. Gathering is people, Access is the route to the target those people
/// open, and Ready is the year the thing is attempted. A plot can be exposed, betrayed or
/// abandoned in any of them, which is why most plots never reach the third.
/// </remarks>
public enum PlotPhase
{
    Gathering = 0,
    Access = 1,
    Ready = 2,
}

/// <summary>How a plot ended, or that it has not.</summary>
public enum PlotOutcome
{
    Ongoing = 0,

    /// <summary>Nobody ever knew. The leader let it go, or lost the reason for it.</summary>
    Abandoned = 1,

    /// <summary>The court found it.</summary>
    Exposed = 2,

    /// <summary>One of its own gave it up.</summary>
    Betrayed = 3,

    /// <summary>It was attempted, and the attempt missed.</summary>
    Failed = 4,

    Succeeded = 5,
}

/// <summary>What actually bound one person to a plot on the year they joined it.</summary>
/// <remarks>
/// Recorded per member rather than derived later, because the whole claim of this model is that
/// recruitment is grounded: the tie was tested against a bond, a grievance or an office that
/// existed at the time, and a reader can see which. <see cref="Household"/> is the one that does
/// not require belief in the plot — see <see cref="PlotMember.Witting"/>.
/// </remarks>
public enum PlotTie
{
    ObligationToLeader = 0,
    TrustInLeader = 1,
    GrievanceAgainstTarget = 2,
    Ambition = 3,
    Household = 4,
}

/// <summary>The side from which an exported figure page reads a plot.</summary>
public enum PlotViewpoint
{
    Leader = 0,
    Member = 1,
    Target = 2,
}

/// <summary>
/// One person committed to a plot, and what committed them.
/// </summary>
/// <param name="Witting">
/// False for the servant, kinsman or officer whose access was used without their knowing what it
/// was for. They carry no memory of it and no record of it on their own page; they are named here
/// because the retrospective truth of how the plot reached its target includes them.
/// </param>
public sealed record PlotMember(EntityId FigureId, int JoinedYear, PlotTie Tie, bool Witting);

/// <summary>
/// One thing that was done in the course of a plot.
/// </summary>
/// <param name="Known">
/// Whether this was public in the year it happened. Almost nothing is: a plot's own record is the
/// retrospective truth, and <see cref="FigurePlot.PublicYear"/> is the year any of it became
/// something the world could say out loud.
/// </param>
public sealed record PlotAct(
    int Year,
    EventKind SourceKind,
    PlotPhase Phase,
    EntityId ActorId,
    string Detail,
    bool Known);

/// <summary>
/// A persistent conspiracy: who wanted whom removed, who joined them, and what became of it.
/// </summary>
/// <remarks>
/// <para><b>The engine keeps the whole truth; the chronicle keeps what got out.</b> A plot writes
/// nothing to the timeline while it is secret, and most plots are secret for their whole lives —
/// an abandoned one never becomes an event at all. <see cref="PublicYear"/> is the year the world
/// learned of it, absent while it never did, and it is what separates a fact a contemporary could
/// have known from one only a later reader has.</para>
///
/// <para>One record, held by the leader and by every witting member, on the same reasoning as
/// <see cref="FigureDispute"/>: a conspiracy is a single fact about several lives, and storing it
/// once per participant invites the copies to disagree about what happened.</para>
/// </remarks>
public sealed class FigurePlot
{
    public FigurePlot(
        int id,
        EntityId leaderId,
        EntityId targetId,
        EntityId realmId,
        PlotObjective objective,
        int startYear,
        PlotCause cause,
        EventKind sourceKind,
        EntityId sourceEntityId,
        EntityId placeId,
        int requiredProgress)
    {
        Id = id;
        LeaderId = leaderId;
        TargetId = targetId;
        RealmId = realmId;
        Objective = objective;
        StartYear = startYear;
        LastActionYear = startYear;
        Cause = cause;
        SourceKind = sourceKind;
        SourceEntityId = sourceEntityId;
        PlaceId = placeId;
        RequiredProgress = requiredProgress;
        Members = new List<PlotMember>();
        Acts = new List<PlotAct>();
    }

    /// <summary>Stable within the leader.</summary>
    public int Id { get; }

    public EntityId LeaderId { get; }

    public EntityId TargetId { get; }

    /// <summary>The realm the plot sits in, kept so a fallen realm can close its plots.</summary>
    public EntityId RealmId { get; }

    public PlotObjective Objective { get; }

    public int StartYear { get; }

    public int? EndYear { get; set; }

    public PlotCause Cause { get; }

    /// <summary>
    /// The event family the cause came from, and the entity the grievance is held against.
    /// </summary>
    /// <remarks>
    /// The target, in every current cause, because every current cause is a wrong this person holds
    /// the target responsible for. It is stored rather than implied so a later cause that blames
    /// somebody else — a faction, a realm — does not have to change the record's shape.
    /// </remarks>
    public EventKind SourceKind { get; }

    public EntityId SourceEntityId { get; }

    public EntityId PlaceId { get; set; }

    public PlotPhase Phase { get; set; }

    public int Progress { get; set; }

    public int RequiredProgress { get; }

    /// <summary>How well the plot is still kept, in [0, 1].</summary>
    public double Secrecy { get; set; }

    /// <summary>What the court has come to suspect, in [0, 1].</summary>
    public double Suspicion { get; set; }

    /// <summary>How close the plot can get to its target, in [0, 1].</summary>
    public double Access { get; set; }

    public PlotOutcome Outcome { get; set; }

    /// <summary>Why it ended, in the words the life page prints.</summary>
    public string? Resolution { get; set; }

    /// <summary>The member who gave it up, where one did.</summary>
    public EntityId BetrayerId { get; set; } = EntityId.None;

    /// <summary>The year the world learned of it, or absent if the world never did.</summary>
    public int? PublicYear { get; set; }

    public int LastActionYear { get; set; }

    public List<PlotMember> Members { get; }

    public List<PlotAct> Acts { get; }

    public bool IsOpen => Outcome == PlotOutcome.Ongoing;

    /// <summary>Whether anything about it ever became public.</summary>
    public bool WasKnown => PublicYear is not null;

    public bool Involves(EntityId id) =>
        id == LeaderId || id == TargetId || Members.Exists(member => member.FigureId == id);

    public bool HasMember(EntityId id) => Members.Exists(member => member.FigureId == id);

    /// <summary>Members who knew what they were part of, in the order they joined.</summary>
    public int WittingCount
    {
        get
        {
            int count = 0;
            foreach (PlotMember member in Members)
            {
                if (member.Witting) count++;
            }

            return count;
        }
    }
}
