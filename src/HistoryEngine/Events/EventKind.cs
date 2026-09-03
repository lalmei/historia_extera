namespace HistoryEngine.Events;

/// <summary>
/// Every kind of thing that can happen.
/// </summary>
/// <remarks>
/// Numbered in blocks of one hundred per system, with the gaps left deliberately. These
/// values are part of the export format, so they must never be renumbered — blocks mean a
/// new war event can be added in Milestone 6 without disturbing anything already written to
/// a world file.
///
/// <para>Adding a kind requires a matching entry in <see cref="Narration"/>, which
/// <c>NarrationTests</c> enforces. That is the only place the viewer needs to learn about
/// it: templates ship inside the export, so a new event kind renders in the viewer with no
/// viewer change at all.</para>
/// </remarks>
public enum EventKind
{
    Unknown = 0,

    // ---- World (000) ----
    WorldCreated = 1,

    // ---- Civilizations (100) ----
    CivilizationFounded = 100,
    CivilizationFell = 101,
    CapitalMoved = 102,

    // ---- Settlements (200) ----
    SettlementFounded = 200,
    SettlementPromoted = 201,
    SettlementDeclined = 202,
    SettlementAbandoned = 203,
    SettlementFortified = 204,
    SettlementSpecialized = 205,
    SettlementFamine = 206,

    // ---- Figures (300) ----
    FigureBorn = 300,
    FigureDied = 301,
    RulerCrowned = 302,
    RulerDeposed = 303,
    FigureMarried = 304,
    RulerTermEnded = 305,
    RegencyBegan = 306,
    RegencyEnded = 307,
    SuccessionDisputed = 308,
    RulerAbdicated = 309,

    // ---- Dynasties (310, within the figures block) ----
    DynastyFounded = 310,
    DynastyEnded = 311,
    DynastyAscended = 312,

    // ---- Offices (320, within the figures block) ----
    // Only the decisions are recorded. A holder's death already has an event, and an appointment
    // lapsing with the reign that made it carries nothing the next grant does not — recording
    // both ends of every office roughly doubles what the appointment system writes to say nothing.
    OfficeGranted = 320,
    OfficeRevoked = 321,

    /// <summary>
    /// A recorded person took a trade. Children of a house, and consorts raised into the
    /// record already grown; a notable appointed from the population is covered by the grant.
    /// </summary>
    OccupationTaken = 322,

    /// <summary>A recorded person travelled and was expected home again.</summary>
    JourneyMade = 323,

    /// <summary>
    /// A journey went wrong: robbed, turned back, or ended in a death away from home.
    /// </summary>
    /// <remarks>
    /// Separate from the journey itself rather than a field on it, because this is the half a
    /// reader came for. <see cref="JourneyMade"/> is an itinerary and stays Routine; this is an
    /// event on the road and stands on the spine with the rest of the year's violence.
    /// </remarks>
    JourneyWaylaid = 324,

    /// <summary>A named participant carried a wound away from an engagement.</summary>
    FigureWounded = 325,

    UndertakingStarted = 326,
    UndertakingCompleted = 327,
    UndertakingFailed = 328,

    ConspiratorJoined = 329,
    ConspiracyExposed = 330,

    /// <summary>Two named people fell out over something the chronicle already recorded.</summary>
    /// <remarks>
    /// Routine on its own. A quarrel opening is a fact about two people rather than about the
    /// realm, and a timeline that carried every one of them on the spine would bury the year's
    /// actual violence under courtiers not speaking to each other.
    /// </remarks>
    DisputeOpened = 331,

    /// <summary>A quarrel was carried a rung further into the open.</summary>
    DisputeEscalated = 332,

    /// <summary>A quarrel ended without blood: withdrawn, forgiven, or judged.</summary>
    DisputeSettled = 333,

    /// <summary>The two met over it, and the meeting decided it.</summary>
    DuelFought = 334,

    /// <summary>
    /// A recorded person wrote down a comet the sky actually returned that year.
    /// </summary>
    /// <remarks>
    /// Routine unless the comet was a great one. It is a fact about a person and their register
    /// rather than about the realm, and most of the value is that the register exists at all —
    /// but a great comet is the sort of thing a chronicle still mentions a century later, and the
    /// spine is where a reader would expect to find it.
    /// </remarks>
    ApparitionRecorded = 335,

    /// <summary>Somebody said what a light in the sky was, and sometimes when it would return.</summary>
    /// <remarks>
    /// Notable when it names a year, because that is a claim the world can answer and a reader will
    /// want to be there when it does. Routine when it only explains.
    /// </remarks>
    SkyClaimMade = 336,

    /// <summary>The comet came back in the year somebody said it would.</summary>
    SkyClaimConfirmed = 337,

    /// <summary>It did not.</summary>
    SkyClaimRefuted = 338,

    /// <summary>
    /// A conspiracy moved against the person it named, and missed.
    /// </summary>
    /// <remarks>
    /// The one plot ending that is neither a discovery nor a death: the attempt itself is what
    /// made it public. Notable, because a court that survived one is a court whose next decade
    /// reads differently.
    /// </remarks>
    ConspiracyAttempted = 339,

