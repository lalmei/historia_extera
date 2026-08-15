using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.World;
using Xunit;

namespace HistoryEngine.Tests;

/// <summary>
/// That the dynastic machinery actually runs, and that the family tree it produces is coherent.
/// </summary>
/// <remarks>
/// Split the way Milestone 4 taught: the first class asserts that the interesting things
/// <em>happen</em>, because a succession system can be entirely correct and never once be asked to
/// find an heir; the second asserts that the tree they happen in is well formed, because a family
/// tree is a graph and the failures a graph has — a cycle, a one-sided link, a child born after its
/// father died — are all invisible in aggregate and immediately obvious on a single page of the
/// viewer.
/// </remarks>
public sealed class DynastyTests
{
    /// <summary>
    /// A three-century chronicle must contain the whole dynastic repertoire.
    /// </summary>
    /// <remarks>
    /// The Milestone 4 lesson applied to Milestone 5. Every one of these paths can be written
    /// correctly and never execute: houses that never fail because nobody dies young, regencies
    /// that never begin because no ruler predeceases their heir's majority, elections that never
    /// change anything because only one house is ever resident. Asserting the outcomes appear is
    /// the only version of this test with teeth.
    /// </remarks>
    [Fact]
    public void AChronicleContainsTheWholeRepertoireOfSuccession()
    {
        HistoryRun run = HistoryRun.Execute(TestWorlds.Standard());
        Counter counts = Counter.Of(run.World);

        Assert.True(counts[EventKind.FigureMarried] > 100, "Nobody married.");
        Assert.True(counts[EventKind.FigureBorn] > 200, "No children were born.");
        Assert.True(counts[EventKind.DynastyFounded] > 8, "No house rose after the founding.");
        Assert.True(counts[EventKind.DynastyEnded] > 0, "No house ever died out.");
        Assert.True(counts[EventKind.DynastyAscended] > 0, "No crown ever changed house.");
        Assert.True(counts[EventKind.SuccessionDisputed] > 0, "Every succession went uncontested.");
        Assert.True(counts[EventKind.RegencyBegan] > 0, "No child ever inherited.");
        Assert.True(counts[EventKind.RulerTermEnded] > 0, "No elected ruler ever stood down.");
    }

    /// <summary>
    /// Event volume must climb sharply, which is half of what the milestone is for.
    /// </summary>
    /// <remarks>
    /// A floor rather than a range: the brief wants a chronicle of tens of thousands of events, and
    /// this is the milestone where the count stops being governed by how many settlements exist.
    /// Before dynasties the same world produced 950 events and 81 figures.
    /// </remarks>
    [Fact]
    public void DynastiesMultiplyTheChronicle()
    {
        HistoryRun run = HistoryRun.Execute(TestWorlds.Standard());

        Assert.True(
            run.World.Chronicle.Count > 2000,
            $"A standard run produced only {run.World.Chronicle.Count} events.");

        Assert.True(
            run.World.Figures.Count > 400,
            $"A standard run recorded only {run.World.Figures.Count} people.");
    }

    /// <summary>
    /// Every succession must be explicable: an heir of the house, or a recorded change of house.
    /// </summary>
    /// <remarks>
    /// The whole point of the milestone in one assertion. Before it, consecutive rulers were
    /// unrelated strangers and this test could not have been written at all.
    /// </remarks>
    [Fact]
    public void EveryRulerInheritsFromTheirHouseOrTheHouseIsRecordedAsChanging()
    {
        HistoryRun run = HistoryRun.Execute(TestWorlds.Standard());
        WorldState world = run.World;

        var houseChanges = new HashSet<(EntityId Civilization, EntityId House)>();
        foreach (HistoryEvent entry in world.Chronicle.Events)
        {
            if (entry.Kind == EventKind.DynastyAscended)
            {
                houseChanges.Add((entry.Object, entry.Subject));
            }
        }

        int inherited = 0;

        foreach (Civilization civilization in world.Civilizations)
        {
            for (int i = 1; i < civilization.RulerIds.Count; i++)
            {
                Figure previous = world.Figures[civilization.RulerIds[i - 1]];
                Figure next = world.Figures[civilization.RulerIds[i]];

                if (next.DynastyId == previous.DynastyId)
                {
                    inherited++;
                    continue;
                }

                Assert.True(
                    houseChanges.Contains((civilization.Id, next.DynastyId)),
                    $"{next.Name} followed {previous.Name} in {civilization.Name} out of a " +
                    "different house, with no recorded change of house.");
            }
        }

        Assert.True(inherited > 20, "Almost no throne was inherited within its own house.");
    }

