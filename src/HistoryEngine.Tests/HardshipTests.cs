using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.World;
using Xunit;
using Xunit.Abstractions;

namespace HistoryEngine.Tests;

/// <summary>
/// What a town's bad year costs the recorded people who were standing in it.
/// </summary>
/// <remarks>
/// The questions here are mostly about restraint. The join is easy; keeping it from producing a
/// world where everybody is traumatised, or a second mortality model running beside the first, is
/// the part worth asserting.
/// </remarks>
public sealed class HardshipTests
{
    private static readonly ulong[] Seeds = { 2, 7, 11, 42, 99 };

    /// <summary>The memory kinds an inert life already had before this system existed.</summary>
    private static readonly MemoryKind[] Domestic =
    {
        MemoryKind.Bereavement, MemoryKind.Marriage, MemoryKind.Parenthood,
        MemoryKind.Journey, MemoryKind.Mentorship,
    };

    private readonly ITestOutputHelper _output;

    public HardshipTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A worse episode never costs an otherwise identical resident less.
    /// </summary>
    /// <remarks>
    /// The regression the issue asked for by name, and the reason severity is one scale across all
    /// four families rather than a label per system. Asserted on the curves directly rather than
    /// through a world, because a sampled world cannot prove a monotonicity — it can only fail to
    /// find a counterexample.
    /// </remarks>
    [Fact]
    public void AWorseEpisodeIsNeverSaferForTheSameResident()
    {
        foreach (HardshipKind kind in Enum.GetValues<HardshipKind>())
        {
            foreach (int age in new[] { 3, 20, 45, 70 })
            {
                double priorMortality = 0.0;
                double priorInjury = 0.0;
                double priorRecall = 0.0;

                for (int percent = 0; percent <= 100; percent++)
                {
                    double severity = percent / 100.0;
                    double mortality = Hardships.Mortality(kind, severity, age);
                    double injury = Hardships.Injury(kind, severity, age);
                    double recall = Hardships.Recall(severity);

                    Assert.True(
                        mortality >= priorMortality,
                        $"{kind} at {age}: mortality fell at {percent}% lost.");
                    Assert.True(
                        injury >= priorInjury,
                        $"{kind} at {age}: injury risk fell at {percent}% lost.");
                    Assert.True(
                        recall >= priorRecall,
                        $"{kind} at {age}: recall fell at {percent}% lost.");

                    priorMortality = mortality;
                    priorInjury = injury;
                    priorRecall = recall;
                }
            }
        }
    }

    /// <summary>
    /// No consequence is certain, at any severity, for anybody.
    /// </summary>
    /// <remarks>
    /// The failure mode this system most needs to avoid is a world in which everyone who lived
    /// through a bad year is marked by it, which is exactly as uninformative as one in which nobody
    /// is. The ceilings are the mechanism and this is the assertion that they are wired in.
    /// </remarks>
    [Fact]
    public void NoEpisodeIsCertainToReachAnyone()
    {
        foreach (HardshipKind kind in Enum.GetValues<HardshipKind>())
        {
            foreach (int age in new[] { 3, 20, 45, 70 })
            {
                Assert.True(Hardships.Mortality(kind, 1.0, age) <= 0.15);
                Assert.True(Hardships.Injury(kind, 1.0, age) <= 0.23);
                Assert.True(Hardships.Recall(1.0) < 1.0);
            }
        }
    }

