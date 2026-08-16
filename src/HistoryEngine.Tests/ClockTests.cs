using HistoryEngine.Core;
using HistoryEngine.Events;
using HistoryEngine.Systems;
using HistoryEngine.World;
using Xunit;

namespace HistoryEngine.Tests;

public sealed class StampTests
{
    [Fact]
    public void OrdersByYearThenDay()
    {
        Assert.True(new Stamp(4, 359) < new Stamp(5, 0));
        Assert.True(new Stamp(5, 0) < new Stamp(5, 1));
        Assert.True(new Stamp(5, 90) > new Stamp(5, 89));
        Assert.True(new Stamp(5, 90) >= new Stamp(5, 90));
        Assert.Equal(0, new Stamp(5, 90).CompareTo(new Stamp(5, 90)));
    }

    /// <summary>
    /// A comparison must not need to know how long a year is.
    /// </summary>
    /// <remarks>
    /// The reason <c>Stamp</c> is two integers and not one absolute day. If ordering went through a
    /// calendar, two stamps could compare differently in two worlds — and every sorted structure
    /// holding them would have a configuration-dependent order, which is the class of bug this
    /// engine has a whole test file against.
    /// </remarks>
    [Fact]
    public void OrderingIsIndependentOfAnyCalendar()
    {
        var early = new Stamp(3, 400);
        var late = new Stamp(4, 1);

        Assert.True(early < late);
    }

    [Fact]
    public void OpeningIsDayZero()
    {
        Assert.Equal(new Stamp(7, 0), Stamp.Opening(7));
        Assert.Equal("7.0", Stamp.Opening(7).ToString());
    }
}

public sealed class CalendarTests
{
    [Fact]
    public void TheStandardYearDividesExactly()
    {
        var calendar = new Calendar();

        Assert.Equal(360, calendar.DaysPerYear);
        Assert.Equal(4, calendar.SeasonsPerYear);
        Assert.Equal(90, calendar.DaysPerSeason);

        calendar.Validate();
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(360, 0)]
    [InlineData(100, 3)]
    public void RejectsYearsThatDoNotDivideIntoWholeSeasons(int days, int seasons) =>
        Assert.Throws<InvalidOperationException>(() => new Calendar(days, seasons).Validate());

    [Fact]
    public void AbsoluteDayCountsFromYearZero()
    {
        var calendar = new Calendar();

        Assert.Equal(0L, calendar.AbsoluteDay(new Stamp(0, 0)));
        Assert.Equal(360L, calendar.AbsoluteDay(new Stamp(1, 0)));
        Assert.Equal(450L, calendar.AbsoluteDay(new Stamp(1, 90)));
    }

    /// <summary>
    /// "Forty days from now" is allowed to run past the end of the year.
    /// </summary>
    /// <remarks>
    /// The arithmetic a docket exists to serve produces exactly this stamp, and it must land where
    /// the days say rather than where the year field says. See <c>Docket</c>'s comparer.
    /// </remarks>
    [Fact]
    public void ADayPastTheEndOfItsYearStillCounts()
    {
        var calendar = new Calendar();

        Assert.Equal(
            calendar.AbsoluteDay(new Stamp(4, 40)), calendar.AbsoluteDay(new Stamp(3, 400)));
    }

    /// <summary>A world's calendar is validated with the rest of its config, not on first use.</summary>
    [Fact]
    public void AnImpossibleCalendarFailsConfigValidation()
    {
        var config = new WorldConfig { Calendar = new Calendar(365, 4) };

        Assert.Throws<InvalidOperationException>(() => config.Validate());
    }
}

public sealed class DocketTests
{
    private static readonly EntityId Battle3 = new(EntityKind.Battle, 3);
    private static readonly EntityId Battle7 = new(EntityKind.Battle, 7);

