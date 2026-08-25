using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.Serialization;
using HistoryEngine.World;
using Xunit;
using Xunit.Abstractions;

namespace HistoryEngine.Tests;

/// <summary>
/// Quarrels between two named people: what starts them, how far they go, and how they end.
/// </summary>
public sealed class DisputeTests
{
    /// <summary>
    /// Resampled when persistent conspiracies landed. A duel was always the rarest ending; more of
    /// the world's anger now runs through plots against people rank forbids anyone to face, so the
    /// panel is seeds that still carry a wound and a death in the current checkout.
    /// </summary>
    private static readonly ulong[] Seeds = { 11, 16, 22, 42, 43 };

    private readonly ITestOutputHelper _output;

    public DisputeTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Nobody quarrels with themselves, and nobody quarrels over nothing.
    /// </summary>
    /// <remarks>
    /// The second half is the one worth having. A model that can produce hostility between two
    /// people who merely exist in the same realm produces a great deal of it, and none of it means
    /// anything; requiring a bond that already carries a grievance is what makes every quarrel in
    /// the export traceable to a year and an event.
    /// </remarks>
    [Fact]
    public void QuarrelsNeedTwoPeopleAndARecordedGrievance()
    {
        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (FigureDispute dispute in All(world))
            {
                Assert.NotEqual(dispute.OpenerId, dispute.RivalId);
                Assert.True(world.Figures.Contains(dispute.OpenerId));
                Assert.True(world.Figures.Contains(dispute.RivalId));

                Figure opener = world.Figures[dispute.OpenerId];
                Figure rival = world.Figures[dispute.RivalId];

                Assert.True(
                    dispute.SourceKind is EventKind.OfficeRevoked
                        or EventKind.SuccessionDisputed
                        or EventKind.ConspiracyExposed
                        or EventKind.ConspiracyAttempted
                        or EventKind.RulerDeposed
                        or EventKind.SkyClaimRefuted
                        or EventKind.FigureDied,
                    $"Seed {seed}: a quarrel came from {dispute.SourceKind}, which is not one of "
                    + "the recorded causes.");
                Assert.False(dispute.SourceEntityId.IsNone);

                // Both were alive and grown when it started, and both were there to have it.
                Assert.True(opener.BirthYear + Succession.MajorityAge <= dispute.StartYear);
                Assert.True(rival.BirthYear + Succession.MajorityAge <= dispute.StartYear);
                Assert.True((opener.DeathYear ?? world.EndYear) >= dispute.StartYear);
                Assert.True((rival.DeathYear ?? world.EndYear) >= dispute.StartYear);

                Assert.NotEmpty(dispute.Acts);
                Assert.Equal(dispute.StartYear, dispute.Acts[0].Year);
            }
        }
    }

    /// <summary>A quarrel is one fact about two lives, and both of them carry the same one.</summary>
    [Fact]
    public void BothPartiesCarryTheSameEpisodeFromTheirOwnSide()
    {
        HistoryRun run = HistoryRun.Execute(TestWorlds.Standard(11));
        WorldState world = run.World;
        WorldExport export = run.ToExport();
        int checkedPairs = 0;

        foreach (FigureDispute dispute in All(world))
        {
            Figure opener = world.Figures[dispute.OpenerId];
            Figure rival = world.Figures[dispute.RivalId];

            Assert.Contains(dispute, opener.Disputes);
            Assert.Contains(dispute, rival.Disputes);
            Assert.Equal(rival.Id, dispute.Other(opener.Id));
            Assert.Equal(opener.Id, dispute.Other(rival.Id));

            ExportDispute fromOpener = Exported(export, opener, dispute);
            ExportDispute fromRival = Exported(export, rival, dispute);

            // The viewpoint differs; every fact under it is identical.
            Assert.True(fromOpener.Opened);
            Assert.False(fromRival.Opened);
            Assert.Equal(rival.Id, fromOpener.OtherId);
            Assert.Equal(opener.Id, fromRival.OtherId);
            Assert.Equal(fromOpener.Stage, fromRival.Stage);
            Assert.Equal(fromOpener.Outcome, fromRival.Outcome);
            Assert.Equal(fromOpener.Resolution, fromRival.Resolution);
            Assert.Equal(fromOpener.StartYear, fromRival.StartYear);
            Assert.Equal(fromOpener.EndYear, fromRival.EndYear);
            Assert.Equal(fromOpener.Acts.Count, fromRival.Acts.Count);
            checkedPairs++;
        }

        Assert.True(checkedPairs > 0, "Seed 11 produced no quarrel to read from both sides.");
    }

    /// <summary>
    /// No quarrel outlives the people in it, and none survives its own ending.
    /// </summary>
    [Fact]
    public void NoQuarrelContinuesPastDeathReconciliationOrDistance()
    {
        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (FigureDispute dispute in All(world))
            {
                Figure opener = world.Figures[dispute.OpenerId];
                Figure rival = world.Figures[dispute.RivalId];

                if (dispute.IsOpen)
                {
                    Assert.True(
                        opener.IsAlive && rival.IsAlive,
                        $"Seed {seed}: a quarrel begun in {dispute.StartYear} is still open with "
                        + "a dead party in it.");
                    Assert.Null(dispute.EndYear);
                    continue;
                }

                Assert.NotNull(dispute.EndYear);
                Assert.NotNull(dispute.Resolution);
                Assert.True(dispute.EndYear >= dispute.StartYear);

                // Nothing was done in its name after it ended.
                Assert.All(dispute.Acts, act => Assert.True(act.Year <= dispute.EndYear));
            }
        }
    }

    /// <summary>
    /// The ladder is climbed a rung at a time, and violence is only ever at the top of it.
    /// </summary>
    [Fact]
    public void EscalationIsOrderedAndBloodOnlyFollowsAChallenge()
    {
        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (FigureDispute dispute in All(world))
            {
                int previous = int.MinValue;
                DisputeStage reached = DisputeStage.Grudge;
                foreach (DisputeAct act in dispute.Acts)
                {
                    Assert.True(
                        act.Year >= previous,
                        $"Seed {seed}: a quarrel's acts are out of order at {act.Year}.");
                    Assert.True(
                        (int)act.Stage <= (int)reached + 1,
                        $"Seed {seed}: a quarrel jumped from {reached} to {act.Stage}.");
                    previous = act.Year;
                    if (act.Stage > reached) reached = act.Stage;
                }

                if (dispute.Outcome is DisputeOutcome.Wounded or DisputeOutcome.Killed)
                {
                    Assert.Equal(DisputeStage.Challenge, dispute.Stage);
                }
            }
        }
    }

    /// <summary>
    /// Escalation reads two people and a year, and nothing else in the world.
    /// </summary>
    /// <remarks>
    /// Run against the same pair twice, once with a crowd of unrelated people present. If any part
    /// of the roll came from a shared stream or from iteration order, the second run diverges — and
    /// that is exactly the failure that makes a quarrel's outcome an accident of who else was born.
    /// </remarks>
    [Fact]
    public void AnUnrelatedFigureCannotChangeHowAQuarrelGoes()
    {
        (List<string> alone, DisputeOutcome aloneOutcome) = Quarrel(bystanders: 0);
        (List<string> crowded, DisputeOutcome crowdedOutcome) = Quarrel(bystanders: 40);

        Assert.Equal(aloneOutcome, crowdedOutcome);
        Assert.Equal(alone, crowded);

        static (List<string> Acts, DisputeOutcome Outcome) Quarrel(int bystanders)
        {
            WorldState world = WorldBuilder.Create(TestWorlds.Standard(42));
            Civilization civilization = world.Civilizations[EntityId.Civilization(0)];

            Figure holder = Enrol(world, civilization, 100, "Adair", 0.80);
            Figure ruler = Enrol(world, civilization, 101, "Berran", 0.70);
            civilization.CurrentRulerId = ruler.Id;

            for (int i = 0; i < bystanders; i++)
            {
                Enrol(world, civilization, 200 + i, "Bystander" + i, 0.5);
            }

            LifeStories.AddRivalry(
                holder, ruler, 40, EventKind.OfficeRevoked, civilization.CapitalId, 0.62);
            Disputes.Consider(
                world,
                holder,
                ruler,
                DisputeCause.OfficeRevoked,
                EventKind.OfficeRevoked,
                civilization.Id,
                40);

            for (int year = 41; year <= 80; year++) Disputes.Tick(world, year);

            FigureDispute dispute = Assert.Single(holder.Disputes);
            var acts = new List<string>();
            foreach (DisputeAct act in dispute.Acts) acts.Add($"{act.Year}:{act.Stage}:{act.Detail}");
            return (acts, dispute.Outcome);
        }
    }

    /// <summary>
    /// Quarrels are rare, finite, and reach every ending the model has, across the seed panel.
    /// </summary>
    /// <remarks>
    /// The upper bound is the one this system most needs. A world in which two hundred courtiers
    /// are feuding is not a livelier world, it is a timeline in which nothing else is legible; the
    /// four causes are rare political events and the quarrel count should stay of their order.
    /// </remarks>
    [Fact]
    public void QuarrelsStayRareFiniteAndReachBothPeacefulAndViolentEndings()
    {
        var outcomes = new Dictionary<DisputeOutcome, int>();
        var causes = new Dictionary<DisputeCause, int>();
        int total = 0;
        int duels = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;
            List<FigureDispute> disputes = All(world);
            int adults = world.Figures.Count(figure =>
                (figure.DeathYear ?? world.EndYear) - figure.BirthYear >= Succession.MajorityAge);

            int longest = 0;
            foreach (FigureDispute dispute in disputes)
            {
                outcomes[dispute.Outcome] = outcomes.GetValueOrDefault(dispute.Outcome) + 1;
                causes[dispute.Cause] = causes.GetValueOrDefault(dispute.Cause) + 1;
                longest = Math.Max(
                    longest, (dispute.EndYear ?? world.EndYear) - dispute.StartYear);
                total++;
            }

            int lines = world.Chronicle.Events.Count(entry => entry.Kind is EventKind.DisputeOpened
                or EventKind.DisputeEscalated
                or EventKind.DisputeSettled
                or EventKind.DuelFought);
            duels += world.Chronicle.Events.Count(entry => entry.Kind == EventKind.DuelFought);

            _output.WriteLine(
                $"seed {seed}: adults={adults}, quarrels={disputes.Count}, longest={longest}y, "
                + $"lines={lines} of {world.Chronicle.Events.Count} events "
                + $"({(double)lines / world.Chronicle.Events.Count:P2})");

            Assert.NotEmpty(disputes);
            Assert.True(
                disputes.Count < adults / 20,
                $"Seed {seed}: {disputes.Count} quarrels among {adults} adults is a feuding court, "
                + "not a chronicle.");
            Assert.True(
                lines < world.Chronicle.Events.Count / 50,
                $"Seed {seed}: personal quarrels wrote {lines} of {world.Chronicle.Events.Count} "
                + "events and are crowding the timeline.");

            // Bounded in time as well as in number: nothing may smoulder for a whole history.
            Assert.True(longest <= 60, $"Seed {seed}: a quarrel ran {longest} years.");
        }

        _output.WriteLine("outcomes " + string.Join(", ", outcomes.Select(p => $"{p.Key}={p.Value}")));
        _output.WriteLine("causes   " + string.Join(", ", causes.Select(p => $"{p.Key}={p.Value}")));

        Assert.Equal(4, causes.Count);
        Assert.True(
            outcomes.GetValueOrDefault(DisputeOutcome.Reconciled)
            + outcomes.GetValueOrDefault(DisputeOutcome.Settled) > 0,
            "No quarrel across the seed panel was ever answered without blood.");
        Assert.True(
            outcomes.GetValueOrDefault(DisputeOutcome.Wounded)
            + outcomes.GetValueOrDefault(DisputeOutcome.Killed) > 0,
            "No quarrel across the seed panel ever came to blows.");
        Assert.True(duels > 0);
        Assert.True(total > 0);
    }

    /// <summary>
    /// Blood spilt in a quarrel goes through the same lifecycle as blood spilt in a war.
    /// </summary>
    [Fact]
    public void DuelWoundsAndDeathsUseTheSharedLifecycle()
    {
        int wounds = 0;
        int deaths = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (FigureDispute dispute in All(world))
            {
                if (dispute.Outcome == DisputeOutcome.Wounded)
                {
                    Figure hurt = Wounded(world, dispute);
                    FigureInjury injury = hurt.Injuries.Single(item =>
                        item.SourceKind == EventKind.DuelFought
                        && item.Year == dispute.EndYear);

                    Assert.Equal(dispute.Other(hurt.Id), injury.CauseId);
                    Assert.True(injury.RecoveryYear > injury.Year);

                    // The same rule that keeps a battle casualty off the road keeps this one off it.
                    Assert.Equal(0.0, LifeStories.Fitness(hurt, injury.Year));
                    Assert.DoesNotContain(hurt.Journeys, journey =>
                        journey.Year > injury.Year && journey.Year < injury.RecoveryYear);
                    wounds++;
                }

                if (dispute.Outcome != DisputeOutcome.Killed) continue;

                Figure dead = world.Figures[dispute.OpenerId].IsAlive
                        && world.Figures[dispute.OpenerId].DeathYear != dispute.EndYear
                    ? world.Figures[dispute.RivalId]
                    : Slain(world, dispute);

                Assert.Equal(DeathCause.Duel, dead.DeathCause);
                Assert.Equal(dispute.EndYear, dead.DeathYear);
                Assert.False(dead.IsAlive);
                deaths++;
            }
        }

        Assert.True(wounds > 0, "No quarrel in the seed panel left a wound to check.");
        Assert.True(deaths > 0, "No quarrel in the seed panel left a death to check.");
    }

    /// <summary>
    /// A settled quarrel leaves the relationship changed, not merely closed.
    /// </summary>
    /// <remarks>
    /// The rival role stays and the grievance goes. Two people who quarrelled and made it up are
    /// not two people who never quarrelled, and the bond is where the chronicle remembers that.
    /// </remarks>
    [Fact]
    public void ReconciliationClearsTheGrievanceAndKeepsTheHistory()
    {
        Figure first = Person(0, "Alda");
        Figure second = Person(1, "Bera");
        EntityId court = EntityId.Settlement(2);

        LifeStories.AddRivalry(first, second, 10, EventKind.OfficeRevoked, court, 0.70);
        double before = LifeStories.BondTo(first, second.Id)!.Grievance;
        Assert.True(before > 0.5);

        LifeStories.Reconcile(first, second, 14, EventKind.DisputeSettled, court, 0.80, warmly: true);

        FigureBond bond = LifeStories.BondTo(first, second.Id)!;
        Assert.True(bond.Grievance < before);
        Assert.True(bond.Kinds.HasFlag(BondKind.Rival));
        Assert.Equal(14, bond.LastChangedYear);
        Assert.Equal(EventKind.DisputeSettled, bond.LastEventKind);
        Assert.Contains(
            first.Memories,
            memory => memory.Kind == MemoryKind.Gratitude && memory.AboutId == second.Id);
    }

    /// <summary>A subject does not call their own ruler out, whatever they feel about them.</summary>
    /// <remarks>
    /// The anger is real and stays in the bond; what changes is the route it can take. Rank is why
    /// the same grievance produces a duel between two officers and a conspiracy against a king,
    /// and it is the reason this system and <see cref="Conspiracies"/> are not the same system.
    /// </remarks>
    [Fact]
    public void RankKeepsAQuarrelWithARulerOffTheDuellingGround()
    {
        int metTheRuler = 0;
        int metAnEqual = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (FigureDispute dispute in All(world))
            {
                if (dispute.Outcome is not (DisputeOutcome.Wounded or DisputeOutcome.Killed))
                {
                    continue;
                }

                // Whether they were on the throne in the year of the meeting, not whether they
                // ever sat on it. Most of the people this system reaches are dynasts, and half of
                // them reign eventually; the question rank asks is about the year.
                int fought = dispute.EndYear ?? world.EndYear;
                Figure rival = world.Figures[dispute.RivalId];
                bool reigning = rival.Offices.Exists(office =>
                    office.Kind == OfficeKind.Ruler
                    && office.FromYear <= fought
                    && (office.ToYear ?? world.EndYear) >= fought);

                if (reigning) metTheRuler++;
                else metAnEqual++;
            }
        }

        Assert.True(metAnEqual > 0, "No quarrel between equals ever came to blows.");
        Assert.True(
            metTheRuler <= metAnEqual,
            $"{metTheRuler} duels were fought against reigning rulers and {metAnEqual} were not.");
    }

    /// <summary>Everything a life page needs is in the export, on both pages.</summary>
    [Fact]
    public void ExportCarriesTheQuarrelOnBothPages()
    {
        WorldExport export = HistoryRun.Execute(TestWorlds.Standard(2)).ToExport();
        var byId = export.Figures.ToDictionary(figure => figure.Id);
        int seen = 0;

        foreach (ExportFigure figure in export.Figures)
        {
            foreach (ExportDispute dispute in figure.Disputes)
            {
                Assert.True(byId.ContainsKey(dispute.OtherId));
                Assert.NotNull(dispute.SourceEntityId);
                Assert.NotEmpty(dispute.Acts);
                Assert.True(dispute.StartYear > 0);

                ExportDispute mirrored = Assert.Single(
                    byId[dispute.OtherId].Disputes,
                    other => other.OtherId == figure.Id && other.StartYear == dispute.StartYear);
                Assert.NotEqual(dispute.Opened, mirrored.Opened);
                Assert.Equal(dispute.Outcome, mirrored.Outcome);
                seen++;
            }
        }

        Assert.True(seen > 0, "Seed 2 exported no quarrel.");
    }

    // -----------------------------------------------------------------------

    /// <summary>Every quarrel in the world, once, in a stable order.</summary>
    private static List<FigureDispute> All(WorldState world)
    {
        var seen = new List<FigureDispute>();
        foreach (Figure figure in world.Figures)
        {
            foreach (FigureDispute dispute in figure.Disputes)
            {
                if (dispute.OpenerId == figure.Id) seen.Add(dispute);
            }
        }

        return seen;
    }

    private static ExportDispute Exported(
        WorldExport export, Figure figure, FigureDispute dispute)
    {
        ExportFigure exported = export.Figures.Single(item => item.Id == figure.Id);
        return exported.Disputes.Single(item =>
            item.OtherId == dispute.Other(figure.Id) && item.StartYear == dispute.StartYear);
    }

    private static Figure Wounded(WorldState world, FigureDispute dispute)
    {
        Figure opener = world.Figures[dispute.OpenerId];
        return opener.Injuries.Exists(injury =>
            injury.SourceKind == EventKind.DuelFought && injury.Year == dispute.EndYear)
            ? opener
            : world.Figures[dispute.RivalId];
    }

    private static Figure Slain(WorldState world, FigureDispute dispute)
    {
        Figure opener = world.Figures[dispute.OpenerId];
        return !opener.IsAlive
            && opener.DeathYear == dispute.EndYear
            && opener.DeathCause == DeathCause.Duel
            ? opener
            : world.Figures[dispute.RivalId];
    }

    private static Figure Enrol(
        WorldState world, Civilization civilization, int id, string name, double aggression)
    {
        var figure = new Figure(
            EntityId.Figure(id),
            civilization.Id,
            civilization.CultureId,
            name,
            Sex.Male,
            0)
        {
            Disposition = new Disposition(
                new CultureValues(
                    Aggression: aggression,
                    Expansionism: 0.5,
                    Piety: 0.3,
                    Tradition: 0.3,
                    Mercantile: 0.5,
                    Learning: 0.5),
                Centralism: 0.5,
                Independence: 0.7),
            ResidenceSettlementId = civilization.CapitalId,
        };

        world.Figures.Add(figure);
        return figure;
    }

    private static Figure Person(int id, string name) =>
        new(EntityId.Figure(id), EntityId.Civilization(0), EntityId.Culture(0), name, Sex.Female, 0);
}
