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
                ("familyVerb", "was slain"),
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
    public void AnOrdinaryDeathReadsAsDeathRatherThanSlayingToItsFamily()
    {
        EntityId victim = EntityId.Figure(2);
        EntityId child = EntityId.Figure(5);
        var death = new HistoryEvent(
            0, 80, EventKind.FigureDied, victim, default, default,
            Extra: new[] { child },
            Data: Chronicle.Data(("cause", "old age"), ("familyVerb", "died")));

        Assert.Equal("fig:2 died, of old age.", Narration.Render(death, Name, child));
    }

    /// <summary>
    /// A birth is read by three people, and only one of them was born.
    /// </summary>
    /// <remarks>
    /// The <c>.self</c> template used to be ungated, so the child's sentence was handed to the
    /// parents as well: a mother of six read "Was born to Jaroslav" six times on her own page,
    /// once per child. Every viewpoint the event indexes needs its own clause.
    /// </remarks>
    [Fact]
    public void ABirthReadsDifferentlyForTheChildAndForEachParent()
    {
        EntityId child = EntityId.Figure(11);
        EntityId father = EntityId.Figure(4);
        EntityId mother = EntityId.Figure(7);
        var birth = new HistoryEvent(
            0, 140, EventKind.FigureBorn, child, father, EntityId.Settlement(6),
            Extra: new[] { mother },
            Data: Chronicle.Data(("child", "daughter")));

        Assert.Equal(
            "fig:11 was born to fig:7 and fig:4 in set:6.",
            Narration.Render(birth, Name));
        Assert.Equal(
            "Was born to fig:7 and fig:4 in set:6.",
            Narration.Render(birth, Name, child));
        Assert.Equal(
            "fig:7 bore him a daughter, fig:11, at set:6.",
            Narration.Render(birth, Name, father));
        Assert.Equal(
            "Bore fig:4 a daughter, fig:11, at set:6.",
            Narration.Render(birth, Name, mother));
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
                    || token.StartsWith("self:", StringComparison.Ordinal)
                    || IsKnownExtraSlot(token);

                Assert.True(valid, $"Template for {pair.Key} uses unknown placeholder '{token}'");
                i = close;
            }
        }
    }

    /// <summary>
    /// An <c>{extra:kind}</c> slot must name a kind prefix the engine actually issues.
    /// </summary>
    /// <remarks>
    /// The prefix is the whole of this slot's contract, so a typo in one is a clause that silently
    /// never renders — the failure mode this whole test exists to catch.
    /// </remarks>
    private static bool IsKnownExtraSlot(string token) =>
        token.StartsWith("extra:", StringComparison.Ordinal)
        && EntityKindExtensions.TryParsePrefix(token.Substring(6), out _);

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

    /// <summary>
    /// <c>{extra:kind}</c> picks the first entity of that kind out of the event's extra ids, and
    /// is absent when the event carries none — which is what lets one template hold several
    /// mutually exclusive clauses.
    /// </summary>
    /// <remarks>
    /// The journey line is the case it was added for: a trip's reason is a holy site for a
    /// pilgrim, a faith for a priest on circuit and a realm for a guest, and one template has to
    /// say all three without the viewer learning what a journey is.
    /// </remarks>
    [Fact]
    public void ExtraSlotResolvesByKindAndIsAbsentWhenTheKindIsNot()
    {
        var pilgrimage = new HistoryEvent(
            Id: 0,
            Year: 12,
            Kind: EventKind.JourneyMade,
            Subject: EntityId.Figure(1),
            Object: default,
            Location: EntityId.Settlement(2),
            Extra: new[] { EntityId.Settlement(3), EntityId.HolySite(4) },
            Data: Chronicle.Data(("purpose", "on pilgrimage to")));

        Assert.Equal(
            "fig:1 travelled to set:2, on pilgrimage to the hol:4.",
            Narration.Render(pilgrimage, Name));

        // The same template, an errand with no holy site in it: every clause whose kind is absent
        // drops, and the line stays grammatical.
        var trade = new HistoryEvent(
            Id: 1,
            Year: 12,
            Kind: EventKind.JourneyMade,
            Subject: EntityId.Figure(1),
            Object: default,
            Location: EntityId.Settlement(2),
            Extra: new[] { EntityId.Settlement(3), EntityId.TradeRoute(5) },
            Data: Chronicle.Data(("purpose", "on trade")));

        Assert.Equal(
            "fig:1 travelled to set:2, on trade along the rte:5.",
            Narration.Render(trade, Name));
    }

    [Fact]
    public void AnUndertakingReadsFromItsLeadersAndTargetsViewpoints()
    {
        EntityId mourner = EntityId.Figure(1);
        EntityId deceased = EntityId.Figure(2);
        var vow = new HistoryEvent(
            Id: 0,
            Year: 12,
            Kind: EventKind.UndertakingStarted,
            Subject: mourner,
            Object: deceased,
            Location: EntityId.Settlement(3),
            Data: Chronicle.Data(("objective", "a pilgrimage in memory of fig:2")));

        Assert.Equal(
            "Undertook a pilgrimage in memory of fig:2, bound for set:3.",
            Narration.Render(vow, Name, mourner));
        Assert.Equal(
            "fig:1 undertook a pilgrimage in memory of fig:2, bound for set:3.",
            Narration.Render(vow, Name, deceased));
    }

    [Fact]
    public void InheritedArtifactsDoNotImplyActionAfterTheFormerHoldersDeath()
    {
        var artifact = new EntityId(EntityKind.Artifact, 1);
        EntityId heir = EntityId.Figure(2);
        EntityId formerHolder = EntityId.Figure(3);
        var inheritance = new HistoryEvent(
            Id: 0,
            Year: 12,
            Kind: EventKind.ArtifactGiven,
            Subject: artifact,
            Object: heir,
            Location: EntityId.Settlement(4),
            Extra: new[] { formerHolder },
            Data: Chronicle.Data(("manner", "inherited with the crown")));

        Assert.Equal(
            "art:1 passed from them to fig:2 at set:4, inherited with the crown.",
            Narration.Render(inheritance, Name, formerHolder));
    }

    /// <summary>An unknown kind prefix resolves to nothing rather than throwing.</summary>
    /// <remarks>
    /// Templates are data that ships in the export. A typo in one must degrade to a dropped
    /// segment, not to an exception in whatever is rendering a chronicle.
    /// </remarks>
    [Fact]
    public void AnUnknownExtraKindDropsItsSegment()
    {
        var entry = new HistoryEvent(
            Id: 0,
            Year: 12,
            Kind: EventKind.Unknown,
            Subject: EntityId.Figure(1),
            Object: default,
            Location: default,
            Extra: new[] { EntityId.HolySite(4) });

        Assert.Equal(string.Empty, Narration.RenderTemplate("[{extra:nope}]", entry, Name));
        Assert.Equal("hol:4", Narration.RenderTemplate("[{extra:hol}]", entry, Name));
    }
}
