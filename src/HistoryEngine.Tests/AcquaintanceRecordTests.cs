using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.World;
using Xunit;
using Xunit.Abstractions;

namespace HistoryEngine.Tests;

public sealed class AcquaintanceRecordTests
{
    private readonly ITestOutputHelper _output;

    public AcquaintanceRecordTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(2UL)]
    [InlineData(7UL)]
    [InlineData(11UL)]
    [InlineData(42UL)]
    [InlineData(99UL)]
    public void MeetingsStayInTheSharedRecordWithoutDuplicatingTheChronicle(ulong seed)
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;
        int meetings = 0;
        int deepened = 0;
        foreach (Figure figure in world.Figures)
        {
            foreach (FigureAffinity affinity in figure.Affinities)
            {
                if (affinity.OpenerId != figure.Id) continue;
                meetings++;
                Assert.Same(affinity, Assert.Single(
                    world.Figures[affinity.FriendId].Affinities,
                    other => other.OpenerId == figure.Id && other.FriendId == affinity.FriendId));
                AffinityAct meeting = affinity.Acts[0];
                Assert.Equal(EventKind.AcquaintanceFormed, meeting.SourceKind);
                Assert.Equal(AffinityStage.Acquaintance, meeting.Stage);
                Assert.Equal(affinity.StartYear, meeting.Year);
                Assert.False(string.IsNullOrWhiteSpace(meeting.Detail));
                foreach (AffinityAct act in affinity.Acts)
                {
                    if (act.SourceKind != EventKind.AffinityDeepened) continue;
                    deepened++;
                    Assert.Contains(world.Chronicle.Events, entry =>
                        entry.Kind == EventKind.AffinityDeepened && entry.Year == act.Year &&
                        entry.Subject == act.ActorId &&
                        entry.Object == (act.ActorId == affinity.OpenerId
                            ? affinity.FriendId : affinity.OpenerId));
                }
            }
        }
        Assert.True(meetings > 0, $"Seed {seed}: no meetings exercised the assertion.");
        Assert.True(deepened > 0, $"Seed {seed}: no later rungs exercised the assertion.");
        Assert.DoesNotContain(world.Chronicle.Events, entry => entry.Kind == EventKind.AcquaintanceFormed);
        _output.WriteLine($"Seed {seed}: {meetings} meetings retained in acts; " +
            $"{deepened} later rungs; {world.Chronicle.Events.Count} chronicle events.");
    }
}
