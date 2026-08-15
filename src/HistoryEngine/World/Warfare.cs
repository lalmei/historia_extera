using System.Globalization;
using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;

namespace HistoryEngine.World;

/// <summary>
/// Declaring wars, fighting battles, sacking towns, and settling the peace.
/// </summary>
/// <remarks>
/// <para>The mutating counterpart to <see cref="Diplomacy"/>, which only reads. Everything here
/// changes the world and writes an event; the systems above decide only <em>whether</em> and
/// <em>when</em>. Keeping the two apart is what lets the war system be short enough to read.</para>
///
/// <para><b>A battle is not a peace.</b> Winning a siege sacks a town; it does not take it.
/// Borders move at the treaty and nowhere else, which is both how it actually worked and what
/// makes <see cref="MakePeace"/> the event a war is remembered for rather than a formality after
/// the fact.</para>
/// </remarks>
public static class Warfare
{
    /// <summary>Share of the levy that actually reaches a given field, at worst and at best.</summary>
    /// <remarks>
    /// Without it, two realms of similar size fight a coin toss every year and the larger one
    /// eventually wins every war by arithmetic. The spread is what lets a smaller realm win a
    /// battle it had no business winning, which is most of what makes a war worth reading about.
    /// </remarks>
    private const double MinCommitment = 0.55;

    private const double MaxCommitment = 1.0;

    /// <summary>Defender's advantage for fighting on ground it knows.</summary>
    private const double HomeGroundBonus = 1.15;

    /// <summary>What walls are worth. The single largest term in a battle.</summary>
    /// <remarks>
    /// Fortification was a yearly dice roll that changed nothing before this milestone. Making it
    /// worth half again a defender's strength is what turns <see cref="CultureValues.Aggression"/>
    /// — which drives wall-building — into a defensive trait as well as an offensive one, and what
    /// gives a small realm a way to survive a large neighbour.
    /// </remarks>
    private const double FortificationBonus = 1.5;

    /// <summary>Steadying effect of a ruler present on the field.</summary>
    private const double CommanderBonus = 1.08;

    /// <summary>Fraction of the smaller army the losing side leaves on the field, at worst and best.</summary>
    private const double MinLoserLosses = 0.18;

    private const double MaxLoserLosses = 0.32;

    private const double MinVictorLosses = 0.06;

    private const double MaxVictorLosses = 0.15;

    /// <summary>Most of its people one battle may cost a single settlement.</summary>
    /// <remarks>
    /// A levy is drawn from everywhere, but the arithmetic that spreads casualties over settlements
    /// in proportion to their size will happily empty a hamlet that contributed forty men. Capping
    /// the share keeps a lost battle from abandoning villages that were never near it.
    /// </remarks>
    private const double MaxSettlementLossFraction = 0.20;

    /// <summary>Population a sack carries off, at its mildest and its worst.</summary>
    private const double MinSackFraction = 0.20;

    private const double MaxSackFraction = 0.45;

    /// <summary>Dead below which a battle's cost is not worth stating.</summary>
    private const int NotableLosses = 25;

    /// <summary>Below this, a settlement is not worth the trouble of sacking.</summary>
    /// <remarks>
    /// Sacking every hamlet an army walked past made a sack the commonest war event in the
    /// chronicle — more than one for every two battles fought — which is precisely backwards. A
    /// sack should be the thing a war is remembered for.
    /// </remarks>
    private const int WorthSacking = 250;

    /// <summary>Odds a named figure posted in a sacked town does not survive it.</summary>
    /// <remarks>
    /// High, deliberately. A governor is not a bystander when the walls are carried — they are
    /// whoever the storming party is looking for — and the whole point of giving an office a
    /// street address was to make a posting somewhere dangerous mean something.
    /// </remarks>
    private const double SackedResidentFalls = 0.35;

    /// <summary>Odds a standing marshal takes a given field, rather than some other dynast.</summary>
    /// <remarks>
    /// Not one. A realm fights on more than one frontier and a marshal cannot be at both, and a
    /// figure who commands every engagement of a forty-year career is a hero rather than a record.
    /// </remarks>
    private const double MarshalTakesTheField = 0.75;

    /// <summary>Chance the losing commander does not come home, and the winning one's.</summary>
    private const double LoserCommanderFalls = 0.14;

    private const double VictorCommanderFalls = 0.03;

    /// <summary>Chance a court appoints an adult dynast when the ruler stays home.</summary>
    private const double OfficerTakesField = 0.72;

    /// <summary>
    /// Accumulated advantage at which one side can dictate terms.
    /// </summary>
    /// <remarks>
    /// Roughly three clear victories, and set against how many battles a war actually contains
    /// rather than against how decisive the number sounds. At four battles' worth, half of all
    /// wars ended in a stalemate that transferred nothing — which is not implausible history, but
    /// it is a chronicle in which most wars have no consequence to write down.
    /// </remarks>
    public const double DecisiveScore = 1.8;

    /// <summary>Advantage above <see cref="DecisiveScore"/> that buys one more region at the table.</summary>
    private const double ScorePerExtraRegion = 1.6;

    /// <summary>Most regions one peace may transfer, however total the victory.</summary>
    private const int MaxCededRegions = 3;

    // -----------------------------------------------------------------------
    // Declaration
    // -----------------------------------------------------------------------

