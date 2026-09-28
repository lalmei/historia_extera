using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Serialization;
using HistoryEngine.World;
using Xunit;

namespace HistoryEngine.Tests;

public sealed class ClaimTransitionCauseTests
{
    private static (HistoryRun Run, Figure Author, Settlement Home, Settlement Other, Artifact Work) Fixture()
    {
        var run = HistoryRun.Execute(TestWorlds.Small() with { Years = 0 });
        var world = run.World;
        var home = world.Settlements.First();
        var other = world.Settlements.First(place => place.CivilizationId != home.CivilizationId);
        var author = world.Figures.First();
        author.CivilizationId = home.CivilizationId;
        author.Claims.Add(new Claim(0, author.Id, home.CivilizationId, ClaimSubject.Comet(0), 1,
            ClaimRegister.Measured, "it returns"));
        var work = new Artifact(world.Artifacts.NextId, "Register", ArtifactKind.Tome, home.Id, 1)
        {
            TomeContents = new TomeContents(TomeContentKind.Cosmology, author.Id, EntityId.None,
                Array.Empty<TomeSection>(), 1) { CopyLimit = 3 },
        };
        work.TomeContents.Carries.Add(new ClaimRef(author.Id, 0));
        world.Artifacts.Add(work);
        return (run, author, home, other, work);
    }

    [Theory]
    [InlineData(false, "its author left the realm")]
    [InlineData(true, "its author died")]
    public void AuthorDeathAndMigrationAreDifferentLosses(bool dies, string expected)
    {
        var (run, author, home, other, work) = Fixture();
        work.Lose(1, "in a fire");
        ClaimTransmission.Trace(run.World, 1);
        if (dies) author.DeathYear = 2;
        else author.CivilizationId = other.CivilizationId;
        ClaimTransmission.Trace(run.World, 2);
        var loss = run.ToExport().ClaimTransitions.Single(x => x.Kind == ClaimTransitionKind.Lost);
        Assert.Equal(expected, loss.Cause);
        Assert.Equal(home.CivilizationId, loss.RealmId);
    }

    [Fact]
    public void BorderLossDoesNotBorrowTheCopysLaterFire()
    {
        var (run, author, home, other, work) = Fixture();
        author.DeathYear = 1;
        work.TomeContents!.CopyTo(1, other.Id, home.Id);
        var realm = other.CivilizationId;
        ClaimTransmission.Trace(run.World, 1);
        other.CivilizationId = home.CivilizationId;
        ClaimTransmission.Trace(run.World, 2);
        work.TomeContents.LoseCopyAt(other.Id, 40, "in the sack");
        var loss = run.ToExport().ClaimTransitions.Single(x => x.Kind == ClaimTransitionKind.Lost);
        Assert.Equal(realm, loss.RealmId);
        Assert.Equal(2, loss.Year);
        Assert.Equal("the town holding it left the realm", loss.Cause);
        Assert.True(work.IsExtant);
        var entry = run.World.Chronicle.Events.Single(x => x.Kind == HistoryEngine.Events.EventKind.ClaimLost);
        Assert.Contains("because the town holding it left the realm", run.World.Narrate(entry));
    }

    [Theory]
    [InlineData(false, "its last local copy was lost (in the sack)")]
    [InlineData(true, "the town holding it was abandoned")]
    public void CopyLossAndAbandonmentNameTheirOwnCause(bool abandoned, string expected)
    {
        var (run, author, home, other, work) = Fixture();
        author.DeathYear = 1;
        work.TomeContents!.CopyTo(1, other.Id, home.Id);
        ClaimTransmission.Trace(run.World, 1);
        if (abandoned) other.AbandonedYear = 2;
        work.TomeContents.LoseCopyAt(other.Id, 2, "in the sack");
        ClaimTransmission.Trace(run.World, 2);
        var loss = run.ToExport().ClaimTransitions.Single(x => x.Kind == ClaimTransitionKind.Lost);
        Assert.Equal(expected, loss.Cause);
        Assert.Equal(work.TomeContents.Copies.Single().LostYear, loss.Year);
    }

    [Fact]
    public void OriginalLossLeavesTheRealmWithASurvivingCopyHoldingTheReading()
    {
        var (run, author, home, other, work) = Fixture();
        author.DeathYear = 1;
        work.TomeContents!.CopyTo(1, other.Id, home.Id);
        ClaimTransmission.Trace(run.World, 1);
        work.Lose(2, "in a fire");
        ClaimTransmission.Trace(run.World, 2);
        var loss = run.ToExport().ClaimTransitions.Single(x => x.Kind == ClaimTransitionKind.Lost);
        Assert.Equal("the original work was lost", loss.Cause);
        Assert.Equal(home.CivilizationId, loss.RealmId);
        Assert.Contains(run.World.ClaimHoldings, x => x.RealmId == other.CivilizationId);
    }

    [Fact]
    public void OldDestroyedCopyDoesNotExplainTheLossOfAnOriginalAtTheSameTown()
    {
        var (run, author, home, other, work) = Fixture();
        author.DeathYear = 1;
        work.TomeContents!.CopyTo(1, other.Id, home.Id);
        work.TomeContents.LoseCopyAt(other.Id, 2, "in the sack");
        work.MoveTo(other.Id, 3, "a gift");
        ClaimTransmission.Trace(run.World, 3);
        work.Lose(4, "in a fire");
        ClaimTransmission.Trace(run.World, 4);
        Assert.Equal("the original work was lost",
            run.ToExport().ClaimTransitions.Single(x => x.Kind == ClaimTransitionKind.Lost).Cause);
    }
}