    [Fact]
    public void ComesOffInStampOrderWhateverOrderItWentOnIn()
    {
        var docket = new Docket(new Calendar());

        docket.Schedule(new Stamp(5, 200), DocketKind.Arrival, Battle3);
        docket.Schedule(new Stamp(4, 10), DocketKind.Arrival, Battle3);
        docket.Schedule(new Stamp(5, 20), DocketKind.Arrival, Battle3);
        docket.Schedule(new Stamp(4, 350), DocketKind.Arrival, Battle3);

        Assert.Equal(
            new[] { new Stamp(4, 10), new Stamp(4, 350), new Stamp(5, 20), new Stamp(5, 200) },
            docket.Entries.Select(e => e.Due).ToArray());
    }

    /// <summary>
    /// Two schedulings that differ only in the order they were made produce the same queue.
    /// </summary>
    /// <remarks>
    /// The property the whole type exists for, and the one a <c>PriorityQueue</c> would not have
    /// given: what comes off next is a function of the keys, not of how the structure was filled.
    /// </remarks>
    [Fact]
    public void TheQueueIsAFunctionOfItsKeys()
    {
        var forwards = new Docket(new Calendar());
        var backwards = new Docket(new Calendar());

        (Stamp Due, DocketKind Kind, EntityId Subject)[] work =
        {
            (new Stamp(2, 30), DocketKind.SiegeResolves, Battle3),
            (new Stamp(2, 30), DocketKind.OutbreakStep, Battle7),
            (new Stamp(1, 300), DocketKind.Arrival, Battle3),
            (new Stamp(2, 30), DocketKind.SiegeResolves, Battle7),
        };

        foreach ((Stamp due, DocketKind kind, EntityId subject) in work)
        {
            forwards.Schedule(due, kind, subject);
        }

        for (int i = work.Length - 1; i >= 0; i--)
        {
            backwards.Schedule(work[i].Due, work[i].Kind, work[i].Subject);
        }

        Assert.Equal(
            forwards.Entries.Select(e => (e.Due, e.Kind, e.Subject)).ToArray(),
            backwards.Entries.Select(e => (e.Due, e.Kind, e.Subject)).ToArray());
    }

    /// <summary>
    /// Scheduling an unrelated entry first must not move an existing one.
    /// </summary>
    /// <remarks>
    /// The episodic RNG rule leans on this: a siege's dice must not depend on how many other sieges
    /// were scheduled before it. Order-dependence here would reintroduce that dependency through
    /// the queue rather than through the fork.
    /// </remarks>
    [Fact]
    public void AnUnrelatedSchedulingDoesNotDisturbWhatIsAlreadyQueued()
    {
        var docket = new Docket(new Calendar());

        docket.Schedule(new Stamp(9, 100), DocketKind.SiegeResolves, Battle3);
        DocketEntry before = docket.Entries[0];

        docket.Schedule(new Stamp(3, 1), DocketKind.OutbreakStep, Battle7);
        docket.Schedule(new Stamp(40, 1), DocketKind.Arrival, Battle7);

        Assert.Equal(before, docket.Entries[1]);
    }

    /// <summary>Days ordered across a year boundary, not years then days.</summary>
    [Fact]
    public void ADueDateThatOverrunsItsYearSortsWhereItsDaysPutIt()
    {
        var docket = new Docket(new Calendar());

        docket.Schedule(new Stamp(4, 100), DocketKind.SiegeResolves, Battle3);

        // Day 400 of year three is day 40 of year four, which is earlier.
        docket.Schedule(new Stamp(3, 400), DocketKind.SiegeResolves, Battle3);

        Assert.Equal(new Stamp(3, 400), docket.Entries[0].Due);
    }

    [Fact]
    public void TakesEverythingOwedAtOrBeforeNowAndNothingAfter()
    {
        var docket = new Docket(new Calendar());

        docket.Schedule(new Stamp(1, 10), DocketKind.OutbreakStep, Battle3);
        docket.Schedule(new Stamp(1, 40), DocketKind.OutbreakStep, Battle3);
        docket.Schedule(new Stamp(2, 0), DocketKind.OutbreakStep, Battle3);

        var taken = new List<Stamp>();
        while (docket.TryTakeDue(new Stamp(1, 40), out DocketEntry entry)) taken.Add(entry.Due);

        Assert.Equal(new[] { new Stamp(1, 10), new Stamp(1, 40) }, taken.ToArray());
        Assert.Equal(1, docket.Count);
    }