    /// <summary>Opens a war and records it.</summary>
    public static War Declare(
        WorldState world,
        Civilization aggressor,
        Civilization defender,
        CasusBelli cause,
        EntityId claimedRelicId,
        EntityId aggressorReligionId,
        EntityId defenderReligionId,
        IReadOnlyList<Region> frontier,
        int year)
    {
        EntityId id = world.Wars.NextId;

        var war = new War(
            id,
            NameWar(world, aggressor, defender, cause, claimedRelicId, frontier),
            aggressor.Id,
            defender.Id,
            cause,
            claimedRelicId,
            aggressorReligionId,
            defenderReligionId,
            year);

        world.Wars.Add(war);

        world.Chronicle.Record(
            year,
            EventKind.WarDeclared,
            aggressor.Id,
            obj: defender.Id,
            location: war.Id,
            extra: CauseReferences(claimedRelicId, aggressorReligionId, defenderReligionId),
            data: Chronicle.Data(("cause", DeclarationCause(world, war))));

        return war;
    }

    /// <summary>Brings an ally into a war already running.</summary>
    public static void Join(
        WorldState world, War war, Civilization joiner, Civilization calledBy, bool attacking, int year)
    {
        List<EntityId> side = attacking ? war.Attackers : war.Defenders;
        if (side.Contains(joiner.Id)) return;

        side.Add(joiner.Id);

        world.Chronicle.Record(
            year, EventKind.WarJoined, joiner.Id, obj: war.Id, location: calledBy.Id);
    }

    // -----------------------------------------------------------------------
    // Battles
    // -----------------------------------------------------------------------

    /// <summary>
    /// How far behind the aggressor must fall before the defender takes the offensive.
    /// </summary>
    /// <remarks>
    /// <para>Hysteresis, and it is not cosmetic. Handing the initiative to whichever side is ahead
    /// — the obvious rule — makes the war score an oscillator rather than a random walk, because
    /// the side attacking is the side giving up the defender's advantage and so the side likely to
    /// lose the next battle. The score is pushed back across zero every time it crosses, never
    /// reaches a decisive margin, and every war runs to the exhaustion cap: on seed 42 four wars
    /// in six ran the full twenty-five years and one of them fought seventeen battles.</para>
    ///
    /// <para>With a band, a war has a direction. The aggressor presses until it is clearly losing,
    /// the defender counter-attacks, and whoever is winning by then goes on winning.</para>
    /// </remarks>
    private const double InitiativeSwing = 1.0;

    /// <summary>
    /// Fights one engagement, or does nothing if the two sides cannot reach each other.
    /// </summary>
    /// <remarks>
    /// The aggressor presses the war it started. A defender that has driven the score past
    /// <see cref="InitiativeSwing"/> takes the offensive instead, which is the counter-offensive —
    /// the most legible thing a war that turned can do, and the only way a defender ever takes
    /// ground.
    /// </remarks>
    public static Battle? Fight(WorldState world, War war, int year, IRng rng)
    {
        bool attackersHaveInitiative = war.Score > -InitiativeSwing;

        IReadOnlyList<EntityId> attacking = attackersHaveInitiative ? war.Attackers : war.Defenders;
        IReadOnlyList<EntityId> defending = attackersHaveInitiative ? war.Defenders : war.Attackers;

        if (!FindField(
                world, attacking, defending, rng,
                out Civilization? invader, out Civilization? holder, out Region? field))
        {
            return null;
        }

        Settlement? contested = SettlementIn(world, field!, holder!);

        var battle = new Battle(
            world.Battles.NextId,
            NameBattle(world, field!, contested),
            war.Id,
            year,
            field!.Id)
        {
            SettlementId = contested?.Id ?? EntityId.None,
            IsSiege = IsSiege(contested),
            AttackerId = invader!.Id,
            DefenderId = holder!.Id,
        };

        int attackerForce = Commit(world, attacking, rng);
        int defenderForce = Commit(world, defending, rng);

        battle.AttackerStrength = attackerForce;
        battle.DefenderStrength = defenderForce;

        battle.AttackerCommanderId = Commander(world, invader, year, rng);
        battle.DefenderCommanderId = Commander(world, holder, year, rng);

        double attackerPower = attackerForce
            * (battle.AttackerCommanderId.IsNone ? 1.0 : CommanderBonus);

        double defenderPower = defenderForce
            * HomeGroundBonus
            * DefenceOf(field, contested)
            * (battle.DefenderCommanderId.IsNone ? 1.0 : CommanderBonus);

        // Both sides being nothing is possible when a realm has been reduced to hamlets; a coin
        // toss is the honest answer, and the levies are too small for the result to matter.
        double total = attackerPower + defenderPower;
        bool attackerWins = total <= 0.0
            ? rng.Chance(0.5)
            : rng.Chance(attackerPower / total);

        battle.VictorId = attackerWins ? invader.Id : holder.Id;

        // Only the two realms that actually met on the field carry the memory of it. A coalition
        // partner that sent no levy to this battle is not marked by a day it did not have.
        (attackerWins ? invader : holder).Fortunes.WonABattle();
        (attackerWins ? holder : invader).Fortunes.LostABattle();

        int contestedForce = Math.Max(1, Math.Min(attackerForce, defenderForce));
        int victorLosses = (int)(contestedForce * rng.NextDouble(MinVictorLosses, MaxVictorLosses));
        int loserLosses = (int)(contestedForce * rng.NextDouble(MinLoserLosses, MaxLoserLosses));

        battle.AttackerLosses = attackerWins ? victorLosses : loserLosses;
        battle.DefenderLosses = attackerWins ? loserLosses : victorLosses;

        TakeCasualties(world, attacking, battle.AttackerLosses);
        TakeCasualties(world, defending, battle.DefenderLosses);

        world.Battles.Add(battle);
        war.BattleIds.Add(battle.Id);
        war.AttackerLosses += attackersHaveInitiative ? battle.AttackerLosses : battle.DefenderLosses;
        war.DefenderLosses += attackersHaveInitiative ? battle.DefenderLosses : battle.AttackerLosses;

        // The butcher's bill is stated only when there was one. A rout that cost three men reads
        // far better as "Heraanes prevailed at the Battle of Vikrastad" than with the figure
        // attached, and the tail of a war against a realm with nothing left is full of them.
        var record = Chronicle.Data(("region", world.NameOf(field.Id)));
        if (battle.TotalLosses >= NotableLosses)
        {
            record["losses"] = battle.TotalLosses.ToString(CultureInfo.InvariantCulture);
        }

        world.Chronicle.Record(
            year,
            EventKind.BattleFought,
            battle.Id,
            obj: battle.VictorId,
            location: contested?.Id ?? EntityId.None,
            extra: Participants(war, battle),
            data: record);

        // Score is kept from the war aggressor's point of view whoever holds the initiative, so
        // the peace can read it without asking who was attacking in which year.
        double swing = Swing(battle, contestedForce);
        bool warAttackersWon = attackersHaveInitiative == attackerWins;
        war.Score += warAttackersWon ? swing : -swing;

        Casualty(world, battle, attackerWins, year, rng);

        if (attackerWins && contested is not null) MaybeSack(world, war, battle, contested, year, rng);

        return battle;
    }

