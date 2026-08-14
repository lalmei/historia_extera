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

    // ---- Dynasties (310, within the figures block) ----
    DynastyFounded = 310,
    DynastyEnded = 311,
    DynastyAscended = 312,

    // ---- Territory (400) ----
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

    // ---- Religion (600) ----
    ReligionFounded = 600,
    ReligionAdopted = 601,
    ReligionSchism = 602,
    ReligionFaded = 603,
    StateFaithChanged = 604,

    // ---- Artifacts (620) ----
    ArtifactCreated = 620,
    ArtifactTaken = 621,
    ArtifactLost = 622,

    // ---- Plague (640) ----
    PlagueBegan = 640,
    PlagueSpread = 641,
    PlagueEnded = 642,

    // ---- Disasters (660) ----
    DisasterStruck = 660,
}
