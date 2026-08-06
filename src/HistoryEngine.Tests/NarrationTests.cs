using HistoryEngine.Core;
using HistoryEngine.Events;
using Xunit;

namespace HistoryEngine.Tests;

/// <summary>
/// Covers the narration layer, which is what lets the viewer render event kinds it has never
/// heard of.
/// </summary>
public sealed class NarrationTests
{
    /// <summary>
    /// Every event kind needs a template.
    /// </summary>
    /// <remarks>
    /// The one thing that must not be forgotten when a milestone adds event kinds. A missing
    /// template does not crash — the event renders as "Something happened" in the viewer, which is
    /// the kind of defect that survives a demo and ships.
    /// </remarks>
    [Fact]
    public void EveryEventKindHasATemplate()
    {
        IReadOnlyList<EventKind> missing = Narration.MissingTemplates();

        Assert.True(
            missing.Count == 0,
            "Event kinds without a narration template: " + string.Join(", ", missing));
    }

    [Fact]
    public void AllSlotsResolveWhenPresent()
    {
        var entry = new HistoryEvent(
            Id: 0,
            Year: 40,
            Kind: EventKind.RulerCrowned,
            Subject: EntityId.Figure(3),
            Object: EntityId.Civilization(1),
            Location: EntityId.Settlement(2),
            Data: Chronicle.Data(("title", "Queen")));

        string prose = Narration.Render(entry, Name);

        Assert.Equal("fig:3 became Queen of civ:1 at set:2.", prose);
    }

    /// <summary>
    /// An absent optional slot must drop its whole segment.
    /// </summary>
    /// <remarks>
    /// The reason the template grammar has explicit <c>[ ]</c> segments rather than inferring
    /// optionality from punctuation. Inference works until an event has two absent slots in one
    /// clause, and then produces text like "was born in , of ".
    /// </remarks>
    [Fact]
    public void AbsentOptionalSlotDropsItsSegment()
    {
        var withBirthplace = new HistoryEvent(
            0, 10, EventKind.FigureBorn, EntityId.Figure(1), default, EntityId.Settlement(5));

        var withoutBirthplace = new HistoryEvent(
            1, 10, EventKind.FigureBorn, EntityId.Figure(1), default, EntityId.None);

        Assert.Equal("fig:1 was born in set:5.", Narration.Render(withBirthplace, Name));
        Assert.Equal("fig:1 was born.", Narration.Render(withoutBirthplace, Name));
    }

    [Fact]
    public void AbsentDataKeyDropsItsSegment()
    {
        var withAge = new HistoryEvent(
            0, 80, EventKind.FigureDied, EntityId.Figure(2), default, default,
            Data: Chronicle.Data(("age", "71"), ("cause", "old age")));

        var withoutAnything = new HistoryEvent(
            1, 80, EventKind.FigureDied, EntityId.Figure(2), default, default);

        Assert.Equal("fig:2 died at the age of 71, of old age.", Narration.Render(withAge, Name));
        Assert.Equal("fig:2 died.", Narration.Render(withoutAnything, Name));
    }

    [Fact]
    public void PartiallyPresentDataRendersOnlyWhatItHas()
    {
        var causeOnly = new HistoryEvent(
            0, 80, EventKind.FigureDied, EntityId.Figure(2), default, default,
            Data: Chronicle.Data(("cause", "illness")));

        Assert.Equal("fig:2 died, of illness.", Narration.Render(causeOnly, Name));
    }

    /// <summary>Templates must not reference slots their emitting system never fills — spot-checked here.</summary>
    [Fact]
    public void TemplatesUseOnlyKnownPlaceholders()
    {
        var allowed = new[] { "subject", "object", "location" };

        foreach (KeyValuePair<string, string> pair in Narration.Templates)
        {
            string template = pair.Value;

            for (int i = 0; i < template.Length; i++)
            {
                if (template[i] != '{') continue;

                int close = template.IndexOf('}', i);
                Assert.True(close > i, $"Unclosed placeholder in template for {pair.Key}");

                string token = template.Substring(i + 1, close - i - 1);
                bool valid = allowed.Contains(token)
                    || token.StartsWith("data:", StringComparison.Ordinal);

                Assert.True(valid, $"Template for {pair.Key} uses unknown placeholder '{token}'");
                i = close;
            }
        }
    }

    /// <summary>Optional segments must be balanced, since the viewer parses the same grammar.</summary>
    [Fact]
    public void OptionalSegmentsAreBalanced()
    {
        foreach (KeyValuePair<string, string> pair in Narration.Templates)
        {
            int depth = 0;
            foreach (char c in pair.Value)
            {
                if (c == '[') depth++;
                else if (c == ']') depth--;

                Assert.True(depth is 0 or 1, $"Nested or unbalanced segment in template for {pair.Key}");
            }

            Assert.Equal(0, depth);
        }
    }

    private static string Name(EntityId id) => id.ToString();
}
