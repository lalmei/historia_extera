using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.World;
using Xunit;
using Xunit.Abstractions;

namespace HistoryEngine.Tests;

/// <summary>
/// What a maker must be true of: they practised the trade the thing needed, and they were alive,
/// grown and in the town on the year it was made.
/// </summary>
/// <remarks>
/// These are asked of the finished record rather than of the decision, which is the opposite of
/// what <see cref="CraftTests"/> does and for the opposite reason. A craft gate reads a world that
/// moves afterwards, so re-deciding is the only exact question. A maker is a claim about one past
/// year that the record is supposed to keep true for ever — <see cref="Figure.Residences"/> dates
/// every move — so the finished world is precisely where the claim can be checked.
/// </remarks>
public sealed class MakerTests
{
    private static readonly ulong[] Panel = { 2, 7, 11, 42, 99 };

    private readonly ITestOutputHelper _out;

    public MakerTests(ITestOutputHelper output) => _out = output;

    /// <summary>Where somebody lived in a given year, from the dated moves they made.</summary>
    private static EntityId ResidenceIn(Figure figure, int year)
    {
        EntityId where = EntityId.None;

        foreach (Residence residence in figure.Residences)
        {
            if (residence.FromYear > year) break;
            where = residence.SettlementId;
        }

        return where;
    }

    /// <summary>Every object that names a maker, books excepted — their author is Tomes' business.</summary>
    private static IEnumerable<(Artifact Object, Figure Maker)> Made(WorldState world)
    {
        foreach (Artifact artifact in world.Artifacts)
        {
            if (artifact.Kind == ArtifactKind.Tome) continue;
            if (artifact.CreatorId.IsNone || !world.Figures.Contains(artifact.CreatorId)) continue;

            yield return (artifact, world.Figures[artifact.CreatorId]);
        }
    }

    [Fact]
    public void NoObjectNamesAMakerWhoWasUnbornOrAlreadyDead()
    {
        int checkedCount = 0;

        foreach (ulong seed in Panel)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach ((Artifact made, Figure maker) in Made(world))
            {
                checkedCount++;

                Assert.True(
                    maker.AgeIn(made.CreatedYear) >= Succession.MajorityAge,
                    $"seed {seed}: {made.Name} was made in {made.CreatedYear} by {maker.Name}, "
                    + $"born {maker.BirthYear}");

                Assert.True(
                    maker.DeathYear is not int died || died >= made.CreatedYear,
                    $"seed {seed}: {made.Name} was made in {made.CreatedYear} by {maker.Name}, "
                    + $"dead since {maker.DeathYear}");
            }
        }

        Assert.True(checkedCount > 0, "the panel made nothing with a maker on it");
    }

    [Fact]
    public void NoObjectNamesAMakerWhoWasSomewhereElse()
    {
        foreach (ulong seed in Panel)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach ((Artifact made, Figure maker) in Made(world))
            {
                Assert.Equal(made.OriginSettlementId, ResidenceIn(maker, made.CreatedYear));
            }
        }
    }

    [Fact]
    public void AMakerPractisedTheTradeTheThingNeeded()
    {
        foreach (ulong seed in Panel)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach ((Artifact made, Figure maker) in Made(world))
            {
                Assert.Contains(maker.Craft, Makers.CraftsFor(made.Kind));
            }
        }
    }

    /// <summary>
    /// The defect this milestone came to undo: the field that was supposed to name a craftsman
    /// held the person who paid, so every object in every world was made by nobody.
    /// </summary>
    [Fact]
    public void ThePersonWhoPaidIsNeverRecordedAsTheMaker()
    {
        foreach (ulong seed in Panel)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Artifact artifact in world.Artifacts)
            {
                if (artifact.Kind == ArtifactKind.Tome) continue;
                if (artifact.CreatorId.IsNone || artifact.PatronId.IsNone) continue;

                Assert.NotEqual(artifact.PatronId, artifact.CreatorId);
            }
        }
    }

    /// <summary>
    /// Regalia keeps both facts, and the patron half is the one that existed before: nothing that
    /// used to name the commissioning ruler may have stopped naming them.
    /// </summary>
    [Fact]
    public void RegaliaRecordsThePatronItAlwaysDidAndSometimesAMakerToo()
    {
        int crowns = 0;
        int byAGoldsmith = 0;

        foreach (ulong seed in Panel)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Artifact artifact in world.Artifacts)
            {
                if (artifact.Kind != ArtifactKind.Regalia) continue;

                crowns++;

                Assert.False(
                    artifact.PatronId.IsNone,
                    $"seed {seed}: {artifact.Name} was commissioned by nobody");

                if (artifact.CreatorId.IsNone) continue;

                byAGoldsmith++;
                Assert.Equal(Craft.Goldsmith, world.Figures[artifact.CreatorId].Craft);
            }
        }

        Assert.True(crowns > 0, "the panel crowned nobody");
        _out.WriteLine($"regalia {crowns}, of which {byAGoldsmith} name a goldsmith");
    }

    /// <summary>
    /// How much of a world's making has a maker, and how many makers are known for more than one
    /// thing — the second being the whole reason to record the first.
    /// </summary>
    [Fact]
    public void ReportHowMuchOfAWorldsMakingHasAMaker()
    {
        _out.WriteLine("seed | objects | with maker | makers | with >1");

        foreach (ulong seed in Panel)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            int objects = 0;
            var works = new Dictionary<EntityId, int>();

            foreach (Artifact artifact in world.Artifacts)
            {
                if (artifact.Kind == ArtifactKind.Tome) continue;

                objects++;
                if (artifact.CreatorId.IsNone) continue;

                works[artifact.CreatorId] = works.GetValueOrDefault(artifact.CreatorId) + 1;
            }

            int attributed = 0;
            int prolific = 0;
            foreach (int count in works.Values)
            {
                attributed += count;
                if (count > 1) prolific++;
            }

            _out.WriteLine(
                $"{seed} | {objects} | {attributed} | {works.Count} | {prolific}");
        }
    }
}
