using System.Globalization;
using System.Text;
using HistoryEngine.Core;
using HistoryEngine.Entities;

namespace HistoryEngine.Events;

/// <summary>
/// Prose templates, one per <see cref="EventKind"/>.
/// </summary>
/// <remarks>
/// <para><b>These ship inside the export.</b> That is the point of them. The viewer does not
/// know what a <see cref="EventKind.RulerCrowned"/> is, or that Milestone 6 will add battles
/// — it reads the template table out of the world file, substitutes entity names for slots,
/// and renders. So every event kind added to the engine appears correctly in the viewer with
/// no viewer change, which matters because the alternative is a per-kind switch statement
/// that has to be kept in sync across a language boundary and will not be.</para>
///
/// <para><b>Template syntax</b> — the whole grammar:</para>
/// <list type="bullet">
///   <item><description><c>{subject}</c>, <c>{object}</c>, <c>{location}</c> — resolve to
///   entity names, and become cross-links in the viewer.</description></item>
///   <item><description><c>{data:key}</c> — a string from <see cref="HistoryEvent.Data"/>,
///   rendered as plain text.</description></item>
///   <item><description><c>{extra:kind}</c> — the first entity of that kind among the event's
///   <see cref="HistoryEvent.Extra"/> ids, by its short prefix (<c>hol</c>, <c>rel</c>,
///   <c>civ</c>, …), and a cross-link like the named slots. Absent when the event carries none
///   of that kind, which is what lets one template carry several mutually exclusive clauses: a
///   journey's reason is a holy site, a faith or a realm depending on why it was made, and only
///   the segment whose kind is actually present survives.</description></item>
///   <item><description><c>{self}</c> — the figure whose page is being read. <c>{other}</c> is
///   the other figure among subject and object.</description></item>
///   <item><description><c>{as:key}</c> / <c>{not:key}</c> — succeed (as empty text) when a
///   named actor in data is, or is not, the figure being read. <c>{self:subject}</c> and
///   the same for object, location and extra test the slots themselves.</description></item>
///   <item><description><c>[ ... ]</c> — an optional segment, dropped in its entirety if any
///   placeholder inside it is unresolvable. Segments nest: an outer role-gate can wrap several
///   independent inner clauses, which is what lets a death drop the office or the fever without
///   duplicating the sentence.</description></item>
///   <item><description><c>{cap}</c> — capitalize the next letter. A leading <c>the</c>/<c>a</c>
///   is also capitalized, so a house line can start <c>{the}{subject}</c>. A dropped prefix
///   still needs <c>{cap}</c> — that is what lets a figure line start <c>was born</c> rather
///   than glue <c>Was</c> onto a role test.</description></item>
///   <item><description><c>{a}</c> / <c>{an}</c> — the indefinite article matching the next word.
///   <c>{the}</c> — <c>the</c>, unless that word is already <c>the</c>, which is how a war named
///   <c>the Vethric Succession</c> and a house named <c>Vethric</c> share one template.</description></item>
///   <item><description><c>{they:slot}</c>, <c>{them:slot}</c>, <c>{their:slot}</c> — a pronoun
///   for the named figure in that slot (<c>subject</c>, <c>object</c>, <c>location</c>,
///   <c>self</c>, <c>other</c>, <c>extra</c>). Absent sex falls back to they/them/their.</description></item>
/// </list>
///
/// <para>A second template per kind, keyed <c>Kind.self</c>, is what a figure's chronicle uses.
/// The world line names realms; the figure line is the same fact told as something they did.
/// Kinds without a <c>.self</c> template keep the world wording. Further keys the engine selected
/// — <c>Kind.elective</c>, <c>Kind.1</c> — are the same fact in another wording. The viewer stays
/// kind-blind: it looks the key up, it does not choose it.</para>
///
/// <para>The optional segment is what keeps prose grammatical when slots are absent. A figure
/// born before any settlement exists has no birthplace, and <c>"{subject} was born[ in
/// {location}]."</c> renders as "Aeda was born." rather than "Aeda was born in ." Marking
/// optionality explicitly puts that judgement on the template author, where it belongs —
/// inferring it from comma positions looks like it works until an event has two absent slots
/// in one clause.</para>
/// </remarks>
public static class Narration
{
    /// <summary>
    /// Bumped when the template grammar changes in a way the viewer must match. Exported so a
    /// viewer reading a newer world file can say so instead of rendering it wrongly.
    /// </summary>
    public const int SyntaxVersion = 4;

    /// <summary>Suffix on a world template's key for the wording a figure's page uses.</summary>
    public const string SelfKeySuffix = ".self";

    /// <summary>
    /// Data key naming a factual register — <c>elective</c>, a temple annal, a town hand.
    /// The viewer looks up <c>Kind.{voice}</c>; it does not interpret the word.
    /// </summary>
    public const string VoiceDataKey = "voice";

    /// <summary>
    /// Multiplier shared with the viewer so <c>Kind.1</c> / <c>Kind.2</c> pick the same line.
    /// </summary>
    internal const uint VariantMix = 2654435761u;

    private static readonly DetMap<string, string> TemplatesByKind = BuildTemplates();

