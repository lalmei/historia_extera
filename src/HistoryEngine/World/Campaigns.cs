using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.Systems;

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

    /// <summary>
    /// Travel days at which a soldier's chance of being the one levied has fallen to half of a
    /// garrison man's.
    /// </summary>
    /// <remarks>
    /// <para><b>A reciprocal, not an exponential.</b> <c>ReachHalfLifeDays / (ReachHalfLifeDays +
    /// days)</c> uses only the four arithmetic operators, so it carries no cross-runtime rounding
    /// risk the way <c>Math.Pow</c> or <c>Math.Exp</c> would — see <c>DeterminismGuardTests</c>,
    /// which bans both outright. It is 1 at zero days, 0.5 at the half-life, and falls toward zero
    /// without ever reaching it, which is the right shape: a soldier three weeks off is unlikely,
    /// never impossible — including a soldier the active route network cannot reach at all, who is
    /// now priced by <see cref="Itineraries.OverlandOneWayDays"/> rather than excluded outright; see
    /// that method and <see cref="NoteSoldiers"/> for why a route is a discount, not a gate.</para>
    ///
    /// <para><b>Calibrated against this world's measured travel days, not a real-world marching
    /// pace.</b> A "day" here is whatever <see cref="TravelPace"/> makes it, and that unit is not
    /// small: across the 199 multi-hop journeys in the standard seed-42 export, one leg costs a
    /// median of 47.3 travel days (p25 29.7, p75 56.0). An early draft set this to 6 by reasoning
    /// from a real week's march, which prices a soldier a single hop from the muster at
    /// <c>6 / (6 + 47) ≈ 0.11</c> — a 90% penalty for the ordinary case, not the exception. 90 puts
    /// that same one-hop soldier at <c>90 / (90 + 47) ≈ 0.66</c> — noticeably favoured over someone
    /// three or four hops out (<c>90 / (90 + 190) ≈ 0.32</c>) — while a soldier at the far end of a
    /// mature realm is still clearly the less likely draw.</para>
    ///
    /// <para><b>Two sweeps, because the first one measured the wrong thing.</b> A first pass gated
    /// an unreached residence out entirely rather than pricing it, which measured out to excluding
    /// 202 of 268 soldier-battle candidate pairs on seed 42 (75%) before this weight was ever read —
    /// most settlement pairs a realm's own towns form have no trade route between them at all
    /// (seed 42: 51 settlements, 18 separate connected components, only 48% of same-realm pairs
    /// route-connected), which says where merchants run, not whether an army can march. Under that
    /// gate even an infinite half-life — no distance discrimination at all — topped out at 28
    /// levied <see cref="CampaignRole.Fought"/> presences against a pre-distance baseline of 88, and
    /// no half-life could move that ceiling. Once the gate became a fallback price instead (this
    /// revision), the same sweep on seed 42 reads:</para>
    ///
    /// <para>30 → 50 Fought, 70 marches; 45 → 54, 63; 60 → 61, 70; 90 → 72, 81; 120 → 76, 85;
    /// 150 → 77, 86; 300 → 81, 90. Growth is steep through 90 and flattens after: 60→90 adds 11
    /// presences, 90→120 adds 4, 120→150 adds 1. The march-distance mix stays dominated by the near
    /// end at every value in this range (≈88–90% of marches ≤45 days, roughly the median leg cost,
    /// at 60 through 120 alike), so the choice is about how much of the plateau to buy, not about
    /// losing the skew. 90 was kept: it recovers participation to 72/88 (82% of the pre-distance
    /// baseline) and 184/186 figures with any campaign memory (99%) while sitting right at the knee
    /// of the curve — 120 buys four more presences for a third again the half-life, and 150 barely
    /// buys one more than that.</para>
    /// </remarks>
    private const double ReachHalfLifeDays = 90.0;

    /// <summary>Records who stood at this engagement, while the outcome may still be open.</summary>
    public static void NoteBattle(WorldState world, War war, Battle battle, int year)
    {
        // Searched once and shared by every named participant this call notes — commanders and
        // soldiery alike — rather than once per person. Null exactly when the battle has no
        // settlement to search from; see NoteSoldiers for why that is the one case distance cannot
        // be asked about at all.
        RouteGraph.SettlementReach? reach = battle.SettlementId.IsNone
            ? null
            : Itineraries.ReachFrom(world, battle.SettlementId, year);

        NoteCommander(world, war, battle, battle.AttackerCommanderId, battle.AttackerId, year, reach);
        NoteCommander(world, war, battle, battle.DefenderCommanderId, battle.DefenderId, year, reach);
        NoteSoldiers(world, war, battle, year, reach);

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
        WorldState world,
        War war,
        Battle battle,
        EntityId commanderId,
        EntityId sideId,
        int year,
        RouteGraph.SettlementReach? reach)
    {
        if (!world.Figures.Contains(commanderId)) return;

        Figure commander = world.Figures[commanderId];
        Remember(commander, war.Id, battle.Id, sideId, year, CampaignRole.Commanded);
        Witness(battle, commanderId);

        // A command is never withheld for distance — that reflects a choice the marshal roll
        // already made, not a levy this call could still decline. It only gains a march when one
        // is knowable, the same as any other named participant.
        MaybeRecordMarch(world, commander, reach, battle.SettlementId, year);
    }

    /// <summary>
    /// Levies the soldiery of both belligerents onto this field, weighted down by how far each
    /// one's home is from it.
    /// </summary>
    /// <remarks>
    /// <para><b>One search, not one per candidate.</b> Every soldier of every belligerent realm is
    /// walked for every battle, so a route search per candidate would be a fresh Dijkstra per
    /// soldier per battle. <see cref="Itineraries.ReachFrom"/> instead prices every settlement the
    /// active network reaches from the battle's own ground in a single pass — built once by <see
    /// cref="NoteBattle"/>, shared with the commander checks, and only ever read here, never
    /// searched again.</para>
    ///
    /// <para><b>A route is a discount on the distance, not a precondition for crossing it.</b> When
    /// the battle has no settlement to search from — an open-field engagement with nothing at stake
    /// to found the search on — distance cannot be asked at all, and the levy falls back to
    /// availability alone, exactly as before this existed. When it does have one, a residence the
    /// active route network does not reach is not excluded: it is priced at <see
    /// cref="Itineraries.OverlandOneWayDays"/> instead, the straight line at plain marching pace —
    /// the same fallback <see cref="Systems.TravelSystem.DurationDays(WorldState, EntityId,
    /// EntityId, Itinerary?, int)"/> already gives an ordinary journey with no itinerary, rather
    /// than declaring the trip impossible. An early version of this method treated <see
    /// cref="RouteGraph.SettlementReach.CostTo"/> returning null as "not levied", which measured
    /// out to excluding three quarters of a realm's own soldier-battle pairs on seed 42 — most
    /// settlement pairs a realm's own towns form have no trade route between them at all, which
    /// says something about where merchants run, not about whether an army can march. Distance
    /// alone is what should make a genuinely remote soldier unlikely, not an accident of trade
    /// topology.</para>
    ///
    /// <para><b>Independence survives the weighting.</b> Each candidate's distance term is a pure
    /// function of that candidate's own residence, read off one shared search but never compared
    /// against another candidate's distance or folded into a total. The roll stays a <see
    /// cref="IRng.Chance"/> forked by figure id alone, so adding one soldier's presence anywhere in
    /// the world still cannot move another soldier's odds — see the class remarks on why that
    /// invariant matters.</para>
    /// </remarks>
    private static void NoteSoldiers(
        WorldState world, War war, Battle battle, int year, RouteGraph.SettlementReach? reach)
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

            EntityId residenceId = world.ResidenceOf(figure);

            double reachWeight = 1.0;
            if (reach is not null)
            {
                // No resolvable home and no settlement to search from are the same fact: nothing
                // to price this candidate's distance against.
                if (residenceId.IsNone) continue;

                reachWeight = ReachWeight(TravelDaysTo(world, reach, residenceId, battle.SettlementId));
            }

            IRng fate = levy.Fork("figure", figure.Id.ToDiscriminator());

            // An officer goes where the host goes; a recruit is whoever was left at home this
            // season. It is also the loop the rank model turns on — a rung buys fields, fields buy
            // renown, and renown is the way up the next rung.
            double availability = LifeStories.Fitness(figure, year)
                * Readiness(figure, year)
                * Ranks.Turnout(figure.Rank);
            if (!fate.Chance(SoldierTakesField * availability * reachWeight)) continue;

            Remember(figure, war.Id, battle.Id, sideId, year, CampaignRole.Fought);
            Witness(battle, figure.Id);

            MaybeRecordMarch(world, figure, reach, battle.SettlementId, year);
        }
    }

    /// <summary>How far a residence's own reach cost pulls the levy chance down, in (0, 1].</summary>
    /// <remarks>Internal so a test can pin that a genuinely remote distance prices as vanishingly
    /// small without having to win — or lose — a coin flip to demonstrate it.</remarks>
    internal static double ReachWeight(int travelDays) =>
        DetMath.Clamp01(ReachHalfLifeDays / (ReachHalfLifeDays + travelDays));

    /// <summary>
    /// The one-way travel days from a residence to the battle's settlement: a discounted route cost
    /// where the search found one, the overland fallback where it did not, and zero when the two
    /// are the same settlement — checked by identity rather than left to the search.
    /// </summary>
    /// <remarks>
    /// <see cref="WorldState.RouteGraphFor"/> caches the route graph once per year, so a settlement
    /// founded — or reactivated — after that year's graph was built is invisible to it until the
    /// year turns, and <see cref="RouteGraph.SettlementReach.CostTo"/> can answer null for the
    /// search's own source settlement in that window. A soldier who lives in the very settlement the
    /// battle is fought at has to read as zero days regardless of whether this year's cached graph
    /// happens to know that settlement yet; the overland fallback would otherwise price it at the
    /// one-day floor <see cref="TravelPace.OneWayDays"/> gives any non-negative distance, including
    /// zero, and that day would wrongly count as a march instead of the garrison case.
    /// </remarks>
    private static int TravelDaysTo(
        WorldState world, RouteGraph.SettlementReach reach, EntityId residenceId, EntityId battleSettlementId)
    {
        if (residenceId == battleSettlementId) return 0;

        return reach.CostTo(residenceId) ?? Itineraries.OverlandOneWayDays(world, residenceId, battleSettlementId);
    }

    /// <summary>
    /// Gives a named participant the march their presence away from home otherwise leaves
    /// unexplained, when the distance to charge it against is actually knowable.
    /// </summary>
    /// <remarks>
    /// Shared by <see cref="NoteCommander"/> and <see cref="NoteSoldiers"/> so a commander who rode
    /// out and a soldier who marched get the same treatment from the same search, rather than two
    /// near-identical checks that could quietly drift apart. Silent whenever there is nothing to
    /// record: no ground to search from, no resolvable residence, or a residence that already is
    /// the battle's own settlement. An unreached residence is no longer one of those cases — <see
    /// cref="RecordMarch"/> already hands a null itinerary through to <see
    /// cref="Systems.TravelSystem.DurationDays(WorldState, EntityId, EntityId, Itinerary?, int)"/>,
    /// which prices it at the same overland fallback the levy weighed the candidate against.
    /// </remarks>
    private static void MaybeRecordMarch(
        WorldState world, Figure figure, RouteGraph.SettlementReach? reach, EntityId battleSettlementId, int year)
    {
        if (reach is null) return;

        EntityId residenceId = world.ResidenceOf(figure);
        if (residenceId.IsNone) return;

        // Zero days means the battle was fought at their own residence: not a march, the garrison
        // case NoteBesieged already covers under its own role.
        if (TravelDaysTo(world, reach, residenceId, battleSettlementId) == 0) return;

        RecordMarch(world, figure, reach, residenceId, battleSettlementId, year);
    }

    /// <summary>
    /// Gives a levied figure the march their presence at the field otherwise leaves unexplained.
    /// </summary>
    /// <remarks>
    /// <para>Built from the search that already priced the levy, not a second guess at the same
    /// distance: <paramref name="reach"/> was searched from the battle's settlement, so the path it
    /// hands back for this figure's residence runs battle-to-home and is reversed once to read
    /// home-to-battle — valid because the route network is undirected, see <see
    /// cref="Itinerary.Reversed"/>.</para>
    ///
    /// <para>Duration reuses <see cref="TravelSystem.DurationDays(WorldState, EntityId, EntityId,
    /// Itinerary?, int)"/> rather than a third formula for the same arithmetic every other journey
    /// in the engine already uses.</para>
    ///
    /// <para><b>Chronicled, like every other journey.</b> <c>TravelTests.JourneysAreTripsNotMoves</c>
    /// already pins a stronger invariant than "a march happens": every entry on
    /// <see cref="Figure.Journeys"/> is exactly one <see cref="EventKind.JourneyMade"/> event, so a
    /// life page's journeys and its chronicle never disagree about how many there were. A march that
    /// skipped the chronicle to save events would be the one journey kind that broke that rule, not
    /// a cheaper version of it. It also does not compete on volume with the acquaintance events
    /// #183 removed — those were near-quadratic in the figure count; a march is bounded by how many
    /// soldiers a battle levies, the same bound <see cref="CampaignMemory"/> already accepts without
    /// needing a discount.</para>
    /// </remarks>
    private static void RecordMarch(
        WorldState world,
        Figure figure,
        RouteGraph.SettlementReach reach,
        EntityId fromId,
        EntityId toId,
        int year)
    {
        Itinerary? itinerary = reach.To(fromId)?.Reversed();
        int durationDays = TravelSystem.DurationDays(world, fromId, toId, itinerary, year);
        Stamp departed = world.Now;
        Stamp expectedReturn = world.Config.Calendar.Plus(departed, durationDays);

        var journey = new Journey(
            JourneyKind.Campaign, departed, fromId, toId, EntityId.None, durationDays, expectedReturn, itinerary);
        figure.Journeys.Add(journey);

        world.Chronicle.Record(
            year,
            EventKind.JourneyMade,
            figure.Id,
            location: toId,
            extra: new[] { fromId },
            data: Chronicle.Data(("purpose", "with the host"), ("kind", journey.Kind.ToString())),
            significance: Significance.Routine);
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