    /// <summary>
    /// How much one battle moves the war.
    /// </summary>
    /// <remarks>
    /// A margin rather than a win: a battle that cost the loser three men for every one is worth
    /// far more than one decided by a handful, and a siege carried is worth more again because it
    /// opens a road. Without the margin term every engagement counts the same and a war is
    /// decided by whoever wins three coin tosses first.
    /// </remarks>
    private static double Swing(Battle battle, int contestedForce)
    {
        int winnerLosses = battle.VictorId == battle.AttackerId
            ? battle.AttackerLosses
            : battle.DefenderLosses;

        int loserLosses = battle.TotalLosses - winnerLosses;

        double margin = DetMath.Clamp01((loserLosses - winnerLosses) / (double)contestedForce);
        double swing = 0.5 + margin;

        if (battle.IsSiege && battle.VictorId == battle.AttackerId) swing += 0.5;

        return swing;
    }

    /// <summary>How deep into the defender's territory a campaign will look for something to fight over.</summary>
    /// <remarks>
    /// Three regions. One would send every army at the single nearest province whatever stood in
    /// it; letting the whole territory compete would have a campaign open by besieging the
    /// defender's largest city wherever in the realm it happened to be.
    /// </remarks>
    private const int FrontDepth = 3;

    /// <summary>Finds somewhere on the defending coalition's front that the attacking one can reach.</summary>
    /// <remarks>
    /// Principals first, so the war is normally fought between the two realms that started it, and
    /// an ally's territory only becomes the front when there is no other. A coalition that cannot
    /// reach its enemy at all fights no battles and the war ends in exhaustion, which is the right
    /// outcome for two realms with a continent between them.
    /// </remarks>
    private static bool FindField(
        WorldState world,
        IReadOnlyList<EntityId> attacking,
        IReadOnlyList<EntityId> defending,
        IRng rng,
        out Civilization? invader,
        out Civilization? holder,
        out Region? field)
    {
        foreach (EntityId attackerId in attacking)
        {
            Civilization attacker = world.Civilizations[attackerId];
            if (!attacker.IsActive) continue;

            foreach (EntityId defenderId in defending)
            {
                Civilization defender = world.Civilizations[defenderId];
                if (!defender.IsActive) continue;

                List<Region> front = Diplomacy.Frontline(world, attacker, defender);
                if (front.Count == 0) continue;

                invader = attacker;
                holder = defender;
                field = Objective(world, front, defender, rng);
                return true;
            }
        }

        invader = null;
        holder = null;
        field = null;
        return false;
    }

    /// <summary>Weight an empty province carries against a populated one, when a campaign chooses.</summary>
    private const double EmptyGroundAppeal = 300.0;

    /// <summary>
    /// The province on the front this year's campaign goes for.
    /// </summary>
    /// <remarks>
    /// <para>Armies march on towns, so the choice is weighted by population — but weighted, not
    /// decided. Always taking the largest sends a war back to the same walls every single year:
    /// on seed 42 one thirteen-year war produced ten engagements at one town and a chronicle line
    /// reading "the 16th Siege of Ascula". A weighted draw keeps the important place important
    /// while letting a campaign go somewhere else, which is what a war looks like.</para>
    /// </remarks>
    private static Region Objective(
        WorldState world, List<Region> front, Civilization holder, IRng rng)
    {
        int depth = Math.Min(FrontDepth, front.Count);

        var weights = new double[depth];
        double total = 0.0;

        for (int i = 0; i < depth; i++)
        {
            Settlement? settlement = SettlementIn(world, front[i], holder);
            weights[i] = (settlement?.Population ?? 0) + EmptyGroundAppeal;
            total += weights[i];
        }

        double roll = rng.NextDouble() * total;

        for (int i = 0; i < depth; i++)
        {
            roll -= weights[i];
            if (roll < 0.0) return front[i];
        }

        return front[depth - 1];
    }