    private static DetMap<string, string> BuildTemplates()
    {
        var map = new DetMap<string, string>();

        void Set(EventKind kind, string template) => map[kind.ToString()] = template;

        void SetSelf(EventKind kind, string template) =>
            map[kind.ToString() + SelfKeySuffix] = template;

        void SetAlt(EventKind kind, int n, string template) =>
            map[kind.ToString() + "." + n.ToString(CultureInfo.InvariantCulture)] = template;

        void SetKeyed(EventKind kind, string key, string template) =>
            map[kind.ToString() + "." + key] = template;

        void SetKeyedSelf(EventKind kind, string key, string template) =>
            map[kind.ToString() + "." + key + SelfKeySuffix] = template;

        // The enum name is retained for old event contracts. This line is the beginning of the
        // human record, not the creation of a planet billions of years after its galaxy.
        Set(EventKind.WorldCreated,
            "Recorded history on {data:designation} began; the world was already "
            + "{data:worldAgeGyr} billion years old.");

        Set(EventKind.CivilizationFounded, "{subject} was founded[, with its seat at {location}].");
        Set(EventKind.CivilizationFell,
            "{subject} came to an end[ after {data:years}][, {data:cause}][ by {object}].");
        Set(EventKind.CapitalMoved, "{subject} moved its seat of government to {location}.");

        Set(EventKind.SettlementFounded, "{subject} was founded[ by {object}].");
        Set(EventKind.SettlementPromoted, "{subject} grew into {a}{data:tier}.");
        Set(EventKind.SettlementDeclined, "{subject} dwindled to {a}{data:tier}.");
        Set(EventKind.SettlementAbandoned,
            "{subject} was abandoned[ after {data:years}][, its people lost to {data:cause}]"
            + "[, {data:resettled} of them removing to {data:refuge}].");
        Set(EventKind.SettlementFortified, "Walls were raised around {subject}.");
        Set(EventKind.SettlementSpecialized, "{subject} came to be known for {data:trade}.");
        Set(EventKind.SettlementFamine,
            "{subject} suffered {data:severity}[, losing {data:lost} people].");
        SetAlt(EventKind.SettlementFamine, 1,
            "{data:severity} struck {subject}[, which lost {data:lost} people].");
        SetAlt(EventKind.SettlementFamine, 2,
            "{subject} went hungry — {data:severity}[, losing {data:lost} people].");

        Set(EventKind.FigureBorn,
            "{subject} was born[ to {extra:fig} and {object}][ in {location}].");
        Set(EventKind.FigureDied,
            "{subject} died[ as {data:office}][ at the age of {data:age}][, of {data:cause}]"
            + "[, and the court named {data:suspect}].");
        Set(EventKind.RulerCrowned,
            "{subject} became {data:title} of {object}[ at {location}][, {data:claim}].");
        SetKeyed(EventKind.RulerCrowned, "elective",
            "{subject} was chosen as {data:title} of {object}[ at {location}].");
        Set(EventKind.RulerDeposed,
            "{subject} was deposed as {data:title} of {object}[, {data:cause}].");
        Set(EventKind.FigureMarried, "{subject} married {object}[ at {location}].");
        SetAlt(EventKind.FigureMarried, 1,
            "{subject} and {object} were wed[ at {location}].");
        Set(EventKind.RulerTermEnded,
            "{subject} laid down the office of {data:title}[ of {object}][ after {data:years}].");
        Set(EventKind.RegencyBegan,
            "{subject} governed as regent for {object}[, a child of {data:age}].");
        Set(EventKind.RegencyEnded, "{subject} came of age and took {object} in hand.");
        Set(EventKind.SuccessionDisputed,
            "{subject} prevailed over {object} in a disputed succession[ in {location}].");
        Set(EventKind.RulerAbdicated,
            "{subject} abdicated as {data:title} of {object}[, {data:cause}].");

        Set(EventKind.OfficeGranted,
            "{subject} was made {data:office}[ of {object}][ at {location}][, {data:claim}].");
        Set(EventKind.OfficeRevoked,
            "{subject} was stripped of the office of {data:office}[ of {object}][, {data:cause}].");
        Set(EventKind.OccupationTaken,
            "{subject} took to {data:occupation}[ at {location}].");
        // Beside the line above it, not instead of it: OccupationTaken fires the same year and
        // says they took to a craft, so this one has to say which without saying "craft" again.
        // The label rather than Crafts.Phrase, because the phrases were written for a person's own
        // prose and one of them refuses this sentence outright — "went to the sea" is a sailor and
        // "went to the forge" is a walk. "Set to the trade of" is the idiom for entering a craft
        // and it takes every one of the twenty-one labels unaltered.
        Set(EventKind.CraftTaken,
            "{subject} was set to the trade of {data:craft}[ in {location}].");
        Set(EventKind.RankGranted,
            "{subject} was raised to {data:rank}[ in the army of {object}][, {data:claim}].");
        // One template, four errands. The reason a journey was made is a holy site for a pilgrim,
        // a monastery for a scribe fetching copies, a faith for a priest on circuit and a realm
        // for a guest, and a trade route may add the road itself. Each clause is gated on the kind
        // of thing carried; `purpose` supplies any preposition needed by person, faith or site.
        Set(EventKind.JourneyMade,
            "{subject} travelled to {location}[, {data:purpose}]"
            + "[ {the}{extra:hol}][ {the}{extra:rel}][ {extra:civ}]"
            + "[ along {the}{extra:rte}].");
        Set(EventKind.JourneyWaylaid,
            "{subject} came to grief[ on the way to {location}][, {data:cause}].");
        Set(EventKind.FigureWounded,
            "{subject} was {data:severity} wounded at {the}{object}[, {data:injury}].");
        Set(EventKind.UndertakingStarted,
            "{subject} undertook {data:objective}[, bound for {location}].");
        Set(EventKind.UndertakingCompleted,
            "{subject} completed {data:objective}[ at {location}][, after {data:years}].");
        Set(EventKind.UndertakingFailed,
            "{subject}'s undertaking, {data:objective}, failed[ at {location}][, {data:cause}].");
        Set(EventKind.ConspiratorJoined,
            "{subject} drew {object} into a conspiracy against {extra:fig}.");
        Set(EventKind.ConspiracyExposed,
            "The conspiracy of {subject} against {object} was exposed[ at {location}]"
            + "[, {data:manner}][, after {data:years}].");
        Set(EventKind.ConspiracyAttempted,
            "{subject} moved against {object} and failed[ at {location}][, seeking {data:objective}].");
        Set(EventKind.GuardianAssigned,
            "{subject} took {object} into {their:subject} care[ at {location}][, {data:cause}].");
        Set(EventKind.GuardianshipEnded,
            "The guardianship of {object} by {subject} ended[, {data:cause}].");
        Set(EventKind.FigureMoved, "{subject} removed to {location}[, {data:cause}].");
        // A quarrel names both parties in every line it writes. The one thing a reader of a
        // personal dispute always wants is who it was with, and it is never the location.
        Set(EventKind.DisputeOpened,
            "{subject} fell out with {object}[ at {location}][, over {data:cause}].");
        Set(EventKind.DisputeEscalated,
            "{subject} {data:act} {object}[ at {location}].");
        Set(EventKind.DisputeSettled,
            "The quarrel between {subject} and {object} ended[ at {location}]"
            + "[ when {data:manner}][ before {extra:fig}].");
        // A friendship names both parties in every line, for the reason a quarrel does: the fact
        // is about the two of them, and the town is only where they happened to be standing.
        Set(EventKind.SpouseBetrayed,
            "{subject} turned on {object}, {their:subject} own {data:tie}[ at {location}]"
            + "[, over {data:cause}].");
        Set(EventKind.OfficePassedOver,
            "{subject} was passed over for {data:office}[ at {location}]"
            + "[ in favour of {object}][, having {data:claim}].");
        Set(EventKind.AcquaintanceFormed,
            "{subject} came to know {object}[ at {location}][, through {data:cause}].");
        Set(EventKind.AffinityDeepened,
            "{subject} {data:act} {object}[ at {location}].");
        Set(EventKind.AffinityEnded,
            "The friendship between {subject} and {object} ended[ at {location}]"
            + "[ when {data:manner}].");
        Set(EventKind.FriendshipBetrayed,
            "{subject} turned on {object}[ at {location}][, over {data:cause}].");
        Set(EventKind.DuelFought,
            "{subject} met {object}[ at {location}][ over {data:cause}]"
            + "[ and {data:result} {them:object}][, {data:injury}].");
        // The interval clause is the point of the line. A comet on its own is a portent; a comet
        // with the years since the last one written beside it is the beginning of an argument.
        Set(EventKind.ApparitionRecorded,
            "{subject} recorded {data:grade}[ at {location}]"
            + "[, {data:since} after the last].");
        Set(EventKind.SkyClaimMade,
            "{subject} held {data:reading}[, and looked for it in {data:due}].");
        Set(EventKind.SkyClaimConfirmed,
            "The sky bore out {subject}, who held {data:reading}[, in {data:made}].");
        Set(EventKind.SkyClaimRefuted,
            "The sky did not bear out {subject}, who held {data:reading}[, in {data:made}].");
        Set(EventKind.ClaimCarried,
            "{object} came by what {subject} held[, {data:reading}][, at {location}].");
        Set(EventKind.ClaimLost,
            "{object} no longer held what {subject} said[, {data:reading}]"
            + "[, having lost {data:carrier}][ {data:cause}].");

        Set(EventKind.DynastyFounded, "{the}{subject} rose[ under {object}][ in {location}].");
        Set(EventKind.DynastyEnded, "{the}{subject} died out[ after {data:years}].");
        Set(EventKind.DynastyAscended, "{the}{subject} took the throne of {object}.");

        Set(EventKind.RegionClaimed,
            "{object} extended its reach into {subject}[ under {data:ruler}].");
        Set(EventKind.RegionCeded,
            "{subject} was ceded[ by {data:from}] to {object}[, and with it {location}]"
            + "[, in the peace that ended the {data:war}].");
        Set(EventKind.RegionReleased, "{subject} passed out of the reach of {object}.");

        Set(EventKind.AllianceFormed, "{subject} and {object} swore an alliance.");
        SetAlt(EventKind.AllianceFormed, 1,
            "{subject} and {object} bound themselves in alliance.");
        SetAlt(EventKind.AllianceFormed, 2,
            "An alliance was sworn between {subject} and {object}.");
        Set(EventKind.AllianceBroken,
            "The alliance between {subject} and {object} was broken[ after {data:years}].");
        Set(EventKind.WarDeclared,
            "[{data:ruler} of ]{subject} declared war on {object}[, {data:cause}]. "
            + "So began {the}{location}.");
        SetAlt(EventKind.WarDeclared, 1,
            "{subject} went to war with {object}[, {data:cause}]. So began {the}{location}.");
        SetAlt(EventKind.WarDeclared, 2,
            "War was declared by {subject} upon {object}[, {data:cause}]. "
            + "So began {the}{location}.");
        Set(EventKind.WarJoined,
            "{subject} entered {the}{object} alongside {location}.");
        Set(EventKind.BattleFought,
            "{object} prevailed at {the}{subject}[ under {data:victor}]"
            + "[, at a cost of {data:losses} dead].");
        Set(EventKind.SettlementSacked,
            "{subject} was sacked by {object}[ under {data:captain}][, losing {data:lost} people].");
        Set(EventKind.WarEnded,
            "{the}{subject} ended[ after {data:years}][, {data:outcome}][ for {object}].");
        Set(EventKind.SiegeBegan,
            "{object} invested {location}. So began {the}{subject}.");
        Set(EventKind.SiegeLifted,
            "{the}{subject} was lifted[, {data:cause}].");
        Set(EventKind.SettlementOccupied,
            "{subject} fell to {object}[ under {data:captain}] and was held under arms.");
        Set(EventKind.SettlementRestored,
            "{subject} was recovered from {object}[ {data:manner}] and returned to {location}"
            + "[, after {data:years} under occupation].");

        Set(EventKind.ReligionFounded,
            "{the}{subject} was first preached[ by {object}][ at {location}].");
        Set(EventKind.ReligionAdopted, "{subject} came to follow {the}{object}.");
        Set(EventKind.ReligionSchism,
            "{the}{subject} broke from {the}{object}[ at {location}].");
        Set(EventKind.ReligionFaded,
            "{the}{subject} passed out of memory[, {data:years} after it was first preached].");
        Set(EventKind.StateFaithChanged,
            "{subject} took {the}{object} for its own[, under {data:ruler}].");
        Set(EventKind.HolySiteFounded,
            "{subject} was established for {the}{object}[ at {location}].");

        Set(EventKind.ArtifactCreated,
            "{subject}, {data:kind}, was made[ at {location}][ by {data:maker}][ for {object}].");
        Set(EventKind.ArtifactTaken,
            "{subject} was carried off[ to {location}][ by {object}].");
        Set(EventKind.ArtifactLost, "{subject} was lost[ at {location}][, {data:cause}].");
        Set(EventKind.ArtifactCopied,
            "A copy of {subject} was made[ at {location}][ from the exemplar at {object}].");
        Set(EventKind.ArtifactClaimed,
            "{subject} was yielded to {object}[ at {location}] as a term of peace.");
        Set(EventKind.ArtifactGiven,
            "{subject} was given[ to {object}][ at {location}][, {data:manner}].");
        Set(EventKind.ArtifactFound,
            "{subject} was found[ at {location}][ by {object}].");
        Set(EventKind.ArtifactDestroyed,
            "{subject} was destroyed[ at {location}][, {data:cause}].");
        Set(EventKind.ArtifactRecovered,
            "{subject} was recovered[ at {location}][ by {object}].");
        Set(EventKind.ArtifactRevised,
            "{subject} was continued[ at {location}][ under {object}].");
        Set(EventKind.ArtifactCopyLost,
            "The copy of {subject}[ kept at {location}] did not survive[, {data:cause}].");

        Set(EventKind.PlagueBegan,
            "{the}{data:name} broke out in {subject}[, carrying off {data:lost} people].");
        Set(EventKind.PlagueSpread,
            "{the}{data:name} reached {subject}[ from {location}][, carrying off {data:lost} people].");
        Set(EventKind.PlagueEnded,
            "{the}{data:name} burned itself out[ after {data:years}][, having killed {data:dead} in all].");

        Set(EventKind.DisasterStruck,
            "{subject} was struck by {data:kind}[, losing {data:lost} people].");

        Set(EventKind.TradeRouteOpened,
            "Trade opened between {object} and {location}[, {data:mode}], establishing {the}{subject}.");
        Set(EventKind.TradeRouteFlourished,
            "{the}{subject} flourished[ with traffic at {data:traffic}].");
        Set(EventKind.TradeRouteDeclined,
            "{the}{subject} began to decline[ as traffic fell to {data:traffic}].");
        Set(EventKind.TradeRouteClosed, "{the}{subject} closed[, {data:cause}].");

        // Both name the two towns and neither names the route, which the viewer renders as
        // "A-B route" — so the older wording said the same pair of names twice in one sentence.
        Set(EventKind.RoadBuilt,
            "A road was cut between {object} and {location}, the traffic between them having earned a made way.");
        Set(EventKind.RoadPaved,
            "The road between {object} and {location} was bridged and paved"
            + "[ after {data:stood} of use][, shortening the way by {data:saved}].");

        Set(EventKind.BrigandageWorsened,
            "Brigands took to the roads around {subject}[, {data:cause}].");
        Set(EventKind.RevoltBroke,
            "{subject} rose in revolt against {object}[, led by {data:leader}][, {data:cause}].");
        Set(EventKind.RevoltCrushed,
            "The rising in {subject} was put down by {object}[, at a cost of {data:lost} dead].");
        Set(EventKind.RevoltPrevailed,
            "{subject} threw off {object}[ and passed to {location}][, losing {data:lost} people in the rising].");
        Set(EventKind.RevoltSeceded,
            "{subject} broke from {object} and rose as {location}[, under {data:ruler}][, {data:cause}][, losing {data:lost} people in the rising].");
        Set(EventKind.RevoltUsurped,
            "{location} took the throne of {object} after the rising in {subject}[, {data:how}][, at a cost of {data:lost} dead].");

        Set(EventKind.Unknown, "Something happened.");

        // The person is the implied subject. Nested role-gates wrap the optional clauses so a
        // dropped office or a missing birthplace does not force a duplicate of the whole sentence,
        // and {cap} starts the line after the gate rather than gluing Was onto it.
        SetSelf(EventKind.FigureBorn,
            "[{self:subject}{cap}was born[ to {extra:fig} and {object}][ in {location}].]"
            + "[{self:object}{extra:fig} bore a {data:child}, {subject}[, at {location}].]"
            + "[{self:extra}{cap}bore {them:object} a {data:child}, {subject}[, at {location}].]");
        SetSelf(EventKind.FigureDied,
            "[{self:subject}{cap}died[ as {data:office}][ at the age of {data:age}]"
            + "[, of {data:cause}][, and the court named {data:suspect}].]"
            + "[{as:suspect}{cap}was named in the death of {subject}[, of {data:cause}].]"
            + "[{not:suspect}{self:extra}{subject} {data:familyVerb}[, of {data:cause}]"
            + "[, and the court named {data:suspect}].]");
        SetSelf(EventKind.RulerCrowned,
            "Became {data:title} of {object}[ at {location}][, {data:claim}].");
        SetKeyedSelf(EventKind.RulerCrowned, "elective",
            "Was chosen as {data:title} of {object}[ at {location}].");
        SetSelf(EventKind.RulerDeposed,
            "Was deposed as {data:title} of {object}[, {data:cause}].");
        SetSelf(EventKind.FigureMarried, "Married {other}[ at {location}].");
        SetSelf(EventKind.RulerTermEnded,
            "Laid down the office of {data:title}[ of {object}][ after {data:years}].");
        SetSelf(EventKind.RegencyBegan,
            "[{self:subject}{cap}governed as regent for {object}[, a child of {data:age}].]"
            + "[{self:object}{cap}came under the regency of {other}[, at the age of {data:age}].]");
        SetSelf(EventKind.RegencyEnded,
            "[{self:subject}{cap}came of age and took {object} in hand.]"
            + "[{self:object}{cap}ended the regency over {other}.]");
        SetSelf(EventKind.SuccessionDisputed,
            "[{self:subject}{cap}prevailed over {other} in a disputed succession[ in {location}].]"
            + "[{self:object}{cap}lost a disputed succession to {other}[ in {location}].]");
        SetSelf(EventKind.RulerAbdicated,
            "Abdicated as {data:title} of {object}[, {data:cause}].");
        SetSelf(EventKind.OfficeGranted,
            "Was made {data:office}[ of {object}][ at {location}][, {data:claim}].");
        SetSelf(EventKind.OfficeRevoked,
            "Was stripped of the office of {data:office}[ of {object}][, {data:cause}].");
        SetSelf(EventKind.OccupationTaken,
            "Took to {data:occupation}[ at {location}].");
        SetSelf(EventKind.CraftTaken,
            "Was set to the trade of {data:craft}[ in {location}].");
        SetSelf(EventKind.RankGranted,
            "Was raised to {data:rank}[ in the army of {object}][, {data:claim}].");
        SetSelf(EventKind.JourneyMade,
            "Travelled to {location}[, {data:purpose}]"
            + "[ {the}{extra:hol}][ {the}{extra:rel}][ {extra:civ}]"
            + "[ along {the}{extra:rte}].");
        SetSelf(EventKind.JourneyWaylaid,
            "Came to grief[ on the way to {location}][, {data:cause}].");
        SetSelf(EventKind.FigureWounded,
            "Was {data:severity} wounded at {the}{object}[, {data:injury}].");
        SetSelf(EventKind.UndertakingStarted,
            "[{self:subject}{cap}undertook {data:objective}[, bound for {location}].]"
            + "[{self:object}{subject} undertook {data:objective}[, bound for {location}].]");
        SetSelf(EventKind.UndertakingCompleted,
            "[{self:subject}{cap}completed {data:objective}[ at {location}][, after {data:years}].]"
            + "[{self:object}{subject} completed {data:objective}[ at {location}].]"
            + "[{self:extra}{cap}helped {subject} complete {data:objective}.]");
        SetSelf(EventKind.UndertakingFailed,
            "[{self:subject}{cap}could not complete {data:objective}[ at {location}][, because {data:cause}].]"
            + "[{self:object}{subject} could not complete {data:objective}[, because {data:cause}].]"
            + "[{self:extra}{cap}was implicated when {subject} failed to complete {data:objective}.]");
        SetSelf(EventKind.ConspiratorJoined,
            "[{self:subject}{cap}drew {other} into a conspiracy against {extra:fig}.]"
            + "[{self:object}{cap}joined {other} in a conspiracy against {extra:fig}.]");
        SetSelf(EventKind.ConspiracyExposed,
            "[{self:subject}{the}conspiracy against {other} was exposed[, {data:manner}].]"
            + "[{self:object}{cap}discovered the conspiracy of {other}.]"
            + "[{self:extra}{cap}was implicated in {subject}'s conspiracy against {object}.]");
        SetSelf(EventKind.ConspiracyAttempted,
            "[{self:subject}{cap}moved against {other}, and failed.]"
            + "[{self:object}{cap}survived an attempt by {other}.]"
            + "[{self:extra}{cap}was implicated in {subject}'s attempt on {object}.]");
        SetSelf(EventKind.GuardianAssigned,
            "[{self:subject}{cap}took {other} into {their:self} care.]"
            + "[{self:object}{cap}was taken into the care of {other}.]");
        SetSelf(EventKind.GuardianshipEnded,
            "[{self:subject}{cap}{their:self} care of {other} ended[, {data:cause}].]"
            + "[{self:object}{cap}{their:self} guardianship under {other} ended[, {data:cause}].]");
        SetSelf(EventKind.FigureMoved, "Removed to {location}[, {data:cause}].");
        // The same episode from either side. One record, two readings: the aggrieved party fell
        // out with someone, and the other party was fallen out with, and neither page is a
        // separate incident.
        SetSelf(EventKind.DisputeOpened,
            "[{self:subject}{cap}fell out with {other}[, over {data:cause}].]"
            + "[{self:object}{other} fell out with {them:self}[, over {data:cause}].]");
        SetSelf(EventKind.DisputeEscalated,
            "[{self:subject}{data:actSelf} {other}.]"
            + "[{self:object}{other} {data:act} {them:self}.]");
        SetSelf(EventKind.DisputeSettled,
            "[{self:subject}{the}quarrel with {other} ended[ when {data:manner}].]"
            + "[{self:object}{the}quarrel with {other} ended[ when {data:manner}].]"
            + "[{self:extra}{cap}judged between {subject} and {object}.]");
        SetSelf(EventKind.SpouseBetrayed,
            "[{self:subject}{cap}turned on {other}, {their:self} own {data:tie}[, over {data:cause}].]"
            + "[{self:object}{cap}was betrayed by {other}[, over {data:cause}].]");
        SetSelf(EventKind.OfficePassedOver,
            "[{self:subject}{cap}was passed over for {data:office}[ in favour of {other}]"
            + "[, having {data:claim}].]"
            + "[{self:object}{cap}was given {data:office} over {other}.]");
        SetSelf(EventKind.AcquaintanceFormed,
            "[{self:subject}{cap}came to know {other}[, through {data:cause}].]"
            + "[{self:object}{cap}came to know {other}[, through {data:cause}].]");
        SetSelf(EventKind.AffinityDeepened,
            "[{self:subject}{data:actSelf} {other}.]"
            + "[{self:object}{other} {data:act} {them:self}.]");
        SetSelf(EventKind.AffinityEnded,
            "[{self:subject}{the}friendship with {other} ended[ when {data:manner}].]"
            + "[{self:object}{the}friendship with {other} ended[ when {data:manner}].]");
        SetSelf(EventKind.FriendshipBetrayed,
            "[{self:subject}{cap}turned on {other}[, over {data:cause}].]"
            + "[{self:object}{cap}was betrayed by {other}[, over {data:cause}].]");
        SetSelf(EventKind.DuelFought,
            "[{self:subject}{cap}met {other}[ over {data:cause}][ and {data:result} {them:other}].]"
            + "[{self:object}{cap}was {data:result} by {other}[ over {data:cause}].]");
        SetSelf(EventKind.ApparitionRecorded,
            "Recorded {data:grade}[ at {location}][, {data:since} after the last].");
        SetSelf(EventKind.SkyClaimMade,
            "Held {data:reading}[, and looked for it in {data:due}].");
        SetSelf(EventKind.SkyClaimConfirmed,
            "The sky bore out what {they:self} held[, {data:reading}][ — said in {data:made}].");
        SetSelf(EventKind.SkyClaimRefuted,
            "The sky did not bear out what {they:self} held[, {data:reading}][ — said in {data:made}].");
        SetSelf(EventKind.ClaimCarried,
            "[{self:subject}{cap}what {they:self} held was copied into {object}[ at {location}].]");
        SetSelf(EventKind.ClaimLost,
            "[{self:subject}{object} no longer held what {they:self} said]"
            + "[{self:subject}, having lost {data:carrier}][{self:subject} {data:cause}].");
        SetSelf(EventKind.DynastyFounded,
            "[{self:object}{cap}raised {the}{subject}[ in {location}].]");
        SetSelf(EventKind.DynastyAscended,
            "[{self:extra}{cap}took the throne of {object} in the name of {the}{subject}.]");
        SetSelf(EventKind.RegionClaimed,
            "[{as:ruler}{cap}claimed {subject} for {object}.]");
        SetSelf(EventKind.SettlementFounded,
            "[{self:extra}{cap}founded {subject}[ for {object}].]");
        SetSelf(EventKind.WarDeclared,
            "[{as:ruler}{cap}declared war on {object}[, {data:cause}]. So began {the}{location}.]"
            + "[{not:ruler}{subject} declared war[, {data:cause}]. So began {the}{location}.]");
        // The joining ruler is extra, not a named data slot: the three named slots are already
        // the joiner, the war and the ally who called them.
        SetSelf(EventKind.WarJoined,
            "[{self:extra}{cap}entered {the}{object} alongside {location}.]");
        SetSelf(EventKind.BattleFought,
            "[{as:victor}{cap}prevailed at {the}{subject}[, at a cost of {data:losses} dead].]"
            + "[{not:victor}{cap}was at {the}{subject}, which {object} won"
            + "[, at a cost of {data:losses} dead].]");
        SetSelf(EventKind.SiegeBegan,
            "[{self:extra}{the}{subject} began against {location}.]");
        SetSelf(EventKind.SiegeLifted,
            "[{self:extra}{the}{subject} was lifted[, {data:cause}].]");
        SetSelf(EventKind.SettlementSacked,
            "[{as:captain}{cap}sacked {subject}[, losing {data:lost} people].]"
            + "[{not:captain}{self:extra}{cap}was in {subject} when it was sacked by {object}"
            + "[, losing {data:lost} people].]");
        SetSelf(EventKind.SettlementOccupied,
            "[{as:captain}{cap}took {subject} and held it under arms.]");
        SetSelf(EventKind.WarEnded,
            "[{self:extra}{the}{subject} ended[ after {data:years}][, {data:outcome}][ for {object}].]");
        SetSelf(EventKind.ReligionFounded,
            "[{self:object}{cap}first preached {the}{subject}[ at {location}].]");
        SetSelf(EventKind.StateFaithChanged,
            "[{as:ruler}{cap}took {the}{object} as the faith of {subject}.]");
        SetSelf(EventKind.ArtifactCreated,
            "[{self:object}{cap}had {subject} made[ at {location}].]"
            + "[{self:extra}{cap}came into {subject}[ at {location}].]");
        SetSelf(EventKind.ArtifactTaken,
            "[{self:extra}{cap}took {subject}[ to {location}].]");
        SetSelf(EventKind.ArtifactClaimed,
            "[{self:extra}{cap}received {subject}[ at {location}][ as a term of peace].]");
        SetSelf(EventKind.ArtifactGiven,
            "[{self:object}{cap}received {subject}[ at {location}][, {data:manner}].]"
            + "[{self:extra}{subject} passed from {them:self}[ to {object}][ at {location}][, {data:manner}].]");
        SetSelf(EventKind.ArtifactFound,
            "[{self:object}{cap}found {subject}[ at {location}].]");
        SetSelf(EventKind.ArtifactRecovered,
            "[{self:object}{cap}recovered {subject}[ at {location}].]");
        SetSelf(EventKind.ArtifactRevised,
            "[{self:object}{cap}had {subject} continued[ at {location}].]");
        SetSelf(EventKind.DisasterStruck,
            "[{self:extra}{cap}was caught in {data:kind} at {subject}[, which lost {data:lost} people].]");
        SetSelf(EventKind.RevoltBroke,
            "[{as:leader}{cap}led {subject} in revolt against {object}[, {data:cause}].]");
        SetSelf(EventKind.RevoltSeceded,
            "[{as:ruler}{cap}broke {subject} from {object} and rose as {location}[, {data:cause}].]");
        SetSelf(EventKind.RevoltUsurped,
            "Took the throne of {object} after the rising in {subject}"
            + "[, {data:how}][, at a cost of {data:lost} dead].");

        return map;
    }

