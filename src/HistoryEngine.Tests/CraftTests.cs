using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.World;
using Xunit;
using Xunit.Abstractions;

namespace HistoryEngine.Tests;

/// <summary>
/// What a guildsman's trade must be true of: it has to be a trade the place could support, it has
/// to run in families, and no single craft may swallow the world's guilds.
/// </summary>
public sealed class CraftTests
{
    private static readonly ulong[] Panel = { 2, 7, 11, 42, 99 };

    private readonly ITestOutputHelper _out;

    public CraftTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// Assignment never hands somebody a trade the place they live in cannot support.
    /// </summary>
    /// <remarks>
    /// <para>Asked by re-running the choice against a finished world rather than by inspecting the
    /// crafts a run produced, and the difference matters. Assignment reads the settlement somebody
    /// lived in the year they came of age, and everything it read moves afterwards: people migrate,
    /// towns are taken and abandoned, and trade routes close — a sailor in a port whose last route
    /// died is a true record, not a violated gate. Re-deciding here puts the question and the world
    /// at the same instant, which is the only way to ask it exactly.</para>
    /// </remarks>
    [Fact]
    public void AssignmentNeverPicksACraftThePlaceCannotSupport()
    {
        int decided = 0;

        foreach (ulong seed in Panel)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure figure in world.Figures)
            {
                if (!figure.IsAlive || figure.Occupation != Occupation.Guild) continue;

                EntityId home = world.ResidenceOf(figure);
                if (!world.Settlements.Contains(home)) continue;

                figure.Craft = Craft.None;
                Crafts.Ensure(world, figure, world.Year);

                if (figure.Craft == Craft.None) continue;

                decided++;
                Assert.True(
                    Crafts.Supports(world, world.Settlements[home], figure.Craft),
                    $"seed {seed}: {world.Settlements[home].Name} cannot support {Crafts.Label(figure.Craft)}");
            }
        }

        Assert.True(decided > 0, "the panel decided no crafts at all");
    }

    /// <summary>Only guildsmen have a craft, and a craft is never unlearned.</summary>
    /// <remarks>
    /// The second half is why this is not simply an equality. A mason who is made guild master
    /// holds <see cref="Occupation.Guild"/> still, but a mason appointed governor holds office, and
    /// a mason who dies in office dies an official — in all three cases they are still a mason, and
    /// that is the fact the record is supposed to keep.
    /// </remarks>
    [Fact]
    public void ACraftIsOnlyTakenInAGuildAndIsNeverUnlearned()
    {
        foreach (ulong seed in Panel)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure figure in world.Figures)
            {
                if (figure.Craft == Craft.None) continue;

                bool everInAGuild = figure.Occupation == Occupation.Guild
                    || figure.PriorOccupation == Occupation.Guild
                    || figure.Offices.Exists(office => office.Kind == OfficeKind.GuildMaster);

                Assert.True(
                    everInAGuild,
                    $"seed {seed}: {figure.Name} holds a craft without ever having been in a guild");
            }
        }
    }

    /// <summary>The same seed always produces the same crafts, for the same people.</summary>
    [Fact]
    public void CraftsAreDeterministic()
    {
        foreach (ulong seed in new ulong[] { 7, 42 })
        {
            WorldState first = HistoryRun.Execute(TestWorlds.Standard(seed)).World;
            WorldState second = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            Assert.Equal(first.Figures.Count, second.Figures.Count);

            foreach (Figure figure in first.Figures)
            {
                Assert.Equal(figure.Craft, second.Figures[figure.Id].Craft);
            }
        }
    }

    /// <summary>
    /// A guild runs in families, and harder than a career does.
    /// </summary>
    /// <remarks>
    /// The whole reason <see cref="Crafts"/> has its own pull constant. If a craftsman's children
    /// took up the family trade at chance rates there would be no craft dynasties to read, and the
    /// craft would be a label on a person rather than a thing a household holds.
    /// </remarks>
    [Fact]
    public void ChildrenOfCraftsmenFollowTheirParentsTrade()
    {
        int followed = 0;
        int eligible = 0;

        foreach (ulong seed in Panel)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure figure in world.Figures)
            {
                if (figure.Craft == Craft.None) continue;

                foreach (EntityId parentId in new[] { figure.FatherId, figure.MotherId })
                {
                    if (!world.Figures.Contains(parentId)) continue;

                    Craft parent = world.Figures[parentId].Craft;
                    if (parent == Craft.None) continue;

                    eligible++;
                    if (parent == figure.Craft) followed++;
                    break;
                }
            }
        }

        double share = eligible == 0 ? 0.0 : (double)followed / eligible;
        _out.WriteLine($"children of craftsmen who took the family trade: {followed}/{eligible} ({share:P1})");

        Assert.True(eligible > 0, "no craftsman in the panel had a recorded child in a guild");

        // 51.2% across the panel (65 of 127), against a blind draw over twenty-one crafts at 4.8%:
        // a guild that admitted its own on terms nobody else got, which is what HouseholdPull is
        // for. It was 60.3% when that number was set, and the drop is craft towns: M30 made
        // SettlementSpecialization.Crafts reachable, and a craft town supports more trades well
        // than a farming village does, so a craftsman's child there has real alternatives to the
        // family forge. HouseholdPull was measured in a world with no craft towns in it and is due
        // a second look; the bar is set where the pull is still unmistakably a pull.
        Assert.True(share > 0.40, $"the family trade is followed only {share:P1} of the time");
    }

    /// <summary>No craft may be a quarter of a world's guildsmen.</summary>
    /// <remarks>
    /// A third was the bound before <see cref="Crafts"/> had been measured. The commonest trade is
    /// weaving, at 18.0% of the panel at three centuries and 15.5% at a thousand years, and textiles
    /// being a world's largest single trade is both intended and what the guild rolls say. A quarter
    /// leaves that where it is and still catches the failure this guards — one gate widening until
    /// it swallows the others.
    /// </remarks>
    [Fact]
    public void NoSingleCraftSwallowsTheGuilds()
    {
        var totals = new Dictionary<Craft, int>();
        int held = 0;

        foreach (ulong seed in Panel)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure figure in world.Figures)
            {
                if (figure.Craft == Craft.None) continue;

                totals[figure.Craft] = totals.GetValueOrDefault(figure.Craft) + 1;
                held++;
            }
        }

        Assert.True(held > 0, "the panel produced no craftsmen at all");

        foreach ((Craft craft, int count) in totals.OrderByDescending(entry => entry.Value))
        {
            _out.WriteLine($"{Crafts.Label(craft),-16}{count,5}  {(double)count / held:P1}");
        }

        (Craft commonest, int most) = totals.OrderByDescending(entry => entry.Value).First();
        Assert.True(
            (double)most / held <= 0.25,
            $"{Crafts.Label(commonest)} is {(double)most / held:P1} of every guildsman in the panel");
    }

    /// <summary>
    /// A craft the world has room for is a craft the world actually contains.
    /// </summary>
    /// <remarks>
    /// The failure this guards against is a gate written so narrowly that a trade exists only on
    /// paper — which is exactly what happened to craftwork as a settlement specialization, where a
    /// tier floor and the year the question was asked between them made the value unreachable in
    /// every measured world.
    /// </remarks>
    [Fact]
    public void EveryCraftTheGroundSupportsIsPractisedSomewhere()
    {
        var supported = new HashSet<Craft>();
        var practised = new HashSet<Craft>();
        var places = new Dictionary<Craft, int>();

        foreach (ulong seed in Panel)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Civilization civilization in world.ActiveCivilizations())
            {
                foreach (Settlement settlement in world.ActiveSettlementsOf(civilization))
                {
                    foreach (Craft craft in Enum.GetValues<Craft>())
                    {
                        if (craft == Craft.None) continue;
                        if (!Crafts.Supports(world, settlement, craft)) continue;

                        supported.Add(craft);
                        places[craft] = places.GetValueOrDefault(craft) + 1;
                    }
                }
            }

            foreach (Figure figure in world.Figures)
            {
                if (figure.Craft != Craft.None) practised.Add(figure.Craft);
            }
        }

        foreach ((Craft craft, int count) in places.OrderByDescending(entry => entry.Value))
        {
            _out.WriteLine($"settlements supporting {Crafts.Label(craft),-16}{count,5}");
        }

        supported.ExceptWith(practised);
        _out.WriteLine(supported.Count == 0
            ? "every supported craft is practised"
            : "supported but never practised: " + string.Join(", ", supported.Select(Crafts.Label)));

        Assert.Empty(supported);
    }
}