    private static Settlement? SettlementIn(WorldState world, Region region, Civilization owner)
    {
        Settlement? found = null;

        foreach (EntityId id in owner.SettlementIds)
        {
            Settlement settlement = world.Settlements[id];
            if (!settlement.IsActive || settlement.RegionId != region.Id) continue;

            if (found is null || settlement.Population > found.Population) found = settlement;
        }

        return found;
    }

    /// <summary>
    /// A settlement is besieged rather than merely fought over once it is worth investing.
    /// </summary>
    /// <remarks>
    /// A hamlet has nothing to stand behind, so a battle in its region is a field battle that
    /// happens to be near it. Gating on walls or on a town keeps sieges meaning something and
    /// keeps the fortification bonus from applying to open ground.
    /// </remarks>
    private static bool IsSiege(Settlement? settlement) =>
        settlement is not null
        && (settlement.IsFortified || settlement.Tier >= SettlementTier.Town);

    /// <summary>Terrain and walls, as a multiplier on the defender.</summary>
    private static double DefenceOf(Region region, Settlement? contested)
    {
        // High ground and broken country, both read straight off the region's cached statistics.
        double terrain = 1.0 + (0.25 * DetMath.InverseLerp(400.0, 2000.0, region.MeanHeight));
        if (region.HasRiver) terrain += 0.08;
        if (region.Biome == Biome.Wetland) terrain += 0.10;

        if (IsSiege(contested) && contested!.IsFortified) terrain *= FortificationBonus;

        return terrain;
    }

    /// <summary>What a coalition actually puts on this field, after the levy and the roads.</summary>
    private static int Commit(WorldState world, IReadOnlyList<EntityId> coalition, IRng rng) =>
        (int)(Diplomacy.Levy(world, coalition) * rng.NextDouble(MinCommitment, MaxCommitment));

    /// <summary>The ruler, or an adult dynast appointed when the ruler stays home.</summary>
    /// <remarks>
    /// A chiefdom's leader is expected to fight and a republic's consul mostly is not, so the
    /// chance is drawn from government as well as from how martial the culture is. It is also the
    /// only way a ruler dies of anything but age or illness at a believable rate, which is what
    /// makes a war able to end a dynasty. A ruler who stays home no longer leaves a blank command
    /// slot: courts entrust campaigns to adult dynasts, giving cadets and heirs a military life the
    /// chronicle can follow and a battlefield on which they can die.
    /// </remarks>
    private static EntityId Commander(
        WorldState world, Civilization civilization, int year, IRng rng)
    {
        if (!world.Figures.Contains(civilization.CurrentRulerId)) return EntityId.None;

        Figure ruler = world.Figures[civilization.CurrentRulerId];
        if (!ruler.IsAlive) return EntityId.None;

        // A child on the throne does not lead an army; the regent governs, not campaigns.
        if (ruler.AgeIn(year) < Succession.MajorityAge) return EntityId.None;

        Culture culture = world.CultureOf(civilization);
        double chance = culture.Government switch
        {
            // Government form is the people's constitution, not the ruler's mood.
            GovernmentForm.Chiefdom => 0.80,
            GovernmentForm.Monarchy => 0.60,
            GovernmentForm.Theocracy => 0.35,
            GovernmentForm.Oligarchy => 0.30,
            _ => 0.25,
        };

        // Whether this particular ruler rides out is theirs to decide, so it takes the realm's
        // effective aggression: a cautious king of a warlike people sends someone else, and so
        // does a bold one whose realm has just been bled white.
        if (rng.Chance(chance * DetMath.Lerp(0.6, 1.3, world.ValuesFor(civilization).Aggression)))
        {
            return ruler.Id;
        }

        IRng officers = rng.Fork("officer", civilization.Id.ToDiscriminator());
        if (!officers.Chance(OfficerTakesField)) return EntityId.None;

        // A realm with a standing marshal has mostly answered the question of who commands — but
        // only mostly, and deliberately. Handing every campaign to one man collapses the variance
        // that made 216 of 604 named commands go to non-rulers: he either dies in his first season
        // or appears undefeated for thirty years, and no other cadet ever sees a battlefield.
        Figure? marshal = Offices.HolderOf(world, civilization, OfficeKind.Marshal);
        if (marshal is not null
            && marshal.AgeIn(year) >= Succession.MajorityAge
            && officers.Chance(MarshalTakesTheField))
        {
            return marshal.Id;
        }

        var candidates = new List<Figure>();
        foreach (Figure kin in Succession.Kin(world, civilization))
        {
            if (!kin.IsAlive || kin.Id == ruler.Id) continue;
            if (kin.CivilizationId != civilization.Id) continue;
            if (kin.AgeIn(year) < Succession.MajorityAge) continue;

            candidates.Add(kin);
        }

        return candidates.Count == 0 ? EntityId.None : officers.Pick(candidates).Id;
    }

