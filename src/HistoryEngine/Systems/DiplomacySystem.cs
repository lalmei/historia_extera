using System.Globalization;
using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.World;

namespace HistoryEngine.Systems;

/// <summary>
/// Opinion between realms, the pacts it produces, and the wars it eventually declares.
/// </summary>
/// <remarks>
/// <para><b>Relations are pulled toward a level, not accumulated.</b> Each pair has a natural
/// standing set by the things that do not change from year to year — a shared culture, a shared
/// border, a marriage between the two houses, how much each side lives by trade — and the actual
/// opinion drifts toward it. Everything a war does is a step change on top, which then fades.
/// That is what makes a grudge a grudge: it is felt sharply, it decays over a generation, and it
/// leaves the two realms back at whatever their geography always said they should be.</para>
///
/// <para>A running sum of yearly deltas was the first attempt and cannot work. Any constant
/// pressure saturates it at ±1 within a few decades, after which every neighbour is at war with
/// every neighbour for ever and no peace ever holds. The natural-level model has no such
/// runaway, and the strength of the pull is the only thing that needs calibrating.</para>
///
/// <para><b>Contact is a shared frontier.</b> Realms that have never met have no opinion of each
/// other and cannot go to war, which is what keeps a world of fifteen civilizations from being
/// a hundred and five simultaneous quarrels. An entry once made is kept, so two realms that have
/// drifted apart still remember each other.</para>
/// </remarks>
public sealed class DiplomacySystem : IYearSystem
{
    /// <summary>How far toward its natural level a relation moves each year.</summary>
    /// <remarks>
    /// Six percent gives a half-life of about eleven years, so a war's grudge is still felt by the
    /// generation that fought it and largely spent by their grandchildren. Faster and no peace
    /// treaty has consequences; slower and the first war between two realms decides the rest of
    /// the chronicle.
    /// </remarks>
    private const double DriftRate = 0.06;

    /// <summary>Year-to-year noise, standing in for envoys, insults and marriages not modelled.</summary>
    /// <remarks>
    /// Small, and load-bearing. Without it a pair sits exactly at its natural level for ever, and
    /// since war needs an opinion below a threshold, either every pair of neighbours fights or
    /// none of them ever does — determined entirely by their fixed traits.
    /// </remarks>
    private const double Jitter = 0.03;

    /// <summary>Warmth between realms of one culture. Kinship is the strongest single term.</summary>
    private const double SharedCultureBonus = 0.40;

    /// <summary>Warmth from a living marriage between the two ruling houses.</summary>
    private const double MarriageBonus = 0.30;

    /// <summary>Warmth per realm both sides regard as hostile, up to <see cref="MaxCommonEnemies"/>.</summary>
    /// <remarks>
    /// <para>The enemy of my enemy, and the term that makes alliances happen at all. Without it the
    /// only warmth available is a shared culture, a marriage and trade — and since every
    /// civilization is founded with a culture of its own, the first of those never applies, so
    /// nothing in a three-century run ever reached the threshold and not one pact was sworn in a
    /// milestone that lists alliances among its deliverables.</para>
    ///
    /// <para>It is also the only one of the four terms that is about somebody else, which is what
    /// alliances are for. A pact sworn because two realms both fear a third is a pact that means
    /// something when the third one declares, because that is exactly who it will be called
    /// against.</para>
    /// </remarks>
    private const double CommonEnemyBonus = 0.24;

    /// <summary>Shared enemies beyond this add nothing. Two is already an encirclement.</summary>
    private const int MaxCommonEnemies = 2;

    /// <summary>Warmth between two realms currently fighting the same war on the same side.</summary>
    /// <remarks>
    /// Standing shoulder to shoulder is worth more than agreeing about a third party in the
    /// abstract, and it is what turns a pact that was called once into one that lasts — and, when
    /// two realms are dragged in on the same side without a pact between them, what gives them a
    /// reason to swear one afterwards. It lapses with the war, so a coalition that never becomes
    /// an alliance simply cools again.
    /// </remarks>
    private const double ComradeshipBonus = 0.30;

