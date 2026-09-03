using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.Serialization;
using HistoryEngine.World;
using Xunit;
using Xunit.Abstractions;

namespace HistoryEngine.Tests;

/// <summary>
/// Friendships between two named people: what allows one, how far it goes, and how it ends.
/// </summary>
public sealed class AffinityTests
{
    /// <summary>
    /// Seeds that carry a betrayal, which is the scarce ending.
    /// </summary>
    /// <remarks>
    /// <para>Every seed with a standing realm produces friendships in the hundreds, so the panel is
    /// chosen entirely on the rare outcome: a friend turning on the other appears in five of the
    /// first forty seeds and never more than once in a world. That is a property of the gate rather
    /// than of the odds — a betrayal needs a wrong the world already recorded between the two of
    /// them, and the engine's wrongs are almost all vertical (a ruler and the man he dismissed, an
    /// heir and the claimant he beat) while friendships are almost all horizontal, because rank is
    /// one of the things that keeps two people from becoming friends in the first place. Raising
    /// the chance would not fix that; giving peers something to fall out over would. See the
    /// decision log.</para>
    ///
    /// <para>Seeds 20, 24, 28, 31 and 34 are unusable here and it is not this model's doing: they
    /// produce no figures at all.</para>
    /// </remarks>
    private static readonly ulong[] Seeds = { 5, 12, 15, 22, 26 };

    private readonly ITestOutputHelper _output;