    /// <summary>The template table, keyed by <see cref="EventKind"/> name, <c>Kind.self</c>, and variants.</summary>
    public static DetMap<string, string> Templates => TemplatesByKind;

    public static string TemplateFor(EventKind kind) =>
        TemplatesByKind.TryGetValue(kind.ToString(), out string? template)
            ? template!
            : TemplatesByKind[EventKind.Unknown.ToString()];

    /// <summary>The wording a figure's page uses, or the world line if none was written.</summary>
    public static string TemplateFor(EventKind kind, EntityId viewpoint)
    {
        if (!viewpoint.IsNone && viewpoint.Kind == EntityKind.Figure
            && TemplatesByKind.TryGetValue(kind.ToString() + SelfKeySuffix, out string? self)
            && self is not null)
        {
            return self;
        }

        return TemplateFor(kind);
    }

    /// <summary>
    /// The template the engine selected for this event: a factual <c>voice</c> key, a numbered
    /// variant mixed from the event id, a <c>.self</c> line, or the world wording.
    /// </summary>
    public static string TemplateFor(HistoryEvent entry, EntityId viewpoint = default)
    {
        string kind = entry.Kind.ToString();
        string? voice = entry.DataValue(VoiceDataKey);
        bool self = !viewpoint.IsNone && viewpoint.Kind == EntityKind.Figure;

        if (!string.IsNullOrEmpty(voice))
        {
            if (self && TryTemplate(kind + "." + voice + SelfKeySuffix, out string? voicedSelf))
            {
                return voicedSelf;
            }
        }

        if (self && TemplatesByKind.ContainsKey(kind + SelfKeySuffix))
        {
            IReadOnlyList<string> selfKeys = NumberedKeys(kind, SelfKeySuffix);
            if (selfKeys.Count > 1)
            {
                return TemplatesByKind[selfKeys[VariantIndex(entry.Id, selfKeys.Count)]];
            }

            return TemplatesByKind[kind + SelfKeySuffix];
        }

        if (!string.IsNullOrEmpty(voice) && TryTemplate(kind + "." + voice, out string? voiced))
        {
            return voiced;
        }

        IReadOnlyList<string> keys = NumberedKeys(kind, string.Empty);
        if (keys.Count > 1)
        {
            return TemplatesByKind[keys[VariantIndex(entry.Id, keys.Count)]];
        }

        return TemplateFor(entry.Kind);
    }