    /// <summary>Nobody sits on two thrones. A personal union is Milestone 6's business.</summary>
    [Fact]
    public void NoFigureRulesTwoRealmsAtOnce()
    {
        HistoryRun run = HistoryRun.Execute(TestWorlds.Standard());

        var thrones = new Dictionary<EntityId, EntityId>();

        foreach (Civilization civilization in run.World.Civilizations)
        {
            if (civilization.CurrentRulerId.IsNone) continue;

            Assert.False(
                thrones.ContainsKey(civilization.CurrentRulerId),
                $"{run.World.NameOf(civilization.CurrentRulerId)} holds two thrones at once.");

            thrones[civilization.CurrentRulerId] = civilization.Id;
        }
    }

    /// <summary>
    /// A standing realm has a living ruler who lives in it.
    /// </summary>
    /// <remarks>
    /// <para>The invariant a whole class of silent failures hides behind, and the reason this test
    /// exists rather than being assumed. Marriage relocated whichever partner had the weaker claim,
    /// including a reigning one — and a ruler living abroad has their death routed to the wrong
    /// realm, so the realm they actually ruled keeps a dead ruler on record for ever and is skipped
    /// by succession every year after.</para>
    ///
    /// <para>Nothing about that is visible in aggregate: the event count merely comes out lower
    /// than it would have, which during calibration reads as a dial needing a turn. Three realms
    /// in a three-century run had been governed by the dead for over a century each.</para>
    /// </remarks>
    [Fact]
    public void NoStandingRealmIsRuledByTheDeadOrTheAbsent()
    {
        HistoryRun run = HistoryRun.Execute(TestWorlds.Standard() with { Years = 500 });
        WorldState world = run.World;

        foreach (Civilization civilization in world.ActiveCivilizations())
        {
            Assert.False(
                civilization.CurrentRulerId.IsNone,
                $"{civilization.Name} still stands but has no ruler at all.");

            Figure ruler = world.Figures[civilization.CurrentRulerId];

            Assert.True(ruler.IsAlive, $"{civilization.Name} is ruled by {ruler.Name}, who is dead.");
            Assert.Equal(civilization.Id, ruler.CivilizationId);
        }
    }

    /// <summary>Reigns must not run past a human lifetime, which is what a stale throne looks like.</summary>
    /// <remarks>
    /// The same fault seen from the other side, and the cheaper signal: a realm whose ruler is
    /// never replaced reports a reign of two centuries long before anyone thinks to check whether
    /// the ruler is alive.
    /// </remarks>
    [Fact]
    public void NoReignOutlastsItsHolder()
    {
        HistoryRun run = HistoryRun.Execute(TestWorlds.Standard() with { Years = 500 });
        WorldState world = run.World;

        foreach (Figure figure in world.Figures)
        {
            foreach (OfficeHolding holding in figure.Offices)
            {
                int ended = holding.ToYear ?? world.EndYear;

                Assert.True(
                    ended <= (figure.DeathYear ?? world.EndYear),
                    $"{figure.Name} held {holding.Title} until {ended}, having died in " +
                    $"{figure.DeathYear}.");

                Assert.True(
                    ended - holding.FromYear <= 100,
                    $"{figure.Name} held {holding.Title} for {ended - holding.FromYear} years.");
            }
        }
    }

