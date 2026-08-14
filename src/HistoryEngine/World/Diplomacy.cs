using HistoryEngine.Core;
using HistoryEngine.Entities;

namespace HistoryEngine.World;

/// <summary>
/// The vocabulary of relations between realms: who borders whom, who is allied, who is at
/// war, and how much of a realm's strength it can actually put in the field.
/// </summary>
/// <remarks>
/// <para>Reads, plus the two setters that own the shape of <see cref="Civilization.Relations"/>.
/// The split from <see cref="Warfare"/> is the same one <see cref="Succession"/> makes against
/// <see cref="Houses"/>: everything here answers a question about the world as it stands, and
/// everything in <c>Warfare</c> changes it and writes an event. Both the diplomacy and war
/// systems need these answers, and one copy is what keeps the war system's idea of a frontier
/// identical to the one the declaration was based on.</para>
///
/// <para><b>Costs no terrain samples.</b> Everything below reads region statistics that were
/// derived once from the already-primed lattice, so a milestone that adds two systems running
/// every year adds nothing to the sample budget. That is deliberate and is asserted by
/// <c>TerrainDisciplineTests</c>.</para>
/// </remarks>
public static class Diplomacy
{
    /// <summary>Fraction of a realm's people it can put under arms at all.</summary>
    /// <remarks>
    /// Four percent of the population, before culture. Pre-modern states mobilised somewhere
    /// between one and five percent for a campaign of any length; much above that and the
    /// harvest fails, which is a second war the realm loses. It matters here because army size
    /// is the only thing that makes a large realm beat a small one, and therefore the only
    /// thing that makes conquest run in one direction rather than being a coin toss.
    /// </remarks>
    private const double LevyFraction = 0.04;

    /// <summary>Opinion at which a realm will consider war. Above it, nothing can start one.</summary>
    public const double HostilityThreshold = -0.30;

    /// <summary>Opinion at which two realms will consider a pact.</summary>
    /// <remarks>
    /// Both sides must reach it, which is a far higher bar than it looks under directed relations
    /// — an alliance needs two realms that each think well of the other, and mutual warmth is much
    /// rarer than one-sided goodwill. Set against what the terms in <c>NaturalStanding</c> can
    /// actually produce rather than against how friendly the number sounds.
    /// </remarks>
    public const double AllianceThreshold = 0.28;

    /// <summary>Opinion below which a standing pact lapses. Hysteresis, so alliances do not flicker.</summary>
    public const double AllianceCollapseThreshold = 0.02;

    public static double Relation(Civilization from, Civilization to) =>
        from.Relations.GetOrDefault(to.Id, 0.0);

    public static void SetRelation(Civilization from, Civilization to, double value) =>
        from.Relations[to.Id] = DetMath.Clamp(value, -1.0, 1.0);

    /// <summary>Moves one realm's opinion of another. Missing entries count as indifference.</summary>
    public static void Nudge(Civilization from, Civilization to, double delta) =>
        SetRelation(from, to, Relation(from, to) + delta);

    /// <summary>
    /// How near two realms have to come before they have an opinion of each other, in world units.
    /// </summary>
    /// <remarks>
    /// <para><b>Contact is proximity, not a shared border.</b> Adjacent territory was the first
    /// definition and it is far too strict for the worlds this engine generates: eight
    /// civilizations on a four-thousand-unit map hold ninety-odd regions out of a thousand, so on
    /// seed 42 the first two realms whose territory actually touched did so in year 201, and half
    /// the pairs that eventually met did so after year 245. A milestone about war produced no war
    /// at all, not because anything was mis-tuned but because nobody could reach anybody.</para>
    ///
    /// <para>It is also the wrong model on its own terms. Two realms with a day's ride of empty
    /// forest between them knew perfectly well who each other were and fought accordingly; empty
    /// land is something armies march through, not a wall. Measured between settlements, because
    /// that is where the people and the roads are, and against a range a little under the
    /// <c>SupplyRange</c> a realm can already project force over.</para>
    /// </remarks>
    public const double ContactRange = 1600.0;