    /// <summary>Every kind with no template. Should always be empty — asserted by <c>NarrationTests</c>.</summary>
    public static IReadOnlyList<EventKind> MissingTemplates()
    {
        var missing = new List<EventKind>();

        foreach (EventKind kind in Enum.GetValues(typeof(EventKind)))
        {
            if (!TemplatesByKind.ContainsKey(kind.ToString()))
            {
                missing.Add(kind);
            }
        }

        return missing;
    }

    /// <summary>
    /// Renders an event to prose. The engine-side twin of what the viewer does, used for CLI
    /// output and for tests that assert a history reads correctly.
    /// </summary>
    /// <param name="viewpoint">
    /// The figure whose chronicle is being read. Selects the <c>.self</c> template and resolves
    /// <c>{self}</c>, <c>{other}</c> and the role tests.
    /// </param>
    public static string Render(
        HistoryEvent entry,
        Func<EntityId, string> nameOf,
        EntityId viewpoint = default,
        Func<EntityId, Sex?>? sexOf = null)
    {
        string template = TemplateFor(entry, viewpoint);
        string prose = RenderTemplate(template, entry, nameOf, viewpoint, sexOf);

        // A role-gated .self template can drop every segment for a witness it does not cover.
        if (prose.Length == 0 && !viewpoint.IsNone)
        {
            prose = RenderTemplate(TemplateFor(entry), entry, nameOf, viewpoint, sexOf);
        }

        return prose;
    }