    /// <summary>A people that will not crown its own daughters will not crown anyone else's.</summary>
    /// <remarks>
    /// Worth asserting separately because the law is applied in the line of succession, and the
    /// paths around it — a widowed consort's claim, a resident of another house, a house founded
    /// from nowhere — each had to be taught it independently.
    /// </remarks>
    [Fact]
    public void AgnaticRealmsAreNeverRuledByAWoman()
    {
        // Over several worlds rather than one. Whether any realm happens to roll agnatic
        // succession is a property of the seed, so a single world can leave this asserting
        // nothing — and did, once M10 changed every history and seed 42 came up with none.
        // Checking more worlds is also a strictly stronger test of the invariant itself.
        int checkedRulers = 0;

        foreach (ulong seed in new ulong[] { 42, 2, 7, 11, 99 })
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Civilization civilization in world.Civilizations)
            {
                if (world.CultureOf(civilization).Succession != SuccessionLaw.Agnatic) continue;

                foreach (EntityId id in civilization.RulerIds)
                {
                    Figure ruler = world.Figures[id];

                    Assert.True(
                        ruler.Sex == Sex.Male,
                        $"{ruler.Name} held {civilization.Name} in seed {seed}, which inherits "
                        + "in the male line only.");

                    checkedRulers++;
                }
            }
        }

        Assert.True(checkedRulers > 0, "No agnatic realm was generated to check.");
    }

    /// <summary>A minor on a throne is governed for, and the regency ends when they come of age.</summary>
    [Fact]
    public void ChildRulersAreGovernedForUntilTheyComeOfAge()
    {
        HistoryRun run = HistoryRun.Execute(TestWorlds.Standard());
        WorldState world = run.World;

        int regencies = 0;

        foreach (HistoryEvent entry in world.Chronicle.Events)
        {
            if (entry.Kind == EventKind.RegencyBegan)
            {
                Figure regent = world.Figures[entry.Subject];
                Figure ward = world.Figures[entry.Object];

                Assert.True(
                    regent.AgeIn(entry.Year) >= Succession.MajorityAge,
                    $"{regent.Name} governed as regent while a child.");

                Assert.True(
                    ward.AgeIn(entry.Year) < Succession.MajorityAge,
                    $"{ward.Name} was given a regent at {ward.AgeIn(entry.Year)}.");

                regencies++;
            }

            if (entry.Kind == EventKind.RegencyEnded)
            {
                Figure ruler = world.Figures[entry.Subject];

                Assert.True(
                    ruler.AgeIn(entry.Year) >= Succession.MajorityAge,
                    $"{ruler.Name}'s regency ended at {ruler.AgeIn(entry.Year)}.");
            }
        }

        Assert.True(regencies > 0, "No regency occurred, so nothing was checked.");
    }

    /// <summary>A house is extinct exactly when the last of its blood has died.</summary>
    [Fact]
    public void HousesAreMarkedExtinctPreciselyWhenTheirBloodRunsOut()
    {
        HistoryRun run = HistoryRun.Execute(TestWorlds.Standard() with { Years = 500 });
        WorldState world = run.World;

        int extinct = 0;

        foreach (Dynasty house in world.Dynasties)
        {
            bool anyAlive = false;
            foreach (EntityId id in house.MemberIds)
            {
                if (world.Figures[id].IsAlive) anyAlive = true;
            }

            if (house.IsExtinct)
            {
                extinct++;
                Assert.False(anyAlive, $"The {house.Name} is marked extinct but has living blood.");
            }
            else
            {
                Assert.True(
                    anyAlive,
                    $"The {house.Name} has no living blood but is not recorded as having ended.");
            }
        }

        Assert.True(extinct > 0, "No house died out in five centuries.");
    }

    private sealed class Counter
    {
        private readonly Dictionary<EventKind, int> _counts = new();

        public int this[EventKind kind] => _counts.TryGetValue(kind, out int n) ? n : 0;

        public static Counter Of(WorldState world)
        {
            var counter = new Counter();

            foreach (HistoryEvent entry in world.Chronicle.Events)
            {
                counter._counts[entry.Kind] = counter[entry.Kind] + 1;
            }

            return counter;
        }
    }
}

