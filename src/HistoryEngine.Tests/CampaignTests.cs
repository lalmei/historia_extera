using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.World;
using Xunit;
using Xunit.Abstractions;

namespace HistoryEngine.Tests;

/// <summary>
/// That wars, battles and sieges land on the lives of the people who actually stood in them.
/// </summary>
public sealed class CampaignTests
{
    private static readonly ulong[] Seeds = { 2, 7, 11, 42, 99 };

    private readonly ITestOutputHelper _output;

    public CampaignTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A commander named on a battle is remembered for commanding it, on the side that took
    /// the field, and the memory agrees with who won.
    /// </summary>
    [Fact]
    public void CommandersAreRememberedForTheBattlesTheyLed()
    {
        int commanded = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Battle battle in world.Battles)
            {
                RememberedCommander(world, battle, battle.AttackerCommanderId, battle.AttackerId, ref commanded);
                RememberedCommander(world, battle, battle.DefenderCommanderId, battle.DefenderId, ref commanded);
            }
        }

        Assert.True(commanded > 50, $"Only {commanded} named commands were checked.");
    }

    /// <summary>
    /// Soldiers of a house sometimes reach the field their realm fought on, even when they
    /// did not hold the command.
    /// </summary>
    [Fact]
    public void SoldiersTakeTheField()
    {
        int fought = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure figure in world.Figures)
            {
                foreach (CampaignMemory memory in figure.Campaigns)
                {
                    if (memory.Role != CampaignRole.Fought) continue;

                    fought++;
                    Assert.True(world.Battles.Contains(memory.BattleId));
                    Assert.True(world.Wars.Contains(memory.WarId));

                    Battle battle = world.Battles[memory.BattleId];
                    Assert.True(
                        memory.SideId == battle.AttackerId || memory.SideId == battle.DefenderId,
                        $"{figure.FullName} fought {battle.Name} for a realm that was not there.");
                    Assert.True(
                        battle.WitnessIds.Contains(figure.Id),
                        $"{figure.FullName} fought {battle.Name} but is not among its witnesses.");
                }
            }
        }

        Assert.True(fought > 20, $"Only {fought} soldier-campaigns were recorded.");
    }

    /// <summary>
    /// Sitting rulers of a belligerent are remembered for the war, including those crowned
    /// while it was already being fought.
    /// </summary>
    [Fact]
    public void RulersAreRememberedForTheirWars()
    {
        int ruled = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (War war in world.Wars)
            {
                Assert.True(
                    HasRulerMemory(world, war, war.AggressorId),
                    $"{war.Name} has no recorded ruler on the attacking throne.");
                Assert.True(
                    HasRulerMemory(world, war, war.DefenderId),
                    $"{war.Name} has no recorded ruler on the defending throne.");

                foreach (Figure figure in world.Figures)
                {
                    CampaignMemory? memory = Ruled(figure, war.Id);
                    if (memory is null) continue;

                    ruled++;
                    Assert.True(
                        war.Involves(memory.SideId),
                        $"{figure.FullName} is remembered for {war.Name} on a side that was not fighting.");
                    Assert.True(
                        HeldTheThroneDuring(figure, memory.SideId, war),
                        $"{figure.FullName} is remembered as leading {war.Name} without holding that throne.");

                    if (war.Outcome == WarOutcome.Stalemate || war.IsActive)
                    {
                        Assert.Null(memory.Triumphant);
                    }
                    else
                    {
                        bool attackersWon = war.Outcome == WarOutcome.AggressorVictory;
                        Assert.Equal(war.IsAttacker(memory.SideId) == attackersWon, memory.Triumphant);
                    }
                }
            }
        }

        Assert.True(ruled > 20, $"Only {ruled} wartime reigns were checked.");
    }

    /// <summary>
    /// Anyone living in a town the chronicle invests is remembered for the siege.
    /// </summary>
    [Fact]
    public void ResidentsEndureSieges()
    {
        int endured = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure figure in world.Figures)
            {
                foreach (CampaignMemory memory in figure.Campaigns)
                {
                    if (memory.Role != CampaignRole.EnduredSiege) continue;

                    endured++;
                    Assert.True(world.Battles.Contains(memory.BattleId));

                    Battle battle = world.Battles[memory.BattleId];
                    Assert.True(battle.IsSiege, $"{figure.FullName} endured a field battle as a siege.");
                    Assert.Equal(battle.DefenderId, memory.SideId);

                    if (battle.IsResolved)
                    {
                        Assert.Equal(
                            battle.SiegeOutcome is not SiegeOutcome.Carried,
                            memory.Triumphant);
                    }
                }
            }
        }

        Assert.True(endured > 0, "No named person ever lived through a siege.");
    }

    /// <summary>
    /// Presence is indexed onto the chronicle: a battle a soldier stood in appears on their page.
    /// </summary>
    [Fact]
    public void CampaignsAppearOnTheChroniclePage()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard(42)).World;
        int indexed = 0;

        foreach (Figure figure in world.Figures)
        {
            foreach (CampaignMemory memory in figure.Campaigns)
            {
                if (memory.BattleId.IsNone) continue;

                bool mentioned = false;
                foreach (HistoryEvent entry in world.Chronicle.Events)
                {
                    if (entry.Subject != memory.BattleId) continue;
                    if (entry.Kind is not (EventKind.BattleFought or EventKind.SiegeBegan or EventKind.SiegeLifted))
                    {
                        continue;
                    }

                    if (entry.Extra is null) continue;
                    foreach (EntityId id in entry.Extra)
                    {
                        if (id != figure.Id) continue;
                        mentioned = true;
                        break;
                    }
                }

                Assert.True(
                    mentioned,
                    $"{figure.FullName} is remembered for a battle that never names them.");
                indexed++;
            }
        }

        Assert.True(indexed > 10, $"Only {indexed} battle-presences were indexed.");
    }

    [Fact]
    public void HigherRecordedLossesNeverLowerParticipantRisk()
    {
        foreach (CampaignRole role in new[]
                 {
                     CampaignRole.Commanded,
                     CampaignRole.Fought,
                     CampaignRole.EnduredSiege,
                 })
        {
            double priorFatal = -1.0;
            double priorInjury = -1.0;
            for (int percent = 0; percent <= 100; percent += 5)
            {
                double lossRate = percent / 100.0;
                double fatal = Campaigns.ParticipantFatalRisk(
                    role, triumphant: false, siegeCarried: true, sacked: false, lossRate);
                double injury = Campaigns.ParticipantInjuryRisk(
                    role, siegeCarried: true, sacked: false, lossRate);

                Assert.True(fatal >= priorFatal, $"{role} fatal risk fell at {percent}% losses.");
                Assert.True(injury >= priorInjury, $"{role} injury risk fell at {percent}% losses.");
                priorFatal = fatal;
                priorInjury = injury;
            }
        }
    }

    [Fact]
    public void NamedParticipantsReceiveBoundedRoleSensitiveConsequencesAcrossSeeds()
    {
        var totals = new Dictionary<CampaignRole, int>();
        var changed = new Dictionary<CampaignRole, int>();
        var returned = new Dictionary<CampaignRole, int>();
        var wounded = new Dictionary<CampaignRole, int>();
        var killed = new Dictionary<CampaignRole, int>();
        var trauma = new Dictionary<CampaignRole, int>();
        var renown = new Dictionary<CampaignRole, int>();
        int nonCommanderWounds = 0;
        int nonCommanderDeaths = 0;
        int promotions = 0;
        int permanentInjuries = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure figure in world.Figures)
            {
                foreach (CampaignMemory memory in figure.Campaigns)
                {
                    if (memory.BattleId.IsNone || memory.Role == CampaignRole.Ruled) continue;

                    Battle battle = world.Battles[memory.BattleId];
                    if (!battle.IsResolved)
                    {
                        Assert.Equal(CampaignFate.Unresolved, memory.Fate);
                        continue;
                    }

                    totals[memory.Role] = totals.GetValueOrDefault(memory.Role) + 1;
                    Assert.True(
                        memory.Fate != CampaignFate.Unresolved,
                        $"Seed {seed}: {figure.FullName} has no fate for {battle.Name} ({battle.SiegeOutcome}).");

                    if (memory.Fate == CampaignFate.ReturnedUnharmed)
                    {
                        returned[memory.Role] = returned.GetValueOrDefault(memory.Role) + 1;
                    }
                    else
                    {
                        changed[memory.Role] = changed.GetValueOrDefault(memory.Role) + 1;
                    }

                    if (memory.Fate == CampaignFate.Wounded)
                    {
                        wounded[memory.Role] = wounded.GetValueOrDefault(memory.Role) + 1;
                        Assert.Contains(figure.Injuries, injury => injury.CauseId == memory.BattleId);
                        if (memory.Role != CampaignRole.Commanded) nonCommanderWounds++;
                    }

                    if (memory.Fate == CampaignFate.Killed)
                    {
                        killed[memory.Role] = killed.GetValueOrDefault(memory.Role) + 1;
                        Assert.Equal(DeathCause.Battle, figure.DeathCause);
                        Assert.Equal(battle.EndYear ?? memory.Year, figure.DeathYear);
                        if (memory.Role != CampaignRole.Commanded) nonCommanderDeaths++;
                    }

                    if (memory.Traumatized)
                    {
                        trauma[memory.Role] = trauma.GetValueOrDefault(memory.Role) + 1;
                    }
                    if (memory.RenownGained > 0)
                    {
                        renown[memory.Role] = renown.GetValueOrDefault(memory.Role) + 1;
                    }

                    if (memory.PromotionYear is not null)
                    {
                        Assert.True(memory.RenownGained > 0);
                        Assert.Contains(figure.Offices, office =>
                            office.Kind == OfficeKind.Marshal
                            && office.FromYear == memory.PromotionYear);
                        promotions++;
                    }
                }

                foreach (FigureInjury injury in figure.Injuries)
                {
                    Assert.Equal(0.0, LifeStories.Fitness(figure, injury.Year));
                    if (injury.Permanent) permanentInjuries++;
                    Assert.DoesNotContain(figure.Campaigns, memory =>
                    {
                        if (memory.Role == CampaignRole.EnduredSiege) return false;
                        int occurred = memory.BattleId.IsNone
                            ? memory.Year
                            : world.Battles[memory.BattleId].EndYear ?? memory.Year;
                        return occurred > injury.Year && occurred < injury.RecoveryYear;
                    });
                    Assert.DoesNotContain(figure.Journeys, journey =>
                        journey.Year > injury.Year && journey.Year < injury.RecoveryYear);
                }
            }
        }

        foreach (CampaignRole role in new[]
                 {
                     CampaignRole.Commanded,
                     CampaignRole.Fought,
                     CampaignRole.EnduredSiege,
                 })
        {
            Assert.True(totals.GetValueOrDefault(role) > 0, $"No {role} participants were measured.");
            Assert.True(changed.GetValueOrDefault(role) > 0, $"No {role} participant was changed.");
            Assert.True(returned.GetValueOrDefault(role) > 0, $"Every {role} participant was erased.");

            double scale = 100.0 / totals[role];
            _output.WriteLine(
                $"{role}: {totals[role]} presences; "
                + $"wounded {wounded.GetValueOrDefault(role) * scale:F1}, "
                + $"killed {killed.GetValueOrDefault(role) * scale:F1}, "
                + $"traumatized {trauma.GetValueOrDefault(role) * scale:F1}, "
                + $"renowned {renown.GetValueOrDefault(role) * scale:F1}, "
                + $"unharmed {returned.GetValueOrDefault(role) * scale:F1} per 100.");
        }

        Assert.True(nonCommanderWounds > 0, "No non-commander was wounded.");
        Assert.True(nonCommanderDeaths > 0, "No non-commander was killed.");
        Assert.True(promotions > 0, "No battle-earned renown led to promotion.");
        Assert.True(permanentInjuries > 0, "No wound left a permanent consequence.");
    }

    private static void RememberedCommander(
        WorldState world, Battle battle, EntityId commanderId, EntityId sideId, ref int commanded)
    {
        if (!world.Figures.Contains(commanderId)) return;

        Figure commander = world.Figures[commanderId];
        CampaignMemory? memory = null;
        foreach (CampaignMemory candidate in commander.Campaigns)
        {
            if (candidate.BattleId == battle.Id && candidate.Role == CampaignRole.Commanded)
            {
                memory = candidate;
                break;
            }
        }

        Assert.True(memory is not null, $"{commander.FullName} led {battle.Name} with no campaign recorded.");
        Assert.Equal(sideId, memory!.SideId);
        Assert.Equal(battle.WarId, memory.WarId);

        if (battle.IsResolved && !battle.VictorId.IsNone)
        {
            Assert.Equal(memory.SideId == battle.VictorId, memory.Triumphant);
        }

        commanded++;
    }

    private static CampaignMemory? Ruled(Figure figure, EntityId warId)
    {
        foreach (CampaignMemory memory in figure.Campaigns)
        {
            if (memory.WarId == warId && memory.Role == CampaignRole.Ruled) return memory;
        }

        return null;
    }

    private static bool HasRulerMemory(WorldState world, War war, EntityId civilizationId)
    {
        foreach (Figure figure in world.Figures)
        {
            CampaignMemory? memory = Ruled(figure, war.Id);
            if (memory is not null && memory.SideId == civilizationId) return true;
        }

        return false;
    }

    private static bool HeldTheThroneDuring(Figure ruler, EntityId civilizationId, War war)
    {
        int warEnd = war.EndYear ?? int.MaxValue;

        foreach (OfficeHolding held in ruler.Offices)
        {
            if (held.Kind != OfficeKind.Ruler || held.CivilizationId != civilizationId) continue;

            int from = held.FromYear;
            int to = held.ToYear ?? int.MaxValue;
            if (from <= warEnd && to >= war.StartYear) return true;
        }

        return false;
    }
}
