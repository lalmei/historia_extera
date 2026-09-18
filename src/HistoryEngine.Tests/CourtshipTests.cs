using System.Linq;
using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.Systems;
using HistoryEngine.World;
using Xunit;
using Xunit.Abstractions;

namespace HistoryEngine.Tests;

/// <summary>
/// Courtship: the rung above <see cref="AffinityStage.Friendship"/>, and what a marriage does with
/// it. Issue #175.
/// </summary>
/// <remarks>
/// Reuses <see cref="AffinityTests"/>'s own five-seed panel rather than picking a fresh one — see
/// that class for why it is (3, 5, 27, 51, 58) rather than the original (17, 19, 22, 32, 39). Both
/// outcomes this file needs are common enough in it that a fresh sample was not needed to find one.
/// </remarks>
public sealed class CourtshipTests
{
    private static readonly ulong[] Seeds = { 3, 5, 27, 51, 58 };

    /// <summary>
    /// Wide enough to have caught the bug it exists for: a scan across these ten found two figures
    /// (seed 22's <c>fig:1600</c>, seed 32's <c>fig:1278</c>) each carrying two open courtships at
    /// once, before <see cref="Affinities.CourtshipEligible"/> refused the climb to anybody who
    /// already had one. <see cref="Seeds"/> alone did not surface it.
    /// </summary>
    private static readonly ulong[] WidePanel = { 17, 19, 22, 32, 39, 2, 7, 11, 42, 99 };

    private readonly ITestOutputHelper _output;