    /// <summary>
    /// Distance within which two realms are simply neighbours, and friction is full.
    /// </summary>
    /// <remarks>
    /// A plain linear falloff from zero to <see cref="ContactRange"/> put most real neighbours at
    /// half pressure, which halved the friction term and left every opinion in the world bunched
    /// between −0.3 and +0.2: too warm for war, too cool for a pact. Holding pressure at full out
    /// to a few regions' distance and ramping only beyond that separates "next door" from "some
    /// way off", which is the distinction the whole model needs and the one a flat ramp blurs.
    /// </remarks>
    private const double NeighbourRange = 450.0;

    /// <summary>Nearest approach between two realms, in world units, or infinity if either is empty.</summary>
    public static double Proximity(WorldState world, Civilization a, Civilization b)
    {
        double nearest = double.PositiveInfinity;

        foreach (Settlement mine in world.ActiveSettlementsOf(a))
        {
            foreach (Settlement theirs in world.ActiveSettlementsOf(b))
            {
                // Compared squared and rooted once, which is both faster and exact under IEEE 754.
                double distance = world.DistanceSquared(mine.X, mine.Z, theirs.X, theirs.Z);
                if (distance < nearest) nearest = distance;
            }
        }

        return double.IsPositiveInfinity(nearest) ? nearest : DetMath.Sqrt(nearest);
    }

    /// <summary>Every active realm within <see cref="ContactRange"/>, valued by that distance.</summary>
    public static DetMap<EntityId, double> Neighbours(WorldState world, Civilization civilization)
    {
        var found = new DetMap<EntityId, double>();

        foreach (Civilization other in world.ActiveCivilizations())
        {
            if (other.Id == civilization.Id) continue;

            double distance = Proximity(world, civilization, other);
            if (distance <= ContactRange) found[other.Id] = distance;
        }

        return found;
    }

    /// <summary>One while two realms are neighbours, falling to zero at the edge of contact.</summary>
    public static double Pressure(double proximity) =>
        1.0 - DetMath.InverseLerp(NeighbourRange, ContactRange, proximity);

    /// <summary>
    /// The other realm's territory, nearest to this one first.
    /// </summary>
    /// <remarks>
    /// <para>The front. A war is fought at the head of this list and settled out of it, so the same
    /// ordering decides which town is besieged and which provinces change hands — which is what
    /// makes a peace treaty transfer the ground the chronicle has already named a battle after.</para>
    ///
    /// <para>Ordered by the distance from each region's centre to this realm's nearest settlement,
    /// with region id breaking ties, since <see cref="List{T}.Sort"/> is unstable and equal
    /// distances are common on a square lattice.</para>
    /// </remarks>
    public static List<Region> Frontline(
        WorldState world, Civilization civilization, Civilization other)
    {
        var front = new List<Region>();
        var distances = new List<double>();

        foreach (EntityId regionId in other.TerritoryRegionIds)
        {
            Region region = world.Regions[regionId];
            double nearest = double.PositiveInfinity;

            foreach (Settlement mine in world.ActiveSettlementsOf(civilization))
            {
                double distance = world.DistanceSquared(
                    mine.X, mine.Z, region.CenterX, region.CenterZ);

                if (distance < nearest) nearest = distance;
            }

            front.Add(region);
            distances.Add(nearest);
        }

        var order = new List<int>(front.Count);
        for (int i = 0; i < front.Count; i++) order.Add(i);

        order.Sort((a, b) =>
        {
            int byDistance = distances[a].CompareTo(distances[b]);
            return byDistance != 0 ? byDistance : front[a].Id.CompareTo(front[b].Id);
        });

        var sorted = new List<Region>(front.Count);
        foreach (int i in order) sorted.Add(front[i]);
        return sorted;
    }

    public static bool AreAllied(Civilization a, Civilization b) => a.Allies.ContainsKey(b.Id);

    /// <summary>True while a settlement's truce still runs. Checked before any declaration.</summary>
    public static bool TruceHolds(Civilization a, Civilization b, int year) =>
        a.Truces.GetOrDefault(b.Id, int.MinValue) >= year;