    /// <summary>Kills a commander who did not come home. Succession runs later the same year.</summary>
    private static void Casualty(
        WorldState world, Battle battle, bool attackerWon, int year, IRng rng)
    {
        Fall(battle.AttackerCommanderId, attackerWon);
        Fall(battle.DefenderCommanderId, !attackerWon);

        void Fall(EntityId commanderId, bool won)
        {
            if (!world.Figures.Contains(commanderId)) return;

            Figure commander = world.Figures[commanderId];
            if (!commander.IsAlive) return;

            if (rng.Chance(won ? VictorCommanderFalls : LoserCommanderFalls))
            {
                Houses.Die(world, commander, year, DeathCause.Battle);
            }
        }
    }

    // -----------------------------------------------------------------------
    // Sacking
    // -----------------------------------------------------------------------

    /// <summary>
    /// Puts a taken settlement to the sack.
    /// </summary>
    /// <remarks>
    /// The immediate cost of losing a siege, and the one thing in this milestone that can empty a
    /// city inside a single year. Walls come down with it, so a sacked town is easier to take the
    /// next time — which is what turns a war into a campaign rather than a series of unrelated
    /// engagements. A settlement reduced far enough is finished off by the settlement lifecycle in
    /// the ordinary way, without war needing its own destruction rule.
    /// </remarks>
    private static void MaybeSack(
        WorldState world, War war, Battle battle, Settlement target, int year, IRng rng)
    {
        if (target.Population < WorthSacking) return;
        if (AlreadySacked(world, war, target)) return;

        Civilization sacker = world.Civilizations[battle.AttackerId];

        // Whether a taken town is put to the sack is the commanding realm's decision on the day,
        // not a standing property of its people.
        double chance = DetMath.Lerp(0.30, 0.65, world.ValuesFor(sacker).Aggression);
        if (!rng.Chance(chance)) return;

        int before = target.Population;
        int lost = (int)(before * rng.NextDouble(MinSackFraction, MaxSackFraction));
        if (lost <= 0) return;

        target.Population = Math.Max(0, before - lost);
        target.IsFortified = false;
        battle.Sacked = true;

        Civilization owner = world.Civilizations[target.CivilizationId];
        owner.Population = Math.Max(0, owner.Population - lost);

        owner.Fortunes.TownSacked();
        sacker.Fortunes.SackedATown();

        List<Figure> fallen = ResidentCasualties(world, target, year, rng);

        world.Chronicle.Record(
            year,
            EventKind.SettlementSacked,
            target.Id,
            obj: sacker.Id,
            location: target.RegionId,
            extra: Sacked(war, battle, owner, fallen),
            data: Chronicle.Data(("lost", lost.ToString(CultureInfo.InvariantCulture))));

        // The cause precedes its named casualties, as a disaster's does.
        foreach (Figure figure in fallen)
        {
            Houses.Die(world, figure, year, DeathCause.Battle, "in the sack of " + target.Name);
        }

        // What the place was keeping goes home with the army, or does not survive the night.
        // Recorded after the sack so the chronicle reads in the order it happened.
        Treasures.Loot(world, target, sacker, year, rng);
    }

    /// <summary>
    /// Named people who were in the town when it fell.
    /// </summary>
    /// <remarks>
    /// <para>The exposure a governorship exists to create, and the one the offices design named
    /// first. A figure's residence is a realm for almost everybody, and the court can honestly be
    /// placed only at the capital — but a governor lives in the town they govern, so a storming is
    /// something that can actually reach them.</para>
    ///
    /// <para><b>Sacking rather than the earthquake this was first wired to.</b> Disaster exposure
    /// is correct and fires about once in three worlds: calamities are rare, most fall on
    /// settlements nobody governs, and the per-figure risk is a fraction of an already small
    /// severity. A sack is aimed at a particular town by an army that has just carried it, which
    /// makes it both far likelier to coincide with a governor and a far better thing for a
    /// chronicle to record.</para>
    /// </remarks>
    private static List<Figure> ResidentCasualties(
        WorldState world, Settlement target, int year, IRng rng)
    {
        var fallen = new List<Figure>();
        IRng storm = rng.Fork("sack-casualties", target.Id.ToDiscriminator());

        foreach (Figure figure in world.Figures)
        {
            if (!figure.IsAlive || figure.ResidenceSettlementId != target.Id) continue;

            // A capital's whole court is not in the streets when the walls come down; the figures
            // a sack reaches are the ones actually posted here.
            if (target.IsCapital) continue;

            IRng fate = storm.Fork("figure", figure.Id.ToDiscriminator());
            if (fate.Chance(SackedResidentFalls)) fallen.Add(figure);
        }

        return fallen;
    }

    /// <summary>The war, the battle, the dispossessed realm, and anyone named who fell with it.</summary>
    private static EntityId[] Sacked(
        War war, Battle battle, Civilization owner, List<Figure> fallen)
    {
        var ids = new List<EntityId>(3 + fallen.Count) { war.Id, battle.Id, owner.Id };
        foreach (Figure figure in fallen) ids.Add(figure.Id);
        return ids.ToArray();
    }