    /// <summary>Warmth between two realms that both live by trade.</summary>
    /// <remarks>
    /// The counterweight to friction, and the reason a mercantile people can hold a border with
    /// someone it has every geographic reason to resent. Scaled by both sides' Mercantile, so it
    /// is largest exactly where it should be — between two trading realms — rather than being a
    /// flat discount on being neighbours.
    /// </remarks>
    private const double TradeBonus = 0.38;

    /// <summary>
    /// Warmth between two realms of one faith, and coldness between two of different ones.
    /// </summary>
    /// <remarks>
    /// <para>Deliberately smaller than <see cref="SharedCultureBonus"/>. Faith is the one M8 term
    /// that changes what M6 does rather than adding beside it, and a large coefficient would have
    /// rewritten every war in every world — the milestone is meant to add flavour to a working
    /// model of conquest, not to replace its causes. At this weight a shared faith softens a
    /// frontier without preventing a war that geography and temper have already made likely, and
    /// a religious divide is a thumb on the scale rather than a cause of war in itself.</para>
    ///
    /// <para>Both directions are scaled by the holder's own piety, so a devout realm cares who its
    /// neighbour prays to and a worldly one barely notices — the same asymmetry that makes border
    /// friction produce an aggressor without anything nominating one.</para>
    /// </remarks>
    private const double SharedFaithBonus = 0.16;

    /// <summary>Coldness between realms of different faiths, at full piety.</summary>
    private const double FaithDivideMalus = 0.20;

    /// <summary>Coldness from being neighbours at all, before either side's temper is counted.</summary>
    private const double BorderFriction = 0.18;

    /// <summary>Additional coldness at full aggression, with a neighbour on the doorstep.</summary>
    private const double BorderFrictionFromAggression = 0.55;

    /// <summary>Yearly chance two realms warm enough to ally actually swear to it.</summary>
    private const double AllianceChance = 0.25;

    /// <summary>Baseline yearly chance of a declaration at full hostility, before aggression.</summary>
    /// <remarks>
    /// Calibrated against how much of a realm's history should be war, not against how it reads as
    /// a probability. A realm can hold only one war at a time and cannot declare during a truce,
    /// so most of a hostile pair's years are ineligible and the headline number has to be several
    /// times the rate actually wanted.
    /// </remarks>
    private const double WarChance = 0.28;

    /// <summary>How much larger an aggressor must be before it will start a war of pure conquest.</summary>
    private const double ConquestStrengthRatio = 1.8;

    /// <summary>
    /// People a realm needs behind it before it will declare a war at all.
    /// </summary>
    /// <remarks>
    /// <para>Without a floor, realms declare war in their first decades and the chronicle opens
    /// with a Battle of somewhere fought by eight men against six — on seed 42 the first five
    /// battles in the world were all at that scale, one of them ending in the sack of a hamlet by
    /// an army of eight. The mechanics were behaving correctly; four percent of a realm of two
    /// hundred people is simply not an army.</para>
    ///
    /// <para>Two thousand is roughly the point at which a realm holds a town rather than a
    /// scattering of hamlets, which a founding population of seventy reaches in about sixty years.
    /// Before that a realm is establishing itself, which is what those decades should read as.</para>
    /// </remarks>
    private const int MinimumWarPopulation = 2000;

    public string Name => "diplomacy";

    public void Tick(WorldState world, int year)
    {
        IRng rng = world.Root.Fork(Name, year);

        // Who is within reach of whom, resolved once. Proximity is the most expensive thing this
        // system asks — every settlement against every settlement — and nothing before the
        // declarations moves a settlement, so recomputing it in the second pass would cost the
        // same again for an identical answer.
        var civilizations = new List<Civilization>();
        var contacts = new List<DetMap<EntityId, double>>();

        foreach (Civilization civilization in world.ActiveCivilizations())
        {
            civilizations.Add(civilization);
            contacts.Add(Diplomacy.Neighbours(world, civilization));
        }

        for (int i = 0; i < civilizations.Count; i++)
        {
            foreach (KeyValuePair<EntityId, double> contact in contacts[i])
            {
                Civilization other = world.Civilizations[contact.Key];
                Drift(world, civilizations[i], other, contact.Value, rng);
            }

            ReviewAlliances(world, civilizations[i], contacts[i], year, rng);
        }

        // Declarations run in a second pass, after every opinion has moved. Otherwise the realm
        // with the lowest id declares against relations a year fresher than the ones its
        // neighbours are judged by, and low ids start disproportionately many wars.
        for (int i = 0; i < civilizations.Count; i++)
        {
            ConsiderWar(world, civilizations[i], contacts[i], year, rng);
        }
    }