    /// <summary>
    /// Renders one template against one event. Internal so the grammar itself can be tested
    /// without going through a kind that happens to use the construct under test.
    /// </summary>
    internal static string RenderTemplate(
        string template,
        HistoryEvent entry,
        Func<EntityId, string> nameOf,
        EntityId viewpoint = default,
        Func<EntityId, Sex?>? sexOf = null)
    {
        var ctx = new RenderCtx(entry, nameOf, viewpoint, sexOf);
        var result = new StringBuilder(template.Length + 32);
        Walk(template, ctx, optional: false, result);
        return Finish(result.ToString());
    }

    /// <summary>
    /// Which numbered variant an event id selects. Shared with the viewer via
    /// <see cref="VariantMix"/>; tests check both sides pick the same line.
    /// </summary>
    internal static int VariantIndex(int eventId, int count)
    {
        if (count <= 1) return 0;

        unchecked
        {
            return (int)(((uint)eventId * VariantMix) % (uint)count);
        }
    }

    private readonly record struct RenderCtx(
        HistoryEvent Entry,
        Func<EntityId, string> NameOf,
        EntityId Viewpoint,
        Func<EntityId, Sex?>? SexOf);

    private const char CapMark = '\u0001';
    private const char AMark = '\u0002';
    private const char TheMark = '\u0003';