    /// <summary>
    /// Whether this war has already put this settlement to the sack.
    /// </summary>
    /// <remarks>
    /// Once per war. A town besieged year after year was otherwise sacked year after year — three
    /// times in three years on seed 42, each entry a little smaller than the last, which reads as
    /// a loop rather than as a war. A place has one fortune to lose, and losing it is the thing
    /// the war is remembered for. Across wars it can happen again, a generation having rebuilt it.
    /// </remarks>
    private static bool AlreadySacked(WorldState world, War war, Settlement target)
    {
        foreach (EntityId battleId in war.BattleIds)
        {
            Battle past = world.Battles[battleId];
            if (past.Sacked && past.SettlementId == target.Id) return true;
        }

        return false;
    }

    // -----------------------------------------------------------------------
    // Peace
    // -----------------------------------------------------------------------

    /// <summary>
    /// Ends a war: terms, territory, truces, and the grudge that outlives all three.
    /// </summary>
    /// <remarks>
    /// <para>Every civilization on the losing side gives ground, and it gives it to whichever
    /// member of the winning coalition actually borders it. Handing the whole settlement to the
    /// principal would produce realms holding provinces on the far side of an ally, which nothing
    /// else in the simulation can explain and expansion would then try to reach across.</para>
    ///
    /// <para>The truce is not decoration. Without it the loser's collapsed opinion of the winner
    /// re-declares the same war the following spring, wars run continuously, and neither
    /// exhaustion nor recovery ever appears in a chronicle.</para>
    /// </remarks>
    public static void MakePeace(WorldState world, War war, int year, IRng rng)
    {
        WarOutcome outcome = war.Score >= DecisiveScore
            ? WarOutcome.AggressorVictory
            : war.Score <= -DecisiveScore
                ? WarOutcome.DefenderVictory
                : WarOutcome.Stalemate;

        war.Outcome = outcome;
        war.EndYear = year;

        IReadOnlyList<EntityId> winners = outcome == WarOutcome.DefenderVictory
            ? war.Defenders
            : war.Attackers;

        IReadOnlyList<EntityId> losers = outcome == WarOutcome.DefenderVictory
            ? war.Attackers
            : war.Defenders;

        EntityId victor = EntityId.None;

        if (outcome != WarOutcome.Stalemate)
        {
            victor = winners[0];

            int spoils = 1 + (int)((Math.Abs(war.Score) - DecisiveScore) / ScorePerExtraRegion);
            spoils = Math.Min(spoils, MaxCededRegions);

            // The aggressor named an object rather than a province. If it prevails, the relic is
            // the ordinary term of peace; a defender that turns the war around still takes land.
            if (outcome == WarOutcome.AggressorVictory && war.Cause == CasusBelli.RelicClaim)
            {
                spoils = 0;
            }

            foreach (EntityId loserId in losers)
            {
                Civilization loser = world.Civilizations[loserId];
                if (!loser.IsActive) continue;

                Civilization? taker = NearestVictor(world, winners, loser);
                if (taker is null) continue;

                foreach (Region region in Spoils(world, war, taker, loser, spoils))
                {
                    Realms.Cede(world, region, loser, taker, year, war);
                }
            }

            if (outcome == WarOutcome.AggressorVictory
                && war.Cause == CasusBelli.RelicClaim
                && !war.ClaimedRelicId.IsNone
                && world.Artifacts.Contains(war.ClaimedRelicId))
            {
                Treasures.Claim(
                    world,
                    world.Artifacts[war.ClaimedRelicId],
                    world.Civilizations[war.AggressorId],
                    war,
                    year);
            }
        }

        Settle(world, war, winners, losers, outcome, year, rng);

        var data = Chronicle.Data(
            ("outcome", outcome == WarOutcome.Stalemate ? "with neither side prevailing" : "in victory"),
            ("battles", war.BattleIds.Count.ToString(CultureInfo.InvariantCulture)),
            ("dead", (war.AttackerLosses + war.DefenderLosses).ToString(CultureInfo.InvariantCulture)));

        int span = year - war.StartYear;
        if (span > 0) data["years"] = Chronicle.Years(span);

        world.Chronicle.Record(
            year,
            EventKind.WarEnded,
            war.Id,
            obj: victor,
            extra: Belligerents(war),
            data: data);

        // Checked after the peace is written so the chronicle reads in the order it happened: the
        // treaty, and then the realm it finished.
        foreach (EntityId loserId in losers)
        {
            Civilization loser = world.Civilizations[loserId];

            if (loser.IsActive && Realms.IsFinished(world, loser))
            {
                Realms.Fall(world, loser, year, "its last holdings taken in war", victor);
            }
        }
    }

