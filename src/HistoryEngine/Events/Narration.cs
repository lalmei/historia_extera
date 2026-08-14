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
/// <para><b>Template syntax</b> — three constructs, and the whole grammar:</para>
/// <list type="bullet">
///   <item><description><c>{subject}</c>, <c>{object}</c>, <c>{location}</c> — resolve to
///   entity names, and become cross-links in the viewer.</description></item>
///   <item><description><c>{data:key}</c> — a string from <see cref="HistoryEvent.Data"/>,
///   rendered as plain text.</description></item>
///   <item><description><c>[ ... ]</c> — an optional segment, dropped in its entirety if any
///   placeholder inside it is unresolvable.</description></item>
/// </list>
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
    public const int SyntaxVersion = 1;

    private static readonly DetMap<string, string> TemplatesByKind = BuildTemplates();

    private static DetMap<string, string> BuildTemplates()
    {
        var map = new DetMap<string, string>();

        void Set(EventKind kind, string template) => map[kind.ToString()] = template;

        Set(EventKind.WorldCreated, "The world took shape.");

        Set(EventKind.CivilizationFounded, "{subject} was founded[, with its seat at {location}].");
        Set(EventKind.CivilizationFell,
            "{subject} came to an end[ after {data:years} years][, {data:cause}][ by {object}].");
        Set(EventKind.CapitalMoved, "{subject} moved its seat of government to {location}.");

        Set(EventKind.SettlementFounded, "{subject} was founded[ by {object}].");
        Set(EventKind.SettlementPromoted, "{subject} grew into a {data:tier}.");
        Set(EventKind.SettlementDeclined, "{subject} dwindled to a {data:tier}.");
        Set(EventKind.SettlementAbandoned,
            "{subject} was abandoned[ after {data:years} years][, its people lost to {data:cause}].");
        Set(EventKind.SettlementFortified, "Walls were raised around {subject}.");
        Set(EventKind.SettlementSpecialized, "{subject} came to be known for {data:trade}.");
        Set(EventKind.SettlementFamine, "{subject} suffered {data:severity}[, losing {data:lost} people].");

        Set(EventKind.FigureBorn, "{subject} was born[ to {object}][ in {location}].");
        Set(EventKind.FigureDied, "{subject} died[ at the age of {data:age}][, of {data:cause}].");
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

        Set(EventKind.DynastyFounded, "The {subject} rose[ under {object}][ in {location}].");
        Set(EventKind.DynastyEnded, "The {subject} died out[ after {data:years}].");
        Set(EventKind.DynastyAscended, "The {subject} took the throne of {object}.");

        Set(EventKind.RegionClaimed, "{object} extended its reach into {subject}.");
        Set(EventKind.RegionCeded,
            "{subject} was ceded to {object}[, and with it {location}].");
        Set(EventKind.RegionReleased, "{subject} passed out of the reach of {object}.");

        Set(EventKind.AllianceFormed, "{subject} and {object} swore an alliance.");
        Set(EventKind.AllianceBroken,
            "The alliance between {subject} and {object} was broken[ after {data:years}].");
        Set(EventKind.WarDeclared,
            "{subject} declared war on {object}[, {data:cause}]. So began the {location}.");
        Set(EventKind.WarJoined,
            "{subject} entered the {object} alongside {location}.");
        Set(EventKind.BattleFought,
            "{object} prevailed at the {subject}[, at a cost of {data:losses} dead].");
        Set(EventKind.SettlementSacked,
            "{subject} was sacked by {object}[, losing {data:lost} people].");
        Set(EventKind.WarEnded,
            "The {subject} ended[ after {data:years}][, {data:outcome}][ for {object}].");

        Set(EventKind.ReligionFounded,
            "The {subject} was first preached[ by {object}][ at {location}].");
        Set(EventKind.ReligionAdopted, "{subject} came to follow the {object}.");
        Set(EventKind.ReligionSchism,
            "The {subject} broke from the {object}[ at {location}].");
        Set(EventKind.ReligionFaded,
            "The {subject} passed out of memory[, {data:years} after it was first preached].");
        Set(EventKind.StateFaithChanged,
            "{subject} took the {object} for its own[, under {data:ruler}].");

        Set(EventKind.ArtifactCreated,
            "{subject}, {data:kind}, was made[ at {location}][ for {object}].");
        Set(EventKind.ArtifactTaken,
            "{subject} was carried off[ to {location}][ by {object}].");
        Set(EventKind.ArtifactLost, "{subject} was lost[ at {location}][, {data:cause}].");
        Set(EventKind.ArtifactCopied,
            "A copy of {subject} was made[ at {location}][ from the exemplar at {object}].");
        Set(EventKind.ArtifactClaimed,
            "{subject} was yielded to {object}[ at {location}] as a term of peace.");

        Set(EventKind.PlagueBegan,
            "The {data:name} broke out in {subject}[, carrying off {data:lost} people].");
        Set(EventKind.PlagueSpread,
            "The {data:name} reached {subject}[ from {location}][, carrying off {data:lost} people].");
        Set(EventKind.PlagueEnded,
            "The {data:name} burned itself out[ after {data:years}][, having killed {data:dead} in all].");

        Set(EventKind.DisasterStruck,
            "{subject} was struck by {data:kind}[, losing {data:lost} people].");

        Set(EventKind.Unknown, "Something happened.");

        return map;
    }

    /// <summary>The template table, keyed by <see cref="EventKind"/> name. Written to the export as-is.</summary>
    public static DetMap<string, string> Templates => TemplatesByKind;

    public static string TemplateFor(EventKind kind) =>
        TemplatesByKind.TryGetValue(kind.ToString(), out string? template)
            ? template!
            : TemplatesByKind[EventKind.Unknown.ToString()];

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
    /// <param name="nameOf">Resolves an entity id to its display name.</param>
    public static string Render(HistoryEvent entry, Func<EntityId, string> nameOf)
    {
        string template = TemplateFor(entry.Kind);
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
                    // Unterminated segment: treat the rest as literal rather than throwing.
                    result.Append(template, i, template.Length - i);
                    break;
                }

                string inner = template.Substring(i + 1, close - i - 1);
                if (TryRenderSegment(inner, entry, nameOf, out string rendered))
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

                // A required slot that will not resolve renders as nothing. Templates should
                // wrap genuinely optional slots in [ ] so this stays rare.
                result.Append(Resolve(token, entry, nameOf) ?? string.Empty);
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
        string inner, HistoryEvent entry, Func<EntityId, string> nameOf, out string rendered)
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

            string? value = Resolve(inner.Substring(i + 1, close - i - 1), entry, nameOf);
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

    private static string? Resolve(string token, HistoryEvent entry, Func<EntityId, string> nameOf)
    {
        if (token.StartsWith("data:", StringComparison.Ordinal))
        {
            string? value = entry.DataValue(token.Substring(5));
            return string.IsNullOrEmpty(value) ? null : value;
        }

        EntityId id = token switch
        {
            "subject" => entry.Subject,
            "object" => entry.Object,
            "location" => entry.Location,
            _ => EntityId.None,
        };

        return id.IsNone ? null : nameOf(id);
    }
}