    /// <summary>An adult took responsibility for a recorded child whose parents were unavailable.</summary>
    GuardianAssigned = 340,

    /// <summary>A guardianship ended at majority or at the death of either person.</summary>
    GuardianshipEnded = 341,

    /// <summary>A recorded person changed where they live.</summary>
    /// <remarks>
    /// Routine for the ordinary household move and Notable only where an office or a throne caused
    /// it. The engine moves people often enough that putting every removal on the spine would give
    /// a reader a timeline of house-moves; what the reader wants is the governor going out to his
    /// province, and the ordinary ones are what make a life page answerable rather than a headline.
    /// </remarks>
    FigureMoved = 342,

    /// <summary>
    /// A soldier was raised a rung in their realm's army.
    /// </summary>
    /// <remarks>
    /// Routine below a captaincy. Everyone who ever takes to arms reaches the lower rungs, and a
    /// spine carrying all of them would bury a year's war under its own recruitment; a captaincy
    /// is a decision a realm made about a person, and reads as one.
    /// </remarks>
    RankGranted = 343,

    /// <summary>
    /// Two named people came to know each other.
    /// </summary>
    /// <remarks>
    /// Routine, and the most routine thing this engine writes. It is on the record at all because
    /// it is the bottom rung of a ladder whose top rungs are not routine, and a friendship that
    /// appeared at its third rung with no beginning would be exactly the disconnected flavour the
    /// life model exists to avoid.
    /// </remarks>
    AcquaintanceFormed = 344,

    /// <summary>A friendship was carried a rung further: a good turn done, or a confidence kept.</summary>
    AffinityDeepened = 345,

    /// <summary>A friendship ended without anybody turning: it cooled, or distance took it.</summary>
    AffinityEnded = 346,

    /// <summary>
    /// One friend turned on the other.
    /// </summary>
    /// <remarks>
    /// Notable, unlike every other rung of the ladder. A betrayal is the one thing on it that a
    /// court remembers, and it is the event the rest of the ladder exists to make possible.
    /// </remarks>
    FriendshipBetrayed = 347,

    // ---- Territory (400) ----
    // Claims are written so ownership can be replayed year by year; they are marked Routine
    // so the timeline is not a run of "extended its reach". Cessions and releases stay on
    // the spine — those are the transfers a reader comes for.
    RegionClaimed = 400,
    RegionCeded = 401,
    RegionReleased = 402,

    // ---- Diplomacy and war (500) ----
    AllianceFormed = 500,
    AllianceBroken = 501,
    WarDeclared = 502,
    WarJoined = 503,
    BattleFought = 504,
    SettlementSacked = 505,
    WarEnded = 506,
    SiegeBegan = 507,
    SiegeLifted = 508,

    // A storming and the peace that undoes it. Both are recorded because an occupation is a state
    // the world is in for years at a time — it decides what the war can attack next and what the
    // treaty has to settle — and a state whose beginning is written but whose end is not leaves a
    // reader with towns that are still occupied centuries after the war that took them.
    SettlementOccupied = 509,
    SettlementRestored = 510,

    // ---- Religion (600) ----
    ReligionFounded = 600,
    ReligionAdopted = 601,
    ReligionSchism = 602,
    ReligionFaded = 603,
    StateFaithChanged = 604,
    HolySiteFounded = 605,

    // ---- Artifacts (620) ----
    ArtifactCreated = 620,
    ArtifactTaken = 621,
    ArtifactLost = 622,
    ArtifactCopied = 623,
    ArtifactClaimed = 624,
    ArtifactGiven = 625,
    ArtifactFound = 626,
    ArtifactDestroyed = 627,
    ArtifactRecovered = 628,
    ArtifactRevised = 629,

    // ---- Plague (640) ----
    PlagueBegan = 640,
    PlagueSpread = 641,
    PlagueEnded = 642,

    // ---- Disasters (660) ----
    DisasterStruck = 660,

    // ---- Trade routes (680) ----
    TradeRouteOpened = 680,
    TradeRouteFlourished = 681,
    TradeRouteDeclined = 682,
    TradeRouteClosed = 683,

    // Construction, not commerce: the link already existed and carried enough for long enough
    // that somebody spent on the ground under it.
    RoadBuilt = 684,
    RoadPaved = 685,

    // ---- Unrest (700) ----
    // Grievance a realm has carried without answering it turns, in stages, into lawlessness and
    // then into revolt. Brigandage is the standing condition; a rising then either is put down,
    // defects, throws off a garrison, breaks away as a realm of its own, or a governor takes the
    // throne. Usurpation and secession are separate endings of the same pressure, not one rising
    // with three interchangeable outcomes.
    BrigandageWorsened = 700,
    RevoltBroke = 701,
    RevoltCrushed = 702,
    RevoltPrevailed = 703,
    RevoltSeceded = 704,
    RevoltUsurped = 705,
}
