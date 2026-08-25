using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.Serialization;
using HistoryEngine.World;
using Xunit;
using Xunit.Abstractions;

namespace HistoryEngine.Tests;

/// <summary>
/// Conspiracies as persistent plots: what starts one, who joins it, and how it ends.
/// </summary>
public sealed class ConspiracyTests
{
    /// <summary>Shared with the quarrel panel: the two systems answer the same anger.</summary>
    private static readonly ulong[] Seeds = { 11, 16, 22, 42, 43 };

    private readonly ITestOutputHelper _output;

    public ConspiracyTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Plots are exceptional, finite, and reach more than one kind of ending.
    /// </summary>
    /// <remarks>
    /// The panel is the point of this test. A model of political violence that produces nothing is
    /// as wrong as one that produces a murder a decade, and neither failure is visible from an
    /// invariant — only from the counts.
    /// </remarks>
    [Fact]
    public void PlotsStayRareFiniteAndReachEveryEnding()
    {
        var outcomes = new Dictionary<PlotOutcome, int>();
        var causes = new Dictionary<PlotCause, int>();
        var objectives = new Dictionary<PlotObjective, int>();
        var ties = new Dictionary<PlotTie, int>();
        var resolutions = new Dictionary<string, int>();
        int total = 0;
        int recruited = 0;
        int known = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;
            List<FigurePlot> plots = All(world);
            int adults = world.Figures.Count(figure =>
                (figure.DeathYear ?? world.EndYear) - figure.BirthYear >= Succession.MajorityAge);

            int longest = 0;
            foreach (FigurePlot plot in plots)
            {
                outcomes[plot.Outcome] = outcomes.GetValueOrDefault(plot.Outcome) + 1;
                causes[plot.Cause] = causes.GetValueOrDefault(plot.Cause) + 1;
                objectives[plot.Objective] = objectives.GetValueOrDefault(plot.Objective) + 1;
                foreach (PlotMember member in plot.Members)
                {
                    ties[member.Tie] = ties.GetValueOrDefault(member.Tie) + 1;
                    recruited++;
                }

                string ending = plot.Resolution ?? "still open";
                resolutions[ending] = resolutions.GetValueOrDefault(ending) + 1;
                longest = Math.Max(longest, (plot.EndYear ?? world.EndYear) - plot.StartYear);
                if (plot.WasKnown) known++;
                total++;
            }

            int lines = world.Chronicle.Events.Count(entry => entry.Kind
                is EventKind.ConspiracyExposed
                or EventKind.ConspiracyAttempted
                or EventKind.ConspiratorJoined);

            _output.WriteLine(
                $"seed {seed}: adults={adults}, plots={plots.Count}, longest={longest}y, "
                + $"members={plots.Sum(plot => plot.Members.Count)}, "
                + $"public={plots.Count(plot => plot.WasKnown)}, "
                + $"lines={lines} of {world.Chronicle.Events.Count} events");

            Assert.True(
                plots.Count < adults / 40,
                $"Seed {seed}: {plots.Count} plots among {adults} adults is a court of nothing but "
                + "conspirators.");
            Assert.True(longest <= 80, $"Seed {seed}: a plot ran {longest} years.");
        }

        _output.WriteLine("outcomes   " + Join(outcomes));
        _output.WriteLine("causes     " + Join(causes));
        _output.WriteLine("objectives " + Join(objectives));
        _output.WriteLine("ties       " + Join(ties));
        _output.WriteLine("endings    " + Join(resolutions));
        _output.WriteLine($"total={total}, recruited={recruited}, became public={known}");