    // -----------------------------------------------------------------------
    // Opinion
    // -----------------------------------------------------------------------

    private static void Drift(
        WorldState world, Civilization civilization, Civilization other, double proximity, IRng rng)
    {
        double natural = NaturalStanding(world, civilization, other, proximity);
        double current = Diplomacy.Relation(civilization, other);

        double moved = current + ((natural - current) * DriftRate) + rng.NextDouble(-Jitter, Jitter);
        Diplomacy.SetRelation(civilization, other, moved);
    }

    /// <summary>
    /// Where this pair's opinion belongs, given everything that is not an event.
    /// </summary>
    /// <remarks>
    /// Asymmetric by construction. The friction term is scaled by the <em>holder</em>'s own
    /// aggression, so a martial realm resents a neighbour its placid neighbour is content with —
    /// which is what makes one of the two the aggressor without anything having to nominate one.
    /// </remarks>
    private static double NaturalStanding(
        WorldState world, Civilization civilization, Civilization other, double proximity)
    {
        Culture mine = world.CultureOf(civilization);
        double standing = 0.0;

        if (civilization.CultureId == other.CultureId) standing += SharedCultureBonus;

        if (Diplomacy.MarriedIntoTheHouseOf(world, civilization, other))
        {
            standing += MarriageBonus;
        }

        // Trade cuts both ways round: a mercantile people values a neighbour it sells to, and
        // values it more when that neighbour also trades.
        Culture theirs = world.CultureOf(other);
        standing += TradeBonus * mine.Values.Mercantile * (0.4 + (0.6 * theirs.Values.Mercantile));

        // Faith, weighted by how much this realm's people care about it. Two realms that have not
        // yet taken a faith are not thereby brethren, so both terms need two faiths to exist.
        EntityId ourFaith = world.FaithOf(civilization);
        EntityId theirFaith = world.FaithOf(other);

        if (!ourFaith.IsNone && !theirFaith.IsNone)
        {
            standing += ourFaith == theirFaith
                ? SharedFaithBonus * (0.4 + (0.6 * mine.Values.Piety))
                : -FaithDivideMalus * mine.Values.Piety;
        }

        standing += CommonEnemyBonus * CommonEnemies(world, civilization, other);

        if (Diplomacy.FightingTogether(world, civilization.Id, other.Id))
        {
            standing += ComradeshipBonus;
        }

        double pressure = Diplomacy.Pressure(proximity);
        standing -= (BorderFriction + (BorderFrictionFromAggression * mine.Values.Aggression))
                    * pressure;

        return DetMath.Clamp(standing, -1.0, 1.0);
    }

    /// <summary>
    /// How many realms these two both regard as hostile, capped.
    /// </summary>
    /// <remarks>
    /// Read from the relation maps rather than from who is at war with whom, so a pact can be
    /// sworn in anticipation rather than only after the fighting has started — which is the point
    /// of a pact. Both maps are small and sorted, so this is a walk of at most a dozen entries.
    /// </remarks>
    private static int CommonEnemies(
        WorldState world, Civilization civilization, Civilization other)
    {
        int shared = 0;

        foreach (KeyValuePair<EntityId, double> mine in civilization.Relations)
        {
            if (mine.Key == other.Id || mine.Value > Diplomacy.HostilityThreshold) continue;
            if (!world.Civilizations[mine.Key].IsActive) continue;

            if (other.Relations.GetOrDefault(mine.Key, 0.0) <= Diplomacy.HostilityThreshold)
            {
                shared++;
                if (shared >= MaxCommonEnemies) break;
            }
        }

        return shared;
    }

    // -----------------------------------------------------------------------
    // Alliances
    // -----------------------------------------------------------------------

