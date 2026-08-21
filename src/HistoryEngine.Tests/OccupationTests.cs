using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.Systems;
using HistoryEngine.World;
using Xunit;

namespace HistoryEngine.Tests;

/// <summary>
/// Careers for people the chronicle follows, and the independence that chooses them.
/// </summary>
public sealed class OccupationTests
{
    private static readonly ulong[] Seeds = { 2, 7, 11, 42, 99 };

    /// <summary>
    /// Anyone who lived to majority has a trade. Children do not.
    /// </summary>
    [Fact]
    public void AdultsInTheRecordHaveAnOccupation()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard(42)).World;
        int adults = 0;
        int children = 0;

        foreach (Figure figure in world.Figures)
        {
            int age = figure.AgeAtDeath ?? figure.AgeIn(world.EndYear);

            if (age < Succession.MajorityAge)
            {
                children++;
                if (figure.Occupation == Occupation.None) continue;

                bool satAPost = false;
                foreach (OfficeHolding held in figure.Offices)
                {
                    if (held.Kind == OfficeKind.Consort) continue;
                    satAPost = true;
                    break;
                }

                Assert.True(
                    satAPost,
                    $"{figure.FullName} was {age} with occupation {figure.Occupation} and no office.");
                continue;
            }

            adults++;
            Assert.True(
                figure.Occupation != Occupation.None,
                $"{figure.FullName} lived to {age} with no occupation.");
        }

        Assert.True(adults > 200, $"Only {adults} adults were recorded.");
        Assert.True(children > 0, "No children were left without a trade, so majority is not gating.");
    }

    /// <summary>
    /// A career without an office still belongs in the chronicle. Officials raised into the
    /// record are introduced by the grant; everyone else would otherwise have a trade on the
    /// entity and a page that never mentioned it.
    /// </summary>
    [Fact]
    public void ATradeWithoutAnOfficeIsStillRecorded()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard(42)).World;
        int civilians = 0;

        foreach (Figure figure in world.Figures)
        {
            if (figure.Occupation == Occupation.None) continue;
            if (HeldAPublicOffice(figure)) continue;

            civilians++;

            HistoryEvent taken = Assert.Single(
                world.Chronicle.Events,
                entry => entry.Kind == EventKind.OccupationTaken && entry.Subject == figure.Id);

            Assert.Equal(Significance.Routine, taken.Significance);
            Assert.Equal(Occupations.Phrase(figure.Occupation), taken.DataValue("occupation"));
        }

        Assert.True(civilians > 50, $"Only {civilians} people had a trade and no appointed office.");
    }


    // ─── The vow ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// A panel wide enough to contain celibate faiths, including the world the bug was found in.
    /// </summary>
    private static readonly ulong[] CelibacySeeds = { 2, 7, 11, 42, 99, 1432144466 };

    /// <summary>
    /// Nobody is both in holy orders and married, where the faith forbids it.
    /// </summary>
    /// <remarks>
    /// <para>The rule the faiths' own scripture asserts and the simulation did not keep. It was
    /// checked against the <see cref="OfficeKind.HighPriest"/> seat, which reaches exactly one
    /// person per faith, so every ordinary priest was exempt: 20 of 267 clergy in celibate faiths
    /// across this panel had a spouse.</para>
    ///
    /// <para>Zero rather than a tolerance, and it took closing three doors to get there — the
    /// marriage, the ordination, and the restore that hands a former ruler back the career they
    /// held before the crown. Any of them left open puts figures back in this count, which is why
    /// it is asserted exactly.</para>
    /// </remarks>
    [Fact]
    public void NobodyInHolyOrdersIsMarriedWhereTheFaithForbidsIt()
    {
        int underVow = 0;

        foreach (ulong seed in CelibacySeeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure figure in world.Figures)
            {
                if (figure.Occupation != Occupation.Clergy) continue;
                if (figure.ReligionId.IsNone || !world.Religions.Contains(figure.ReligionId)) continue;
                if (!world.Religions[figure.ReligionId].Character.CelibateClergy) continue;

                underVow++;

                Assert.True(
                    !figure.IsMarried && figure.SpouseId.IsNone,
                    $"{figure.FullName} is in holy orders in a faith that forbids its clergy to "
                    + $"marry, and has a spouse (seed {seed}).");
            }
        }

        Assert.True(
            underVow > 100,
            $"Only {underVow} clergy served a celibate faith across the panel, so the assertion "
            + "above is close to vacuous. Check that celibate faiths are still being generated.");
    }

    /// <summary>
    /// Closing the doors did not empty the temples.
    /// </summary>
    /// <remarks>
    /// The counterweight to the test above, and the one that would have caught the lazy version
    /// of this fix. Barring the married from orders removes people from a pool; barring too many
    /// would satisfy the vow by abolishing the priesthood. Measured before the change, clergy
    /// were 11.5% of recorded figures across this panel and are 11.0% after.
    /// </remarks>
    [Fact]
    public void TheVowDoesNotEmptyThePriesthood()
    {
        int figures = 0;
        int clergy = 0;

        foreach (ulong seed in CelibacySeeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure figure in world.Figures)
            {
                figures++;
                if (figure.Occupation == Occupation.Clergy) clergy++;
            }
        }

        double share = clergy / (double)figures;
        Assert.InRange(share, 0.07, 0.16);
    }

    /// <summary>
    /// The vow refuses a marriage, and refuses orders to someone already married.
    /// </summary>
    /// <remarks>
    /// Both directions on one figure, because the bug was that only one of them was ever asked.
    /// The chronicle's own example ran "took to holy orders at Chernigradun" and "married
    /// Findabaire at Chernigradun" in the same year, which needs both doors shut to be
    /// impossible rather than merely unlikely.
    /// </remarks>
    [Fact]
    public void AVowBindsBothWaysRound()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard(11)).World;

        Religion? celibate = null;
        foreach (Religion religion in world.Religions)
        {
            if (!religion.Character.CelibateClergy) continue;
            celibate = religion;
            break;
        }

        Assert.NotNull(celibate);

        Figure? cleric = null;
        foreach (Figure figure in world.Figures)
        {
            if (figure.Occupation != Occupation.Clergy) continue;
            if (figure.ReligionId != celibate!.Id) continue;
            cleric = figure;
            break;
        }

        Assert.NotNull(cleric);

        // Already in orders: not barred from orders (they are in them), and the household roll
        // must refuse them a marriage. Asserted through the public surface the roll uses.
        Assert.False(Occupations.BarredFromOrders(world, cleric!));
        Assert.True(HouseholdSystem.VowedToCelibacy(world, cleric!));

        // The mirror: mark them married, and orders become unavailable.
        cleric!.SpouseId = cleric.Id;
        Assert.True(Occupations.BarredFromOrders(world, cleric));
    }

    private static bool HeldAPublicOffice(Figure figure)
    {
        foreach (OfficeHolding held in figure.Offices)
        {
            if (held.Kind is not OfficeKind.Consort) return true;
        }

        return false;
    }

    /// <summary>
    /// Someone raised from the population arrives already in the career the office required.
    /// </summary>
    [Fact]
    public void RaisedNotablesKeepTheCareerTheOfficeImplies()
    {
        int checkedHolders = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure figure in world.Figures)
            {
                if (figure.Origin == FigureOrigin.Unrecorded) continue;

                OfficeHolding? posting = null;
                foreach (OfficeHolding held in figure.Offices)
                {
                    if (!Offices.IsAppointed(held.Kind) || held.ToYear is not null) continue;
                    posting = held;
                    break;
                }

                if (posting is null) continue;

                checkedHolders++;
                Assert.Equal(Occupations.ForOffice(posting.Kind), figure.Occupation);
            }
        }

        Assert.True(checkedHolders > 50, $"Only {checkedHolders} raised notables were checked.");
    }

    /// <summary>
    /// A living person who leaves a civic post goes back to the life they had before it.
    /// </summary>
    [Fact]
    public void LeavingOfficeAliveRestoresThePriorCareer()
    {
        int restored = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure figure in world.Figures)
            {
                if (!figure.IsAlive) continue;

                OfficeHolding? ended = null;
                bool stillPosted = false;
                foreach (OfficeHolding held in figure.Offices)
                {
                    if (held.Kind == OfficeKind.Consort) continue;
                    if (held.ToYear is null) stillPosted = true;
                    else if (ended is null) ended = held;
                }

                if (stillPosted || ended is null) continue;
                if (ended.Kind != OfficeKind.Governor) continue;

                restored++;
                Assert.NotEqual(Occupation.Official, figure.Occupation);
            }
        }

        Assert.True(restored > 0, "No living former governor was found to have left office.");
    }

    /// <summary>
    /// Death in a post leaves them as they were in it.
    /// </summary>
    [Fact]
    public void DeathInOfficeKeepsThePostingCareer()
    {
        int diedInPost = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure figure in world.Figures)
            {
                if (figure.IsAlive || figure.DeathYear is not int death) continue;

                OfficeHolding? posting = null;
                foreach (OfficeHolding held in figure.Offices)
                {
                    if (held.Kind == OfficeKind.Consort) continue;
                    if (held.ToYear != death) continue;
                    posting = held;
                }

                if (posting is null) continue;

                bool changedCareerTheYearTheyDied = false;
                foreach (HistoryEvent entry in world.Chronicle.Events)
                {
                    if (entry.Kind == EventKind.OccupationTaken
                        && entry.Subject == figure.Id
                        && entry.Year == death)
                    {
                        changedCareerTheYearTheyDied = true;
                        break;
                    }
                }

                if (changedCareerTheYearTheyDied) continue;

                diedInPost++;
                Assert.Equal(Occupations.ForOffice(posting.Kind), figure.Occupation);
            }
        }

        Assert.True(diedInPost > 20, $"Only {diedInPost} people died still holding a post.");
    }

    /// <summary>
    /// A pious person is likelier to take holy orders than a warlike one is.
    /// </summary>
    /// <remarks>
    /// The inertness test for the choice. If occupation were assigned uniformly, or only from
    /// the office that later found them, clergy and soldiery would have the same mean piety.
    /// </remarks>
    [Fact]
    public void OccupationsFollowDisposition()
    {
        double clergyPiety = 0.0;
        double soldierPiety = 0.0;
        int clergy = 0;
        int soldiers = 0;

        WorldState world = HistoryRun.Execute(TestWorlds.Standard(42)).World;

        foreach (Figure figure in world.Figures)
        {
            if (figure.Occupation == Occupation.Clergy)
            {
                clergyPiety += figure.Disposition.Values.Piety;
                clergy++;
            }
            else if (figure.Occupation == Occupation.Soldiery)
            {
                soldierPiety += figure.Disposition.Values.Piety;
                soldiers++;
            }
        }

        Assert.True(clergy > 20 && soldiers > 20, $"clergy {clergy}, soldiers {soldiers}");
        Assert.True(
            clergyPiety / clergy > soldierPiety / soldiers,
            $"Clergy piety {clergyPiety / clergy:F2} was not above soldiery {soldierPiety / soldiers:F2}.");
    }

    [Fact]
    public void AMatchingCareerFitsItsOffice()
    {
        var soldier = new Figure(EntityId.Figure(1), EntityId.Civilization(0), EntityId.Culture(0), "A", Sex.Male, 1)
        {
            Occupation = Occupation.Soldiery,
        };

        Assert.True(
            Occupations.Affinity(soldier, OfficeKind.Marshal)
            > Occupations.Affinity(soldier, OfficeKind.HighPriest));
    }

    /// <summary>
    /// Followers outnumber rebels, and rebels still exist.
    /// </summary>
    /// <remarks>
    /// A symmetric scatter around the midpoint would make a rebel as common as a follower, which
    /// is not a people. The property worth keeping is the skew: most of the court takes its
    /// culture's advice, and a minority does not.
    /// </remarks>
    [Fact]
    public void FollowersOutnumberRebels()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard(42)).World;
        int followers = 0;
        int rebels = 0;

        foreach (Figure figure in world.Figures)
        {
            Assert.InRange(figure.Disposition.Independence, 0.0, 1.0);

            if (figure.Disposition.Independence < 0.5) followers++;
            else rebels++;
        }

        Assert.True(
            followers > rebels * 2,
            $"Followers {followers} were not clearly more common than rebels {rebels}.");
        Assert.True(rebels > 20, $"Only {rebels} rebels were recorded; the tail has flattened.");
    }
}