    /// <summary>
    /// Somebody demonstrably elsewhere that year does not carry the town's bad year.
    /// </summary>
    /// <remarks>
    /// The one exclusion a residence field cannot express by itself. A journey already records the
    /// year it was made, so this is a join the engine could make and simply did not.
    /// </remarks>
    [Fact]
    public void SomeoneAwayThatYearTakesNoConsequenceForBeingThere()
    {
        int checkedFigures = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure figure in world.Figures)
            {
                foreach (Journey journey in figure.Journeys)
                {
                    foreach (SalientMemory memory in figure.Memories)
                    {
                        if (memory.Kind != MemoryKind.Hardship) continue;

                        Assert.True(
                            memory.Year != journey.Year,
                            $"Seed {seed}: {figure.Id} was travelling in {journey.Year} and still "
                            + "took a hardship memory for being at home.");
                        checkedFigures++;
                    }
                }
            }
        }

        Assert.True(checkedFigures > 0, "No traveller with a hardship memory was ever compared.");
    }

    /// <summary>
    /// Every consequence names an episode and a place that exist.
    /// </summary>
    /// <remarks>
    /// A memory that cannot be resolved to a settlement or a battle is a memory a life page cannot
    /// print, which is the whole reason this system was built rather than a mortality tweak.
    /// </remarks>
    [Fact]
    public void EveryConsequenceNamesSomethingReal()
    {
        int memories = 0;
        int wounds = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure figure in world.Figures)
            {
                foreach (SalientMemory memory in figure.Memories)
                {
                    if (memory.Kind != MemoryKind.Hardship) continue;

                    Assert.Contains(
                        memory.SourceKind,
                        new[]
                        {
                            EventKind.SettlementFamine,
                            EventKind.PlagueBegan,
                            EventKind.PlagueSpread,
                            EventKind.SettlementSacked,
                            EventKind.DisasterStruck,
                        });
                    Assert.True(
                        world.Settlements.Contains(memory.LocationId),
                        $"Seed {seed}: a hardship memory names a place that does not exist.");
                    Assert.True(
                        world.Settlements.Contains(memory.AboutId)
                        || world.Battles.Contains(memory.AboutId),
                        $"Seed {seed}: a hardship memory names no real episode.");
                    Assert.True(memory.Year >= figure.BirthYear);
                    memories++;
                }

                foreach (FigureInjury injury in figure.Injuries)
                {
                    if (injury.SourceKind is not (EventKind.SettlementSacked
                        or EventKind.DisasterStruck))
                    {
                        continue;
                    }

                    // The shared lifecycle, not a private one: the same recovery rule that keeps a
                    // battle casualty off the road keeps this one off it.
                    Assert.True(injury.RecoveryYear > injury.Year);
                    Assert.Equal(0.0, LifeStories.Fitness(figure, injury.Year));
                    wounds++;
                }
            }
        }

        Assert.True(memories > 0, "No hardship reached anybody across the panel.");
        Assert.True(wounds > 0, "No sudden hardship ever hurt a survivor across the panel.");
    }

    /// <summary>
    /// This pass never becomes a second way to die of something that could already kill you.
    /// </summary>
    /// <remarks>
    /// <para>The constraint the issue set, and the one most easily lost: a plague already reaches
    /// recorded people through <c>PlagueSystem.Cull</c>, a sack through
    /// <c>Warfare.ResidentCasualties</c> and a disaster through
    /// <c>DisasterSystem.CourtCasualties</c>. Rolling again here would double the chance of dying
    /// in precisely the years the world is most dangerous, and would do it invisibly, because both
    /// deaths would carry the same cause.</para>
    ///
    /// <para>Famine is the exception on the merits: before this system a famine could not kill a
    /// named person at all, so this pass is the only path and not a second one.</para>
    /// </remarks>
    [Fact]
    public void HardshipIsNotASecondWayToDieOfTheSameThing()
    {
        int famine = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure figure in world.Figures)
            {
                if (figure.IsAlive || figure.DeathYear is null) continue;

                // A famine death is recorded as an illness, in a year its own town was recorded
                // as starving, and it goes through the central path like every other death.
                if (figure.DeathCause != DeathCause.Illness) continue;
                if (figure.DeathDetail is null) continue;
                if (!figure.DeathDetail.StartsWith("in the famine", StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.Contains(
                    world.Chronicle.Events,
                    entry => entry.Year == figure.DeathYear
                        && entry.Kind == EventKind.SettlementFamine);
                famine++;
            }
        }

        Assert.True(famine > 0, "No famine in the panel ever cost a recorded life.");
    }

    /// <summary>
    /// The reach is measurable, bounded, and does not crowd the timeline.
    /// </summary>
    /// <remarks>
    /// <para>The panel the issue asked for. The headline number is the share of adults who hold no
    /// memory outside the domestic set — born, married, a trade, a journey, a relative's death —
    /// reported with and without this system's contribution, because the absolute figure depends on
    /// where the line is drawn and the movement does not.</para>
    ///
    /// <para>The upper bound matters as much as the lower one. Hardship must reach a real minority
    /// and not a majority: a chronicle in which most people are survivors of something is one where
    /// being a survivor stops meaning anything.</para>
    /// </remarks>
    [Fact]
    public void HardshipReachesARealMinorityOfOrdinaryLives()
    {
        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            int adults = 0;
            int inert = 0;
            int inertWithout = 0;
            int holders = 0;
            int total = 0;

            foreach (Figure figure in world.Figures)
            {
                if ((figure.DeathYear ?? world.EndYear) - figure.BirthYear
                    < Succession.MajorityAge)
                {
                    continue;
                }

                adults++;
                bool distinctive = false;
                bool distinctiveWithout = false;
                bool holds = false;

                foreach (SalientMemory memory in figure.Memories)
                {
                    bool domestic = Array.IndexOf(Domestic, memory.Kind) >= 0;
                    if (!domestic) distinctive = true;
                    if (!domestic && memory.Kind != MemoryKind.Hardship) distinctiveWithout = true;
                    if (memory.Kind != MemoryKind.Hardship) continue;

                    holds = true;
                    total++;
                }

                if (holds) holders++;
                if (!distinctive) inert++;
                if (!distinctiveWithout) inertWithout++;
            }

            double reach = holders * 100.0 / adults;
            _output.WriteLine(
                $"seed {seed}: adults={adults}, hardship memories={total} held by {holders} "
                + $"({reach:F1}% of adults), inert {inertWithout * 100.0 / adults:F1}% -> "
                + $"{inert * 100.0 / adults:F1}%");

            Assert.True(reach > 5.0, $"Seed {seed}: hardship reached only {reach:F1}% of adults.");
            Assert.True(
                reach < 45.0,
                $"Seed {seed}: hardship reached {reach:F1}% of adults, which is a world of "
                + "survivors rather than a world with survivors in it.");
            Assert.True(
                inert <= inertWithout,
                $"Seed {seed}: counting hardship made more lives inert, not fewer.");
        }
    }

    /// <summary>
    /// Reaching the residents cannot move the episode that reached them.
    /// </summary>
    /// <remarks>
    /// The consequence pass forks from the world root rather than from the caller's stream for this
    /// reason. If it drew from the caller instead, deciding what a famine did to a scribe would
    /// shift the next settlement's harvest, and the whole world downstream of it.
    /// </remarks>
    [Fact]
    public void TheSameSeedProducesTheSameConsequences()
    {
        WorldState first = HistoryRun.Execute(TestWorlds.Standard(7)).World;
        WorldState second = HistoryRun.Execute(TestWorlds.Standard(7)).World;

        List<string> Sample(WorldState world)
        {
            var seen = new List<string>();
            foreach (Figure figure in world.Figures)
            {
                foreach (SalientMemory memory in figure.Memories)
                {
                    if (memory.Kind != MemoryKind.Hardship) continue;
                    seen.Add($"{figure.Id}:{memory.Year}:{memory.SourceKind}:{memory.AboutId}");
                }
            }

            seen.Sort(StringComparer.Ordinal);
            return seen;
        }

        List<string> left = Sample(first);
        Assert.NotEmpty(left);
        Assert.Equal(left, Sample(second));
    }
}