    /// <summary>
    /// Swears new pacts and lets stale ones lapse.
    /// </summary>
    /// <remarks>
    /// Both ends must want it, which is what makes an alliance mean something in a model where
    /// opinion is directed: a realm that is merely tolerated does not get a pact out of it. The
    /// break threshold sits well below the forming one so a pact does not flicker in and out on
    /// the yearly jitter.
    /// </remarks>
    private static void ReviewAlliances(
        WorldState world,
        Civilization civilization,
        DetMap<EntityId, double> neighbours,
        int year,
        IRng rng)
    {
        // Collected first: breaking a pact writes to the map being walked.
        var lapsed = new List<EntityId>();

        foreach (KeyValuePair<EntityId, int> pact in civilization.Allies)
        {
            Civilization ally = world.Civilizations[pact.Key];

            bool holds = ally.IsActive
                && Diplomacy.Relation(civilization, ally) >= Diplomacy.AllianceCollapseThreshold
                && Diplomacy.Relation(ally, civilization) >= Diplomacy.AllianceCollapseThreshold;

            if (!holds) lapsed.Add(pact.Key);
        }

        foreach (EntityId allyId in lapsed)
        {
            Civilization ally = world.Civilizations[allyId];
            int sworn = civilization.Allies.GetOrDefault(allyId, year);

            // Both ends are cleared together, so whichever realm notices first is the only one
            // that ever notices. Recording only from the lower id would silently lose the lapse
            // whenever it was the higher-id realm that cooled.
            civilization.Allies.Remove(allyId);
            ally.Allies.Remove(civilization.Id);

            // An ally that no longer exists did not break anything.
            if (!ally.IsActive) continue;

            var data = new DetMap<string, string>();
            if (year > sworn) data["years"] = Chronicle.Years(year - sworn);

            // Named in id order, so the sentence does not depend on which side cooled.
            bool first = civilization.Id.CompareTo(allyId) < 0;

            world.Chronicle.Record(
                year,
                EventKind.AllianceBroken,
                first ? civilization.Id : allyId,
                obj: first ? allyId : civilization.Id,
                data: data);
        }

        foreach (KeyValuePair<EntityId, double> neighbour in neighbours)
        {
            Civilization other = world.Civilizations[neighbour.Key];

            // One side proposes. Letting both propose in the same year doubles the rate for no
            // difference in the result, since the pact is symmetric once sworn.
            if (civilization.Id.CompareTo(other.Id) >= 0) continue;
            if (Diplomacy.AreAllied(civilization, other)) continue;
            if (Diplomacy.AtWar(world, civilization.Id, other.Id)) continue;

            if (Diplomacy.Relation(civilization, other) < Diplomacy.AllianceThreshold) continue;
            if (Diplomacy.Relation(other, civilization) < Diplomacy.AllianceThreshold) continue;

            if (!rng.Chance(AllianceChance)) continue;

            civilization.Allies[other.Id] = year;
            other.Allies[civilization.Id] = year;

            world.Chronicle.Record(
                year, EventKind.AllianceFormed, civilization.Id, obj: other.Id);
        }
    }

    // -----------------------------------------------------------------------
    // Declarations
    // -----------------------------------------------------------------------