    public AffinityTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Nobody befriends themselves, and nobody befriends a stranger they never shared a town with.
    /// </summary>
    /// <remarks>
    /// The second half is the one worth having, for the reason the same test on quarrels is worth
    /// having: a model that can produce warmth between two people who merely exist in the same
    /// realm produces a great deal of it and none of it means anything. Every friendship here
    /// names the place that made it possible and the year both of them were grown and standing in
    /// it.
    /// </remarks>
    [Fact]
    public void FriendshipsNeedTwoGrownPeopleAndATownTheyShared()
    {
        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;
            int counted = 0;

            foreach (FigureAffinity affinity in All(world))
            {
                Assert.NotEqual(affinity.OpenerId, affinity.FriendId);
                Assert.True(world.Figures.Contains(affinity.OpenerId));
                Assert.True(world.Figures.Contains(affinity.FriendId));

                Figure opener = world.Figures[affinity.OpenerId];
                Figure friend = world.Figures[affinity.FriendId];

                Assert.True(
                    affinity.SourceKind is EventKind.AcquaintanceFormed
                        or EventKind.BattleFought
                        or EventKind.OfficeGranted,
                    $"Seed {seed}: a friendship came from {affinity.SourceKind}, which is not one "
                    + "of the recorded circumstances.");
                Assert.False(affinity.SourceEntityId.IsNone);

                // The town is a real one, which is what makes the beginning answerable at all.
                Assert.True(
                    world.Settlements.Contains(affinity.PlaceId),
                    $"Seed {seed}: a friendship names a place that is not a settlement.");

                // Both were alive and grown when it began, and both were there to begin it.
                Assert.True(opener.BirthYear + Succession.MajorityAge <= affinity.StartYear);
                Assert.True(friend.BirthYear + Succession.MajorityAge <= affinity.StartYear);
                Assert.True((opener.DeathYear ?? world.EndYear) >= affinity.StartYear);
                Assert.True((friend.DeathYear ?? world.EndYear) >= affinity.StartYear);

                Assert.NotEmpty(affinity.Acts);
                Assert.Equal(affinity.StartYear, affinity.Acts[0].Year);
                Assert.Equal(AffinityStage.Acquaintance, affinity.Acts[0].Stage);
                counted++;
            }

            Assert.True(counted > 0, $"Seed {seed} produced no friendship at all.");
        }
    }

    /// <summary>A friendship is one fact about two lives, and both of them carry the same one.</summary>
    [Fact]
    public void BothPartiesCarryTheSameEpisodeFromTheirOwnSide()
    {
        HistoryRun run = HistoryRun.Execute(TestWorlds.Standard(5));
        WorldState world = run.World;
        WorldExport export = run.ToExport();
        int checkedPairs = 0;

        foreach (FigureAffinity affinity in All(world))
        {
            Figure opener = world.Figures[affinity.OpenerId];
            Figure friend = world.Figures[affinity.FriendId];

            Assert.Contains(affinity, opener.Affinities);
            Assert.Contains(affinity, friend.Affinities);
            Assert.Equal(friend.Id, affinity.Other(opener.Id));
            Assert.Equal(opener.Id, affinity.Other(friend.Id));

            ExportAffinity fromOpener = Exported(export, opener, affinity);
            ExportAffinity fromFriend = Exported(export, friend, affinity);

            // The viewpoint differs; every fact under it is identical.
            Assert.True(fromOpener.Sought);
            Assert.False(fromFriend.Sought);
            Assert.Equal(friend.Id, fromOpener.OtherId);
            Assert.Equal(opener.Id, fromFriend.OtherId);
            Assert.Equal(fromOpener.Stage, fromFriend.Stage);
            Assert.Equal(fromOpener.Outcome, fromFriend.Outcome);
            Assert.Equal(fromOpener.Origin, fromFriend.Origin);
            Assert.Equal(fromOpener.Resolution, fromFriend.Resolution);
            Assert.Equal(fromOpener.StartYear, fromFriend.StartYear);
            Assert.Equal(fromOpener.EndYear, fromFriend.EndYear);
            Assert.Equal(fromOpener.BetrayerId, fromFriend.BetrayerId);
            Assert.Equal(fromOpener.Acts.Count, fromFriend.Acts.Count);
            checkedPairs++;
        }

        Assert.True(checkedPairs > 0, "Seed 5 produced no friendship to read from both sides.");
    }

    /// <summary>
    /// The ladder is climbed a rung at a time, and never more than one rung in a year.
    /// </summary>
    /// <remarks>
    /// This is the whole claim the record makes: a friendship in the export shows the years it took
    /// to become one. A model that could jump from an introduction to a confidence in the same pass
    /// would be back to declaring relationships rather than growing them.
    /// </remarks>
    [Fact]
    public void EveryRungIsWalkedInOrderAndAtMostOnePerYear()
    {
        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (FigureAffinity affinity in All(world))
            {
                int previousYear = int.MinValue;
                AffinityStage reached = AffinityStage.Acquaintance;
                int climbedThisYear = 0;

                foreach (AffinityAct act in affinity.Acts)
                {
                    Assert.True(
                        act.Year >= previousYear,
                        $"Seed {seed}: a friendship's acts are out of order at {act.Year}.");
                    Assert.True(
                        (int)act.Stage <= (int)reached + 1,
                        $"Seed {seed}: a friendship jumped from {reached} to {act.Stage}.");

                    climbedThisYear = act.Year == previousYear ? climbedThisYear : 0;
                    if (act.Stage > reached)
                    {
                        climbedThisYear++;
                        Assert.True(
                            climbedThisYear == 1,
                            $"Seed {seed}: a friendship climbed twice in {act.Year}.");
                        reached = act.Stage;
                    }

                    Assert.True(
                        affinity.Involves(act.ActorId),
                        $"Seed {seed}: a friendship's act was done by somebody not in it.");
                    previousYear = act.Year;
                }

                Assert.Equal(reached, affinity.Stage);
            }
        }
    }

    /// <summary>
    /// No friendship outlives the people in it, and none survives its own ending.
    /// </summary>
    [Fact]
    public void NoFriendshipContinuesPastDeathDistanceOrItsOwnEnd()
    {
        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (FigureAffinity affinity in All(world))
            {
                Figure opener = world.Figures[affinity.OpenerId];
                Figure friend = world.Figures[affinity.FriendId];

                if (affinity.IsOpen)
                {
                    Assert.True(
                        opener.IsAlive && friend.IsAlive,
                        $"Seed {seed}: a friendship begun in {affinity.StartYear} still stands "
                        + "with a dead party in it.");
                    Assert.Null(affinity.EndYear);

                    // Deliberately not asserting that the two are still in the same realm. A
                    // border that moves after the year's friendship pass — a secession, a treaty,
                    // an accession — is closed as Parted by the next pass, so mid-run cases heal
                    // themselves; the final year has no next pass, and a run that ends the year a
                    // realm broke up can honestly export a standing friendship across the new
                    // border. Parting is exercised by the hundreds of Parted endings instead.
                    continue;
                }

                Assert.NotNull(affinity.EndYear);
                Assert.NotNull(affinity.Resolution);
                Assert.True(affinity.EndYear >= affinity.StartYear);
                Assert.All(affinity.Acts, act => Assert.True(act.Year <= affinity.EndYear));
            }
        }
    }

    /// <summary>
    /// A betrayal needs something to betray, and a reason the world already wrote down.
    /// </summary>
    /// <remarks>
    /// The gate, asserted from the other end. Every turn in the export must sit at a rung where
    /// trust had actually been given, must name which of the two turned, and must leave the wronged
    /// party a memory and an enmity that point back at the person who did it.
    /// </remarks>
    [Fact]
    public void ABetrayalOnlyHappensWhereTrustWasGivenAndAReasonExisted()
    {
        int betrayals = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (FigureAffinity affinity in All(world))
            {
                if (affinity.Outcome != AffinityOutcome.Betrayed) continue;

                Assert.True(
                    affinity.Stage >= AffinityStage.Confidence,
                    $"Seed {seed}: a friendship was betrayed at {affinity.Stage}, before anything "
                    + "had been entrusted.");
                Assert.True(affinity.Involves(affinity.BetrayerId));

                Figure betrayer = world.Figures[affinity.BetrayerId];
                Figure betrayed = world.Figures[affinity.Other(affinity.BetrayerId)];

                SalientMemory? wound = betrayed.Memories.Find(
                    memory => memory.Kind == MemoryKind.Betrayal
                        && memory.AboutId == betrayer.Id);
                // The kind and the person, not the source: a later betrayal by the same man —
                // informing on a plot they were both in — reinforces this memory and takes over
                // its source, which is the memory model working as intended.
                Assert.NotNull(wound);
                Assert.True(wound!.Year >= affinity.StartYear);

                FigureBond? bond = LifeStories.BondTo(betrayed, betrayer.Id);
                Assert.NotNull(bond);
                Assert.True(
                    bond!.Kinds.HasFlag(BondKind.Enemy),
                    $"Seed {seed}: a betrayal left no enmity behind it.");

                // The friendship itself is not erased by it. Two people who were friends and are
                // now enemies are not two people who were never friends.
                if (affinity.Stage == AffinityStage.Friendship)
                {
                    Assert.True(bond.Kinds.HasFlag(BondKind.Friend));
                }

                _output.WriteLine(
                    $"seed {seed}: {betrayer.FullName} turned on {betrayed.FullName} in "
                    + $"{affinity.EndYear} at {affinity.Stage}, {affinity.EndYear - affinity.StartYear} "
                    + "years in.");
                betrayals++;
            }
        }

        Assert.True(betrayals > 0, "The panel produced no betrayal, which is what it is for.");
    }

    /// <summary>
    /// The <see cref="BondKind.Friend"/> flag is set by the ladder and by nothing else.
    /// </summary>
    /// <remarks>
    /// The flag was declared years before anything wrote it, and the point of this pass is that it
    /// now has exactly one write site. A second one appearing later — a shortcut that declares two
    /// people friends because it needed them to be — is the regression this catches.
    /// </remarks>
    [Fact]
    public void EveryFriendBondIsBackedByAFriendshipThatReachedTheTop()
    {
        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;
            int backed = 0;

            foreach (Figure figure in world.Figures)
            {
                foreach (FigureBond bond in figure.Bonds)
                {
                    if (!bond.Kinds.HasFlag(BondKind.Friend)) continue;

                    FigureAffinity? affinity = figure.Affinities.Find(
                        candidate => candidate.Involves(bond.OtherId));
                    Assert.NotNull(affinity);
                    Assert.Equal(AffinityStage.Friendship, affinity!.Stage);
                    Assert.True(affinity.StartYear <= bond.LastChangedYear);
                    backed++;
                }
            }

            Assert.True(backed > 0, $"Seed {seed} produced no friend bond.");
        }
    }

    /// <summary>
    /// Friendship does not crowd the memory list.
    /// </summary>
    /// <remarks>
    /// The list is twelve long and it already holds bereavements, wounds and hardships. A new
    /// category that arrived loud would evict them and the life pages would get worse rather than
    /// richer, which is why a favour is remembered faintly and a standing friendship is reinforced
    /// rather than re-added. This is the guard on that judgement, not on the feature.
    /// </remarks>
    [Fact]
    public void FriendshipDoesNotEvictTheThingsAPageIsFor()
    {
        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;
            int friendship = 0;
            int bereavement = 0;
            int total = 0;

            foreach (Figure figure in world.Figures)
            {
                Assert.True(figure.Memories.Count <= LifeStories.MemoryCapacity);

                foreach (SalientMemory memory in figure.Memories)
                {
                    total++;
                    if (memory.Kind == MemoryKind.Friendship) friendship++;
                    if (memory.Kind == MemoryKind.Bereavement) bereavement++;
                }
            }

            Assert.True(total > 0);
            Assert.True(
                friendship * 4 < total,
                $"Seed {seed}: friendship is {friendship} of {total} memories, which is enough to "
                + "be pushing other things out.");
            Assert.True(
                bereavement > friendship,
                $"Seed {seed}: friendship ({friendship}) has overtaken bereavement "
                + $"({bereavement}) in the memory lists.");
        }
    }

    /// <summary>
    /// A favour is asymmetric, and the gratitude lands on the person who received it.
    /// </summary>
    /// <remarks>
    /// Asserted in aggregate rather than per favour, because a memory list is twelve long and
    /// fades: any single gratitude may have been evicted by the time the run ends, and a giver may
    /// separately owe their receiver for something else entirely. What must not be true is that the
    /// asymmetry runs the other way, and across a world's favours it plainly would if the receiver
    /// and the giver had been swapped.
    /// </remarks>
    [Fact]
    public void AFavourLeavesTheGratitudeWithThePersonWhoReceivedIt()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard(5)).World;
        int favours = 0;
        int onTheReceiver = 0;
        int onTheGiver = 0;

        foreach (FigureAffinity affinity in All(world))
        {
            AffinityAct? favour = affinity.Acts.Find(
                act => act.Stage == AffinityStage.Kindness
                    && act.SourceKind == EventKind.AffinityDeepened);
            if (favour is null) continue;

            Assert.True(affinity.Involves(favour.ActorId));

            Figure giver = world.Figures[favour.ActorId];
            Figure receiver = world.Figures[affinity.Other(favour.ActorId)];

            if (Grateful(receiver, giver)) onTheReceiver++;
            if (Grateful(giver, receiver)) onTheGiver++;
            favours++;
        }

        Assert.True(favours > 0, "Seed 5 produced no favour between friends.");
        Assert.True(
            onTheReceiver > onTheGiver,
            $"Of {favours} favours, {onTheReceiver} left the receiver grateful and "
            + $"{onTheGiver} left the giver grateful, which is the wrong way round.");
    }

    private static bool Grateful(Figure figure, Figure toward) =>
        figure.Memories.Exists(
            memory => memory.Kind == MemoryKind.Gratitude && memory.AboutId == toward.Id);

    /// <summary>Reads each friendship once, from the side that sought it.</summary>
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

    private static ExportAffinity Exported(
        WorldExport export, Figure figure, FigureAffinity affinity)
    {
        ExportFigure exported = export.Figures.Single(candidate => candidate.Id == figure.Id);
        return exported.Affinities.Single(
            candidate => candidate.Id == affinity.Id
                && candidate.OtherId == affinity.Other(figure.Id));
    }
}
