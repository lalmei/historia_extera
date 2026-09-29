using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.Terrain;
using HistoryEngine.World;
using Xunit;

namespace HistoryEngine.Tests;

/// <summary>
/// Bynames earned at the grave, and seasons that belong to the ground they are named on.
/// </summary>
public sealed class EpithetAndSeasonTests
{
    private static Figure Ruler(
        int id, int born, int crowned, double piety = 0.5, double aggression = 0.5)
    {
        var figure = new Figure(
            EntityId.Figure(id), EntityId.Civilization(0), EntityId.Culture(0), "Aeda", Sex.Female, born)
        {
            Disposition = new Disposition(
                new CultureValues(aggression, 0.5, piety, 0.5, 0.5, 0.5), 0.5, 0.5),
        };
        figure.Offices.Add(new OfficeHolding(OfficeKind.Ruler, "Queen", EntityId.Civilization(0), crowned, null));
        return figure;
    }

    private static void War(Figure figure, int year, bool won) =>
        figure.Campaigns.Add(new CampaignMemory(
            EntityId.War(year), EntityId.None, EntityId.Civilization(0), year, CampaignRole.Ruled)
        {
            Triumphant = won,
        });

    [Fact]
    public void AReignThatWonEveryWarIsRememberedForIt()
    {
        Figure queen = Ruler(1, born: 10, crowned: 30);
        War(queen, 35, won: true);
        War(queen, 40, won: true);
        War(queen, 45, won: true);

        Epithets.Verdict? verdict = Epithets.Judge(queen, 60);

        Assert.NotNull(verdict);
        Assert.Contains(verdict!.Value.Epithet, new[] { "the Conqueror", "the Great", "the Victorious" });
        Assert.Equal("for three wars won and none lost", verdict.Value.Reason);
    }

    [Fact]
    public void AFallInBattleOutranksEverythingElse()
    {
        Figure queen = Ruler(2, born: 10, crowned: 30, piety: 0.95);
        War(queen, 35, won: true);
        War(queen, 40, won: true);
        queen.DeathCause = DeathCause.Battle;

        Assert.Equal("for falling in battle", Epithets.Judge(queen, 50)!.Value.Reason);
    }

    [Fact]
    public void TemperAloneEarnsNothing()
    {
        // As pious as a person can be, and never seen to do anything about it.
        Figure queen = Ruler(3, born: 10, crowned: 30, piety: 1.0, aggression: 0.5);

        Assert.Null(Epithets.Judge(queen, 50));
    }

    [Fact]
    public void AChildCrownedAndDeadYoungIsTheChild()
    {
        Figure king = Ruler(4, born: 100, crowned: 106);

        Epithets.Verdict? verdict = Epithets.Judge(king, 115);

        Assert.Contains(verdict!.Value.Epithet, new[] { "the Child", "the Young" });
    }

    [Fact]
    public void OnlyRulersAndUnbeatenCaptainsAreNamed()
    {
        var soldier = new Figure(
            EntityId.Figure(5), EntityId.Civilization(0), EntityId.Culture(0), "Thor", Sex.Male, 10);
        Assert.Null(Epithets.Judge(soldier, 90));

        for (int i = 0; i < 3; i++)
        {
            soldier.Campaigns.Add(new CampaignMemory(
                EntityId.War(1), EntityId.Battle(i), EntityId.Civilization(0), 30 + i, CampaignRole.Commanded)
            {
                Triumphant = true,
            });
        }

        Assert.Equal("for never losing a field", Epithets.Judge(soldier, 90)!.Value.Reason);
    }

    private static Region At(int centerZ, double temperature) => new(
        EntityId.Region(0),
        new TerrainBounds(0, centerZ - 8, 16, 16),
        default,
        fertility: 0.5,
        meanHeight: 0.5,
        rainfall: 0.5,
        temperature: temperature,
        geologicActivity: 0.0,
        isLand: true,
        hasRiver: false,
        isCoastal: false,
        riverAccess: 0.0,
        harbourQuality: 0.0,
        ruggedness: 0.0);

    [Fact]
    public void TheSameQuarterIsWinterInTheNorthAndSummerInTheSouth()
    {
        const int size = 4096;
        Region north = At(300, 20.0);
        Region south = At(size - 300, 20.0);

        Assert.Equal("winter", Seasons.Name(north, 0, 4, size));
        Assert.Equal("high summer", Seasons.Name(south, 0, 4, size));
        Assert.Equal("spring", Seasons.Name(north, 1, 4, size));
        Assert.Equal("autumn", Seasons.Name(south, 1, 4, size));
    }

    [Fact]
    public void TheTropicsAndOddCalendarsNameNoSeason()
    {
        Assert.Null(Seasons.Name(At(2048, 25.0), 0, 4, 4096));
        Assert.Null(Seasons.Name(At(300, 10.0), 0, 5, 4096));
    }

    [Fact]
    public void AWinterTooColdToCampaignIsTheDepthOfWinter()
    {
        Assert.Equal("the depth of winter", Seasons.Name(At(100, -5.0), 0, 4, 4096));
    }

    private static string Name(EntityId id) => id.ToString();

    [Fact]
    public void ASeasonLeadsTheLineAndIsCapitalisedEitherWay()
    {
        var sacked = new HistoryEvent(
            0, 90, EventKind.SettlementSacked, EntityId.Settlement(1), EntityId.Civilization(2), default,
            Data: Chronicle.Data(("season", "the depth of winter")));

        Assert.Equal(
            "In the depth of winter, set:1 was sacked by civ:2.", Narration.Render(sacked, Name));
        Assert.Equal(
            "set:1 was sacked by civ:2.", Narration.Render(sacked with { Data = null }, Name));
    }

    /// <summary>
    /// A figure's own page has more than one way to say its commonest lines, chosen the same way
    /// the world lines are.
    /// </summary>
    [Fact]
    public void AFiguresOwnLinesAreNotAllOneSentence()
    {
        EntityId self = EntityId.Figure(1);
        var moved = new HistoryEvent(
            0, 30, EventKind.FigureMoved, self, default, EntityId.Settlement(4),
            Data: Chronicle.Data(("cause", "on marrying")));

        var lines = new HashSet<string>();
        for (int id = 0; id < 8; id++) lines.Add(Narration.Render(moved with { Id = id }, Name, self));

        Assert.Contains("Moved to set:4, on marrying.", lines);
        Assert.Contains("Settled in set:4, on marrying.", lines);
        Assert.Contains("Removed to set:4, on marrying.", lines);
    }
}