    /// <summary>A world carries its own docket, so a split run carries it too.</summary>
    [Fact]
    public void EveryWorldHasOne()
    {
        WorldState world = WorldBuilder.Create(TestWorlds.Standard());

        Assert.Equal(0, world.Docket.Count);
        Assert.Equal(Stamp.Opening(world.StartYear), world.Now);
        Assert.Equal(world.Now.Year, world.Year);
    }
}

/// <summary>
/// Cadence is part of what a run is, and an all-annual run is the run it always was.
/// </summary>
public sealed class CadenceTests
{
    /// <summary>
    /// War runs on the season; everything else still runs on the year.
    /// </summary>
    /// <remarks>
    /// The re-phasing is deliberately one system at a time, and this is the record of how far it
    /// has got. Each system that moves changes every history in the world, so a milestone that
    /// moved four of them at once would present one fingerprint change with four calibrations
    /// inside it and no way to read them apart.
    /// </remarks>
    [Fact]
    public void OnlyWarAndExpansionHaveLeftTheYear()
    {
        foreach (ISystem system in Simulator.DefaultSystems())
        {
            Cadence expected = system.Name is "war" or "expansion" ? Cadence.Seasonal : Cadence.Annual;

            Assert.Equal(expected, system.Cadence);
        }
    }

    /// <summary>
    /// An annual system runs once a year, whatever the calendar divides the year into.
    /// </summary>
    /// <remarks>
    /// The property that made it safe to turn the seasonal loop on under sixteen annual systems:
    /// they see the world at the step they always saw it and cannot tell that three more follow.
    /// </remarks>
    [Fact]
    public void AnAnnualSystemStillTicksOncePerYear()
    {
        var counter = new CountingSystem(Cadence.Annual);
        var seasonal = new CountingSystem(Cadence.Seasonal, "seasonal");

        WorldState world = WorldBuilder.Create(TestWorlds.Small());
        new Simulator(new ISystem[] { counter, seasonal }).Advance(world, 3);

        Assert.Equal(3, counter.Ticks);
        Assert.Equal(3 * world.Config.Calendar.SeasonsPerYear, seasonal.Ticks);
    }

    private sealed class CountingSystem : ISystem
    {
        public CountingSystem(Cadence cadence, string name = "counting")
        {
            Cadence = cadence;
            Name = name;
        }

        public string Name { get; }

        public Cadence Cadence { get; }

        public int Ticks { get; private set; }

        public void Tick(WorldState world, Stamp now) => Ticks++;
    }

    /// <summary>
    /// Changing a system's cadence changes the run's identity, exactly as reordering it does.
    /// </summary>
    [Fact]
    public void ADifferentCadenceIsADifferentRun()
    {
        var annual = new Simulator(new ISystem[] { new StubSystem(Cadence.Annual) });
        var seasonal = new Simulator(new ISystem[] { new StubSystem(Cadence.Seasonal) });
        // Episodic through a stub that can actually be woken, since a system with that cadence and
        // nothing to wake it is now rejected where it is written.
        var episodic = new Simulator(new ISystem[] { new StubEpisodic() });

        Assert.NotEqual(annual.SystemOrderHash, seasonal.SystemOrderHash);
        Assert.NotEqual(seasonal.SystemOrderHash, episodic.SystemOrderHash);
    }