    /// <summary>Truces on every cross-pair, and the grudge the losers carry away.</summary>
    /// <remarks>
    /// The loser's opinion falls twice as far as the winner's. That asymmetry is the entire reason
    /// <see cref="Civilization.Relations"/> is directed rather than shared, and it is what produces
    /// a war of revanche a generation later instead of a permanent stalemate at the same border.
    /// </remarks>
    private static void Settle(
        WorldState world,
        War war,
        IReadOnlyList<EntityId> winners,
        IReadOnlyList<EntityId> losers,
        WarOutcome outcome,
        int year,
        IRng rng)
    {
        int truceEnds = year + rng.NextInt(12, 26);

        foreach (EntityId winnerId in winners)
        {
            Civilization winner = world.Civilizations[winnerId];

            foreach (EntityId loserId in losers)
            {
                Civilization loser = world.Civilizations[loserId];

                winner.Truces[loser.Id] = truceEnds;
                loser.Truces[winner.Id] = truceEnds;

                if (outcome == WarOutcome.Stalemate)
                {
                    Diplomacy.Nudge(winner, loser, -0.25);
                    Diplomacy.Nudge(loser, winner, -0.25);
                    continue;
                }

                Diplomacy.Nudge(loser, winner, -0.55);
                Diplomacy.Nudge(winner, loser, -0.20);
            }
        }
    }

