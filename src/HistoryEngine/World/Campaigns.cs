using HistoryEngine.Core;
using HistoryEngine.Entities;

namespace HistoryEngine.World;

/// <summary>
/// Who was present at a war, and whether they came out of it well.
/// </summary>
/// <remarks>
/// <para>Commanders were already named on the battle; everyone else the chronicle follows was
/// not. A soldier of a house, a king who stayed on the throne, and a governor whose town was
/// invested all have a history the engagement belongs in, and until this it never reached them
/// unless they happened to hold the command.</para>
///
/// <para>Presence is decided on forks of the root stream, never the battle's. Who took the field
/// is a fact about the people, and drawing it from the engagement's rng would move the victory
/// every time a later child of a marshal chose soldiery.</para>
/// </remarks>
public static class Campaigns
{
    /// <summary>Chance an adult soldier of a committing realm actually reaches this field.</summary>
    /// <remarks>
    /// Not one. A realm fights on more than one frontier and the people the chronicle follows
    /// are not a standing company that moves as a body. The ones who do reach it accumulate a
    /// career; the ones who do not are still of the army, just not of this day.
    /// </remarks>
    private const double SoldierTakesField = 0.45;

    /// <summary>Records who stood at this engagement, while the outcome may still be open.</summary>
    public static void NoteBattle(WorldState world, War war, Battle battle, int year)
    {
        NoteCommander(world, war, battle, battle.AttackerCommanderId, battle.AttackerId, year);
        NoteCommander(world, war, battle, battle.DefenderCommanderId, battle.DefenderId, year);
        NoteSoldiers(world, war, battle, year);

        if (battle.IsSiege) NoteBesieged(world, war, battle, year);
    }

    /// <summary>Fills in whether each presence at this engagement was a triumph.</summary>
    public static void SettleBattle(WorldState world, Battle battle)
    {
        foreach (Figure figure in world.Figures)
        {
            foreach (CampaignMemory memory in figure.Campaigns)
            {
                if (memory.BattleId != battle.Id || memory.Triumphant is not null) continue;

                memory.Triumphant = TriumphAt(battle, memory);
            }
        }
    }

    /// <summary>Names the sitting rulers of every belligerent as having led this war.</summary>
    public static void NoteWar(WorldState world, War war, int year)
    {
        NoteRulersOf(world, war, war.Attackers, year);
        NoteRulersOf(world, war, war.Defenders, year);
    }

    /// <summary>Records a newly seated ruler if their realm is already at war.</summary>
    public static void NoteRuler(WorldState world, Figure ruler, Civilization civilization, int year)
    {
        foreach (War war in world.Wars)
        {
            if (!war.IsActive || !war.Involves(civilization.Id)) continue;

            Remember(ruler, war.Id, EntityId.None, civilization.Id, year, CampaignRole.Ruled);
        }
    }

    /// <summary>Fills in whether each ruler's war was a triumph.</summary>
    public static void SettleWar(WorldState world, War war)
    {
        bool? attackersPrevailed = war.Outcome switch
        {
            WarOutcome.AggressorVictory => true,
            WarOutcome.DefenderVictory => false,
            _ => null,
        };

        foreach (Figure figure in world.Figures)
        {
            foreach (CampaignMemory memory in figure.Campaigns)
            {
                if (memory.WarId != war.Id || memory.Role != CampaignRole.Ruled) continue;

                if (attackersPrevailed is null)
                {
                    memory.Triumphant = null;
                    continue;
                }

                memory.Triumphant = war.IsAttacker(memory.SideId) == attackersPrevailed.Value;
            }
        }
    }

    /// <summary>Everyone a battle should appear on the page of, besides the war and the two realms.</summary>
    public static IReadOnlyList<EntityId> Witnesses(Battle battle) => battle.WitnessIds;

