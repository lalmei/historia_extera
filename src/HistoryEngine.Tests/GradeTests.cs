using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.World;
using Xunit;
using Xunit.Abstractions;

namespace HistoryEngine.Tests;

/// <summary>
/// The ladder inside a trade: who climbs it, how far, and what in the world reads it.
/// </summary>
/// <remarks>
/// The premise the office and rank models set — a dimension no other system reads is decoration —
/// so these assert the rest of the engine noticing. A ladder every craftsman reaches the top of has
/// failed as surely as one nobody reaches the top of: mastery is what entitles a man to be named on
/// the work and to speak for his company, and if everybody has it, neither means anything.
/// </remarks>
public sealed class GradeTests
{
    private static readonly ulong[] Panel = { 2, 7, 11, 42, 99 };

    private readonly ITestOutputHelper _out;

    public GradeTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// Every stage is in order, dated, served for, and reached while its holder was alive.
    /// </summary>
    /// <remarks>
    /// The acceptance's four negatives in one walk, because they are all statements about the same
    /// list and a separate run of the panel for each would cost four times as much to say no more.
    /// The one documented exception is the levy — see <see cref="Grades.FoundClaim"/> — which
    /// admits a man the record found already keeping a shop, so his mastery has no term behind it
    /// and is asserted to carry that claim rather than a term.
    /// </remarks>
    [Fact]
    public void NoStageIsOutOfOrderUnservedOrPosthumous()
    {
        int steps = 0;
        int found = 0;

        foreach (ulong seed in Panel)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure figure in world.Figures)
            {
                if (figure.Grades.Count == 0) continue;

                Assert.NotEqual(Craft.None, figure.Craft);

                CraftStep? previous = null;
                foreach (CraftStep step in figure.Grades)
                {
                    steps++;

                    Assert.NotEqual(CraftGrade.None, step.Grade);
                    Assert.True(
                        step.Year >= figure.BirthYear + Succession.MajorityAge,
                        $"seed {seed}: {figure.Name} held a grade before majority");
                    Assert.True(
                        figure.DeathYear is null || step.Year <= figure.DeathYear,
                        $"seed {seed}: {figure.Name} advanced in {step.Year}, dead since {figure.DeathYear}");

                    if (previous is null)
                    {
                        // The first stage is the indenture, or the levy's mastery — nothing else,
                        // and a levied master's whole ladder is that one row: he entered the trade
                        // keeping a shop, so there is no term to have served and no binding to
                        // stack a mastery on top of.
                        if (step.Grade == Grades.Top)
                        {
                            found++;
                            Assert.Equal(Grades.FoundClaim, step.Claim);
                            Assert.Single(figure.Grades);
                        }
                        else
                        {
                            Assert.Equal(CraftGrade.Apprentice, step.Grade);
                        }

                        previous = step;
                        continue;
                    }

                    // Up the ladder one rung at a time, never twice to the same rung.
                    Assert.Equal(Grades.Next(previous.Grade), step.Grade);
                    Assert.True(
                        step.Year - previous.Year >= Grades.Term(previous.Grade),
                        $"seed {seed}: {figure.Name} left {previous.Grade} after "
                        + $"{step.Year - previous.Year} years, term is {Grades.Term(previous.Grade)}");

                    previous = step;
                }
            }
        }

        Assert.True(steps > 0, "the panel recorded no working lives at all");
        Assert.True(found > 0, "no object in the panel needed a maker the record had missed");
        _out.WriteLine($"{steps} stages across the panel, {found} of them a levy's mastery");
    }

    /// <summary>
    /// Every rung is reached, and the men who keep shops stay a minority of the trade.
    /// </summary>
    /// <remarks>
    /// <para>The acceptance's measurement. A third is the shape the model intends and the reason it
    /// is that number is in <see cref="Grades.Establishment"/>: one shop to every three of a
    /// trade's men, asked of the town's own population rather than of the handful of craftsmen the
    /// chronicle names.</para>
    ///
    /// <para>The ceiling is deliberately loose at 45%, because the recorded craftsmen are not a
    /// clean sample of the trade — a levied master is recorded <em>because</em> he kept a shop — and
    /// a test pinned to the panel's exact share would fail on the next calibration without telling
    /// anybody anything. What it refuses is the failure that actually happened while this was being
    /// built: letting an election confer the mastery put 60–69% of every world's craftsmen in
    /// shops.</para>
    /// </remarks>
    [Fact]
    public void MastersStayAMinorityOfTheTrade()
    {
        foreach (ulong seed in Panel)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            var held = new int[4];
            int craftsmen = 0;

            foreach (Figure figure in world.Figures)
            {
                if (figure.Craft == Craft.None) continue;

                craftsmen++;
                held[(int)figure.Grade]++;
            }

            Assert.True(craftsmen > 0, $"seed {seed}: no craftsmen at all");

            // Nobody with a trade is left without a standing in it.
            Assert.Equal(0, held[(int)CraftGrade.None]);

            foreach (CraftGrade grade in new[]
                { CraftGrade.Apprentice, CraftGrade.Journeyman, CraftGrade.Master })
            {
                Assert.True(held[(int)grade] > 0, $"seed {seed}: nobody stands at {grade}");
            }

            double masters = (double)held[(int)CraftGrade.Master] / craftsmen;
            Assert.True(
                masters < 0.45,
                $"seed {seed}: {masters:P1} of craftsmen keep shops, which is not a minority");

            // And the middle of the ladder is where most of a trade is, which is the other half
            // of the same claim: a company is mostly men working for somebody else's shop.
            Assert.True(
                held[(int)CraftGrade.Journeyman] > held[(int)CraftGrade.Master],
                $"seed {seed}: more masters than journeymen");

            _out.WriteLine(
                $"seed {seed}: {craftsmen} craftsmen — apprentice {held[1]}, journeyman {held[2]}, "
                + $"master {held[3]} ({masters:P1})");
        }
    }

    /// <summary>
    /// Every craftsman named on an object was a master when the record names him.
    /// </summary>
    /// <remarks>
    /// The acceptance's "makers recorded on objects are masters", asked of the object rather than
    /// of the person. Tomes are excluded because a tome has an author rather than a maker and
    /// <see cref="Makers.CraftsFor"/> says so by returning no trade for one; the check reads that
    /// same table so the two cannot disagree about which objects are made.
    /// </remarks>
    [Fact]
    public void EveryNamedMakerKeptAShop()
    {
        int made = 0;

        foreach (ulong seed in Panel)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Artifact artifact in world.Artifacts)
            {
                if (artifact.CreatorId.IsNone) continue;
                if (Makers.CraftsFor(artifact.Kind).Length == 0) continue;

                made++;
                Assert.True(world.Figures.Contains(artifact.CreatorId));

                Figure maker = world.Figures[artifact.CreatorId];
                Assert.True(
                    maker.Grade >= Grades.Top,
                    $"seed {seed}: {artifact.Name} is signed by {maker.Name}, a {maker.Grade}");
            }
        }

        Assert.True(made > 0, "the panel made nothing that could name a maker");
        _out.WriteLine($"{made} made objects across the panel, every one signed by a master");
    }

    /// <summary>A company's seat is held by one of the men who keep its shops.</summary>
    /// <remarks>
    /// The coupling that makes the ladder decide something a reader can see: before this, a
    /// mastery went to whoever the record happened to hold in that town and trade, which in a
    /// company of one is nobody's choice at all. Asserted of the grade the holder had reached by
    /// the year he was granted the seat, not of the grade he ended his life at.
    /// </remarks>
    [Fact]
    public void AMasteryIsHeldByAMaster()
    {
        int seats = 0;

        foreach (ulong seed in Panel)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure figure in world.Figures)
            {
                foreach (OfficeHolding held in figure.Offices)
                {
                    if (held.Kind != OfficeKind.GuildMaster) continue;

                    seats++;

                    CraftGrade atGrant = CraftGrade.None;
                    foreach (CraftStep step in figure.Grades)
                    {
                        if (step.Year <= held.FromYear) atGrant = step.Grade;
                    }

                    Assert.True(
                        atGrant >= Grades.Top,
                        $"seed {seed}: {figure.Name} spoke for a company in {held.FromYear} "
                        + $"as a {atGrant}");
                }
            }
        }

        Assert.True(seats > 0, "the panel elected nobody to a mastery");
        _out.WriteLine($"{seats} masteries across the panel, every one held by a master");
    }

    /// <summary>
    /// Only journeymen take to the road for work, and enough of them settle for it to matter.
    /// </summary>
    /// <remarks>
    /// The wander-years, which are the grade's own mechanism: a man with no shop travelled to find
    /// one. Two things are asserted — that nobody else makes the journey, and that some of them end
    /// in a new home, because a wandering that always came home is a journey kind with no
    /// consequence and the craft could not travel by it.
    /// </remarks>
    [Fact]
    public void OnlyJourneymenWanderAndSomeOfThemSettle()
    {
        int walked = 0;
        int settled = 0;

        foreach (ulong seed in Panel)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure figure in world.Figures)
            {
                foreach (Journey journey in figure.Journeys)
                {
                    if (journey.Kind != JourneyKind.Wandering) continue;

                    walked++;
                    Assert.NotEqual(Craft.None, figure.Craft);

                    // He was free of his trade before he set out and had no shop when he did.
                    // Read as two bounds rather than as one grade-at-a-year, because the year has
                    // an order inside it: `travel` runs long before `grades`, so a man can set out
                    // in the spring as a journeyman and be admitted master the same autumn, and a
                    // reading that only compares years would call that a master's journey.
                    int freed = int.MaxValue;
                    int admitted = int.MaxValue;
                    foreach (CraftStep step in figure.Grades)
                    {
                        if (step.Grade == CraftGrade.Journeyman) freed = step.Year;
                        if (step.Grade == Grades.Top) admitted = step.Year;
                    }

                    Assert.True(
                        freed <= journey.Year,
                        $"seed {seed}: {figure.Name} took the road in {journey.Year} "
                        + "before he was free of his trade");
                    Assert.True(
                        admitted >= journey.Year,
                        $"seed {seed}: {figure.Name} kept a shop from {admitted} and went "
                        + $"looking for work in {journey.Year}");

                    // And he walked somewhere else. That the place could work his trade is not
                    // assertable here, and the reason is worth recording: Crafts.Supports reads a
                    // settlement's tier, specialization and routes, all of which move over three
                    // centuries. A town that was a market town with a tannery downstream when he
                    // set out in 140 may be a village on a closed road by 300, so the export's
                    // final state cannot re-ask a gate that was true in the year it was asked.
                    // The gate itself is asserted where it can be — at the call site, on the
                    // world as it then was.
                    Assert.True(world.Settlements.Contains(journey.ToSettlementId));
                    Assert.NotEqual(journey.FromSettlementId, journey.ToSettlementId);

                    if (journey.Outcome == JourneyOutcome.Stayed) settled++;
                }
            }
        }

        Assert.True(walked > 0, "no journeyman in the panel ever went looking for work");
        Assert.True(settled > 0, "every wandering journeyman in the panel came home");
        _out.WriteLine($"{walked} wanderings across the panel, {settled} of them ending in a new home");
    }
}
