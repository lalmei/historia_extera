using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Serialization;
using HistoryEngine.World;
using Xunit;

namespace HistoryEngine.Tests;

/// <summary>
/// The ladder inside a realm's army: who climbs it, how far, and what it costs the world.
/// </summary>
/// <remarks>
/// The premise is the one the office model set — a rank that no other system reads is decoration —
/// so most of these are assertions about the rest of the engine noticing. A ladder every soldier
/// reaches the top of has failed, and so has one that produces captains nobody ever puts in
/// command of anything.
/// </remarks>
public sealed class RankTests
{
    private static readonly ulong[] Seeds = { 2, 7, 11, 42, 99 };

    /// <summary>Every rung is reached, and each climbed one by fewer people than the last.</summary>
    /// <remarks>
    /// <para>The two failures a rank model has: a ladder whose top rungs are unreachable, which is
    /// a career that ends where it started, and one that everyone climbs, which is a promotion that
    /// means nothing.</para>
    ///
    /// <para>The pyramid is counted over rungs that were <em>climbed</em>, and stops below the top
    /// one, because the top one is mostly not climbed at all: a realm's one commander's place is
    /// occupied by its marshal for as long as it has one, so a soldier rises into it only in a
    /// realm that has gone without. That is the model rather than an accident of calibration — see
    /// <see cref="Ranks.Establishment"/> — so the commander's rung is asserted to be reached, and
    /// the shape of the ladder is asserted below it.</para>
    /// </remarks>
    [Fact]
    public void EveryRungIsReachedAndTheClimbStaysAPyramid()
    {
        var reached = new Dictionary<MilitaryRank, int>();
        var climbed = new Dictionary<MilitaryRank, int>();
        int commissioned = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure figure in world.Figures)
            {
                foreach (RankStep step in figure.Service)
                {
                    reached[step.Rank] = reached.GetValueOrDefault(step.Rank) + 1;

                    if (step.Claim == Ranks.CommissionClaim)
                    {
                        commissioned++;
                        continue;
                    }

                    climbed[step.Rank] = climbed.GetValueOrDefault(step.Rank) + 1;
                }
            }
        }

        foreach (MilitaryRank rank in Enum.GetValues<MilitaryRank>())
        {
            if (rank == MilitaryRank.None) continue;

            Assert.True(
                reached.GetValueOrDefault(rank) > 0,
                $"Nobody in five worlds was ever raised to {rank}.");
        }

        Assert.True(commissioned > 0, "No marshalcy ever put its holder on the top rung.");

        for (MilitaryRank rank = MilitaryRank.Soldier; rank <= MilitaryRank.Captain; rank++)
        {
            int above = climbed.GetValueOrDefault(rank);
            int below = climbed.GetValueOrDefault(rank - 1);

            Assert.True(
                above > 0 && above < below,
                $"{above} climbed to {rank} against {below} at the rung below.");
        }
    }

    /// <summary>A rank is only ever gained, and always in a year not before the last one.</summary>
    /// <remarks>
    /// The invariant <see cref="Figure.Rank"/> derives from. If a career could go down, or arrive
    /// out of order, then reading the last entry would be a different answer from reading the
    /// highest, and every consumer would have to know which it wanted.
    /// </remarks>
    [Fact]
    public void ACareerOnlyEverRises()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard()).World;

        foreach (Figure figure in world.Figures)
        {
            for (int i = 1; i < figure.Service.Count; i++)
            {
                RankStep previous = figure.Service[i - 1];
                RankStep step = figure.Service[i];

                Assert.True(
                    step.Rank > previous.Rank,
                    $"{figure.Name} went from {previous.Rank} to {step.Rank}.");
                Assert.True(step.Year >= previous.Year, $"{figure.Name}'s career runs backwards.");
            }

            Assert.Equal(
                figure.Service.Count == 0 ? MilitaryRank.None : figure.Service[^1].Rank,
                figure.Rank);
        }
    }

    /// <summary>No realm carries more officers than it was ever able to promote.</summary>
    /// <remarks>
    /// The establishment governs promotion rather than existence — see
    /// <see cref="Ranks.Establishment"/> — so the two documented arrivals are counted and allowed
    /// for: a marshal put on the top rung by his appointment, and an officer who took an office and
    /// came back to arms carrying the rung he had. Anything beyond those two is the promotion pass
    /// having raised somebody into a place that did not exist, which is the failure this guards.
    /// </remarks>
    [Fact]
    public void NobodyIsRaisedIntoAPlaceTheRealmDoesNotHave()
    {
        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Civilization civilization in world.ActiveCivilizations())
            {
                List<Figure> muster = Ranks.Muster(world, civilization, world.Year);

                for (MilitaryRank rank = MilitaryRank.FileLeader; rank <= Ranks.Top; rank++)
                {
                    int standing = Ranks.Standing(muster, rank);
                    int arrived = Arrivals(muster, rank);
                    int allowed = Ranks.Establishment(rank, muster.Count) + arrived;

                    Assert.True(
                        standing <= allowed,
                        $"{civilization.Name} keeps {standing} at {rank} or above, where "
                        + $"{muster.Count} soldiers and {arrived} arrivals allow {allowed}.");
                }
            }
        }
    }

    /// <summary>Officers at or above a rung who did not climb into their current one here.</summary>
    private static int Arrivals(IReadOnlyList<Figure> muster, MilitaryRank rank)
    {
        int arrived = 0;
        foreach (Figure soldier in muster)
        {
            if (soldier.Rank < rank) continue;

            if (soldier.CurrentRank!.Claim == Ranks.CommissionClaim
                || soldier.PriorOccupation != Occupation.None
                || soldier.Offices.Count > 0)
            {
                arrived++;
            }
        }

        return arrived;
    }

    /// <summary>The way up is the field: soldiers the field noticed climb further.</summary>
    /// <remarks>
    /// <para>The whole model in one assertion, and the one that caught the model's first shape.
    /// Renown weights the yearly odds and, above the file leaders, gates the rung outright — see
    /// <see cref="Ranks.NeedsRenown"/>. Without the gate the two averages came out at 4.06 and
    /// 4.08: everyone who lived past forty made captain, because a place always fell vacant
    /// eventually and merit only ever decided which of two men took one in the same year.</para>
    ///
    /// <para>Measured on the rung they <em>climbed</em> to. A marshalcy puts its holder on the top
    /// rung whatever the field made of him, and counting that as a climb puts appointments into a
    /// measurement about promotions — which is exactly how the flat reading above stayed hidden.
    /// </para>
    /// </remarks>
    [Fact]
    public void RenownIsWhatCarriesASoldierUp()
    {
        double decorated = 0.0;
        int decoratedCount = 0;
        double unnoticed = 0.0;
        int unnoticedCount = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure figure in world.Figures)
            {
                MilitaryRank climbed = Climbed(figure);
                if (climbed == MilitaryRank.None) continue;

                // Long enough a career for the ladder to have been offered to them at all.
                int age = figure.AgeAtDeath ?? figure.AgeIn(world.Year);
                if (age < 40) continue;

                if (Campaigns.Renown(figure) > 0)
                {
                    decorated += (int)climbed;
                    decoratedCount++;
                }
                else
                {
                    unnoticed += (int)climbed;
                    unnoticedCount++;
                }
            }
        }

        Assert.True(decoratedCount > 20, $"Only {decoratedCount} decorated soldiers to compare.");
        Assert.True(unnoticedCount > 20, $"Only {unnoticedCount} unnoticed soldiers to compare.");

        double withRenown = decorated / decoratedCount;
        double without = unnoticed / unnoticedCount;

        // A margin rather than a bare inequality: two averages a hundredth apart would satisfy
        // "greater" and mean nothing, which is what the first cut of this model produced.
        Assert.True(
            withRenown > without + 0.25,
            $"Decorated soldiers climb to {withRenown} against {without} for those never noticed.");
    }

    /// <summary>The highest rung this figure was promoted onto, ignoring any commission.</summary>
    private static MilitaryRank Climbed(Figure figure)
    {
        MilitaryRank climbed = MilitaryRank.None;
        foreach (RankStep step in figure.Service)
        {
            if (step.Claim != Ranks.CommissionClaim) climbed = step.Rank;
        }

        return climbed;
    }

    /// <summary>A realm's marshal stands at the top of its ladder, whatever he was before.</summary>
    [Fact]
    public void EveryMarshalIsHisRealmsRankingSoldier()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard()).World;

        int marshals = 0;
        foreach (Figure figure in world.Figures)
        {
            foreach (OfficeHolding held in figure.Offices)
            {
                if (held.Kind != OfficeKind.Marshal) continue;

                marshals++;
                Assert.True(
                    figure.Rank == Ranks.Top,
                    $"{figure.Name} held a marshalcy at {figure.Rank}.");
                break;
            }
        }

        Assert.True(marshals > 10, $"Only {marshals} marshalcies in a standard run.");
    }

    /// <summary>Armies are commanded by their own officers, and not only by the ruling house.</summary>
    /// <remarks>
    /// <para>What the ladder was built to change. Before it, a campaign the ruler stayed home from
    /// went to the marshal or to whichever adult cousin the court could spare; a realm's senior
    /// soldier was not a person the war model could see.</para>
    ///
    /// <para>Read at the year of the battle rather than at the end of the run, in both halves. A
    /// commander's rank later in life says nothing about what he was on the day, and an officer who
    /// is eventually made marshal is exactly the career this is looking for — asking whether he
    /// ever held an office would throw away the successful ones and keep the rest.</para>
    /// </remarks>
    [Fact]
    public void RankingOfficersTakeCommands()
    {
        int commandedByOfficers = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Battle battle in world.Battles)
            {
                foreach (EntityId commanderId in
                    new[] { battle.AttackerCommanderId, battle.DefenderCommanderId })
                {
                    if (!world.Figures.Contains(commanderId)) continue;

                    Figure commander = world.Figures[commanderId];

                    // Somebody the army raised rather than somebody the court seated: no office of
                    // any kind that year, and a rung high enough to be handed a host.
                    if (Seated(commander, battle.Year)) continue;
                    if (RankIn(commander, battle.Year) < MilitaryRank.FileLeader) continue;

                    commandedByOfficers++;
                }
            }
        }

        Assert.True(
            commandedByOfficers > 12,
            $"Only {commandedByOfficers} commands went to a realm's own officers.");
    }

    /// <summary>Whether this figure held any office in this year.</summary>
    private static bool Seated(Figure figure, int year)
    {
        foreach (OfficeHolding held in figure.Offices)
        {
            if (held.FromYear <= year && (held.ToYear is null || held.ToYear >= year)) return true;
        }

        return false;
    }

    /// <summary>The rung this figure stood on in this year.</summary>
    private static MilitaryRank RankIn(Figure figure, int year)
    {
        MilitaryRank rank = MilitaryRank.None;
        foreach (RankStep step in figure.Service)
        {
            if (step.Year <= year) rank = step.Rank;
        }

        return rank;
    }

    /// <summary>Each government names its own ladder, and no two of them name it alike.</summary>
    /// <remarks>
    /// The cheapest character the engine buys, and the reason the rungs are written out per culture
    /// rather than assembled from a rank word: a reader learns what kind of army it is from the
    /// vocabulary. A ladder shared between two governments would be a branch that does nothing.
    /// </remarks>
    [Fact]
    public void EveryGovernmentNamesItsOwnLadder()
    {
        var ladders = new HashSet<string>();

        foreach (GovernmentForm government in Enum.GetValues<GovernmentForm>())
        {
            var culture = new Culture(
                default,
                government.ToString(),
                languageSeed: 1,
                new CultureValues(0.5, 0.5, 0.5, 0.5, 0.5, 0.5),
                government);

            var rungs = new List<string>();
            for (MilitaryRank rank = MilitaryRank.Recruit; rank <= Ranks.Top; rank++)
            {
                string title = culture.RankTitle(rank);
                Assert.False(string.IsNullOrWhiteSpace(title));
                rungs.Add(title);
            }

            // Within one army, no two rungs share a name — otherwise a promotion reads as nothing
            // having happened.
            Assert.Equal(rungs.Count, new HashSet<string>(rungs).Count);

            ladders.Add(string.Join('/', rungs));
        }

        Assert.Equal(Enum.GetValues<GovernmentForm>().Length, ladders.Count);
    }

    /// <summary>The ladder is exported, so a viewer can show a career it did not simulate.</summary>
    [Fact]
    public void ServiceTravelsInTheExport()
    {
        WorldExport export = HistoryRun.Execute(TestWorlds.Standard()).ToExport();

        int steps = 0;
        foreach (ExportFigure figure in export.Figures)
        {
            steps += figure.Service.Count;

            foreach (ExportRankStep step in figure.Service)
            {
                Assert.NotEqual(MilitaryRank.None, step.Rank);
                Assert.False(string.IsNullOrWhiteSpace(step.Title));
            }
        }

        Assert.True(steps > 50, $"Only {steps} rungs reached the export.");
    }
}
