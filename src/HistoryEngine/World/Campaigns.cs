using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;

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
    /// <remarks>
    /// Walks the battle's own witnesses rather than the whole figure table: every memory of an
    /// engagement is paired with a <see cref="Witness"/> when it is recorded, so the witness list
    /// is exactly the figures a settling pass could touch.
    /// </remarks>
    public static void SettleBattle(WorldState world, Battle battle)
    {
        foreach (EntityId figureId in battle.WitnessIds)
        {
            if (!world.Figures.Contains(figureId)) continue;

            foreach (CampaignMemory memory in world.Figures[figureId].Campaigns)
            {
                if (memory.BattleId != battle.Id || memory.Triumphant is not null) continue;

                memory.Triumphant = TriumphAt(battle, memory);
            }
        }
    }

    /// <summary>
    /// Resolves one stable, role-sensitive fate for every named person present at a battle.
    /// </summary>
    /// <remarks>
    /// Each consequence kind is forked from battle id and figure id. Adding a witness therefore
    /// cannot change anybody already present, and adding renown later cannot move the death roll.
    /// </remarks>
    public static void ResolveConsequences(WorldState world, Battle battle, int year)
    {
        var witnesses = new List<EntityId>(battle.WitnessIds);
        witnesses.Sort();

        foreach (EntityId figureId in witnesses)
        {
            if (!world.Figures.Contains(figureId)) continue;

            Figure figure = world.Figures[figureId];
            var memories = new List<CampaignMemory>();
            foreach (CampaignMemory candidate in figure.Campaigns)
            {
                if (candidate.BattleId == battle.Id) memories.Add(candidate);
            }
            if (memories.Count == 0) continue;

            CampaignMemory memory = PrimaryRole(memories);
            if (memory.Fate != CampaignFate.Unresolved)
            {
                SetFate(memories, memory.Fate);
                continue;
            }

            // A sack is resolved before the rest of the participants so its event can name those
            // who fell. This pass still owns the stored campaign fate and never rolls them twice.
            if (!figure.IsAlive && figure.DeathYear == year && figure.DeathCause == DeathCause.Battle)
            {
                SetFate(memories, CampaignFate.Killed);
                continue;
            }

            double lossRate = LossRate(battle, memory.SideId);
            IRng consequence = world.Root
                .Fork("battle-consequence", battle.Id.ToDiscriminator())
                .Fork("figure", figure.Id.ToDiscriminator());

            bool sackAlreadyResolved = battle.Sacked
                && memories.Exists(item => item.Role == CampaignRole.EnduredSiege);
            double fatalRisk = ParticipantFatalRisk(
                memory.Role,
                memory.Triumphant == true,
                battle.SiegeOutcome == SiegeOutcome.Carried,
                battle.Sacked,
                lossRate);

            if (!sackAlreadyResolved && consequence.Fork("fatal").Chance(fatalRisk))
            {
                SetFate(memories, CampaignFate.Killed);
                Houses.Die(
                    world,
                    figure,
                    year,
                    DeathCause.Battle,
                    "at " + battle.Name,
                    new[] { battle.Id, battle.WarId });
                continue;
            }

            double injuryRisk = ParticipantInjuryRisk(
                memory.Role,
                battle.SiegeOutcome == SiegeOutcome.Carried,
                battle.Sacked,
                lossRate);
            bool wounded = LifeStories.Wound(
                world,
                figure,
                memory,
                battle,
                year,
                consequence.Fork("injury"),
                injuryRisk);
            SetFate(
                memories,
                wounded ? CampaignFate.Wounded : CampaignFate.ReturnedUnharmed);

            if (memory.Triumphant == false)
            {
                double traumaRisk = DetMath.Clamp(
                    0.05 + (0.42 * lossRate)
                    + (memory.Role == CampaignRole.EnduredSiege ? 0.06 : 0.0),
                    0.02,
                    0.34);
                memory.Traumatized = consequence.Fork("trauma").Chance(traumaRisk);

                if (memory.Role == CampaignRole.Fought)
                {
                    double desertionRisk = DetMath.Clamp(0.015 + (0.16 * lossRate), 0.0, 0.09);
                    memory.Deserted = consequence.Fork("desertion").Chance(desertionRisk);
                }
            }

            if (memory.Triumphant == true)
            {
                double notice = memory.Role switch
                {
                    CampaignRole.Commanded => 0.88,
                    CampaignRole.Fought => 0.34,
                    CampaignRole.EnduredSiege => battle.SiegeOutcome == SiegeOutcome.Relieved
                        ? 0.16
                        : 0.05,
                    _ => 0.0,
                };
                if (consequence.Fork("renown").Chance(notice))
                {
                    memory.RenownGained = memory.Role == CampaignRole.Commanded ? 3 : 1;
                }
            }
        }
    }

    /// <summary>Whether the separately narrated sack kills this named resident.</summary>
    internal static bool SackKills(WorldState world, Battle battle, Figure figure)
    {
        CampaignMemory? memory = figure.Campaigns.Find(item =>
            item.BattleId == battle.Id && item.Role == CampaignRole.EnduredSiege);
        if (memory is null) return false;

        double risk = ParticipantFatalRisk(
            memory.Role,
            triumphant: false,
            siegeCarried: true,
            sacked: true,
            LossRate(battle, memory.SideId));
        return world.Root
            .Fork("battle-consequence", battle.Id.ToDiscriminator())
            .Fork("figure", figure.Id.ToDiscriminator())
            .Fork("fatal")
            .Chance(risk);
    }

    /// <summary>Fatal risk is monotone in the recorded loss rate for an otherwise identical role.</summary>
    internal static double ParticipantFatalRisk(
        CampaignRole role,
        bool triumphant,
        bool siegeCarried,
        bool sacked,
        double lossRate)
    {
        lossRate = DetMath.Clamp01(lossRate);
        if (role == CampaignRole.EnduredSiege)
        {
            if (sacked) return 0.18;
            return siegeCarried ? 0.018 : 0.002;
        }

        double risk = role switch
        {
            CampaignRole.Commanded => (triumphant ? 0.012 : 0.042) + (0.18 * lossRate),
            CampaignRole.Fought => (triumphant ? 0.002 : 0.006) + (0.10 * lossRate),
            _ => 0.0,
        };
        return DetMath.Clamp(risk, 0.0, role == CampaignRole.Commanded ? 0.14 : 0.075);
    }

    internal static double ParticipantInjuryRisk(
        CampaignRole role, bool siegeCarried, bool sacked, double lossRate)
    {
        lossRate = DetMath.Clamp01(lossRate);
        double risk = role switch
        {
            CampaignRole.Commanded => 0.055 + (0.58 * lossRate),
            CampaignRole.Fought => 0.075 + (0.72 * lossRate),
            CampaignRole.EnduredSiege when sacked => 0.24,
            CampaignRole.EnduredSiege when siegeCarried => 0.12,
            CampaignRole.EnduredSiege => 0.035,
            _ => 0.0,
        };
        return DetMath.Clamp(risk, 0.0, 0.43);
    }

    /// <summary>Recent trauma and desertion reduce later service without becoming permanent traits.</summary>
    public static double Readiness(Figure figure, int year)
    {
        double readiness = 1.0;
        foreach (CampaignMemory memory in figure.Campaigns)
        {
            int elapsed = year - memory.Year;
            if (elapsed <= 0) continue;
            if (memory.Deserted && elapsed <= 6) readiness = Math.Min(readiness, 0.18);
            if (memory.Traumatized && elapsed <= 5) readiness = Math.Min(readiness, 0.62);
        }

        return readiness;
    }

    public static int Renown(Figure figure)
    {
        int renown = 0;
        foreach (CampaignMemory memory in figure.Campaigns) renown += memory.RenownGained;
        return renown;
    }

    /// <summary>The unconsumed battle most responsible for a later marshal appointment.</summary>
    public static CampaignMemory? PromotionCause(Figure figure)
    {
        CampaignMemory? best = null;
        foreach (CampaignMemory memory in figure.Campaigns)
        {
            if (memory.RenownGained <= 0 || memory.PromotionYear is not null) continue;
            if (best is null
                || memory.RenownGained > best.RenownGained
                || (memory.RenownGained == best.RenownGained && memory.Year > best.Year))
            {
                best = memory;
            }
        }

        return best;
    }

    private static double LossRate(Battle battle, EntityId sideId)
    {
        int losses = sideId == battle.AttackerId
            ? battle.AttackerLosses
            : battle.DefenderLosses;
        int strength = sideId == battle.AttackerId
            ? battle.AttackerStrength
            : battle.DefenderStrength;
        return DetMath.Clamp01((double)losses / Math.Max(1, strength));
    }

    private static CampaignMemory PrimaryRole(List<CampaignMemory> memories)
    {
        CampaignMemory primary = memories[0];
        foreach (CampaignMemory memory in memories)
        {
            if (ExposureRank(memory.Role) < ExposureRank(primary.Role)) primary = memory;
        }

        return primary;
    }

    private static int ExposureRank(CampaignRole role) => role switch
    {
        CampaignRole.Commanded => 0,
        CampaignRole.Fought => 1,
        CampaignRole.EnduredSiege => 2,
        _ => 3,
    };

    private static void SetFate(List<CampaignMemory> memories, CampaignFate fate)
    {
        foreach (CampaignMemory memory in memories) memory.Fate = fate;
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

        foreach (EntityId figureId in RulersOf(world, war))
        {
            foreach (CampaignMemory memory in world.Figures[figureId].Campaigns)
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

    /// <summary>
    /// The figures who ruled a belligerent while this war ran, in id order.
    /// </summary>
    /// <remarks>
    /// A <see cref="CampaignRole.Ruled"/> memory is only ever given to the sitting ruler of a
    /// belligerent, so the coalitions' own ruler lists bound the search to a handful of candidates
    /// instead of the whole figure table. Sorted by id so a caller that exposes the result keeps
    /// the order a full-table scan produced.
    /// </remarks>
    public static List<EntityId> RulersOf(WorldState world, War war)
    {
        var ids = new List<EntityId>();
        CollectRulers(world, war, war.Attackers, ids);
        CollectRulers(world, war, war.Defenders, ids);
        ids.Sort();
        return ids;
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

            // An officer goes where the host goes; a recruit is whoever was left at home this
            // season. It is also the loop the rank model turns on — a rung buys fields, fields buy
            // renown, and renown is the way up the next rung.
            double availability = LifeStories.Fitness(figure, year)
                * Readiness(figure, year)
                * Ranks.Turnout(figure.Rank);
            if (!fate.Chance(SoldierTakesField * availability)) continue;

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
            if (!world.IsPresentAt(figure, battle.SettlementId, world.Now)) continue;

            Remember(
                figure,
                war.Id,
                battle.Id,
                battle.DefenderId,
                year,
                CampaignRole.EnduredSiege);

            if (figure.AgeIn(year) < Succession.MajorityAge)
            {
                LifeStories.Remember(
                    figure,
                    MemoryKind.Siege,
                    year,
                    EventKind.SiegeBegan,
                    battle.Id,
                    battle.SettlementId,
                    0.84);
            }

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

    private static void CollectRulers(
        WorldState world, War war, IReadOnlyList<EntityId> coalition, List<EntityId> into)
    {
        foreach (EntityId civilizationId in coalition)
        {
            if (!world.Civilizations.Contains(civilizationId)) continue;

            foreach (EntityId figureId in world.Civilizations[civilizationId].RulerIds)
            {
                if (into.Contains(figureId)) continue;
                if (!world.Figures.Contains(figureId)) continue;
                if (!HasRuledMemory(world.Figures[figureId], war.Id)) continue;

                into.Add(figureId);
            }
        }
    }

    private static bool HasRuledMemory(Figure figure, EntityId warId)
    {
        foreach (CampaignMemory memory in figure.Campaigns)
        {
            if (memory.WarId == warId && memory.Role == CampaignRole.Ruled) return true;
        }

        return false;
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