    private static bool Walk(string template, RenderCtx ctx, bool optional, StringBuilder output)
    {
        int i = 0;
        while (i < template.Length)
        {
            char c = template[i];

            if (c == '[')
            {
                int close = FindSegmentEnd(template, i);
                if (close < 0)
                {
                    output.Append(template, i, template.Length - i);
                    break;
                }

                string inner = template.Substring(i + 1, close - i - 1);
                var nested = new StringBuilder(inner.Length + 16);
                if (Walk(inner, ctx, optional: true, nested))
                {
                    output.Append(nested);
                }

                i = close + 1;
                continue;
            }

            if (c == '{')
            {
                int close = template.IndexOf('}', i);
                if (close < 0)
                {
                    output.Append(template, i, template.Length - i);
                    break;
                }

                string? value = Resolve(template.Substring(i + 1, close - i - 1), ctx);
                if (value is null)
                {
                    if (optional) return false;
                    value = string.Empty;
                }

                output.Append(value);
                i = close + 1;
                continue;
            }

            output.Append(c);
            i++;
        }

        return true;
    }

    private static int FindSegmentEnd(string template, int openIndex)
    {
        int depth = 1;
        for (int i = openIndex + 1; i < template.Length; i++)
        {
            char c = template[i];
            if (c == '[') depth++;
            else if (c == ']')
            {
                depth--;
                if (depth == 0) return i;
            }
        }

        return -1;
    }

