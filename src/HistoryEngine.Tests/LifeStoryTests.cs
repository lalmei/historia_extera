using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.Serialization;
using HistoryEngine.World;
using Xunit;
using Xunit.Abstractions;

namespace HistoryEngine.Tests;

/// <summary>The durable character state left behind by ordinary historical events.</summary>
public sealed class LifeStoryTests
{
    private static readonly ulong[] Seeds = { 2, 7, 11, 42, 99 };

    private readonly ITestOutputHelper _output;

    public LifeStoryTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void RolesAreCanonicalButDirectedStateAndProvenanceCanDiffer()
    {
        Figure patron = Person(0, "Alda");
        Figure client = Person(1, "Bera");
        EntityId court = EntityId.Settlement(2);

        LifeStories.AddPatronage(patron, client, 12, court);
        LifeStories.AddPatronage(patron, client, 13, court);

        FigureBond toClient = Assert.Single(patron.Bonds);
        FigureBond toPatron = Assert.Single(client.Bonds);
        Assert.True(toClient.Kinds.HasFlag(BondKind.Patron));
        Assert.True(toPatron.Kinds.HasFlag(BondKind.Client));
        Assert.True(toClient.Obligation < toPatron.Obligation);
        Assert.Equal(12, toClient.SinceYear);
        Assert.Equal(EventKind.OfficeGranted, toClient.OriginEventKind);
        Assert.Equal(client.Id, toClient.OriginEntityId);
        Assert.Equal(court, toClient.OriginLocationId);
        Assert.Equal(13, toClient.LastChangedYear);

        LifeStories.AddRivalry(
            patron, client, 20, EventKind.OfficeRevoked, court, 0.50, patron.Id);

        Assert.Single(patron.Bonds);
        Assert.Single(client.Bonds);
        Assert.True(toClient.Kinds.HasFlag(BondKind.Rival));
        Assert.True(toPatron.Kinds.HasFlag(BondKind.Rival));
        Assert.NotEqual(toClient.Grievance, toPatron.Grievance);
        Assert.Equal(EventKind.OfficeRevoked, toClient.LastEventKind);
        Assert.Equal(patron.Id, toClient.LastEntityId);
        Assert.Equal(court, toClient.LastLocationId);
    }

    [Fact]
    public void BondsAndMemoriesChangeLaterDecisionScores()
    {
        Figure candidate = Person(0, "Alda");
        Figure target = Person(1, "Bera");

        LifeStories.AddRivalry(
            candidate, target, 10, EventKind.OfficeRevoked, grievance: 0.65);
        candidate.Memories.Clear();
        double withBond = Conspiracies.Motive(candidate, target, claimant: false, year: 10);
        candidate.Bonds.Clear();
        double withoutBond = Conspiracies.Motive(candidate, target, claimant: false, year: 10);
        Assert.True(withBond > withoutBond);

        LifeStories.Remember(
            candidate, MemoryKind.Humiliation, 10, EventKind.OfficeRevoked, target.Id,
            intensity: 0.80);
        double withMemory = Conspiracies.Motive(candidate, target, claimant: false, year: 10);
        candidate.Memories.Clear();
        Assert.True(withMemory > Conspiracies.Motive(candidate, target, false, 10));

        candidate = Person(0, "Alda", aggression: 0.5, piety: 0.9, tradition: 0.8);
        Assert.Equal(0.0, Undertakings.BereavementVowChance(candidate, target, 10));
        LifeStories.Remember(
            candidate, MemoryKind.Bereavement, 10, EventKind.FigureDied, target.Id,
            intensity: 0.80);
        Assert.True(Undertakings.BereavementVowChance(candidate, target, 10) > 0.0);
    }