    /// <summary>The member of the winning coalition standing closest to this loser.</summary>
    private static Civilization? NearestVictor(
        WorldState world, IReadOnlyList<EntityId> winners, Civilization loser)
    {
        Civilization? best = null;
        double nearest = double.PositiveInfinity;

        foreach (EntityId id in winners)
        {
            Civilization candidate = world.Civilizations[id];
            if (!candidate.IsActive) continue;

            double distance = Diplomacy.Proximity(world, candidate, loser);
            if (distance < nearest)
            {
                nearest = distance;
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>
    /// What the victor takes, in the order it takes it.
    /// </summary>
    /// <remarks>
    /// Land that was fought over comes first. A treaty that hands over provinces no army ever
    /// reached is legally possible and reads as arbitrary; ceding the ground the chronicle has
    /// already named a battle after is the same list of regions the reader has been following.
    /// </remarks>
    private static List<Region> Spoils(
        WorldState world, War war, Civilization taker, Civilization loser, int count)
    {
        List<Region> front = Diplomacy.Frontline(world, taker, loser);
        var chosen = new List<Region>();

        foreach (EntityId battleId in war.BattleIds)
        {
            Battle battle = world.Battles[battleId];

            foreach (Region region in front)
            {
                if (region.Id != battle.RegionId || chosen.Contains(region)) continue;

                chosen.Add(region);
                break;
            }
        }

        foreach (Region region in front)
        {
            if (chosen.Count >= count) break;
            if (!chosen.Contains(region)) chosen.Add(region);
        }

        if (chosen.Count > count) chosen.RemoveRange(count, chosen.Count - count);
        return chosen;
    }

    // -----------------------------------------------------------------------
    // Casualties
    // -----------------------------------------------------------------------

    private static void TakeCasualties(
        WorldState world, IReadOnlyList<EntityId> coalition, int losses)
    {
        if (losses <= 0) return;

        int total = 0;
        foreach (EntityId id in coalition)
        {
            Civilization member = world.Civilizations[id];
            if (member.IsActive) total += member.Population;
        }

        if (total <= 0) return;

        foreach (EntityId id in coalition)
        {
            Civilization member = world.Civilizations[id];
            if (!member.IsActive || member.Population <= 0) continue;

            TakeFrom(world, member, (int)((long)losses * member.Population / total));
        }
    }

    /// <summary>Spreads a realm's dead across its settlements in proportion to their size.</summary>
    private static void TakeFrom(WorldState world, Civilization civilization, int losses)
    {
        if (losses <= 0 || civilization.Population <= 0) return;

        int population = civilization.Population;
        int taken = 0;

        foreach (Settlement settlement in world.ActiveSettlementsOf(civilization))
        {
            int share = (int)((long)losses * settlement.Population / population);
            int cost = Math.Min(share, (int)(settlement.Population * MaxSettlementLossFraction));
            if (cost <= 0) continue;

            settlement.Population -= cost;
            taken += cost;
        }

        // Kept consistent within the year. The population system recomputes it from settlements
        // next year regardless, but war reads it again before then.
        civilization.Population = Math.Max(0, civilization.Population - taken);
    }

    // -----------------------------------------------------------------------
    // Naming
    // -----------------------------------------------------------------------

    /// <summary>
    /// Names a war after what it was fought over.
    /// </summary>
    /// <remarks>
    /// Composed from names that already exist rather than drawn from a culture's language.
    /// A war is not a thing anybody names in advance: chronicles call it after the province it
    /// ruined or the succession it settled, and both are already in the export as entities the
    /// reader can follow. It also means the name reads correctly under the placeholder name
    /// generator the tests use.
    /// </remarks>
    private static string NameWar(
        WorldState world,
        Civilization aggressor,
        Civilization defender,
        CasusBelli cause,
        EntityId claimedRelicId,
        IReadOnlyList<Region> frontier)
    {
        string subject;

        if (cause == CasusBelli.RelicClaim
            && !claimedRelicId.IsNone
            && world.Artifacts.Contains(claimedRelicId))
        {
            subject = world.NameOf(claimedRelicId);
        }
        else if (cause == CasusBelli.DynasticClaim
                 && world.Dynasties.Contains(aggressor.RulingDynastyId))
        {
            subject = "the " + world.Dynasties[aggressor.RulingDynastyId].Name + " Succession";
        }
        else if (frontier.Count > 0)
        {
            subject = world.NameOf(frontier[0].Id);
        }
        else
        {
            subject = defender.Name;
        }

        string stem = "War of " + subject;
        return Ordinal(CountNamed(world.Wars, stem, static war => war.Name)) + stem;
    }

    /// <summary>
    /// "Battle of Ormsholmadal", "Second Siege of Ekallatograd".
    /// </summary>
    /// <remarks>
    /// <para>Named for the settlement when there is one, and for the region only when the ground is
    /// empty. Naming sieges after the town and field battles after the region was the first
    /// attempt, and it renames a place mid-war: a town sacked out of its tier stops qualifying as
    /// a siege, so the next engagement at the same walls appears under the region's name and the
    /// chronicle reads as though the war moved.</para>
    ///
    /// <para>The ordinal counts engagements at this <em>place</em> rather than under this exact
    /// wording, so a field battle at a town already besieged twice is the third of its name —
    /// which is how a chronicle numbers them.</para>
    /// </remarks>
    private static string NameBattle(WorldState world, Region field, Settlement? contested)
    {
        string place = contested?.Name ?? world.NameOf(field.Id);
        string stem = (IsSiege(contested) ? "Siege of " : "Battle of ") + place;

        int prior = 0;
        foreach (Battle past in world.Battles)
        {
            if (past.RegionId == field.Id) prior++;
        }

        return Ordinal(prior) + stem;
    }

    /// <summary>
    /// How many existing entities already carry this stem, ordinal prefix and all.
    /// </summary>
    /// <remarks>
    /// Matched as the whole name or as the name after an ordinal prefix, rather than as a bare
    /// suffix. Place names are generated, so one can perfectly well end with another — and a bare
    /// <c>EndsWith</c> would then number the first battle at one place after the battles fought at
    /// a differently-named one.
    /// </remarks>
    private static int CountNamed<T>(IReadOnlyList<T> table, string stem, Func<T, string> nameOf)
        where T : class
    {
        int count = 0;
        foreach (T item in table)
        {
            string name = nameOf(item);

            if (string.Equals(name, stem, StringComparison.Ordinal)
                || name.EndsWith(" " + stem, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    private static readonly string[] Ordinals =
    {
        string.Empty, "Second ", "Third ", "Fourth ", "Fifth ",
        "Sixth ", "Seventh ", "Eighth ", "Ninth ", "Tenth ",
    };

    /// <summary>
    /// "", "Second ", "Third " — and a numeral once a place has been fought over eleven times.
    /// </summary>
    /// <remarks>
    /// The numeric fallback exists because an eight-hundred-year run does produce a frontier
    /// contested a dozen times, and a missing case there would be an index out of range in the
    /// one run nobody re-checks.
    /// </remarks>
    private static string Ordinal(int prior)
    {
        if (prior <= 0) return string.Empty;
        if (prior < Ordinals.Length) return Ordinals[prior];

        int n = prior + 1;
        string suffix = (n % 100) is >= 11 and <= 13
            ? "th"
            : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };

        return n.ToString(CultureInfo.InvariantCulture) + suffix + " ";
    }

    public static string CauseLabel(CasusBelli cause) => cause switch
    {
        CasusBelli.BorderDispute => "over the frontier",
        CasusBelli.Conquest => "in naked conquest",
        CasusBelli.DynasticClaim => "pressing a claim through marriage",
        CasusBelli.Revanche => "to retake what had been lost",
        CasusBelli.RelicClaim => "to seize a sacred relic",
        CasusBelli.ReligiousWar => "in a war of faith",
        _ => "for reasons the record does not give",
    };

    private static string DeclarationCause(WorldState world, War war)
    {
        if (war.Cause == CasusBelli.RelicClaim
            && !war.ClaimedRelicId.IsNone
            && world.Artifacts.Contains(war.ClaimedRelicId))
        {
            return "to claim " + world.NameOf(war.ClaimedRelicId);
        }

        return CauseLabel(war.Cause);
    }

    private static EntityId[]? CauseReferences(
        EntityId claimedRelicId, EntityId aggressorReligionId, EntityId defenderReligionId)
    {
        var references = new List<EntityId>(3);

        if (!claimedRelicId.IsNone) references.Add(claimedRelicId);
        if (!aggressorReligionId.IsNone) references.Add(aggressorReligionId);
        if (!defenderReligionId.IsNone) references.Add(defenderReligionId);

        return references.Count == 0 ? null : references.ToArray();
    }

    public static string OutcomeLabel(WarOutcome outcome) => outcome switch
    {
        WarOutcome.AggressorVictory => "won by the aggressor",
        WarOutcome.DefenderVictory => "won by the defender",
        WarOutcome.Stalemate => "fought to exhaustion",
        _ => "still being fought",
    };

    /// <summary>Everyone a battle should appear on the page of.</summary>
    private static EntityId[] Participants(War war, Battle battle)
    {
        var ids = new List<EntityId>(5) { war.Id, battle.AttackerId, battle.DefenderId };

        if (!battle.AttackerCommanderId.IsNone) ids.Add(battle.AttackerCommanderId);
        if (!battle.DefenderCommanderId.IsNone) ids.Add(battle.DefenderCommanderId);

        return ids.ToArray();
    }

    private static EntityId[] Belligerents(War war)
    {
        var ids = new List<EntityId>(war.Attackers.Count + war.Defenders.Count);
        ids.AddRange(war.Attackers);
        ids.AddRange(war.Defenders);
        return ids.ToArray();
    }
}
