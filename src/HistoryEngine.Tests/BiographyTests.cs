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

    [Fact]
    public void SharedFixturesMatchViewerSignatures()
    {
        foreach (BiographyFixtureCase fixture in BiographyFixtures.Load())
        {
            Assert.Equal(fixture.Expected, BiographyFixtures.Signatures(fixture));
        }
    }

    [Fact]
    public void IndependenceAndFrontierResidenceRootednessUsesChosenHomeProse()
    {
        Figure figure = Person(0, "Eira", tradition: 0.4, independence: 0.72);
        figure.Residences.Add(new Residence(EntityId.Settlement(3), 20, ResidenceReason.Settled));
        BiographyInterpretation rooted = Selected(figure, 60)
            .First(i => i.Theme == BiographyTheme.Rootedness);
        Assert.True(rooted.Dials.ContainsKey(BiographyDial.Independence));
        string prose = BiographyBuilder.Build(Named(figure, 60, ("set:3", "Ashfen"))).Prose;
        Assert.Contains("Ashfen", prose, StringComparison.Ordinal);
        Assert.Matches("made a home|chosen rather than inherited|on her own terms", prose);
        Assert.DoesNotContain("established ways", prose, StringComparison.Ordinal);
    }

    [Fact]
    public void FirstApparitionProducesDiscoveryAndScholarlyProse()
    {
        Figure figure = Person(0, "Alda", learning: 0.75);
        figure.Observations.Add(new SkyObservation(
            0, 30, EntityId.Civilization(0), EntityId.Settlement(1), null, ApparitionGrade.Notable));
        IReadOnlyList<BiographyEvidence> evidence = EvidenceExtractor.Extract(Context(figure, 50));
        Assert.Contains(evidence, e => e.Kind == EvidenceKind.Discovery);
        Assert.Contains(Selected(figure, 50), i => i.Theme == BiographyTheme.ScholarlyLife
            && i.Evidence.Any(e => e.Kind == EvidenceKind.Discovery));
        string prose = BiographyBuilder.Build(Named(figure, 50, ("set:1", "Ashfen"))).Prose;
        Assert.Matches("sighting|sky|apparition", prose);
    }

    [Fact]
    public void BiographyCanCarryThreeInterpretations()
    {
        Figure figure = Person(0, "Sveinus", tradition: 0.9, aggression: 0.8);
        figure.Residences.Add(new Residence(EntityId.Settlement(1), 10, ResidenceReason.Birth));
        Figure friend = Person(1, "Ragny");
        Figure rival = Person(2, "Bera");
        var affinity = new FigureAffinity(
            1,
            figure.Id,
            friend.Id,
            17,
            AffinityOrigin.SharedResidence,
            EventKind.FigureBorn,
            figure.Id,
            EntityId.Settlement(1));
        affinity.Stage = AffinityStage.Friendship;
        affinity.Acts.Add(new AffinityAct(17, EventKind.FigureBorn, AffinityStage.Friendship, figure.Id, "friendship"));
        figure.Affinities.Add(affinity);
        figure.Disputes.Add(new FigureDispute(
            1,
            figure.Id,
            rival.Id,
            30,
            DisputeCause.PassedOverForOffice,
            EventKind.OfficeRevoked,
            rival.Id,
            EntityId.Settlement(2)));

        FigureBiography biography = BiographyBuilder.Build(Named(
            figure,
            60,
            ("set:1", "Ashfen"),
            ("fig:1", "Ragny"),
            ("fig:2", "Bera")));
        Assert.Equal(3, biography.Interpretations.Count);
        Assert.Contains(biography.Interpretations, i => i.Theme == BiographyTheme.Rootedness);
        Assert.Contains(biography.Interpretations, i => i.Theme == BiographyTheme.EnduringLoyalty);
        Assert.Contains(biography.Interpretations, i => i.Theme == BiographyTheme.ViolentConflict);
        Assert.Matches("rooted in|closely tied|Familiar places|made a home", biography.Prose);
        Assert.Matches("Ragny|stood by|kept faith", biography.Prose);
        Assert.Matches("Bera|quarrel|feud|dispute", biography.Prose);
    }

    [Fact]
    public void ScholarlyLifeAndReligiousThemesFire()
    {
        Figure scholar = Person(0, "Alda", learning: 0.75);
        scholar.Occupation = Occupation.Scribe;
        Assert.Contains(Selected(scholar, 55), i => i.Theme == BiographyTheme.ScholarlyLife);

        Figure devout = Person(1, "Bera", piety: 0.8);
        devout.Occupation = Occupation.Clergy;
        devout.Journeys.Add(new Journey(
            JourneyKind.Pilgrimage,
            new Stamp(30, 0),
            EntityId.Settlement(1),
            EntityId.Settlement(9),
            EntityId.None,
            20,
            new Stamp(31, 0)));
        IReadOnlyList<BiographyInterpretation> faith = Selected(devout, 50);
        Assert.Contains(faith, i => i.Theme == BiographyTheme.ReligiousDevotion);
        string prose = BiographyBuilder.Build(Named(devout, 50, ("set:9", "Ilen"))).Prose;
        Assert.Contains("Ilen", prose, StringComparison.Ordinal);

        Figure both = Person(2, "Cera", piety: 0.75, learning: 0.75);
        both.Occupation = Occupation.Clergy;
        both.Observations.Add(new SkyObservation(
            0, 30, EntityId.Civilization(0), EntityId.Settlement(1), null, ApparitionGrade.Notable));
        Assert.Contains(
            Selected(both, 50, taken: new[] { 40 }),
            i => i.Theme == BiographyTheme.ReligiousScholarship);
    }

    [Fact]
    public void PowerMobilityAndBetrayalThemesFire()
    {
        Figure ruler = Person(0, "Alda", centralism: 0.75);
        ruler.Offices.Add(new OfficeHolding(OfficeKind.Ruler, "Consul", EntityId.Civilization(0), 20, null));
        Assert.Contains(Selected(ruler, 50), i => i.Theme == BiographyTheme.ConsolidationOfPower);

        Figure servant = Person(1, "Bera", centralism: 0.58);
        servant.Offices.Add(new OfficeHolding(OfficeKind.Governor, "Governor", EntityId.Civilization(0), 20, null));
        Assert.Contains(Selected(servant, 50), i => i.Theme == BiographyTheme.InstitutionalService);

        Figure passed = Person(2, "Cera", independence: 0.7);
        Figure rival = Person(3, "Dera");
        passed.Disputes.Add(new FigureDispute(
            1,
            passed.Id,
            rival.Id,
            30,
            DisputeCause.PassedOverForOffice,
            EventKind.OfficeRevoked,
            rival.Id,
            EntityId.Settlement(2)));
        Assert.Contains(Selected(passed, 45), i => i.Theme == BiographyTheme.ResistanceToAuthority);

        Figure migrant = Person(4, "Eira", independence: 0.72);
        migrant.Residences.Add(new Residence(EntityId.Settlement(3), 35, ResidenceReason.Flight));
        IReadOnlyList<BiographyInterpretation> moved = Selected(migrant, 50);
        Assert.Contains(moved, i => i.Theme == BiographyTheme.FrontierLife);
        Assert.Contains(moved, i => i.Theme == BiographyTheme.Isolation);

        Figure captain = Person(5, "Fara", expansionism: 0.75);
        captain.Campaigns.Add(new CampaignMemory(
            EntityId.War(1), EntityId.Battle(1), EntityId.Civilization(0), 40, CampaignRole.Commanded));
        Assert.Contains(Selected(captain, 50), i => i.Theme == BiographyTheme.TerritorialAmbition);

        Figure betrayer = Person(6, "Gara", tradition: 0.7);
        Figure betrayed = Person(7, "Hara");
        betrayer.Betrayals.Add(new FigureBetrayal(
            1,
            betrayer.Id,
            betrayed.Id,
            40,
            BetrayalTie.Friendship,
            BetrayalCause.Grievance,
            EventKind.OfficeRevoked,
            EntityId.Settlement(1)));
        IReadOnlyList<BiographyInterpretation> broken = Selected(betrayer, 50);
        Assert.Contains(broken, i => i.Theme == BiographyTheme.Betrayal);
        Assert.Equal(BiographyOutcome.Negative, broken.First(i => i.Theme == BiographyTheme.Betrayal).Outcome);
        string turned = BiographyBuilder.Build(Named(betrayer, 50, ("fig:7", "Hara"))).Prose;
        Assert.Contains("Hara", turned, StringComparison.Ordinal);
        Assert.Contains("turned on", turned, StringComparison.Ordinal);
    }

    [Fact]
    public void MalePronounsAppearInRootednessProse()
    {
        Figure figure = Person(0, "Oswin", tradition: 0.82, sex: Sex.Male);
        figure.Residences.Add(new Residence(EntityId.Settlement(1), 10, ResidenceReason.Birth));
        string prose = BiographyBuilder.Build(Named(figure, 60, ("set:1", "Ashfen"))).Prose;
        Assert.Contains("Ashfen", prose, StringComparison.Ordinal);
        Assert.DoesNotContain("her", prose, StringComparison.Ordinal);
    }

    [Fact]
    public void RecentOccupationTakenDoesNotCountAsLongOccupation()
    {
        Figure figure = Person(0, "Alda", learning: 0.8);
        figure.Occupation = Occupation.Scribe;
        Assert.Contains(
            EvidenceExtractor.Extract(Context(figure, 60)),
            e => e.Kind == EvidenceKind.LongOccupation);

        var recent = new BiographyContext(
            figure, 60, id => id.ToString(), occupationTakenYears: new[] { 50 });
        Assert.DoesNotContain(
            EvidenceExtractor.Extract(recent),
            e => e.Kind == EvidenceKind.LongOccupation);
    }

    private static IReadOnlyList<BiographyInterpretation> Selected(
        Figure figure,
        int year,
        int max = 3,
        IReadOnlyList<int>? taken = null)
    {
        var context = new BiographyContext(figure, year, id => id.ToString(), occupationTakenYears: taken);
        return InterpretationSelector.Select(
            InterpretationEngine.Evaluate(context, EvidenceExtractor.Extract(context), InclinationRules.All),
            max);
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

    private static BiographyContext Named(Figure figure, int year, params (string Id, string Name)[] names)
    {
        var map = names.ToDictionary(pair => pair.Id, pair => pair.Name);
        return new BiographyContext(
            figure,
            year,
            id => map.TryGetValue(id.ToString(), out string? name) ? name : figure.Name);
    }

    private static Figure Person(
        int id,
        string name,
        double aggression = 0.5,
        double expansionism = 0.5,
        double piety = 0.5,
        double tradition = 0.5,
        double mercantile = 0.5,
        double learning = 0.5,
        double centralism = 0.5,
        double independence = 0.5,
        Sex sex = Sex.Female)
    {
        return new Figure(
            EntityId.Figure(id),
            EntityId.Civilization(0),
            EntityId.Culture(0),
            name,
            sex,
            0)
        {
            Disposition = new Disposition(
                new CultureValues(
                    Aggression: aggression,
                    Expansionism: expansionism,
                    Piety: piety,
                    Tradition: tradition,
                    Mercantile: mercantile,
                    Learning: learning),
                Centralism: centralism,
                Independence: independence),
        };
    }
}
