using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.World;
using Xunit;
using Xunit.Abstractions;

namespace HistoryEngine.Tests;

/// <summary>
/// What a levied craftsman must be true of: a need asked for him, the place could have held him,
/// and there are not more of him than a town could have hidden.
/// </summary>
/// <remarks>
/// <para>The gate questions are asked by re-deciding against the finished world, which is the
/// discipline <see cref="CraftTests"/> established and for the same reason: a levy reads a
/// settlement at one instant and everything it read moves afterwards — routes close, towns shrink,
/// people migrate. Re-deciding puts the question and the world at the same instant, which is the
/// only way to ask a gate exactly.</para>
///
/// <para>The questions about the finished record — who answered a need, and how old they were when
/// they did — are asked of the record instead, because those are claims about one past year that
/// the record is supposed to keep true for ever.</para>
/// </remarks>
public sealed class LevyTests
{
    private static readonly ulong[] Panel = { 2, 7, 11, 42, 99 };

    private readonly ITestOutputHelper _out;

    public LevyTests(ITestOutputHelper output) => _out = output;

    /// <summary>Everyone the levy put into the record, by the door they came through.</summary>
    private static IEnumerable<Figure> Levied(WorldState world)
    {
        foreach (Figure figure in world.Figures)
        {
            if (figure.Origin == FigureOrigin.Guild) yield return figure;
        }
    }

    /// <summary>
    /// The gate is not bypassable: a trade a place cannot support is refused, every time.
    /// </summary>
    /// <remarks>
    /// The failure this guards against is the one the issue named explicitly — a levy becoming a
    /// back door around the gate that makes crafts geographic. Asked of every settlement and every
    /// craft in the table, so it cannot pass by never reaching the interesting combinations.
    /// </remarks>
    [Fact]
    public void ALevyRefusesEveryTradeAPlaceCannotSupport()
    {
        int refused = 0;

        foreach (ulong seed in Panel)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;
            int before = world.Figures.Count;

            foreach (Settlement settlement in world.Settlements)
            {
                foreach (Craft craft in Enum.GetValues<Craft>())
                {
                    if (craft == Craft.None) continue;
                    if (Crafts.Supports(world, settlement, craft)) continue;

                    refused++;
                    Assert.Null(Levies.Raise(world, settlement, craft, world.Year));
                }
            }

            Assert.Equal(before, world.Figures.Count);
        }

