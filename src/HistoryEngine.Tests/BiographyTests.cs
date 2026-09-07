using HistoryEngine.Biography;
using HistoryEngine.Biography.Evidence;
using HistoryEngine.Biography.Interpretation;
using HistoryEngine.Biography.Selection;
using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using Xunit;

namespace HistoryEngine.Tests;

public sealed class BiographyTests
{
    [Fact]
    public void LongResidenceAndTraditionProduceRootedness()
    {
        Figure figure = Person(0, "Sveinus", tradition: 0.82);
        figure.Residences.Add(new Residence(EntityId.Settlement(1), 10, ResidenceReason.Birth));

        var context = Context(figure, 60);
        IReadOnlyList<BiographyEvidence> evidence = EvidenceExtractor.Extract(context);
        Assert.Contains(evidence, e => e.Kind == EvidenceKind.LongResidence);

        IReadOnlyList<BiographyInterpretation> candidates =
            InterpretationEngine.Evaluate(context, evidence, InclinationRules.All);
        BiographyInterpretation? rooted = candidates
            .FirstOrDefault(c => c.Theme == BiographyTheme.Rootedness);
        Assert.NotNull(rooted);
        Assert.True(rooted.Score > 0.4);
    }

    [Fact]
    public void LongFriendshipProducesEnduringLoyalty()
    {
        Figure sveinus = Person(0, "Sveinus", tradition: 0.70);
        Figure ragny = Person(1, "Ragny");
        var affinity = new FigureAffinity(
            1,
            sveinus.Id,
            ragny.Id,
            17,
            AffinityOrigin.SharedResidence,
            EventKind.FigureBorn,
            sveinus.Id,
            EntityId.Settlement(1));
        affinity.Stage = AffinityStage.Friendship;
        affinity.Acts.Add(new AffinityAct(17, EventKind.FigureBorn, AffinityStage.Friendship, sveinus.Id, "friendship"));
        sveinus.Affinities.Add(affinity);

        var context = Context(sveinus, 60);
        IReadOnlyList<BiographyEvidence> evidence = EvidenceExtractor.Extract(context);
        Assert.Contains(evidence, e => e.Kind == EvidenceKind.Friendship);

        IReadOnlyList<BiographyInterpretation> selected = InterpretationSelector.Select(
            InterpretationEngine.Evaluate(context, evidence, InclinationRules.All),
            3);
        Assert.Contains(selected, i => i.Theme == BiographyTheme.EnduringLoyalty);
    }

    [Fact]
    public void FeudAndAggressionProduceViolentConflict()
    {
        Figure opener = Person(0, "Alda", aggression: 0.72);
        Figure rival = Person(1, "Bera");
        var dispute = new FigureDispute(
            1,
            opener.Id,
            rival.Id,
            30,
            DisputeCause.PassedOverForOffice,
            EventKind.OfficeRevoked,
            rival.Id,
            EntityId.Settlement(2));
        opener.Disputes.Add(dispute);

        var context = Context(opener, 45);
        IReadOnlyList<BiographyInterpretation> selected = InterpretationSelector.Select(
            InterpretationEngine.Evaluate(
                context,
                EvidenceExtractor.Extract(context),
                InclinationRules.All),
            3);
        Assert.Contains(selected, i => i.Theme == BiographyTheme.ViolentConflict);
    }

    [Fact]
    public void MentorshipAndLearningProduceTransmission()
    {
        Figure mentor = Person(0, "Alda", learning: 0.75);
        Figure apprentice = Person(1, "Bera");
        mentor.Mentorships.Add(new FigureMentorship(
            mentor.Id,
            apprentice.Id,
            20,
            CareerFamily.LettersOffice,
            EntityId.Settlement(1)));

        var context = Context(mentor, 55);
        IReadOnlyList<BiographyInterpretation> selected = InterpretationSelector.Select(
            InterpretationEngine.Evaluate(
                context,
                EvidenceExtractor.Extract(context),
                InclinationRules.All),
            3);
        Assert.Contains(selected, i => i.Theme == BiographyTheme.TransmissionOfKnowledge);
    }