    public CourtshipTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Nobody carries two open courtships at once — the "by construction" claim
    /// <see cref="Affinities"/> makes about <c>OpenLover</c>, checked directly rather than trusted.
    /// </summary>
    /// <remarks>
    /// A figure may hold up to three open friendships, and nothing before this guard stopped more
    /// than one of them climbing to <see cref="AffinityStage.Lover"/> in different years. Left
    /// unchecked, whichever of the two a marriage roll happened to read first would leave the other
    /// stuck open on a now-married figure, or closed as <see cref="AffinityOutcome.Overridden"/>
    /// against the very person they married. This asserts the guard that prevents both, not just
    /// their symptoms.
    /// </remarks>
    [Fact]
    public void NobodyCarriesTwoOpenCourtshipsAtOnce()
    {
        int figuresWithOneOrMore = 0;

        foreach (ulong seed in WidePanel)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure figure in world.Figures)
            {
                int open = 0;
                foreach (FigureAffinity affinity in figure.Affinities)
                {
                    if (affinity.IsOpen && affinity.Stage == AffinityStage.Lover) open++;
                }

                Assert.True(
                    open <= 1,
                    $"Seed {seed}: {world.NameOf(figure.Id)} carries {open} open courtships at "
                    + "once.");

                if (open > 0) figuresWithOneOrMore++;
            }
        }

        Assert.True(
            figuresWithOneOrMore > 0,
            "The wide panel produced no standing courtship at all.");
    }

    /// <summary>
    /// A courtship is a friendship that climbed one rung further, never fewer, never more than one
    /// a year, and never from anywhere but the top of the friendship ladder.
    /// </summary>
    [Fact]
    public void ACourtshipIsReachedOnlyFromFriendshipAndNeverMoreThanOneRungAYear()
    {
        int reached = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (FigureAffinity affinity in All(world))
            {
                AffinityAct? climb = affinity.Acts.Find(act => act.Stage == AffinityStage.Lover);
                if (climb is null) continue;

                int index = affinity.Acts.IndexOf(climb);
                Assert.True(index > 0, $"Seed {seed}: a courtship began at the top of the ladder.");

                AffinityAct previous = affinity.Acts[index - 1];
                Assert.Equal(AffinityStage.Friendship, previous.Stage);
                Assert.True(
                    climb.Year >= previous.Year,
                    $"Seed {seed}: a courtship's climb is dated before the friendship under it.");

                // No rung after Lover: nothing in this ladder climbs past it, only a marriage
                // roll can close it.
                Assert.DoesNotContain(
                    affinity.Acts,
                    act => (int)act.Stage > (int)AffinityStage.Lover);

                reached++;
            }
        }

        Assert.True(reached > 0, "The panel produced no courtship at all.");
    }

    /// <summary>
    /// Every pair that reached <see cref="AffinityStage.Lover"/> was one the marriage roll could
    /// actually have married: opposite sexes, not close kin.
    /// </summary>
    /// <remarks>
    /// The guards checked here are the ones that do not change with the calendar — kinship and sex
    /// are facts about who two people are, not about the year. <see cref="MarriageAgeGuardHolds"/>
    /// and <see cref="ExistingSpouseGuardHolds"/> below cover the two that do.
    /// </remarks>
    [Fact]
    public void CourtshipNeverCrossesTheMarriageBarsOfKinshipOrSex()
    {
        int reached = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (FigureAffinity affinity in All(world))
            {
                if (!affinity.Acts.Exists(act => act.Stage == AffinityStage.Lover)) continue;

                Figure opener = world.Figures[affinity.OpenerId];
                Figure friend = world.Figures[affinity.FriendId];

                Assert.NotEqual(opener.Sex, friend.Sex);
                Assert.False(
                    Succession.AreCloseKin(world, opener, friend),
                    $"Seed {seed}: a courtship formed between close kin.");
                reached++;
            }
        }

        Assert.True(reached > 0, "The panel produced no courtship at all.");
    }

    /// <summary>Nobody climbed to <see cref="AffinityStage.Lover"/> under the marriage age.</summary>
    [Fact]
    public void MarriageAgeGuardHolds()
    {
        int reached = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (FigureAffinity affinity in All(world))
            {
                AffinityAct? climb = affinity.Acts.Find(act => act.Stage == AffinityStage.Lover);
                if (climb is null) continue;

                Figure opener = world.Figures[affinity.OpenerId];
                Figure friend = world.Figures[affinity.FriendId];

                Assert.True(opener.AgeIn(climb.Year) >= HouseholdSystem.MarriageAge);
                Assert.True(friend.AgeIn(climb.Year) >= HouseholdSystem.MarriageAge);
                reached++;
            }
        }

        Assert.True(reached > 0, "The panel produced no courtship at all.");
    }

    /// <summary>
    /// The existing-spouse guard, read where it is cheap to read honestly: no married figure ends
    /// the run carrying a standing lover who is not the person they married.
    /// </summary>
    /// <remarks>
    /// Not reconstructed from the chronicle's marriage dates, deliberately: this engine has widows
    /// and widowers who remarry — <c>Houses.Die</c> clears <c>SpouseId</c> on both sides of a death
    /// — so "married before" says nothing about "married now," and a figure legitimately begins a
    /// second courtship after the first marriage ends in bereavement. What must actually hold is
    /// narrower and does not need that history: a live marriage and a standing courtship with a
    /// third party cannot coexist, because <see cref="Affinities.ResolveCourtshipAtMarriage"/> runs
    /// at every wedding and either consumes a figure's own lover or overrides it.
    /// </remarks>
    [Fact]
    public void NoMarriedFigureCarriesAStandingLoverWhoIsNotTheirSpouse()
    {
        int checkedFigures = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure figure in world.Figures)
            {
                if (!figure.IsMarried) continue;

                foreach (FigureAffinity affinity in figure.Affinities)
                {
                    if (!affinity.IsOpen || affinity.Stage != AffinityStage.Lover) continue;

                    Assert.Equal(
                        figure.SpouseId,
                        affinity.Other(figure.Id));
                }

                checkedFigures++;
            }
        }

        Assert.True(checkedFigures > 0, "The panel produced no married figure to check.");
    }

    /// <summary>
    /// A marriage that consumed a standing courtship is distinguishable from an arranged one, both
    /// in the affinity it closed and in the chronicle line the wedding itself wrote.
    /// </summary>
    [Fact]
    public void AMarriageThatConsumedACourtshipIsDistinguishableFromAnArrangedOne()
    {
        int consumed = 0;
        int arranged = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (HistoryEvent entry in world.Chronicle.Events)
            {
                if (entry.Kind != EventKind.FigureMarried) continue;

                bool courtship = entry.DataValue(Narration.VoiceDataKey) == "courtship";
                if (courtship) consumed++;
                else arranged++;
            }

            foreach (FigureAffinity affinity in All(world))
            {
                if (affinity.Outcome != AffinityOutcome.Wed) continue;

                Figure opener = world.Figures[affinity.OpenerId];
                Figure friend = world.Figures[affinity.FriendId];

                // The two actually married each other, not merely that some marriage happened the
                // same year. SpouseIds rather than the live SpouseId: one of the pair may have
                // since died, which clears SpouseId but not this historical record.
                Assert.Contains(friend.Id, opener.SpouseIds);
                Assert.Contains(opener.Id, friend.SpouseIds);
                Assert.Equal(AffinityStage.Lover, affinity.Stage);
                Assert.NotNull(affinity.EndYear);

                Assert.Contains(
                    world.Chronicle.Events,
                    e => e.Kind == EventKind.FigureMarried
                        && e.Year == affinity.EndYear
                        && ((e.Subject == opener.Id && e.Object == friend.Id)
                            || (e.Subject == friend.Id && e.Object == opener.Id))
                        && e.DataValue(Narration.VoiceDataKey) == "courtship");
            }
        }

        Assert.True(consumed > 0, "No marriage in the panel came from a standing courtship.");
        Assert.True(arranged > 0, "No marriage in the panel came from the political draw.");
        _output.WriteLine(
            $"{consumed} courtship marriages and {arranged} arranged marriages "
            + $"across {Seeds.Length} seeds.");
    }

    /// <summary>
    /// A marriage that overrode a standing lover leaves the overridden tie closed, and a memory of
    /// it on both pages — the fact both sides are supposed to keep.
    /// </summary>
    /// <remarks>
    /// The durable facts — the closed affinity, the surviving <see cref="BondKind.Lover"/> flag,
    /// the chronicle line — are asserted for every case. The memory itself is not: it is one of
    /// twelve slots on a page that also holds bereavements and wounds over a run of centuries, and
    /// <see cref="AffinityTests.ABetrayalOnlyHappensWhereTrustWasGivenAndAReasonExisted"/> already
    /// declines to assert survival for the same reason. What is checked instead is that the panel
    /// wrote the memory at all, in aggregate, the way <c>AFavourLeavesTheGratitudeWithThePersonWho
    /// ReceivedIt</c> checks an asymmetry rather than any one instance.
    /// </remarks>
    [Fact]
    public void AnOverriddenCourtshipLeavesAMemoryOnBothSides()
    {
        int overridden = 0;
        int stillCarryingTheMemory = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (FigureAffinity affinity in All(world))
            {
                if (affinity.Outcome != AffinityOutcome.Overridden) continue;

                Assert.Equal(AffinityStage.Lover, affinity.Stage);
                Assert.NotNull(affinity.EndYear);
                Assert.NotNull(affinity.Resolution);

                Figure opener = world.Figures[affinity.OpenerId];
                Figure friend = world.Figures[affinity.FriendId];

                // One of the two actually married somebody else in the year the tie was
                // overridden — read from the chronicle rather than from live SpouseId, which a
                // later bereavement would clear without undoing the fact that this happened.
                bool marriedElsewhere = world.Chronicle.Events.Any(
                    e => e.Kind == EventKind.FigureMarried
                        && e.Year == affinity.EndYear
                        && (e.Subject == opener.Id || e.Object == opener.Id
                            || e.Subject == friend.Id || e.Object == friend.Id)
                        && !(affinity.Involves(e.Subject) && affinity.Involves(e.Object)));
                Assert.True(
                    marriedElsewhere,
                    $"Seed {seed}: an overridden courtship has no marriage elsewhere on record "
                    + $"in {affinity.EndYear}.");

                bool openerRemembers = opener.Memories.Exists(
                    memory => memory.Kind == MemoryKind.Heartbreak && memory.AboutId == friend.Id);
                bool friendRemembers = friend.Memories.Exists(
                    memory => memory.Kind == MemoryKind.Heartbreak && memory.AboutId == opener.Id);
                if (openerRemembers) stillCarryingTheMemory++;
                if (friendRemembers) stillCarryingTheMemory++;

                // The bond survives the ending — a courtship overridden is not erased — and the
                // Lover flag it earned climbing stays on it, the way Friend stays through a
                // betrayal.
                FigureBond? bond = LifeStories.BondTo(opener, friend.Id);
                Assert.NotNull(bond);
                Assert.True(bond!.Kinds.HasFlag(BondKind.Lover));

                Assert.Contains(
                    world.Chronicle.Events,
                    e => e.Kind == EventKind.AffinityEnded
                        && e.Year == affinity.EndYear
                        && affinity.Involves(e.Subject)
                        && affinity.Involves(e.Object)
                        && e.DataValue(Narration.VoiceDataKey) == "courtship");

                overridden++;
            }
        }

        Assert.True(overridden > 0, "No overridden courtship was recorded across the panel.");
        Assert.True(
            stillCarryingTheMemory > 0,
            "Not one of the overridden courtships left a heartbreak memory on either page.");
        _output.WriteLine(
            $"{overridden} courtships overridden by a marriage elsewhere, "
            + $"{stillCarryingTheMemory} of {overridden * 2} sides still carrying the memory.");
    }

    /// <summary>Reads each affinity once, from the side that sought it.</summary>
    private static IEnumerable<FigureAffinity> All(WorldState world)
    {
        foreach (Figure figure in world.Figures)
        {
            foreach (FigureAffinity affinity in figure.Affinities)
            {
                if (affinity.OpenerId == figure.Id) yield return affinity;
            }
        }
    }
}