        Assert.True(total >= Seeds.Length, $"Only {total} plots across {Seeds.Length} worlds.");
        Assert.True(recruited > 0, "No plot ever recruited anybody.");
        Assert.True(known > 0, "No plot ever became public.");
        Assert.True(
            outcomes.ContainsKey(PlotOutcome.Succeeded), "No plot ever reached its objective.");
        Assert.True(
            outcomes.Keys.Count(outcome => outcome != PlotOutcome.Succeeded) >= 2,
            "Plots only ever succeeded or ended one other way: " + Join(outcomes));
    }


    /// <summary>
    /// Nobody plots against nobody, and nobody plots over nothing.
    /// </summary>
    /// <remarks>
    /// The second half is the whole difference from the model this replaced. An annual chance
    /// against a realm produces political murder that is about nothing; requiring a cause the world
    /// already wrote means every plot in the export can name the year and the event behind it.
    /// </remarks>
    [Fact]
    public void EveryPlotNamesACauseALeaderAndATarget()
    {
        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (FigurePlot plot in All(world))
            {
                Assert.NotEqual(plot.LeaderId, plot.TargetId);
                Assert.True(world.Figures.Contains(plot.LeaderId));
                Assert.True(world.Figures.Contains(plot.TargetId));
                Assert.True(world.Civilizations.Contains(plot.RealmId));

                Figure leader = world.Figures[plot.LeaderId];
                Figure target = world.Figures[plot.TargetId];

                Assert.True(
                    plot.SourceKind is EventKind.OfficeRevoked
                        or EventKind.SuccessionDisputed
                        or EventKind.FigureDied
                        or EventKind.DisputeOpened,
                    $"Seed {seed}: a plot came from {plot.SourceKind}, which is not a recorded cause.");
                Assert.False(plot.SourceEntityId.IsNone);

                Assert.True(leader.BirthYear + Succession.MajorityAge <= plot.StartYear);
                Assert.True(target.BirthYear + Succession.MajorityAge <= plot.StartYear);
                Assert.True((leader.DeathYear ?? world.EndYear) >= plot.StartYear);
                Assert.True((target.DeathYear ?? world.EndYear) >= plot.StartYear);

                // The target was reigning when it began. A plot against a private citizen is a
                // quarrel, and quarrels are somebody else's system.
                Assert.Contains(target.Offices, office =>
                    office.Kind == OfficeKind.Ruler
                    && office.FromYear <= plot.StartYear
                    && (office.ToYear ?? world.EndYear) >= plot.StartYear);

                Assert.NotEmpty(plot.Acts);
                Assert.Equal(plot.StartYear, plot.Acts[0].Year);
                for (int i = 1; i < plot.Acts.Count; i++)
                {
                    Assert.True(plot.Acts[i - 1].Year <= plot.Acts[i].Year);
                }
            }
        }
    }

    /// <summary>
    /// Every conspirator joined for a reason that existed before they were asked.
    /// </summary>
    /// <remarks>
    /// The failure this guards against is the cheap one: needing a third man and taking whichever
    /// courtier the loop reached first. Each tie is checked back against the bond, grievance or
    /// claim it was recorded from, and the unwitting member is checked to have been left out of the
    /// belief as well as out of the record.
    /// </remarks>
    [Fact]
    public void RecruitmentRestsOnATestedTie()
    {
        int witting = 0;
        int unwitting = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (FigurePlot plot in All(world))
            {
                Figure leader = world.Figures[plot.LeaderId];
                var seen = new HashSet<EntityId>();

                foreach (PlotMember member in plot.Members)
                {
                    Assert.True(seen.Add(member.FigureId));
                    Assert.NotEqual(plot.LeaderId, member.FigureId);
                    Assert.NotEqual(plot.TargetId, member.FigureId);
                    Assert.True(world.Figures.Contains(member.FigureId));
                    Assert.InRange(
                        member.JoinedYear, plot.StartYear, plot.EndYear ?? world.EndYear);

                    Figure conspirator = world.Figures[member.FigureId];
                    FigureBond? toLeader = LifeStories.BondTo(conspirator, leader.Id);
                    FigureBond? toTarget = LifeStories.BondTo(conspirator, plot.TargetId);

                    switch (member.Tie)
                    {
                        case PlotTie.GrievanceAgainstTarget:
                            Assert.NotNull(toTarget);
                            break;
                        case PlotTie.ObligationToLeader:
                        case PlotTie.TrustInLeader:
                        case PlotTie.Household:
                            Assert.NotNull(toLeader);
                            break;
                        case PlotTie.Ambition:
                            Assert.True(conspirator.Disposition.Independence >= 0.50);
                            break;
                    }

                    if (member.Witting)
                    {
                        witting++;
                        Assert.Contains(plot, conspirator.Plots);
                        Assert.Contains(
                            conspirator.Bonds,
                            bond => bond.OtherId == leader.Id
                                && bond.Kinds.HasFlag(BondKind.CoConspirator));
                    }
                    else
                    {
                        // Their access was used; they were never told. Nothing on their page says
                        // otherwise, which is the only way an unwitting party is worth modelling.
                        unwitting++;
                        Assert.DoesNotContain(plot, conspirator.Plots);
                        Assert.DoesNotContain(
                            conspirator.Memories,
                            memory => memory.Kind == MemoryKind.Conspiracy
                                && memory.Year == member.JoinedYear);
                    }
                }
            }
        }

        Assert.True(witting > 0, "No plot ever recruited a willing conspirator.");
        _output.WriteLine($"witting={witting}, unwitting={unwitting}");
    }

    /// <summary>No plot outlives its leader, its target, its realm, or its own ending.</summary>
    [Fact]
    public void NoPlotOutlivesThePeopleOrTheThroneItNeeds()
    {
        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (FigurePlot plot in All(world))
            {
                Figure leader = world.Figures[plot.LeaderId];
                Figure target = world.Figures[plot.TargetId];

                if (plot.IsOpen)
                {
                    Assert.True(leader.IsAlive && target.IsAlive);
                    Assert.Equal(
                        target.Id, world.Civilizations[plot.RealmId].CurrentRulerId);
                    Assert.Null(plot.EndYear);
                    continue;
                }

                int ended = Assert.IsType<int>(plot.EndYear);
                Assert.True(ended >= plot.StartYear);
                Assert.NotNull(plot.Resolution);
                Assert.True(leader.DeathYear is null || leader.DeathYear >= ended);
                Assert.True(target.DeathYear is null || target.DeathYear >= ended);
            }
        }
    }

    /// <summary>
    /// What was secret at the time is secret in the timeline, and only there.
    /// </summary>
    /// <remarks>
    /// The engine keeps the retrospective truth of a plot whether or not anyone found out. What it
    /// must never do is narrate that truth in the year it happened: a plot nobody discovered leaves
    /// no event at all, and a plot that was discovered writes nothing before the year of discovery.
    /// </remarks>
    [Fact]
    public void SecretPlotsNeverReachTheContemporaryRecord()
    {
        int secret = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;
            List<FigurePlot> plots = All(world);

            foreach (FigurePlot plot in plots)
            {
                foreach (PlotAct act in plot.Acts)
                {
                    if (!act.Known) continue;
                    Assert.NotNull(plot.PublicYear);
                    Assert.True(act.Year >= plot.PublicYear);
                }

                if (plot.WasKnown)
                {
                    Assert.True(plot.PublicYear >= plot.StartYear);
                    continue;
                }

                // Nothing about this plot reached the timeline in the years it ran. Bounded by
                // its own years because a leader may have led an earlier plot that did become
                // public, and that one's events are not this one's leak.
                secret++;
                Assert.DoesNotContain(
                    world.Chronicle.Events,
                    entry => entry.Subject == plot.LeaderId
                        && entry.Year >= plot.StartYear
                        && entry.Year <= (plot.EndYear ?? world.EndYear)
                        && entry.Kind is EventKind.ConspiracyExposed
                            or EventKind.ConspiracyAttempted
                            or EventKind.ConspiratorJoined);
            }

            // Nothing writes the joining of a conspiracy while it is still one. The kind stays for
            // the day a plot is revealed member by member; nothing reaches it from secrecy.
            Assert.DoesNotContain(
                world.Chronicle.Events,
                entry => entry.Kind == EventKind.ConspiratorJoined);
        }

        Assert.True(secret > 0, "Every plot became public, which is not what secrecy means.");
    }

    /// <summary>
    /// A plot that reaches its objective uses the paths the rest of the world already uses.
    /// </summary>
    /// <remarks>
    /// Murder goes through <see cref="Houses.Die"/> with the family indexing and the named suspect
    /// the incident system reads; a deposition goes through the same vacated throne an uprising
    /// leaves, and the ordinary succession fills it. Neither has a private path.
    /// </remarks>
    [Fact]
    public void SuccessReusesTheOrdinaryDeathAndSuccessionPaths()
    {
        int murders = 0;
        int depositions = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (FigurePlot plot in All(world))
            {
                if (plot.Outcome != PlotOutcome.Succeeded) continue;

                Figure leader = world.Figures[plot.LeaderId];
                Figure target = world.Figures[plot.TargetId];
                int ended = plot.EndYear!.Value;

                Assert.Equal(ended, plot.PublicYear);

                if (plot.Objective == PlotObjective.Assassinate)
                {
                    // The court names a hand for a murder — though not necessarily for the last
                    // time, since a later quarrel or plot may overwrite the field with its year.
                    // A successful coup accuses nobody, which is the point of winning one.
                    murders++;
                    Assert.True(leader.AccusedYear >= ended);
                    Assert.Equal(ended, target.DeathYear);
                    Assert.True(
                        target.DeathCause is DeathCause.Assassination or DeathCause.Poisoning);
                    Assert.Contains(
                        world.Chronicle.Events,
                        entry => entry.Kind == EventKind.FigureDied
                            && entry.Subject == target.Id);
                    Assert.All(
                        Succession.ImmediateFamily(world, target),
                        kin => Assert.Equal(ended, kin.KinMurderedYear));
                    continue;
                }

                depositions++;
                Assert.True(target.DeathYear is null || target.DeathYear >= ended);
                Assert.Contains(
                    target.Offices,
                    office => office.Kind == OfficeKind.Ruler && office.ToYear == ended);
                Assert.Contains(
                    world.Chronicle.Events,
                    entry => entry.Kind == EventKind.RulerDeposed
                        && entry.Subject == target.Id
                        && entry.Year == ended);
                Assert.Equal(ended, target.DisgracedYear);
            }
        }

        _output.WriteLine($"murders={murders}, depositions={depositions}");
        Assert.True(murders + depositions > 0, "No plot ever reached its objective.");
    }

    /// <summary>Everything a plot page needs is in the export, on every page that knew of it.</summary>
    [Fact]
    public void ExportCarriesThePlotOnEveryPageThatKnewIt()
    {
        HistoryRun run = HistoryRun.Execute(TestWorlds.Standard(16));
        WorldExport export = run.ToExport();
        var byId = export.Figures.ToDictionary(figure => figure.Id);
        int led = 0;
        int joined = 0;

        foreach (ExportFigure figure in export.Figures)
        {
            foreach (ExportPlot plot in figure.Plots)
            {
                Assert.True(byId.ContainsKey(plot.LeaderId));
                Assert.True(byId.ContainsKey(plot.TargetId));
                Assert.NotEmpty(plot.Acts);
                Assert.True(plot.StartYear > 0);
                Assert.Equal(plot.Led, plot.LeaderId == figure.Id);

                ExportPlot fromLeader = Assert.Single(
                    byId[plot.LeaderId].Plots,
                    other => other.TargetId == plot.TargetId && other.StartYear == plot.StartYear);
                Assert.True(fromLeader.Led);
                Assert.Equal(fromLeader.Outcome, plot.Outcome);
                Assert.Equal(fromLeader.PublicYear, plot.PublicYear);
                Assert.Equal(fromLeader.Members.Count, plot.Members.Count);

                if (plot.Led) led++;
                else joined++;
            }
        }

        Assert.True(led > 0, "Seed 16 exported no plot.");
        _output.WriteLine($"exported: led={led}, joined={joined}");
    }

    // -----------------------------------------------------------------------

    /// <summary>Every plot in the world, once, in a stable order.</summary>
    private static List<FigurePlot> All(WorldState world)
    {
        var seen = new List<FigurePlot>();
        foreach (Figure figure in world.Figures)
        {
            foreach (FigurePlot plot in figure.Plots)
            {
                if (plot.LeaderId == figure.Id) seen.Add(plot);
            }
        }

        return seen;
    }

    private static string Join<T>(Dictionary<T, int> counts)
        where T : notnull =>
        string.Join(", ", counts.OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={pair.Value}"));
}
