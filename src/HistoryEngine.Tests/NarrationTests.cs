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

    [Fact]
    public void AMurderIsToldDifferentlyToKinAndTheNamedHand()
    {
        EntityId victim = EntityId.Figure(2);
        EntityId spouse = EntityId.Figure(5);
        EntityId hand = EntityId.Figure(8);
        var murder = new HistoryEvent(
            0, 80, EventKind.FigureDied, victim, default, default,
            Extra: new[] { spouse, hand },
            Data: Chronicle.Data(
                ("age", "42"),
                ("cause", "a knife in the dark"),
                ("suspect", "fig:8")));

        Assert.Equal(
            "fig:2 died at the age of 42, of a knife in the dark, and the court named fig:8.",
            Narration.Render(murder, Name));
        Assert.Equal(
            "Died at the age of 42, of a knife in the dark, and the court named fig:8.",
            Narration.Render(murder, Name, victim));
        Assert.Equal(
            "fig:2 was slain, of a knife in the dark, and the court named fig:8.",
            Narration.Render(murder, Name, spouse));
        Assert.Equal(
            "Was named in the death of fig:2, of a knife in the dark.",
            Narration.Render(murder, Name, hand));
    }

    [Fact]
    public void AFiguresChronicleIsToldFromTheirPointOfView()
    {
        EntityId ruler = EntityId.Figure(4);
        EntityId spouse = EntityId.Figure(5);
        var claim = new HistoryEvent(
            0, 40, EventKind.RegionClaimed, EntityId.Region(1), EntityId.Civilization(2), default,
            Extra: new[] { ruler },
            Data: Chronicle.Data(("ruler", "fig:4")));

        Assert.Equal(
            "civ:2 extended its reach into reg:1 under fig:4.",
            Narration.Render(claim, Name));
        Assert.Equal("Claimed reg:1 for civ:2.", Narration.Render(claim, Name, ruler));

        var marriage = new HistoryEvent(
            1, 20, EventKind.FigureMarried, ruler, spouse, EntityId.Settlement(8));

        Assert.Equal("fig:4 married fig:5 at set:8.", Narration.Render(marriage, Name));
        Assert.Equal("Married fig:5 at set:8.", Narration.Render(marriage, Name, ruler));
        Assert.Equal("Married fig:4 at set:8.", Narration.Render(marriage, Name, spouse));
    }

    [Fact]
    public void ANamedWitnessDoesNotStealTheActorsLine()
    {
        EntityId victor = EntityId.Figure(1);
        EntityId other = EntityId.Figure(2);
        var battle = new HistoryEvent(
            0, 90, EventKind.BattleFought, EntityId.Battle(3), EntityId.Civilization(4), default,
            Extra: new[] { victor, other },
            Data: Chronicle.Data(("victor", "fig:1"), ("losses", "400")));

        Assert.Equal(
            "Prevailed at the bat:3, at a cost of 400 dead.",
            Narration.Render(battle, Name, victor));
        Assert.Equal(
            "Was at the bat:3, which civ:4 won, at a cost of 400 dead.",
            Narration.Render(battle, Name, other));
    }

    /// <summary>Templates must not reference slots their emitting system never fills — spot-checked here.</summary>
    [Fact]
    public void TemplatesUseOnlyKnownPlaceholders()
    {
        var allowed = new[] { "subject", "object", "location", "self", "other" };

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
                    || token.StartsWith("data:", StringComparison.Ordinal)
                    || token.StartsWith("as:", StringComparison.Ordinal)
                    || token.StartsWith("not:", StringComparison.Ordinal)
                    || token.StartsWith("self:", StringComparison.Ordinal);

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
