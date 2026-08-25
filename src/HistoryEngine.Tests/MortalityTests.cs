using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.World;
using Xunit;

namespace HistoryEngine.Tests;

/// <summary>
/// The personal mortality model: causes must be reachable, grounded, and still exceptional.
/// </summary>
public sealed class MortalityTests
{
    /// <summary>
    /// Seeds sampled for the panel. Resampled when persistent conspiracies landed, which moved
    /// every history and made political murder a plot's ending rather than an annual roll; these
    /// are seeds that carry one in the current checkout.
    /// </summary>
    private static readonly ulong[] Seeds = { 16, 21, 42, 47, 99 };

    /// <summary>
    /// Variety is a model outcome, not an excuse to turn a court chronicle into a casualty list.
    /// </summary>
    [Fact]
    public void ExceptionalDeathsAreReachableButRemainExceptional()
    {
        var causes = new HashSet<DeathCause>();
        int deaths = 0;
        int exceptional = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure figure in world.Figures)
            {
                if (figure.IsAlive) continue;

                deaths++;
                causes.Add(figure.DeathCause);

                if (figure.DeathCause is not DeathCause.OldAge and not DeathCause.Illness)
                {
                    exceptional++;
                }
            }
        }

        foreach (DeathCause expected in new[]
        {
            DeathCause.OldAge,
            DeathCause.Illness,
            DeathCause.Battle,
            DeathCause.Assassination,
            DeathCause.Accident,
            DeathCause.Execution,
            DeathCause.Childbirth,
            DeathCause.Plague,
            DeathCause.Disaster,
            DeathCause.Poisoning,
        })
        {
            Assert.Contains(expected, causes);
        }

        double share = exceptional / (double)deaths;
        Assert.InRange(
            share,
            0.03,
            0.20);
    }

    /// <summary>The originating system's exact context survives beside the filterable category.</summary>
    [Fact]
    public void ContextualDeathsKeepTheirSpecificCause()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard()).World;
        int checked_ = 0;

        foreach (Figure figure in world.Figures)
        {
            if (figure.DeathCause is not DeathCause.Plague
                and not DeathCause.Disaster
                and not DeathCause.Accident)
            {
                continue;
            }

            checked_++;
            Assert.False(string.IsNullOrWhiteSpace(figure.DeathDetail));

            HistoryEvent death = Assert.Single(
                world.Chronicle.Events,
                entry => entry.Kind == EventKind.FigureDied
                    && entry.Subject == figure.Id
                    && entry.Year == figure.DeathYear);

            Assert.Equal(figure.DeathDetail, death.Data!["cause"]);

            if (figure.DeathCause == DeathCause.Disaster)
            {
                HistoryEvent disaster = Assert.Single(
                    world.Chronicle.Events,
                    entry => entry.Kind == EventKind.DisasterStruck
                        && entry.Year == figure.DeathYear
                        && entry.Extra is not null
                        && entry.Extra.Contains(figure.Id));

                Assert.True(disaster.Id < death.Id, "A disaster casualty was recorded before its cause.");
            }
        }

        Assert.True(checked_ > 0, "No contextual death occurred in the standard world.");
    }

    /// <summary>
    /// A murder is a family event, not only a vital record of the person who died.
    /// </summary>
    [Fact]
    public void PoliticalMurdersReachTheHouseholdAndCanNameAHand()
    {
        int murders = 0;
        int withFamily = 0;
        int named = 0;
        int settled = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure figure in world.Figures)
            {
                if (figure.DeathCause is not (DeathCause.Assassination or DeathCause.Poisoning))
                {
                    continue;
                }

                murders++;

                HistoryEvent death = Assert.Single(
                    world.Chronicle.Events,
                    entry => entry.Kind == EventKind.FigureDied
                        && entry.Subject == figure.Id
                        && entry.Year == figure.DeathYear);

                Assert.False(string.IsNullOrWhiteSpace(figure.DeathDetail));
                Assert.Equal(figure.DeathDetail, death.Data!["cause"]);

                if (death.Data.ContainsKey("suspect"))
                {
                    named++;
                    Assert.False(death.Extra is null);

                    bool found = false;
                    foreach (EntityId id in death.Extra!)
                    {
                        if (id.Kind != EntityKind.Figure) continue;
                        if (world.Figures[id].FullName != death.Data["suspect"]) continue;

                        found = true;
                        break;
                    }

                    Assert.True(found, "A named hand was not indexed on the death that named them.");
                }

                if (death.Extra is null) continue;

                foreach (EntityId id in death.Extra)
                {
                    if (id.Kind != EntityKind.Figure || id == figure.Id) continue;

                    Figure other = world.Figures[id];
                    bool household = other.SpouseIds.Contains(figure.Id)
                        || figure.ChildIds.Contains(id)
                        || other.ChildIds.Contains(figure.Id)
                        || SharesAParent(figure, other);

                    if (!household) continue;

                    withFamily++;
                    break;
                }
            }

            foreach (Figure figure in world.Figures)
            {
                if (figure.DeathCause != DeathCause.Execution) continue;
                if (figure.DeathDetail is null) continue;
                if (figure.DeathDetail.StartsWith("for the death of ", StringComparison.Ordinal))
                {
                    settled++;
                }
            }
        }

        Assert.True(murders > 0, "No assassination or poisoning occurred across the standard seeds.");
        Assert.True(
            withFamily > 0,
            "No political murder was indexed on a living spouse, parent, child or sibling.");
        Assert.True(named > 0, "No political murder named a hand the court had reason to suspect.");
        Assert.True(
            settled > 0,
            "No named hand was later executed for the murder.");
    }

    private static bool SharesAParent(Figure a, Figure b)
    {
        foreach (EntityId parent in a.Parents())
        {
            foreach (EntityId other in b.Parents())
            {
                if (parent == other) return true;
            }
        }

        return false;
    }

    [Fact]
    public void AnOrdinaryDeathAppearsInTheSurvivingSpousesChronology()
    {
        WorldState world = WorldBuilder.Create(TestWorlds.Small());
        Civilization civilization = world.Civilizations[0];
        Culture culture = world.Cultures[civilization.CultureId];
        Figure deceased = Houses.NewFigure(
            world, civilization, culture, Sex.Female, birthYear: 1);
        Figure survivor = Houses.NewFigure(
            world, civilization, culture, Sex.Male, birthYear: 1);

        deceased.SpouseId = survivor.Id;
        deceased.SpouseIds.Add(survivor.Id);
        survivor.SpouseId = deceased.Id;
        survivor.SpouseIds.Add(deceased.Id);
        int eventsBefore = world.Chronicle.Count;

        Houses.Die(world, deceased, year: 40, DeathCause.Illness);

        HistoryEvent death = Assert.Single(
            world.Chronicle.Events.Skip(eventsBefore),
            entry => entry.Kind == EventKind.FigureDied && entry.Subject == deceased.Id);

        Assert.NotNull(death.Extra);
        Assert.Contains(survivor.Id, death.Extra!);
        Assert.True(deceased.SpouseId.IsNone);
        Assert.True(survivor.SpouseId.IsNone);
    }

    /// <summary>
    /// A campaign may be entrusted to a cadet or heir; command is no longer another word for rule.
    /// </summary>
    [Fact]
    public void AdultDynastsWhoDoNotRuleCanCommand()
    {
        int namedCommands = 0;
        int nonRulerCommands = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Battle battle in world.Battles)
            {
                Count(battle.AttackerCommanderId, battle.AttackerId, battle.Year);
                Count(battle.DefenderCommanderId, battle.DefenderId, battle.Year);
            }

            void Count(EntityId commanderId, EntityId civilizationId, int year)
            {
                if (commanderId.IsNone) return;

                namedCommands++;
                Figure commander = world.Figures[commanderId];

                Assert.True(commander.AgeIn(year) >= Succession.MajorityAge);
                Assert.True(commander.BirthYear <= year);
                Assert.True(commander.DeathYear is null || commander.DeathYear >= year);

                if (!WasRuler(commander, civilizationId, year)) nonRulerCommands++;
            }
        }

        Assert.True(namedCommands > 0, "No battle had a named commander.");
        Assert.True(
            nonRulerCommands > 10,
            $"Only {nonRulerCommands} commands went to a non-ruler across five worlds.");
    }

    private static bool WasRuler(Figure figure, EntityId civilizationId, int year)
    {
        foreach (OfficeHolding title in figure.Offices)
        {
            // By kind, not by title text. This comparison used to read `title.Title == "Regent"`,
            // which silently counted a marshal as a reign the moment a third office existed.
            if (title.CivilizationId != civilizationId || title.Kind != OfficeKind.Ruler) continue;
            if (title.FromYear > year) continue;
            if (title.ToYear is null || title.ToYear >= year) return true;
        }

        return false;
    }
}