    /// <summary>The running war these two are on opposite sides of, if there is one.</summary>
    public static War? WarBetween(WorldState world, EntityId a, EntityId b)
    {
        foreach (War war in world.ActiveWars())
        {
            IReadOnlyList<EntityId>? enemies = war.EnemiesOf(a);
            if (enemies is not null && enemies.Contains(b)) return war;
        }

        return null;
    }

    public static bool AtWar(WorldState world, EntityId a, EntityId b) =>
        WarBetween(world, a, b) is not null;

    /// <summary>True while these two are fighting the same war on the same side.</summary>
    public static bool FightingTogether(WorldState world, EntityId a, EntityId b)
    {
        foreach (War war in world.ActiveWars())
        {
            if (war.Involves(a) && war.Involves(b) && war.IsAttacker(a) == war.IsAttacker(b))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>How many fighting men a realm can raise this year.</summary>
    public static int Levy(WorldState world, Civilization civilization)
    {
        Culture culture = world.CultureOf(civilization);

        // A martial people puts a larger share of itself under arms, and keeps it there.
        double share = LevyFraction * DetMath.Lerp(0.7, 1.4, culture.Values.Aggression);
        return (int)(civilization.Population * share);
    }

    /// <summary>The combined levy of a coalition, skipping any member that has fallen.</summary>
    public static int Levy(WorldState world, IReadOnlyList<EntityId> coalition)
    {
        int total = 0;
        foreach (EntityId id in coalition)
        {
            Civilization member = world.Civilizations[id];
            if (member.IsActive) total += Levy(world, member);
        }

        return total;
    }

    /// <summary>
    /// Whether the two ruling houses are joined by a living marriage.
    /// </summary>
    /// <remarks>
    /// <para>The one place Milestone 5's family trees reach into Milestone 6's politics, and the
    /// reason both exist. A marriage tie warms relations, makes an alliance likely, and — when
    /// the tie exists and relations have soured anyway — supplies the claim that turns an
    /// ordinary border quarrel into a war of succession. That is how dynastic wars actually
    /// started, and it costs nothing beyond a walk of one house's members.</para>
    ///
    /// <para>Living marriages only. A widow's tie to her late husband's house is a claim, but it
    /// is not an alliance, and treating it as one leaves realms bound to each other by couples
    /// three generations dead.</para>
    /// </remarks>
    public static bool MarriedIntoTheHouseOf(
        WorldState world, Civilization civilization, Civilization other)
    {
        Dynasty? house = Succession.HouseOf(world, civilization);
        Dynasty? theirs = Succession.HouseOf(world, other);

        if (house is null || theirs is null || house.Id == theirs.Id) return false;

        foreach (EntityId memberId in house.MemberIds)
        {
            Figure member = world.Figures[memberId];
            if (!member.IsAlive || !world.Figures.Contains(member.SpouseId)) continue;

            if (world.Figures[member.SpouseId].DynastyId == theirs.Id) return true;
        }

        return false;
    }

    /// <summary>
    /// Regions this realm ceded to that one in past wars and has not taken back.
    /// </summary>
    /// <remarks>
    /// Derived from the war record rather than stored on the civilization, because it is exactly
    /// the war record — a claim that outlives the people who lost the land is what a chronicle
    /// means by a lost province, and duplicating it into a field invites the two to disagree.
    /// </remarks>
    public static List<Region> LostTo(
        WorldState world, Civilization civilization, Civilization other)
    {
        var lost = new List<Region>();

        foreach (War war in world.Wars)
        {
            if (war.IsActive) continue;

            bool takenByThem = war.Outcome switch
            {
                WarOutcome.AggressorVictory =>
                    war.IsAttacker(other.Id) && war.Defenders.Contains(civilization.Id),
                WarOutcome.DefenderVictory =>
                    war.Defenders.Contains(other.Id) && war.IsAttacker(civilization.Id),
                _ => false,
            };

            if (!takenByThem) continue;

            foreach (EntityId regionId in war.CededRegionIds)
            {
                Region region = world.Regions[regionId];

                // Only still-lost land is a grievance. Land already taken back, or since lost to
                // a third realm, is somebody else's quarrel.
                if (region.Owner == other.Id && !lost.Contains(region)) lost.Add(region);
            }
        }

        return lost;
    }
}