    /// <summary>
    /// Decides whether this realm starts a war this year, and against whom.
    /// </summary>
    /// <remarks>
    /// One declaration per realm per year at most, and none at all while it is already fighting.
    /// A realm that can open a second front while losing a first turns every long war into a
    /// general one and leaves the chronicle with no way to say which war anything belonged to.
    /// </remarks>
    private static void ConsiderWar(
        WorldState world,
        Civilization civilization,
        DetMap<EntityId, double> neighbours,
        int year,
        IRng rng)
    {
        if (civilization.CurrentRulerId.IsNone) return;
        if (civilization.Population < MinimumWarPopulation) return;
        if (AlreadyFighting(world, civilization)) return;

        Culture culture = world.CultureOf(civilization);

        Civilization? target = null;
        CasusBelli cause = CasusBelli.Unknown;
        double bestChance = 0.0;

        foreach (KeyValuePair<EntityId, double> neighbour in neighbours)
        {
            Civilization other = world.Civilizations[neighbour.Key];
            if (!other.IsActive) continue;

            if (Diplomacy.AreAllied(civilization, other)) continue;
            if (Diplomacy.TruceHolds(civilization, other, year)) continue;

            // A realm already fighting is not available to be fought. The engine's model is one
            // war at a time per realm — the alternative is a realm on three fronts with one levy
            // divided between them, and every peace negotiated against a score that belongs to a
            // different war. Only the declaring side was checked before this, so a realm that had
            // been called into an ally's war could still be declared on the following year, which
            // is exactly how seed 99 ended with Vladane in two.
            if (AlreadyFighting(world, other)) continue;

            double relation = Diplomacy.Relation(civilization, other);
            if (relation > Diplomacy.HostilityThreshold) continue;

            // Zero at the threshold, one at outright hatred.
            double hostility = DetMath.Clamp01(
                (relation - Diplomacy.HostilityThreshold)
                / (-1.0 - Diplomacy.HostilityThreshold));

            double chance = WarChance * hostility * DetMath.Lerp(0.3, 1.6, culture.Values.Aggression);
            if (chance <= bestChance) continue;

            bestChance = chance;
            target = other;
            cause = CauseAgainst(world, civilization, other);
        }

        if (target is null || !rng.Chance(bestChance)) return;

        List<Region> front = Diplomacy.Frontline(world, civilization, target);
        if (front.Count == 0) return;

        War war = Warfare.Declare(world, civilization, target, cause, front, year);
        CallAllies(world, war, civilization, target, year, rng);
    }

    private static bool AlreadyFighting(WorldState world, Civilization civilization)
    {
        foreach (War war in world.ActiveWars())
        {
            if (war.Involves(civilization.Id)) return true;
        }

        return false;
    }

    /// <summary>
    /// The grievance this realm reaches for, strongest claim first.
    /// </summary>
    /// <remarks>
    /// Order matters and is not arbitrary. Land actually lost is the claim a chronicle finds most
    /// convincing, a marriage into the other house is the next, and a realm with neither and a
    /// decisive advantage in strength simply takes what it wants. A border dispute is what is
    /// left when none of the three applies, which is most of the time.
    /// </remarks>
    private static CasusBelli CauseAgainst(
        WorldState world, Civilization civilization, Civilization other)
    {
        if (Diplomacy.LostTo(world, civilization, other).Count > 0) return CasusBelli.Revanche;

        if (Diplomacy.MarriedIntoTheHouseOf(world, civilization, other))
        {
            return CasusBelli.DynasticClaim;
        }

        int mine = Diplomacy.Levy(world, civilization);
        int theirs = Diplomacy.Levy(world, other);

        if (theirs > 0 && mine >= theirs * ConquestStrengthRatio) return CasusBelli.Conquest;

        return CasusBelli.BorderDispute;
    }

    /// <summary>
    /// Calls both sides' allies to the field.
    /// </summary>
    /// <remarks>
    /// <para>The defender's allies answer far more readily than the aggressor's, because the two
    /// are not the same promise. A pact to defend is the one everybody signs; a pact to join
    /// somebody else's invasion is the one that gets ignored, and modelling both at the same rate
    /// makes every war general.</para>
    ///
    /// <para>An ally already fighting the realm it is being called against, or bound to it by a
    /// pact of its own, stays home. Otherwise a chain of alliances puts a realm on both sides of
    /// the same war.</para>
    /// </remarks>
    private static void CallAllies(
        WorldState world,
        War war,
        Civilization aggressor,
        Civilization defender,
        int year,
        IRng rng)
    {
        Answer(defender, attacking: false, 0.80);
        Answer(aggressor, attacking: true, 0.30);

        void Answer(Civilization caller, bool attacking, double chance)
        {
            Civilization enemy = attacking ? defender : aggressor;

            foreach (KeyValuePair<EntityId, int> pact in caller.Allies)
            {
                Civilization ally = world.Civilizations[pact.Key];

                if (!ally.IsActive || ally.Id == enemy.Id) continue;
                if (Diplomacy.AreAllied(ally, enemy)) continue;
                if (war.Involves(ally.Id)) continue;
                if (AlreadyFighting(world, ally)) continue;

                if (!rng.Chance(chance)) continue;

                Warfare.Join(world, war, ally, caller, attacking, year);
            }
        }
    }
}