        Assert.True(refused > 0, "the panel had no unsupported trade to refuse");
    }

    /// <summary>
    /// A town too small to have hidden another craftsman does not yield one.
    /// </summary>
    /// <remarks>
    /// The population half of the gate, and the ceiling that keeps the levy from compounding. A
    /// place below <see cref="Levies.PeoplePerLevy"/> has an allowance of zero and is refused
    /// whatever its ground supports.
    /// </remarks>
    [Fact]
    public void ALevyRefusesATownTooSmallToHaveMissedAnyone()
    {
        int refused = 0;

        foreach (ulong seed in Panel)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Settlement settlement in world.Settlements)
            {
                if (settlement.Population >= Levies.PeoplePerLevy) continue;

                foreach (Craft craft in Enum.GetValues<Craft>())
                {
                    if (craft == Craft.None) continue;
                    if (!Crafts.Supports(world, settlement, craft)) continue;

                    refused++;
                    Assert.Null(Levies.Raise(world, settlement, craft, world.Year));
                }
            }
        }

        Assert.True(refused > 0, "the panel had no small town supporting a trade");
    }

    /// <summary>
    /// A world that needs nothing raises nobody.
    /// </summary>
    /// <remarks>
    /// The demand rule stated as the record can check it: every levied craftsman is the maker of
    /// something recorded in the very year he was introduced. A levy that fired on anything other
    /// than a specific need — a background trickle of guildsmen — would leave people here with no
    /// object against their name, and this is the assertion that would catch it.
    /// </remarks>
    [Fact]
    public void EveryLeviedCraftsmanAnsweredANeedInTheYearHeAppeared()
    {
        int checkedCount = 0;

        foreach (ulong seed in Panel)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            var madeIn = new Dictionary<EntityId, List<int>>();
            foreach (Artifact artifact in world.Artifacts)
            {
                if (artifact.CreatorId.IsNone) continue;
                if (!madeIn.TryGetValue(artifact.CreatorId, out List<int>? years))
                {
                    years = new List<int>();
                    madeIn[artifact.CreatorId] = years;
                }

                years.Add(artifact.CreatedYear);
            }

            foreach (Figure craftsman in Levied(world))
            {
                checkedCount++;

                Assert.True(
                    craftsman.Background is not null,
                    $"seed {seed}: {craftsman.Name} was raised without a background");

                int introduced = craftsman.Background!.IntroducedYear;

                Assert.True(
                    madeIn.TryGetValue(craftsman.Id, out List<int>? years),
                    $"seed {seed}: {craftsman.Name} was raised in {introduced} and made nothing");

                Assert.Contains(introduced, years!);
            }
        }

        Assert.True(checkedCount > 0, "the panel levied nobody");
    }

    /// <summary>
    /// A levied craftsman arrives at an age his trade could have been learned by.
    /// </summary>
    /// <remarks>
    /// The whole argument for a band per craft is that a master goldsmith is not twenty-two. This
    /// asserts the band was applied; <see cref="Levies.TradeAge"/> carries the reasoning for what
    /// each band is.
    /// </remarks>
    [Fact]
    public void ALeviedCraftsmanArrivesAtAnAgeHisTradeAllows()
    {
        foreach (ulong seed in Panel)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure craftsman in Levied(world))
            {
                (int min, int max) = Levies.TradeAge(craftsman.Craft);
                int age = craftsman.AgeIn(craftsman.Background!.IntroducedYear);

                Assert.True(
                    age >= min && age <= max,
                    $"seed {seed}: {craftsman.Name} took up {Crafts.Label(craftsman.Craft)} at {age}, "
                    + $"outside {min}-{max}");
            }
        }
    }

    /// <summary>
    /// A levied craftsman is a guildsman of a stated trade, and the record says which door.
    /// </summary>
    [Fact]
    public void ALeviedCraftsmanIsAGuildsmanOfAStatedTrade()
    {
        foreach (ulong seed in Panel)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure craftsman in Levied(world))
            {
                Assert.Equal(Occupation.Guild, craftsman.Occupation);
                Assert.NotEqual(Craft.None, craftsman.Craft);
            }
        }
    }

    /// <summary>
    /// The levy does not pull families into the record behind it.
    /// </summary>
    /// <remarks>
    /// The attention budget, asserted rather than assumed. <see cref="Offices.HeadsAHousehold"/>
    /// answers no for a figure holding no appointed office, so a levied craftsman is followed
    /// lightly by construction — but "by construction" is exactly the kind of claim that stops
    /// being true when somebody later gives the levy a seat to go with the trade.
    /// </remarks>
    [Fact]
    public void ALeviedCraftsmanDoesNotFoundAHouseholdTheChronicleFollows()
    {
        foreach (ulong seed in Panel)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure craftsman in Levied(world))
            {
                if (craftsman.Offices.Count > 0) continue;

                Assert.False(
                    Offices.HeadsAHousehold(craftsman, world.Year),
                    $"seed {seed}: {craftsman.Name} was levied and is being followed as a household");
            }
        }
    }

    /// <summary>
    /// The same seed levies the same people, down to the name and the birth year.
    /// </summary>
    /// <remarks>
    /// The fingerprint tests already cover the world as a whole. This one is narrower on purpose:
    /// a levy forked on anything order-dependent would still fingerprint identically on a single
    /// machine while being wrong, so the thing worth asserting is that the levied set itself is
    /// reproduced entry for entry.
    /// </remarks>
    [Fact]
    public void TheSameSeedLeviesTheSamePeople()
    {
        foreach (ulong seed in new ulong[] { 7, 42 })
        {
            WorldState first = HistoryRun.Execute(TestWorlds.Standard(seed)).World;
            WorldState second = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            string[] a = Levied(first)
                .Select(f => $"{f.Id}|{f.Name}|{f.BirthYear}|{f.Craft}|{f.Background!.IntroducedYear}")
                .ToArray();
            string[] b = Levied(second)
                .Select(f => $"{f.Id}|{f.Name}|{f.BirthYear}|{f.Craft}|{f.Background!.IntroducedYear}")
                .ToArray();

            Assert.Equal(a, b);
        }
    }

    /// <summary>
    /// What the levy costs the record, against the ceiling it is allowed.
    /// </summary>
    /// <remarks>
    /// <para>The number that decides whether this feature is affordable. A levy fires only on an
    /// object being made, objects are capped per town by the treasury limit, and a town may hold one
    /// levied craftsman per <see cref="Levies.PeoplePerLevy"/> inhabitants — three independent
    /// bounds, none of which compounds. This measures what they add up to in practice.</para>
    ///
    /// <para><b>The stated ceiling is one percent of a world's recorded people.</b> A levy that cost
    /// more than that would be a second birth rate wearing a trade, which is the failure the
    /// attention budget exists to refuse.</para>
    /// </remarks>
    [Fact]
    public void TheLevyCostsLessThanOnePercentOfARecordedWorld()
    {
        _out.WriteLine("seed | figures | levied | share | attributed objects");

        foreach (ulong seed in Panel)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            int figures = world.Figures.Count;
            int levied = Levied(world).Count();

            int attributed = 0;
            foreach (Artifact artifact in world.Artifacts)
            {
                if (artifact.Kind != ArtifactKind.Tome && !artifact.CreatorId.IsNone) attributed++;
            }

            double share = figures == 0 ? 0.0 : (double)levied / figures;

            _out.WriteLine(
                $"{seed} | {figures} | {levied} | {share:P2} | {attributed}");

            Assert.True(
                share < 0.01,
                $"seed {seed}: the levy added {levied} of {figures} recorded people ({share:P2})");
        }
    }
}