    /// <summary>
    /// The annual default does not enter the hash, so the standard order hashes as it always has.
    /// </summary>
    /// <remarks>
    /// The same argument as <c>ConfigTests.TheStandardCalendarDoesNotEnterTheHash</c>: this value
    /// travels in the export, and a run whose history is byte for byte what it always was must not
    /// claim a new identity because cadences became declarable.
    /// </remarks>
    [Fact]
    public void TheAnnualDefaultDoesNotEnterTheHash()
    {
        var simulator = new Simulator(new ISystem[]
        {
            new StubSystem(Cadence.Annual, "one"),
            new StubSystem(Cadence.Annual, "two"),
        });

        ulong hash = Hash.OfString("systems");
        hash = Hash.Combine(hash, Hash.OfString("one"));
        hash = Hash.Combine(hash, Hash.OfString("two"));

        Assert.Equal(
            hash.ToString("x16", System.Globalization.CultureInfo.InvariantCulture),
            simulator.SystemOrderHash);
    }

    /// <summary>An episodic stub that declares a kind, so the simulator will accept it.</summary>
    private sealed class StubEpisodic : ISystem, IEpisodic
    {
        public string Name => "stub";

        public Cadence Cadence => Cadence.Episodic;

        public IReadOnlyList<DocketKind> Handles => new[] { DocketKind.Arrival };

        public void Tick(WorldState world, Stamp now)
        {
        }

        public void Resolve(WorldState world, DocketEntry entry, Stamp now)
        {
        }
    }

    private sealed class StubSystem : ISystem
    {
        public StubSystem(Cadence cadence, string name = "stub")
        {
            Cadence = cadence;
            Name = name;
        }

        public string Name { get; }

        public Cadence Cadence { get; }

        public void Tick(WorldState world, Stamp now)
        {
        }
    }
}

/// <summary>
/// The rule that keeps the log readable once system order and the calendar disagree.
/// </summary>
/// <remarks>
/// Every system in the engine is annual and stamps day zero, so nothing in a real world exercises
/// this yet — which is exactly why it is worth asserting directly rather than waiting for the first
/// seasonal system to be both the thing under test and the thing that proves the test works.
/// </remarks>
public sealed class ChronicleOrderTests
{
    private static readonly EntityId Someone = new(EntityKind.Figure, 1);

    /// <summary>A step's events come out in day order, whatever order the systems wrote them in.</summary>
    [Fact]
    public void AStepsEventsAreOrderedByDayNotBySystem()
    {
        var chronicle = new Chronicle();
        chronicle.OpenStep(Stamp.Opening(7));

        // War runs before succession in the system list, and here it writes the later day.
        chronicle.EnterSystem(3);
        chronicle.Record(7, EventKind.BattleFought, Someone);

        chronicle.EnterSystem(9);
        chronicle.Record(7, EventKind.FigureDied, Someone);

        // Restamp them as a seasonal pair would: the death on day 40, the battle on day 200.
        Restamp(chronicle, 0, day: 200);
        Restamp(chronicle, 1, day: 40);

        chronicle.CloseStep();

        Assert.Equal(EventKind.FigureDied, chronicle.Events[0].Kind);
        Assert.Equal(40, chronicle.Events[0].Day);
        Assert.Equal(EventKind.BattleFought, chronicle.Events[1].Kind);
        Assert.Equal(200, chronicle.Events[1].Day);
    }

    /// <summary>An id still encodes position in the log after the step is put in order.</summary>
    /// <remarks>
    /// The property the export's indices are plain integer arrays because of. Reordering is only
    /// safe while it holds, and it only holds because the ids are reassigned rather than carried.
    /// </remarks>
    [Fact]
    public void IdsStillEncodePositionAfterReordering()
    {
        var chronicle = new Chronicle();
        chronicle.OpenStep(Stamp.Opening(2));

        for (int i = 0; i < 5; i++)
        {
            chronicle.EnterSystem(i);
            chronicle.Record(2, EventKind.FigureBorn, Someone);
            Restamp(chronicle, i, day: 100 - (i * 10));
        }

        chronicle.CloseStep();

        for (int i = 0; i < chronicle.Count; i++)
        {
            Assert.Equal(i, chronicle.Events[i].Id);
        }

        Assert.Equal(60, chronicle.Events[0].Day);
        Assert.Equal(100, chronicle.Events[4].Day);
    }