    [Fact]
    public void SelectorSuppressesSimilarThemes()
    {
        var candidates = new List<BiographyInterpretation>
        {
            Interpretation(BiographyTheme.Rootedness, 0.9),
            Interpretation(BiographyTheme.Isolation, 0.85),
            Interpretation(BiographyTheme.EnduringLoyalty, 0.8),
        };

        IReadOnlyList<BiographyInterpretation> selected = InterpretationSelector.Select(candidates, 3);
        Assert.Equal(2, selected.Count);
        Assert.DoesNotContain(selected, i => i.Theme == BiographyTheme.Isolation);
    }

    [Fact]
    public void HighDialWithoutEvidenceProducesNothing()
    {
        Figure figure = Person(0, "Alda", tradition: 0.95);
        var context = Context(figure, 40);
        IReadOnlyList<BiographyInterpretation> selected = InterpretationSelector.Select(
            InterpretationEngine.Evaluate(
                context,
                EvidenceExtractor.Extract(context),
                InclinationRules.All),
            3);
        Assert.Empty(selected);
    }

    [Fact]
    public void BuilderProducesDeterministicProse()
    {
        Figure figure = Person(0, "Sveinus", tradition: 0.82);
        figure.Residences.Add(new Residence(EntityId.Settlement(1), 10, ResidenceReason.Birth));

        var context = Context(figure, 60);
        IRng rng1 = new Pcg32(42).Fork("biography", figure.Id.ToDiscriminator());
        IRng rng2 = new Pcg32(42).Fork("biography", figure.Id.ToDiscriminator());
        FigureBiography first = BiographyBuilder.Build(context, rng1);
        FigureBiography second = BiographyBuilder.Build(context, rng2);
        Assert.Equal(first.Prose, second.Prose);
        Assert.NotEmpty(first.Prose);
    }

    [Fact]
    public void TradeSuccessAndFailureYieldDifferentOutcomes()
    {
        Figure winner = Person(0, "Alda", mercantile: 0.7);
        winner.Undertakings.Add(TradeVenture(UndertakingState.Succeeded, 20, 28));
        Figure loser = Person(1, "Bera", mercantile: 0.7);
        loser.Undertakings.Add(TradeVenture(UndertakingState.Failed, 20, 28));

        var winnerContext = Context(winner, 35);
        var loserContext = Context(loser, 35);

        BiographyOutcome winnerOutcome = InterpretationEngine
            .Evaluate(winnerContext, EvidenceExtractor.Extract(winnerContext), InclinationRules.All)
            .First(i => i.Theme == BiographyTheme.CommercialSuccess)
            .Outcome;
        BiographyOutcome loserOutcome = InterpretationEngine
            .Evaluate(loserContext, EvidenceExtractor.Extract(loserContext), InclinationRules.All)
            .First(i => i.Theme == BiographyTheme.CommercialFailure)
            .Outcome;

        Assert.Equal(BiographyOutcome.Positive, winnerOutcome);
        Assert.Equal(BiographyOutcome.Negative, loserOutcome);
    }

    private static BiographyInterpretation Interpretation(BiographyTheme theme, double score) =>
        new(theme, new Dictionary<BiographyDial, double>(), score, Array.Empty<BiographyEvidence>(), BiographyOutcome.Neutral);

    private static FigureUndertaking TradeVenture(UndertakingState state, int start, int end) =>
        new(
            1,
            UndertakingKind.TradeVenture,
            start,
            "trade",
            EntityId.None,
            EntityId.Settlement(3),
            EntityId.None,
            2,
            MemoryKind.Journey,
            EntityId.None,
            EventKind.JourneyMade,
            end + 2)
        {
            State = state,
            EndYear = end,
        };

    private static BiographyContext Context(Figure figure, int year) =>
        new(figure, year, id => id.ToString());

    private static Figure Person(
        int id,
        string name,
        double aggression = 0.5,
        double piety = 0.5,
        double tradition = 0.5,
        double mercantile = 0.5,
        double learning = 0.5)
    {
        return new Figure(
            EntityId.Figure(id),
            EntityId.Civilization(0),
            EntityId.Culture(0),
            name,
            Sex.Female,
            0)
        {
            Disposition = new Disposition(
                new CultureValues(
                    Aggression: aggression,
                    Expansionism: 0.5,
                    Piety: piety,
                    Tradition: tradition,
                    Mercantile: mercantile,
                    Learning: learning),
                Centralism: 0.5,
                Independence: 0.5),
        };
    }
}