    private static string Finish(string raw)
    {
        var output = new StringBuilder(raw.Length);
        bool capNext = false;

        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (c == CapMark)
            {
                capNext = true;
                continue;
            }

            if (c is AMark or TheMark)
            {
                string word = NextWord(raw, i + 1);
                if (c == TheMark)
                {
                    if (!word.Equals("the", StringComparison.OrdinalIgnoreCase))
                    {
                        AppendCapped(output, "the ", ref capNext);
                    }
                }
                else if (!word.Equals("a", StringComparison.OrdinalIgnoreCase)
                         && !word.Equals("an", StringComparison.OrdinalIgnoreCase))
                {
                    bool an = word.Length > 0 && IsVowel(word[0]);
                    AppendCapped(output, an ? "an " : "a ", ref capNext);
                }

                continue;
            }

            if (capNext)
            {
                if (char.IsWhiteSpace(c))
                {
                    output.Append(c);
                    continue;
                }

                if (char.IsLetter(c))
                {
                    output.Append(char.ToUpperInvariant(c));
                    capNext = false;
                    continue;
                }

                capNext = false;
            }

            output.Append(c);
        }

        string text = output.ToString().Trim();
        if (text.Length >= 2
            && char.IsLower(text[0])
            && (text.StartsWith("the ", StringComparison.Ordinal)
                || text.StartsWith("a ", StringComparison.Ordinal)
                || text.StartsWith("an ", StringComparison.Ordinal)))
        {
            text = char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        return text;
    }

    private static void AppendCapped(StringBuilder output, string text, ref bool capNext)
    {
        if (text.Length == 0) return;

        if (capNext && char.IsLetter(text[0]))
        {
            output.Append(char.ToUpperInvariant(text[0]));
            output.Append(text, 1, text.Length - 1);
            capNext = false;
            return;
        }

        output.Append(text);
    }