    /// <summary>
    /// Two events of one day and one system keep the order they were written in.
    /// </summary>
    /// <remarks>
    /// Sequence is the last term of the key, which makes the order total — so the sort has no
    /// freedom to express an opinion, and a step in which nothing claimed a day is left exactly as
    /// it was. That is what lets this land without moving a single existing history.
    /// </remarks>
    [Fact]
    public void EqualStampsKeepTheOrderTheyWereWrittenIn()
    {
        var chronicle = new Chronicle();
        chronicle.OpenStep(Stamp.Opening(1));
        chronicle.EnterSystem(0);

        chronicle.Record(1, EventKind.SettlementFounded, Someone);
        chronicle.Record(1, EventKind.CivilizationFounded, Someone);
        chronicle.Record(1, EventKind.DynastyFounded, Someone);

        chronicle.CloseStep();

        Assert.Equal(EventKind.SettlementFounded, chronicle.Events[0].Kind);
        Assert.Equal(EventKind.CivilizationFounded, chronicle.Events[1].Kind);
        Assert.Equal(EventKind.DynastyFounded, chronicle.Events[2].Kind);
    }

    /// <summary>Events written before any step began are left where they are.</summary>
    /// <remarks>
    /// The world builder writes foundings before the first step opens. They belong to no step, so
    /// no step may reorder them.
    /// </remarks>
    [Fact]
    public void WhatWasWrittenBeforeTheFirstStepIsNotReordered()
    {
        var chronicle = new Chronicle();

        chronicle.Record(1, EventKind.WorldCreated, Someone);
        chronicle.Record(1, EventKind.SettlementFounded, Someone);

        chronicle.OpenStep(Stamp.Opening(1));
        chronicle.EnterSystem(0);
        chronicle.Record(1, EventKind.FigureBorn, Someone);
        Restamp(chronicle, 2, day: 5);
        chronicle.CloseStep();

        Assert.Equal(EventKind.WorldCreated, chronicle.Events[0].Kind);
        Assert.Equal(EventKind.SettlementFounded, chronicle.Events[1].Kind);
        Assert.Equal(EventKind.FigureBorn, chronicle.Events[2].Kind);
    }

    /// <summary>
    /// Gives an already-written event a day, standing in for the seasonal step that will.
    /// </summary>
    /// <remarks>
    /// Reaching into the list rather than opening four steps, because what is under test is the
    /// ordering within one step and no system can yet produce two days inside one.
    /// </remarks>
    private static void Restamp(Chronicle chronicle, int index, int day)
    {
        var events = (List<HistoryEvent>)chronicle.Events;
        events[index] = events[index] with { Day = day };
    }
}

/// <summary>
/// The docket wakes the system that answers for a kind, and nothing else.
/// </summary>
/// <remarks>
/// The queue was built, ordered and tested a milestone before anything could be woken by it. These
/// cover the dispatch that makes it more than a sorted list.
/// </remarks>
public sealed class EpisodicDispatchTests
{
    private static readonly EntityId Subject = new(EntityKind.Battle, 3);

    /// <summary>Due work reaches its owner, once, carrying the stamp it was due at.</summary>
    [Fact]
    public void DueWorkIsHandedToItsOwner()
    {
        var handler = new Recorder(DocketKind.SiegeResolves);
        WorldState world = WorldBuilder.Create(TestWorlds.Small());

        var due = new Stamp(world.StartYear, 40);
        world.Docket.Schedule(due, DocketKind.SiegeResolves, Subject);

        new Simulator(new ISystem[] { handler }).Advance(world, 2);

        Assert.Single(handler.Resolved);
        Assert.Equal(due, handler.Resolved[0].Entry.Due);
        Assert.Equal(0, world.Docket.Count);
    }

    /// <summary>
    /// An episodic system is never woken by the clock, only by the queue.
    /// </summary>
    /// <remarks>
    /// The property that makes the day affordable: a system with nothing scheduled costs nothing,
    /// however many steps the year is divided into.
    /// </remarks>
    [Fact]
    public void AnEpisodicSystemWithNothingScheduledIsNeverRun()
    {
        var handler = new Recorder(DocketKind.Arrival);
        WorldState world = WorldBuilder.Create(TestWorlds.Small());

        new Simulator(new ISystem[] { handler }).Advance(world, 5);

        Assert.Equal(0, handler.Ticks);
        Assert.Empty(handler.Resolved);
    }

