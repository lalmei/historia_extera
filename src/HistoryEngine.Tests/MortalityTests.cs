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
    private static readonly ulong[] Seeds = { 2, 7, 11, 42, 99 };

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