    private static void NoteCommander(
        WorldState world, War war, Battle battle, EntityId commanderId, EntityId sideId, int year)
    {
        if (!world.Figures.Contains(commanderId)) return;

        Remember(
            world.Figures[commanderId],
            war.Id,
            battle.Id,
            sideId,
            year,
            CampaignRole.Commanded);

        Witness(battle, commanderId);
    }

    private static void NoteSoldiers(WorldState world, War war, Battle battle, int year)
    {
        IRng levy = world.Root.Fork("campaign-levy", battle.Id.ToDiscriminator());

        foreach (Figure figure in world.Figures)
        {
            if (!figure.IsAlive || figure.AgeIn(year) < Succession.MajorityAge) continue;
            if (figure.Occupation != Occupation.Soldiery) continue;
            if (figure.Id == battle.AttackerCommanderId || figure.Id == battle.DefenderCommanderId)
            {
                continue;
            }

            // A sitting ruler either took the command or stayed home; the marshal's presence
            // was already the command roll. What remains is the career soldiery of the house.
            if (figure.Holds(OfficeKind.Ruler) || figure.Holds(OfficeKind.Marshal)) continue;

            EntityId sideId = SideOf(battle, figure.CivilizationId);
            if (sideId.IsNone) continue;

            IRng fate = levy.Fork("figure", figure.Id.ToDiscriminator());
            if (!fate.Chance(SoldierTakesField)) continue;

            Remember(figure, war.Id, battle.Id, sideId, year, CampaignRole.Fought);
            Witness(battle, figure.Id);
        }
    }

    private static void NoteBesieged(WorldState world, War war, Battle battle, int year)
    {
        if (!world.Settlements.Contains(battle.SettlementId)) return;

        foreach (Figure figure in world.Figures)
        {
            if (!figure.IsAlive) continue;
            if (world.ResidenceOf(figure) != battle.SettlementId) continue;

            Remember(
                figure,
                war.Id,
                battle.Id,
                battle.DefenderId,
                year,
                CampaignRole.EnduredSiege);

            Witness(battle, figure.Id);
        }
    }

    private static void NoteRulersOf(
        WorldState world, War war, IReadOnlyList<EntityId> coalition, int year)
    {
        foreach (EntityId civilizationId in coalition)
        {
            if (!world.Civilizations.Contains(civilizationId)) continue;

            Civilization civilization = world.Civilizations[civilizationId];
            if (!world.Figures.Contains(civilization.CurrentRulerId)) continue;

            Figure ruler = world.Figures[civilization.CurrentRulerId];
            if (!ruler.IsAlive) continue;

            Remember(ruler, war.Id, EntityId.None, civilization.Id, year, CampaignRole.Ruled);
        }
    }

    private static void Remember(
        Figure figure,
        EntityId warId,
        EntityId battleId,
        EntityId sideId,
        int year,
        CampaignRole role)
    {
        foreach (CampaignMemory existing in figure.Campaigns)
        {
            if (existing.Role != role || existing.WarId != warId) continue;
            if (existing.BattleId != battleId) continue;
            return;
        }

        figure.Campaigns.Add(new CampaignMemory(warId, battleId, sideId, year, role));
    }

    private static void Witness(Battle battle, EntityId figureId)
    {
        if (figureId.IsNone) return;

        foreach (EntityId existing in battle.WitnessIds)
        {
            if (existing == figureId) return;
        }

        battle.WitnessIds.Add(figureId);
    }

    private static EntityId SideOf(Battle battle, EntityId civilizationId)
    {
        if (civilizationId == battle.AttackerId) return battle.AttackerId;
        if (civilizationId == battle.DefenderId) return battle.DefenderId;
        return EntityId.None;
    }

    private static bool? TriumphAt(Battle battle, CampaignMemory memory)
    {
        if (!battle.IsResolved) return null;

        if (memory.Role == CampaignRole.EnduredSiege)
        {
            return battle.SiegeOutcome is not SiegeOutcome.Carried;
        }

        if (battle.VictorId.IsNone) return null;

        return memory.SideId == battle.VictorId;
    }
}