    /// <summary>
    /// An episode is recorded on the day it was due, not the day it was noticed.
    /// </summary>
    /// <remarks>
    /// The whole of what the docket buys. Nothing iterates toward day 40 — the step that opens on
    /// day 90 finds it owed and resolves it — and the chronicle still says day 40, because that is
    /// when it happened.
    /// </remarks>
    [Fact]
    public void AnEpisodeIsDatedWhenItWasDueNotWhenItWasNoticed()
    {
        var handler = new Recorder(DocketKind.SiegeResolves, writes: true);
        WorldState world = WorldBuilder.Create(TestWorlds.Small());

        world.Docket.Schedule(new Stamp(world.StartYear, 40), DocketKind.SiegeResolves, Subject);

        new Simulator(new ISystem[] { handler }).Advance(world, 2);

        HistoryEvent written = Assert.Single(
            world.Chronicle.Events.Where(e => e.Kind == EventKind.BattleFought).ToList());

        Assert.Equal(40, written.Day);
        Assert.Equal(world.StartYear, written.Year);

        // Noticed at the step that opens on day 90, which is after the day it is dated.
        Assert.Equal(new Stamp(world.StartYear, 90), handler.Resolved[0].Now);
    }

    /// <summary>Two owners for one kind is rejected where it is written, not where it bites.</summary>
    [Fact]
    public void OneKindMayNotHaveTwoOwners()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => new Simulator(new ISystem[]
            {
                new Recorder(DocketKind.Arrival),
                new Recorder(DocketKind.Arrival, name: "other"),
            }));

        Assert.Contains("both answer", error.Message, StringComparison.Ordinal);
    }

    /// <summary>A system nothing could ever run is rejected rather than quietly idle.</summary>
    [Fact]
    public void AnEpisodicSystemThatNothingCouldWakeIsRejected()
    {
        Assert.Throws<InvalidOperationException>(
            () => new Simulator(new ISystem[] { new Plain(Cadence.Episodic) }));
    }

    /// <summary>An entry nobody owns is a loud failure, not a dropped siege.</summary>
    [Fact]
    public void WorkNobodyAnswersForIsNotSilentlyDropped()
    {
        WorldState world = WorldBuilder.Create(TestWorlds.Small());
        world.Docket.Schedule(Stamp.Opening(world.StartYear), DocketKind.OutbreakStep, Subject);

        var simulator = new Simulator(new ISystem[] { new Recorder(DocketKind.Arrival) });

        Assert.Throws<InvalidOperationException>(() => simulator.Advance(world, 1));
    }

    /// <summary>A system with a cadence and no way to be woken.</summary>
    private sealed class Plain : ISystem
    {
        public Plain(Cadence cadence) => Cadence = cadence;

        public string Name => "plain";

        public Cadence Cadence { get; }

        public void Tick(WorldState world, Stamp now)
        {
        }
    }

    private sealed class Recorder : ISystem, IEpisodic
    {
        private readonly bool _writes;

        public Recorder(DocketKind handles, string name = "recorder", bool writes = false)
        {
            Handles = new[] { handles };
            Name = name;
            _writes = writes;
        }

        public string Name { get; }

        public Cadence Cadence => Cadence.Episodic;

        public IReadOnlyList<DocketKind> Handles { get; }

        public int Ticks { get; private set; }

        public List<(DocketEntry Entry, Stamp Now)> Resolved { get; } = new();

        public void Tick(WorldState world, Stamp now) => Ticks++;

        public void Resolve(WorldState world, DocketEntry entry, Stamp now)
        {
            Resolved.Add((entry, now));

            if (_writes)
            {
                world.Chronicle.Record(entry.Due.Year, EventKind.BattleFought, entry.Subject);
            }
        }
    }
}