    private static string NextWord(string text, int start)
    {
        int i = start;
        while (i < text.Length)
        {
            char c = text[i];
            if (c is CapMark or AMark or TheMark || char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            break;
        }

        int from = i;
        while (i < text.Length)
        {
            char c = text[i];
            if (c is CapMark or AMark or TheMark || char.IsWhiteSpace(c)) break;
            i++;
        }

        return text.Substring(from, i - from);
    }

    private static bool IsVowel(char c) =>
        char.ToLowerInvariant(c) is 'a' or 'e' or 'i' or 'o' or 'u';

    private static string? Resolve(string token, RenderCtx ctx)
    {
        HistoryEvent entry = ctx.Entry;
        Func<EntityId, string> nameOf = ctx.NameOf;
        EntityId viewpoint = ctx.Viewpoint;

        if (token is "cap") return CapMark.ToString();
        if (token is "a" or "an") return AMark.ToString();
        if (token is "the") return TheMark.ToString();

        if (token.StartsWith("data:", StringComparison.Ordinal))
        {
            string? value = entry.DataValue(token.Substring(5));
            return string.IsNullOrEmpty(value) ? null : value;
        }

        if (token.StartsWith("extra:", StringComparison.Ordinal))
        {
            EntityId found = FirstExtraOfKind(entry, token.Substring(6));
            return found.IsNone ? null : nameOf(found);
        }

        if (token.StartsWith("as:", StringComparison.Ordinal))
        {
            if (viewpoint.IsNone) return null;
            string? named = entry.DataValue(token.Substring(3));
            return named == nameOf(viewpoint) ? string.Empty : null;
        }

        if (token.StartsWith("not:", StringComparison.Ordinal))
        {
            if (viewpoint.IsNone) return null;
            string? named = entry.DataValue(token.Substring(4));
            return named == nameOf(viewpoint) ? null : string.Empty;
        }

        if (token.StartsWith("self:", StringComparison.Ordinal))
        {
            if (viewpoint.IsNone) return null;

            return token.Substring(5) switch
            {
                "subject" => viewpoint == entry.Subject ? string.Empty : null,
                "object" => viewpoint == entry.Object ? string.Empty : null,
                "location" => viewpoint == entry.Location ? string.Empty : null,
                "extra" => Mentions(entry, viewpoint) ? string.Empty : null,
                _ => null,
            };
        }

        if (token.StartsWith("they:", StringComparison.Ordinal)
            || token.StartsWith("them:", StringComparison.Ordinal)
            || token.StartsWith("their:", StringComparison.Ordinal))
        {
            int colon = token.IndexOf(':');
            EntityId id = SlotId(token.Substring(colon + 1), entry, viewpoint);
            if (id.IsNone) return null;

            Sex? sex = ctx.SexOf?.Invoke(id);
            return Pronoun(token.Substring(0, colon), sex);
        }

        EntityId namedId = SlotId(token, entry, viewpoint);
        return namedId.IsNone ? null : nameOf(namedId);
    }

    private static string Pronoun(string form, Sex? sex)
    {
        bool female = sex == Sex.Female;
        bool male = sex == Sex.Male;

        return form switch
        {
            "they" => female ? "she" : male ? "he" : "they",
            "them" => female ? "her" : male ? "him" : "them",
            "their" => female ? "her" : male ? "his" : "their",
            _ => "they",
        };
    }

    private static EntityId SlotId(string slot, HistoryEvent entry, EntityId viewpoint) =>
        slot switch
        {
            "subject" => entry.Subject,
            "object" => entry.Object,
            "location" => entry.Location,
            "self" => viewpoint,
            "other" => OtherFigure(entry, viewpoint),
            "extra" => FirstExtraFigure(entry),
            _ => EntityId.None,
        };

    private static bool TryTemplate(string key, out string template)
    {
        if (TemplatesByKind.TryGetValue(key, out string? found) && found is not null)
        {
            template = found;
            return true;
        }

        template = string.Empty;
        return false;
    }

    private static List<string> NumberedKeys(string kind, string suffix)
    {
        var keys = new List<string>();
        string primary = kind + suffix;
        if (TemplatesByKind.ContainsKey(primary)) keys.Add(primary);

        for (int n = 1; ; n++)
        {
            string key = kind + "." + n.ToString(CultureInfo.InvariantCulture) + suffix;
            if (!TemplatesByKind.ContainsKey(key)) break;
            keys.Add(key);
        }

        return keys;
    }

    /// <summary>The first entity of the given short kind prefix among an event's extra ids.</summary>
    /// <remarks>
    /// First rather than only, because an event may be indexed under several of a kind and a
    /// template asking for one wants the one the recorder put there first. Order in
    /// <see cref="HistoryEvent.Extra"/> is fixed by the system that wrote the event, so this is
    /// as deterministic as the list itself.
    /// </remarks>
    private static EntityId FirstExtraOfKind(HistoryEvent entry, string prefix)
    {
        if (entry.Extra is null) return EntityId.None;
        if (!EntityKindExtensions.TryParsePrefix(prefix, out EntityKind kind)) return EntityId.None;

        for (int i = 0; i < entry.Extra.Count; i++)
        {
            if (entry.Extra[i].Kind == kind) return entry.Extra[i];
        }

        return EntityId.None;
    }

    private static EntityId FirstExtraFigure(HistoryEvent entry)
    {
        if (entry.Extra is null) return EntityId.None;

        for (int i = 0; i < entry.Extra.Count; i++)
        {
            if (entry.Extra[i].Kind == EntityKind.Figure) return entry.Extra[i];
        }

        return EntityId.None;
    }

    private static bool Mentions(HistoryEvent entry, EntityId id)
    {
        if (entry.Extra is null) return false;

        for (int i = 0; i < entry.Extra.Count; i++)
        {
            if (entry.Extra[i] == id) return true;
        }

        return false;
    }

    private static EntityId OtherFigure(HistoryEvent entry, EntityId self)
    {
        if (self.IsNone) return EntityId.None;

        if (!entry.Subject.IsNone
            && entry.Subject.Kind == EntityKind.Figure
            && entry.Subject != self)
        {
            return entry.Subject;
        }

        if (!entry.Object.IsNone
            && entry.Object.Kind == EntityKind.Figure
            && entry.Object != self)
        {
            return entry.Object;
        }

        return EntityId.None;
    }
}
