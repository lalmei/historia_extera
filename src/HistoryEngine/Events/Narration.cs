using System.Text;
using HistoryEngine.Core;

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
///   placeholder inside it is unresolvable.</description></item>
/// </list>
///
/// <para>A second template per kind, keyed <c>Kind.self</c>, is what a figure's chronicle uses.
/// The world line names realms; the figure line is the same fact told as something they did.
/// Kinds without a <c>.self</c> template keep the world wording.</para>
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
    public const int SyntaxVersion = 3;

    /// <summary>Suffix on a world template's key for the wording a figure's page uses.</summary>
    public const string SelfKeySuffix = ".self";

    private static readonly DetMap<string, string> TemplatesByKind = BuildTemplates();

    private static DetMap<string, string> BuildTemplates()
    {
        var map = new DetMap<string, string>();

        void Set(EventKind kind, string template) => map[kind.ToString()] = template;

        void SetSelf(EventKind kind, string template) =>
            map[kind.ToString() + SelfKeySuffix] = template;

        Set(EventKind.WorldCreated, "{data:designation} took shape.");

        Set(EventKind.CivilizationFounded, "{subject} was founded[, with its seat at {location}].");
        Set(EventKind.CivilizationFell,
            "{subject} came to an end[ after {data:years} years][, {data:cause}][ by {object}].");
        Set(EventKind.CapitalMoved, "{subject} moved its seat of government to {location}.");

        Set(EventKind.SettlementFounded,
            "{subject} was founded[ by {object}][, {data:settlers} of them out of {data:from}]"
            + "[, {data:purpose}].");
        Set(EventKind.SettlementPromoted, "{subject} grew into a {data:tier}.");
        Set(EventKind.SettlementDeclined, "{subject} dwindled to a {data:tier}.");
        Set(EventKind.SettlementAbandoned,
            "{subject} was abandoned[ after {data:years} years][, its people lost to {data:cause}]"
            + "[, {data:resettled} of them removing to {data:refuge}].");
        Set(EventKind.SettlementFortified, "Walls were raised around {subject}.");
        Set(EventKind.SettlementSpecialized, "{subject} came to be known for {data:trade}.");
        Set(EventKind.SettlementFamine, "{subject} suffered {data:severity}[, losing {data:lost} people].");

        Set(EventKind.FigureBorn,
            "{subject} was born[ to {data:mother} and {object}][ in {location}].");
        Set(EventKind.FigureDied,
            "{subject}[, {data:office},] died[ at the age of {data:age}][, of {data:cause}]"
            + "[, and the court named {data:suspect}].");
        Set(EventKind.RulerCrowned,
            "{subject} became {data:title} of {object}[ at {location}][, {data:claim}].");
        Set(EventKind.RulerDeposed, "{subject} was deposed as {data:title} of {object}.");
        Set(EventKind.FigureMarried, "{subject} married {object}[ at {location}].");
        Set(EventKind.RulerTermEnded,
            "{subject} laid down the office of {data:title}[ of {object}][ after {data:years} years].");
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
        // One template, four errands. The reason a journey was made is a holy site for a pilgrim,
        // a monastery for a scribe fetching copies, a faith for a priest on circuit and a realm
        // for a guest, and a trade route may add the road itself. Each clause is gated on the kind
        // of thing carried; `purpose` supplies any preposition needed by person, faith or site.
        Set(EventKind.JourneyMade,
            "{subject} travelled to {location}[, {data:purpose}]"
            + "[ the {extra:hol}][ the {extra:rel}][ {extra:civ}]"
            + "[ along the {extra:rte}].");
        Set(EventKind.JourneyWaylaid,
            "{subject} came to grief[ on the way to {location}][, {data:cause}].");
        Set(EventKind.FigureWounded,
            "{subject} was {data:severity} wounded at the {object}[, {data:injury}].");
        Set(EventKind.UndertakingStarted,
            "{subject} undertook {data:objective}[, bound for {location}].");
        Set(EventKind.UndertakingCompleted,
            "{subject} completed {data:objective}[ at {location}][, after {data:years} years].");
        Set(EventKind.UndertakingFailed,
            "{subject}'s undertaking, {data:objective}, failed[ at {location}][, {data:cause}].");
        Set(EventKind.ConspiratorJoined,
            "{subject} drew {object} into a conspiracy against {extra:fig}.");
        Set(EventKind.ConspiracyExposed,
            "The conspiracy of {subject} against {object} was exposed[ at {location}].");

        Set(EventKind.DynastyFounded, "The {subject} rose[ under {object}][ in {location}].");
        Set(EventKind.DynastyEnded, "The {subject} died out[ after {data:years}].");
        Set(EventKind.DynastyAscended, "The {subject} took the throne of {object}.");

        Set(EventKind.RegionClaimed,
            "{object} extended its reach into {subject}[ under {data:ruler}].");
        Set(EventKind.RegionCeded,
            "{subject} was ceded[ by {data:from}] to {object}[, and with it {location}]"
            + "[, in the peace that ended the {data:war}].");
        Set(EventKind.RegionReleased, "{subject} passed out of the reach of {object}.");

        Set(EventKind.AllianceFormed, "{subject} and {object} swore an alliance.");
        Set(EventKind.AllianceBroken,
            "The alliance between {subject} and {object} was broken[ after {data:years}].");
        Set(EventKind.WarDeclared,
            "[{data:ruler} of ]{subject} declared war on {object}[, {data:cause}]. "
            + "So began the {location}.");
        Set(EventKind.WarJoined,
            "{subject} entered the {object} alongside {location}.");
        Set(EventKind.BattleFought,
            "{object} prevailed at the {subject}[ under {data:victor}]"
            + "[, at a cost of {data:losses} dead].");
        Set(EventKind.SettlementSacked,
            "{subject} was sacked by {object}[ under {data:captain}][, losing {data:lost} people].");
        Set(EventKind.WarEnded,
            "The {subject} ended[ after {data:years}][, {data:outcome}][ for {object}].");
        Set(EventKind.SiegeBegan,
            "{object} invested {location}. So began the {subject}.");
        Set(EventKind.SiegeLifted,
            "The {subject} was lifted[, {data:cause}].");
        Set(EventKind.SettlementOccupied,
            "{subject} fell to {object}[ under {data:captain}] and was held under arms.");
        Set(EventKind.SettlementRestored,
            "{subject} was recovered from {object}[ {data:manner}] and returned to {location}"
            + "[, after {data:years} under occupation].");

        Set(EventKind.ReligionFounded,
            "The {subject} was first preached[ by {object}][ at {location}].");
        Set(EventKind.ReligionAdopted, "{subject} came to follow the {object}.");
        Set(EventKind.ReligionSchism,
            "The {subject} broke from the {object}[ at {location}].");
        Set(EventKind.ReligionFaded,
            "The {subject} passed out of memory[, {data:years} after it was first preached].");
        Set(EventKind.StateFaithChanged,
            "{subject} took the {object} for its own[, under {data:ruler}].");
        Set(EventKind.HolySiteFounded,
            "{subject} was established for the {object}[ at {location}].");

        Set(EventKind.ArtifactCreated,
            "{subject}, {data:kind}, was made[ at {location}][ for {object}].");
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

        Set(EventKind.PlagueBegan,
            "The {data:name} broke out in {subject}[, carrying off {data:lost} people].");
        Set(EventKind.PlagueSpread,
            "The {data:name} reached {subject}[ from {location}][, carrying off {data:lost} people].");
        Set(EventKind.PlagueEnded,
            "The {data:name} burned itself out[ after {data:years}][, having killed {data:dead} in all].");

        Set(EventKind.DisasterStruck,
            "{subject} was struck by {data:kind}[, losing {data:lost} people].");

        Set(EventKind.TradeRouteOpened,
            "Trade opened between {object} and {location}[, {data:mode}], establishing the {subject}.");
        Set(EventKind.TradeRouteFlourished,
            "The {subject} flourished[ with traffic at {data:traffic}].");
        Set(EventKind.TradeRouteDeclined,
            "The {subject} began to decline[ as traffic fell to {data:traffic}].");
        Set(EventKind.TradeRouteClosed, "The {subject} closed[, {data:cause}].");

        // Both name the two towns and neither names the route, which the viewer renders as
        // "A-B route" — so the older wording said the same pair of names twice in one sentence.
        Set(EventKind.RoadBuilt,
            "A road was cut between {object} and {location}, the traffic between them having earned a made way.");
        Set(EventKind.RoadPaved,
            "The road between {object} and {location} was bridged and paved"
            + "[ after {data:stood} years of use][, shortening the way by {data:saved}].");

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

        // The person is the implied subject. Named only when a clause has to point at them.
        SetSelf(EventKind.FigureBorn,
            "[{self:subject}Was born][{self:subject} to {data:mother} and {object}]"
            + "[{self:subject} in {location}][{self:subject}.]"
            + "[{self:object}{data:mother} bore him a {data:child}, {subject}]"
            + "[{self:object}, at {location}][{self:object}.]"
            + "[{self:extra}Bore {object} a {data:child}, {subject}]"
            + "[{self:extra}, at {location}][{self:extra}.]");
        SetSelf(EventKind.FigureDied,
            "[{self:subject}Died][{self:subject} as {data:office}][{self:subject} at the age of {data:age}]"
            + "[{self:subject}, of {data:cause}][{self:subject}, and the court named {data:suspect}]"
            + "[{self:subject}.]"
            + "[{as:suspect}Was named in the death of {subject}][{as:suspect}, of {data:cause}]"
            + "[{as:suspect}.]"
            + "[{not:suspect}{self:extra}{subject} {data:familyVerb}]"
            + "[{not:suspect}{self:extra}, of {data:cause}]"
            + "[{not:suspect}{self:extra}, and the court named {data:suspect}]"
            + "[{not:suspect}{self:extra}.]");
        SetSelf(EventKind.RulerCrowned,
            "Became {data:title} of {object}[ at {location}][, {data:claim}].");
        SetSelf(EventKind.RulerDeposed, "Was deposed as {data:title} of {object}.");
        SetSelf(EventKind.FigureMarried, "Married {other}[ at {location}].");
        SetSelf(EventKind.RulerTermEnded,
            "Laid down the office of {data:title}[ of {object}][ after {data:years} years].");
        SetSelf(EventKind.RegencyBegan,
            "[{self:subject}Governed as regent for {object}][{self:subject}, a child of {data:age}]"
            + "[{self:object}Came under the regency of {other}][{self:object}, at the age of {data:age}].");
        SetSelf(EventKind.RegencyEnded,
            "[{self:subject}Came of age and took {object} in hand]"
            + "[{self:object}Ended the regency over {other}].");
        SetSelf(EventKind.SuccessionDisputed,
            "[{self:subject}Prevailed over {other} in a disputed succession][{self:subject} in {location}]"
            + "[{self:object}Lost a disputed succession to {other}][{self:object} in {location}].");
        SetSelf(EventKind.RulerAbdicated,
            "Abdicated as {data:title} of {object}[, {data:cause}].");
        SetSelf(EventKind.OfficeGranted,
            "Was made {data:office}[ of {object}][ at {location}][, {data:claim}].");
        SetSelf(EventKind.OfficeRevoked,
            "Was stripped of the office of {data:office}[ of {object}][, {data:cause}].");
        SetSelf(EventKind.OccupationTaken,
            "Took to {data:occupation}[ at {location}].");
        SetSelf(EventKind.JourneyMade,
            "Travelled to {location}[, {data:purpose}]"
            + "[ the {extra:hol}][ the {extra:rel}][ {extra:civ}]"
            + "[ along the {extra:rte}].");
        SetSelf(EventKind.JourneyWaylaid,
            "Came to grief[ on the way to {location}][, {data:cause}].");
        SetSelf(EventKind.FigureWounded,
            "Was {data:severity} wounded at the {object}[, {data:injury}].");
        SetSelf(EventKind.UndertakingStarted,
            "[{self:subject}Undertook {data:objective}][{self:subject}, bound for {location}]"
            + "[{self:subject}.]"
            + "[{self:object}{subject} undertook {data:objective}]"
            + "[{self:object}, bound for {location}][{self:object}.]");
        SetSelf(EventKind.UndertakingCompleted,
            "[{self:subject}Completed {data:objective}][{self:subject} at {location}]"
            + "[{self:subject}, after {data:years} years][{self:subject}.]"
            + "[{self:object}{subject} completed {data:objective}]"
            + "[{self:object} at {location}][{self:object}.]"
            + "[{self:extra}Helped {subject} complete {data:objective}][{self:extra}.]");
        SetSelf(EventKind.UndertakingFailed,
            "[{self:subject}Could not complete {data:objective}][{self:subject} at {location}]"
            + "[{self:subject}, because {data:cause}][{self:subject}.]"
            + "[{self:object}{subject} could not complete {data:objective}]"
            + "[{self:object}, because {data:cause}][{self:object}.]"
            + "[{self:extra}Was implicated when {subject} failed to complete {data:objective}]"
            + "[{self:extra}.]");
        SetSelf(EventKind.ConspiratorJoined,
            "[{self:subject}Drew {other} into a conspiracy against {extra:fig}.]"
            + "[{self:object}Joined {other} in a conspiracy against {extra:fig}.]");
        SetSelf(EventKind.ConspiracyExposed,
            "[{self:subject}The conspiracy against {other} was exposed.]"
            + "[{self:object}Discovered the conspiracy of {other}.]"
            + "[{self:extra}Was implicated in {subject}'s conspiracy against {object}.]");
        SetSelf(EventKind.DynastyFounded,
            "[{self:object}Raised the {subject}][{self:object} in {location}].");
        SetSelf(EventKind.DynastyAscended,
            "[{self:extra}Took the throne of {object} in the name of the {subject}.]");
        SetSelf(EventKind.RegionClaimed,
            "[{as:ruler}Claimed {subject} for {object}.]");
        SetSelf(EventKind.SettlementFounded,
            "[{self:extra}Founded {subject}][{self:extra} for {object}]"
            + "[{self:extra}, {data:settlers} of them out of {data:from}][{self:extra}, {data:purpose}].");
        SetSelf(EventKind.WarDeclared,
            "[{as:ruler}Declared war on {object}][{as:ruler}, {data:cause}][{as:ruler}. So began the {location}.]"
            + "[{not:ruler}{subject} declared war][{not:ruler}, {data:cause}][{not:ruler}. So began the {location}.]");
        SetSelf(EventKind.BattleFought,
            "[{as:victor}Prevailed at the {subject}][{as:victor}, at a cost of {data:losses} dead]"
            + "[{not:victor}Was at the {subject}, which {object} won]"
            + "[{not:victor}, at a cost of {data:losses} dead].");
        SetSelf(EventKind.SiegeBegan,
            "[{self:extra}The {subject} began against {location}.]");
        SetSelf(EventKind.SiegeLifted,
            "[{self:extra}The {subject} was lifted][{self:extra}, {data:cause}].");
        SetSelf(EventKind.SettlementSacked,
            "[{as:captain}Sacked {subject}][{as:captain}, losing {data:lost} people]"
            + "[{not:captain}{self:extra}Was in {subject} when it was sacked by {object}]"
            + "[{not:captain}{self:extra}, losing {data:lost} people].");
        SetSelf(EventKind.SettlementOccupied,
            "[{as:captain}Took {subject} and held it under arms.]");
        SetSelf(EventKind.ReligionFounded,
            "[{self:object}First preached the {subject}][{self:object} at {location}].");
        SetSelf(EventKind.StateFaithChanged,
            "[{as:ruler}Took the {object} as the faith of {subject}.]");
        SetSelf(EventKind.ArtifactCreated,
            "[{self:object}Had {subject} made][{self:object} at {location}]"
            + "[{self:extra}Came into {subject}][{self:extra} at {location}].");
        SetSelf(EventKind.ArtifactTaken,
            "[{self:extra}Took {subject}][{self:extra} to {location}].");
        SetSelf(EventKind.ArtifactGiven,
            "[{self:object}Received {subject}][{self:object} at {location}][{self:object}, {data:manner}]"
            + "[{self:extra}{subject} passed from them][{self:extra} to {object}]"
            + "[{self:extra} at {location}]"
            + "[{self:extra}, {data:manner}].");
        SetSelf(EventKind.ArtifactFound,
            "[{self:object}Found {subject}][{self:object} at {location}].");
        SetSelf(EventKind.ArtifactRecovered,
            "[{self:object}Recovered {subject}][{self:object} at {location}].");
        SetSelf(EventKind.DisasterStruck,
            "[{self:extra}Was caught in the {data:kind} at {subject}]"
            + "[{self:extra}, which lost {data:lost} people].");
        SetSelf(EventKind.RevoltBroke,
            "[{as:leader}Led {subject} in revolt against {object}][{as:leader}, {data:cause}].");
        SetSelf(EventKind.RevoltSeceded,
            "[{as:ruler}Broke {subject} from {object} and rose as {location}][{as:ruler}, {data:cause}].");
        SetSelf(EventKind.RevoltUsurped,
            "Took the throne of {object} after the rising in {subject}"
            + "[, {data:how}][, at a cost of {data:lost} dead].");

        return map;
    }

    /// <summary>The template table, keyed by <see cref="EventKind"/> name and <c>Kind.self</c>.</summary>
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
        HistoryEvent entry, Func<EntityId, string> nameOf, EntityId viewpoint = default)
    {
        string template = TemplateFor(entry.Kind, viewpoint);
        string prose = RenderTemplate(template, entry, nameOf, viewpoint);

        // A role-gated .self template can drop every segment for a witness it does not cover.
        if (prose.Length == 0 && !viewpoint.IsNone)
        {
            prose = RenderTemplate(TemplateFor(entry.Kind), entry, nameOf, viewpoint);
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
        EntityId viewpoint = default)
    {
        var result = new StringBuilder(template.Length + 32);

        int i = 0;
        while (i < template.Length)
        {
            char c = template[i];

            if (c == '[')
            {
                int close = FindSegmentEnd(template, i);
                if (close < 0)
                {
                    result.Append(template, i, template.Length - i);
                    break;
                }

                string inner = template.Substring(i + 1, close - i - 1);
                if (TryRenderSegment(inner, entry, nameOf, viewpoint, out string rendered))
                {
                    result.Append(rendered);
                }

                i = close + 1;
                continue;
            }

            if (c == '{')
            {
                int close = template.IndexOf('}', i);
                if (close < 0)
                {
                    result.Append(template, i, template.Length - i);
                    break;
                }

                string token = template.Substring(i + 1, close - i - 1);
                result.Append(Resolve(token, entry, nameOf, viewpoint) ?? string.Empty);
                i = close + 1;
                continue;
            }

            result.Append(c);
            i++;
        }

        return result.ToString().Trim();
    }

    /// <summary>Renders an optional segment, reporting false if any placeholder inside is absent.</summary>
    private static bool TryRenderSegment(
        string inner,
        HistoryEvent entry,
        Func<EntityId, string> nameOf,
        EntityId viewpoint,
        out string rendered)
    {
        var buffer = new StringBuilder(inner.Length + 16);

        int i = 0;
        while (i < inner.Length)
        {
            char c = inner[i];
            if (c != '{')
            {
                buffer.Append(c);
                i++;
                continue;
            }

            int close = inner.IndexOf('}', i);
            if (close < 0)
            {
                buffer.Append(inner, i, inner.Length - i);
                break;
            }

            string? value = Resolve(
                inner.Substring(i + 1, close - i - 1), entry, nameOf, viewpoint);
            if (value is null)
            {
                rendered = string.Empty;
                return false;
            }

            buffer.Append(value);
            i = close + 1;
        }

        rendered = buffer.ToString();
        return true;
    }

    private static int FindSegmentEnd(string template, int openIndex)
    {
        for (int i = openIndex + 1; i < template.Length; i++)
        {
            if (template[i] == ']') return i;
        }

        return -1;
    }

    private static string? Resolve(
        string token, HistoryEvent entry, Func<EntityId, string> nameOf, EntityId viewpoint)
    {
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

        EntityId id = token switch
        {
            "subject" => entry.Subject,
            "object" => entry.Object,
            "location" => entry.Location,
            "self" => viewpoint,
            "other" => OtherFigure(entry, viewpoint),
            _ => EntityId.None,
        };

        return id.IsNone ? null : nameOf(id);
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