    [Fact]
    public void DispositionInterpretsTheSameExperienceWithoutRandomness()
    {
        Figure cautious = Person(0, "Alda", aggression: 0.0);
        Figure martial = Person(1, "Bera", aggression: 1.0);
        EntityId battle = EntityId.Battle(3);

        LifeStories.Remember(
            cautious, MemoryKind.Defeat, 20, EventKind.BattleFought, battle, intensity: 0.80);
        LifeStories.Remember(
            martial, MemoryKind.Defeat, 20, EventKind.BattleFought, battle, intensity: 0.80);

        FeelingState cautiousFeelings = LifeStories.Feelings(cautious, 20);
        FeelingState martialFeelings = LifeStories.Feelings(martial, 20);

        Assert.True(cautiousFeelings.Fear > martialFeelings.Fear);
        Assert.True(martialFeelings.Anger > cautiousFeelings.Anger);
    }

    [Fact]
    public void EveryBondInALongWorldHasAReciprocalBond()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard(42)).World;

        foreach (Figure figure in world.Figures)
        {
            Assert.Equal(
                figure.Bonds.Count,
                figure.Bonds.Select(bond => bond.OtherId).Distinct().Count());

            foreach (FigureBond bond in figure.Bonds)
            {
                Assert.True(world.Figures.Contains(bond.OtherId));
                Assert.False(bond.OriginEntityId.IsNone);
                Assert.False(bond.LastEntityId.IsNone);

                Figure other = world.Figures[bond.OtherId];
                FigureBond? reciprocal = LifeStories.BondTo(other, figure.Id);

                Assert.NotNull(reciprocal);
                Assert.InRange(bond.Affection, -1.0, 1.0);
                Assert.InRange(bond.Trust, -1.0, 1.0);
                Assert.InRange(bond.Obligation, 0.0, 1.0);
                Assert.InRange(bond.Fear, 0.0, 1.0);
                Assert.InRange(bond.Grievance, 0.0, 1.0);

                if (bond.Kinds.HasFlag(BondKind.Spouse))
                {
                    Assert.True(reciprocal!.Kinds.HasFlag(BondKind.Spouse));
                }
                if (bond.Kinds.HasFlag(BondKind.Parent))
                {
                    Assert.True(reciprocal!.Kinds.HasFlag(BondKind.Child));
                }
                if (bond.Kinds.HasFlag(BondKind.Patron))
                {
                    Assert.True(reciprocal!.Kinds.HasFlag(BondKind.Client));
                }
                if (bond.Kinds.HasFlag(BondKind.Sibling))
                {
                    Assert.True(reciprocal!.Kinds.HasFlag(BondKind.Sibling));
                }
            }
        }
    }

    [Fact]
    public void FamilyPatronageMentorshipAndRivalryAllLeaveCausalState()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard(7)).World;

        Assert.Contains(world.Figures, figure =>
            figure.Bonds.Exists(bond => bond.Kinds.HasFlag(BondKind.Spouse)));
        Assert.Contains(world.Figures, figure =>
            figure.Bonds.Exists(bond => bond.Kinds.HasFlag(BondKind.Parent)));
        Assert.Contains(world.Figures, figure =>
            figure.Bonds.Exists(bond => bond.Kinds.HasFlag(BondKind.Sibling)));
        Assert.Contains(world.Figures, figure =>
            figure.Bonds.Exists(bond => bond.Kinds.HasFlag(BondKind.Client)));
        Assert.Contains(world.Figures, figure =>
            figure.Bonds.Exists(bond => bond.Kinds.HasFlag(BondKind.Apprentice)));
        Assert.Contains(world.Figures, figure =>
            figure.Bonds.Exists(bond => bond.Kinds.HasFlag(BondKind.Rival)));

        Assert.Contains(world.Figures, figure =>
            figure.Memories.Exists(memory => memory.Kind == MemoryKind.Bereavement));
        Assert.Contains(world.Figures, figure =>
            figure.Memories.Exists(memory => memory.Kind == MemoryKind.Mentorship));
        Assert.Contains(world.Figures, figure =>
            figure.Memories.Exists(memory => memory.Kind == MemoryKind.Humiliation));

        var indexedDeaths = new HashSet<(EntityId Deceased, EntityId Survivor)>();
        foreach (HistoryEvent entry in world.Chronicle.Events)
        {
            if (entry.Kind != EventKind.FigureDied || entry.Extra is null) continue;
            foreach (EntityId indexed in entry.Extra)
            {
                if (indexed.Kind == EntityKind.Figure)
                {
                    indexedDeaths.Add((entry.Subject, indexed));
                }
            }
        }

        foreach (Figure survivor in world.Figures)
        {
            foreach (SalientMemory memory in survivor.Memories)
            {
                Assert.True(!memory.AboutId.IsNone || !memory.LocationId.IsNone);
                if (memory.Kind != MemoryKind.Bereavement) continue;
                Assert.Contains((memory.AboutId, survivor.Id), indexedDeaths);
            }
        }
    }

    [Fact]
    public void MemoryIsBoundedAndReinforcementKeepsAFormativeExperience()
    {
        var figure = new Figure(
            EntityId.Figure(0), EntityId.Civilization(0), EntityId.Culture(0),
            "Alda", Sex.Female, 0);

        for (int i = 0; i < LifeStories.MemoryCapacity; i++)
        {
            LifeStories.Remember(
                figure,
                MemoryKind.Journey,
                i,
                EventKind.JourneyMade,
                EntityId.Figure(i + 1),
                intensity: 0.4);
        }

        LifeStories.Remember(
            figure,
            MemoryKind.Journey,
            20,
            EventKind.JourneyMade,
            EntityId.Figure(1),
            intensity: 0.9);

        LifeStories.Remember(
            figure,
            MemoryKind.Bereavement,
            21,
            EventKind.FigureDied,
            EntityId.Figure(99),
            intensity: 0.8);

        Assert.Equal(LifeStories.MemoryCapacity, figure.Memories.Count);
        Assert.Contains(figure.Memories, memory => memory.AboutId == EntityId.Figure(1));
        Assert.Contains(figure.Memories, memory => memory.AboutId == EntityId.Figure(99));
        Assert.Throws<ArgumentException>(() => LifeStories.Remember(
            figure, MemoryKind.Humiliation, 22, EventKind.OfficeRevoked));
    }

    [Fact]
    public void BondsAndActiveMemoriesStaySparseAcrossTheStandardSeedPanel()
    {
        ulong[] seeds = { 2, 7, 11, 42, 99 };
        var categories = new HashSet<MemoryKind>();

        foreach (ulong seed in seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;
            List<Figure> adults = world.Figures.Where(figure =>
                (figure.DeathYear ?? world.EndYear) - figure.BirthYear
                    >= Succession.MajorityAge).ToList();
            int bonds = adults.Sum(figure => figure.Bonds.Count);
            int maxDegree = adults.Max(figure => figure.Bonds.Count);
            int memories = adults.Sum(figure => figure.Memories.Count);
            int activeFeelings = adults.Count(figure =>
            {
                FeelingState feelings = LifeStories.Feelings(
                    figure, figure.DeathYear ?? world.EndYear);
                return feelings.Grief > 0.0
                    || feelings.Fear > 0.0
                    || feelings.Anger > 0.0
                    || feelings.Pride > 0.0
                    || feelings.Loyalty > 0.0;
            });

            foreach (Figure figure in adults)
            {
                foreach (SalientMemory memory in figure.Memories) categories.Add(memory.Kind);
            }

            double meanDegree = (double)bonds / adults.Count;
            double memoriesPerAdult = (double)memories / adults.Count;
            double feelingPrevalence = (double)activeFeelings / adults.Count;
            _output.WriteLine(
                $"seed {seed}: adults={adults.Count}, mean-degree={meanDegree:F2}, "
                + $"max-degree={maxDegree}, memories/adult={memoriesPerAdult:F2}, "
                + $"active-feelings={feelingPrevalence:P1}");

            Assert.InRange(meanDegree, 0.25, 20.0);
            Assert.True(
                maxDegree < Math.Max(20, adults.Count / 10),
                $"Seed {seed} produced a courtier with {maxDegree} bonds among {adults.Count} adults.");
            Assert.InRange(memoriesPerAdult, 0.25, LifeStories.MemoryCapacity);
            Assert.InRange(feelingPrevalence, 0.01, 0.95);
        }

        Assert.True(categories.Count >= 6, $"Only {categories.Count} memory categories appeared.");
    }

    [Fact]
    public void BattlesLeaveWoundsMemoriesAndRelationshipsThatOutlastThem()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard(42)).World;
        var wounded = world.Figures.Where(figure => figure.Injuries.Count > 0).ToList();

        Assert.NotEmpty(wounded);
        Assert.All(wounded, figure =>
        {
            Assert.All(figure.Injuries, injury =>
            {
                // One wound model, three ways to get one. A battle or a storming points at the
                // battle, a quarrel points at the person who dealt it, and a calamity points at
                // the town it fell on. None of them may point nowhere.
                Assert.True(
                    injury.SourceKind switch
                    {
                        EventKind.DuelFought => world.Figures.Contains(injury.CauseId),
                        EventKind.DisasterStruck => world.Settlements.Contains(injury.CauseId),
                        _ => world.Battles.Contains(injury.CauseId),
                    },
                    $"{figure.FullName} carries a wound from {injury.SourceKind} whose cause "
                    + "is not in the world.");
                Assert.True(injury.RecoveryYear > injury.Year);
            });
        });

        Assert.Equal(
            wounded.Sum(figure => figure.Injuries.Count),
            world.Chronicle.Events.Count(entry => entry.Kind == EventKind.FigureWounded));

        Assert.Contains(world.Figures, figure =>
            figure.Bonds.Exists(bond => bond.Kinds.HasFlag(BondKind.Companion)));
        Assert.Contains(world.Figures, figure =>
            figure.Bonds.Exists(bond => bond.Kinds.HasFlag(BondKind.Rival)));
    }

    [Fact]
    public void EveryJourneyIsAStepInAnUndertakingWithACausalEnding()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard(42)).World;
        int journeys = world.Figures.Sum(figure => figure.Journeys.Count);
        int journeySteps = world.Figures.Sum(figure =>
            figure.Undertakings.Sum(undertaking =>
                undertaking.Steps.Count(step =>
                    step.SourceKind is EventKind.JourneyMade or EventKind.JourneyWaylaid)));

        Assert.True(journeys > 40);
        Assert.Equal(journeys, journeySteps);
        Assert.Contains(world.Figures, figure =>
            figure.Undertakings.Exists(undertaking => undertaking.Steps.Count >= 2));
        Assert.Contains(world.Figures, figure =>
            figure.Undertakings.Exists(undertaking =>
                undertaking.State == UndertakingState.Succeeded));
        Assert.Contains(world.Figures, figure =>
            figure.Undertakings.Exists(undertaking =>
                undertaking.Motive == MemoryKind.Bereavement));
    }

    [Fact]
    public void TravelAndRevengeUndertakingsReachCausalTerminalStatesAcrossSeeds()
    {
        var states = new Dictionary<(UndertakingKind Kind, UndertakingState State), int>();
        int eventVolume = 0;
        int durations = 0;
        int durationTotal = 0;
        int undertakingTotal = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;
            eventVolume += world.Chronicle.Events.Count(entry =>
                entry.Kind is EventKind.UndertakingStarted
                    or EventKind.UndertakingCompleted
                    or EventKind.UndertakingFailed);

            foreach (Figure figure in world.Figures)
            {
                Assert.False(!figure.IsAlive && figure.Undertakings.Exists(item =>
                    item.State == UndertakingState.Active));
                Assert.True(figure.Undertakings.Count(item =>
                    item.State == UndertakingState.Active) <= Undertakings.MaxActive);

                List<FigureUndertaking> publicArcs = figure.Undertakings
                    .OrderBy(item => item.StartYear)
                    .ToList();
                for (int i = 1; i < publicArcs.Count; i++)
                {
                    if (publicArcs[i - 1].EndYear is int ended)
                    {
                        Assert.True(
                            publicArcs[i].StartYear - ended >= Undertakings.CooldownYears,
                            $"{figure.FullName} began public undertakings without a cooldown.");
                    }
                }

                foreach (FigureUndertaking undertaking in figure.Undertakings)
                {
                    undertakingTotal++;
                    states[(undertaking.Kind, undertaking.State)] =
                        states.GetValueOrDefault((undertaking.Kind, undertaking.State)) + 1;
                    Assert.False(undertaking.MotiveEntityId.IsNone);
                    Assert.True(undertaking.DeadlineYear >= undertaking.StartYear);

                    int priorYear = undertaking.StartYear;
                    var unique = new HashSet<UndertakingStep>();
                    foreach (UndertakingStep step in undertaking.Steps)
                    {
                        Assert.True(step.Year >= priorYear);
                        Assert.False(step.PlaceId.IsNone && step.SubjectId.IsNone);
                        Assert.True(unique.Add(step), "An undertaking duplicated a causal step.");
                        priorYear = step.Year;
                    }

                    if (undertaking.State != UndertakingState.Active)
                    {
                        Assert.NotNull(undertaking.EndYear);
                        Assert.False(string.IsNullOrWhiteSpace(undertaking.Outcome));
                        durations++;
                        durationTotal += undertaking.EndYear!.Value - undertaking.StartYear;
                    }
                }
            }
        }

        Assert.True(states.GetValueOrDefault((UndertakingKind.TradeVenture, UndertakingState.Succeeded)) > 0);
        Assert.True(states.GetValueOrDefault((UndertakingKind.TradeVenture, UndertakingState.Failed)) > 0);
        Assert.True(states.GetValueOrDefault((UndertakingKind.Revenge, UndertakingState.Succeeded)) > 0);
        Assert.True(states.GetValueOrDefault((UndertakingKind.Revenge, UndertakingState.Failed)) > 0);
        Assert.Contains(states, pair =>
            pair.Key.State == UndertakingState.Abandoned && pair.Value > 0);
        Assert.True(eventVolume > 0);
        Assert.True(durations > 0 && durationTotal >= 0);
        _output.WriteLine(
            $"Five-seed undertakings: {undertakingTotal}; terminal {durations}; "
            + $"mean terminal duration {(double)durationTotal / durations:F2} years; "
            + $"undertaking events {eventVolume}.");
        foreach (UndertakingKind kind in new[]
                 {
                     UndertakingKind.TradeVenture,
                     UndertakingKind.Revenge,
                 })
        {
            _output.WriteLine(
                $"{kind}: active {states.GetValueOrDefault((kind, UndertakingState.Active))}, "
                + $"succeeded {states.GetValueOrDefault((kind, UndertakingState.Succeeded))}, "
                + $"failed {states.GetValueOrDefault((kind, UndertakingState.Failed))}, "
                + $"abandoned {states.GetValueOrDefault((kind, UndertakingState.Abandoned))}.");
        }
    }

    [Fact]
    public void UndertakingStepsRejectDuplicatesReorderingAndImpossibleEntities()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard(42)).World;
        Settlement place = world.Settlements[0];
        var undertaking = new FigureUndertaking(
            999,
            UndertakingKind.TradeVenture,
            world.StartYear,
            "a test venture",
            place.Id,
            place.Id,
            place.Id,
            2,
            MemoryKind.Ambition,
            place.Id,
            EventKind.JourneyMade,
            world.StartYear + 5);
        var first = new UndertakingStep(
            world.StartYear,
            EventKind.JourneyMade,
            place.Id,
            place.Id,
            "Began");

        Undertakings.AddStep(world, undertaking, first);
        Assert.Throws<InvalidOperationException>(() =>
            Undertakings.AddStep(world, undertaking, first));
        Assert.Throws<InvalidOperationException>(() =>
            Undertakings.AddStep(
                world,
                undertaking,
                first with { Year = world.StartYear - 1, Outcome = "Out of order" }));
        Assert.Throws<InvalidOperationException>(() =>
            Undertakings.AddStep(
                world,
                undertaking,
                first with
                {
                    SubjectId = EntityId.Settlement(int.MaxValue),
                    Outcome = "Impossible",
                }));

        Figure actor = world.Figures[0];
        var officeBound = new FigureUndertaking(
            1000,
            UndertakingKind.Revenge,
            world.EndYear,
            "an office-bound test goal",
            place.CivilizationId,
            place.Id,
            place.Id,
            2,
            MemoryKind.Defeat,
            place.Id,
            EventKind.BattleFought,
            world.EndYear + 5,
            requiredOffice: OfficeKind.Marshal);
        actor.Undertakings.Add(officeBound);

        Undertakings.EndAtLossOfOffice(world, actor, OfficeKind.Marshal, world.EndYear);

        Assert.Equal(UndertakingState.Abandoned, officeBound.State);
        Assert.Equal("the loss of office ended it", officeBound.Outcome);
    }

    [Fact]
    public void BereavementDoesNotStartASecondPublicUndertaking()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard(1630161754)).World;

        Assert.DoesNotContain(world.Figures, figure =>
            figure.Undertakings.Count(item => item.State == UndertakingState.Active) > 1);
    }

    [Fact]
    public void ExportCarriesTheCausalSummaryUsedByTheLifePage()
    {
        HistoryRun run = HistoryRun.Execute(TestWorlds.Standard(42));
        WorldExport export = run.ToExport();
        ExportFigure figure = export.Figures.First(item =>
            item.Bonds.Count > 0
            && item.Memories.Count > 0
            && item.Undertakings.Count > 0);

        Assert.Equal(WorldExport.CurrentSchemaVersion, export.SchemaVersion);
        Assert.NotEmpty(figure.Bonds);
        Assert.NotEmpty(figure.Memories);
        Assert.NotEmpty(figure.Undertakings);
        Assert.All(figure.Bonds, bond =>
        {
            Assert.NotNull(bond.OriginEntityId);
            Assert.NotNull(bond.LastEntityId);
        });
        Assert.All(figure.Memories, memory => Assert.InRange(memory.Intensity, 0.0, 1.0));
        Assert.Equal(
            figure.Journeys.Count,
            figure.Undertakings.Sum(undertaking =>
                undertaking.Steps.Count(step =>
                    step.SourceKind is EventKind.JourneyMade or EventKind.JourneyWaylaid)));

        for (int i = 0; i < export.Figures.Count; i++)
        {
            Figure source = run.World.Figures[i];
            ExportFigure carried = export.Figures[i];
            int at = source.DeathYear ?? run.World.EndYear;

            List<SalientMemory> visible = source.Memories.Where(memory =>
                LifeStories.IsActive(memory, at) || LifeStories.IsFormative(memory)).ToList();
            Assert.Equal(visible.Count, carried.Memories.Count);
            for (int j = 0; j < visible.Count; j++)
            {
                Assert.Equal(
                    LifeStories.EffectiveIntensity(visible[j], at),
                    carried.Memories[j].Intensity,
                    12);
                Assert.Equal(visible[j].Valence, carried.Memories[j].Valence);
                Assert.Equal(LifeStories.IsActive(visible[j], at), carried.Memories[j].Active);
            }
        }
    }

    private static Figure Person(
        int id,
        string name,
        double aggression = 0.5,
        double piety = 0.5,
        double tradition = 0.5)
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
                    Mercantile: 0.5,
                    Learning: 0.5),
                Centralism: 0.5,
                Independence: 0.5),
        };
    }
}