/// <summary>
/// Structural invariants of the family tree itself.
/// </summary>
/// <remarks>
/// These are the failures that a chronicle full of plausible prose hides completely. A child listed
/// by a father who does not list them back renders as an empty family on one page and a full one on
/// the next; a marriage recorded on one side only breaks a widow's claim to a throne; a cycle in
/// descent hangs the line-of-succession walk. None of it shows up in an event count.
/// </remarks>
public sealed class FamilyTreeTests
{
    [Fact]
    public void ParentAndChildLinksAgreeWithEachOther()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard()).World;

        foreach (Figure figure in world.Figures)
        {
            foreach (EntityId childId in figure.ChildIds)
            {
                Figure child = world.Figures[childId];

                Assert.True(
                    child.MotherId == figure.Id || child.FatherId == figure.Id,
                    $"{figure.Name} claims {child.Name} as a child, who claims neither parent.");
            }

            foreach (EntityId parentId in figure.Parents())
            {
                Assert.Contains(figure.Id, world.Figures[parentId].ChildIds);
            }
        }
    }

    /// <summary>
    /// Descent must be a tree, not a graph with a loop in it.
    /// </summary>
    /// <remarks>
    /// The line-of-succession walk recurses through children and iterates upward through parents,
    /// and a single cycle would hang a run rather than produce a wrong answer. Birth years make one
    /// impossible today — a parent is always older — which is exactly why this should be asserted
    /// rather than assumed, since nothing enforces that ordering at the point the links are made.
    /// </remarks>
    [Fact]
    public void NobodyIsTheirOwnAncestor()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard()).World;

        foreach (Figure figure in world.Figures)
        {
            var frontier = new List<EntityId>(figure.Parents());

            for (int depth = 0; depth < 64 && frontier.Count > 0; depth++)
            {
                var next = new List<EntityId>();

                foreach (EntityId id in frontier)
                {
                    Assert.NotEqual(figure.Id, id);
                    next.AddRange(world.Figures[id].Parents());
                }

                frontier = next;
            }

            Assert.Empty(frontier);
        }
    }

    /// <summary>Children arrive while both parents are alive and old enough to have them.</summary>
    [Fact]
    public void ChildrenAreBornWithinTheirParentsLives()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard()).World;

        foreach (Figure child in world.Figures)
        {
            foreach (EntityId parentId in child.Parents())
            {
                Figure parent = world.Figures[parentId];
                int parentAge = child.BirthYear - parent.BirthYear;

                Assert.True(
                    parentAge >= 16,
                    $"{parent.Name} was {parentAge} when {child.Name} was born.");

                Assert.True(
                    parent.DeathYear is null || child.BirthYear <= parent.DeathYear.Value,
                    $"{child.Name} was born in {child.BirthYear}, after {parent.Name} died in " +
                    $"{parent.DeathYear}.");
            }

            if (child.MotherId.IsNone) continue;

            int motherAge = child.BirthYear - world.Figures[child.MotherId].BirthYear;
            Assert.InRange(motherAge, 16, 45);
        }
    }

    /// <summary>Marriages are mutual, between adults, between the living, and not between kin.</summary>
    [Fact]
    public void MarriagesAreMutualAndProperlyMade()
    {
        HistoryRun run = HistoryRun.Execute(TestWorlds.Standard());
        WorldState world = run.World;

        foreach (Figure figure in world.Figures)
        {
            foreach (EntityId spouseId in figure.SpouseIds)
            {
                Assert.Contains(figure.Id, world.Figures[spouseId].SpouseIds);
            }

            if (figure.IsMarried)
            {
                Assert.Equal(figure.Id, world.Figures[figure.SpouseId].SpouseId);
                Assert.True(world.Figures[figure.SpouseId].IsAlive, "A living figure is married to a dead one.");
            }
        }

        int marriages = 0;
        int betweenHouses = 0;

        foreach (HistoryEvent entry in world.Chronicle.Events)
        {
            if (entry.Kind != EventKind.FigureMarried) continue;

            Figure a = world.Figures[entry.Subject];
            Figure b = world.Figures[entry.Object];

            Assert.NotEqual(a.Sex, b.Sex);
            Assert.True(a.AgeIn(entry.Year) >= 16 && b.AgeIn(entry.Year) >= 16, "A child was married.");
            Assert.True(
                a.DeathYear is null || a.DeathYear.Value >= entry.Year, $"{a.Name} married after dying.");
            Assert.True(
                b.DeathYear is null || b.DeathYear.Value >= entry.Year, $"{b.Name} married after dying.");
            Assert.False(
                Succession.AreCloseKin(world, a, b), $"{a.Name} married their near kin {b.Name}.");
            Assert.NotEqual(a.DynastyId, b.DynastyId);

            marriages++;
            if (!a.DynastyId.IsNone && !b.DynastyId.IsNone) betweenHouses++;
        }

        Assert.True(marriages > 100, "Too few marriages to judge.");
        Assert.True(betweenHouses > 0, "No marriage ever joined two houses.");
    }

    /// <summary>
    /// Blood membership of a house and a figure's own house are the same fact.
    /// </summary>
    /// <remarks>
    /// The two are written in different places — the child's <see cref="Figure.DynastyId"/> when it
    /// is born, and the house's roster when it is added — and everything about extinction depends
    /// on them agreeing.
    /// </remarks>
    [Fact]
    public void HouseRostersAndFiguresAgreeOnWhoIsBlood()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard()).World;

        int members = 0;

        foreach (Dynasty house in world.Dynasties)
        {
            foreach (EntityId id in house.MemberIds)
            {
                Assert.Equal(house.Id, world.Figures[id].DynastyId);
                members++;
            }
        }

        foreach (Figure figure in world.Figures)
        {
            if (figure.DynastyId.IsNone) continue;

            Assert.Contains(figure.Id, world.Dynasties[figure.DynastyId].MemberIds);
        }

        Assert.True(members > 100, "Too few house members to judge.");
    }
}
