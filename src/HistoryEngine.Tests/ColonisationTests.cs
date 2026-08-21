using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.World;
using Xunit;

namespace HistoryEngine.Tests;

/// <summary>
/// Founding as a need rather than as a ranking: that realms go somewhere for the ore, that the
/// ground they went for is really there, and that most colonisation is still nobody's errand.
/// </summary>
/// <remarks>
/// <para>The defect these were written against is one no other test could see. Expansion ranked
/// unclaimed neighbours by habitability, habitability is fertility with water and footing on it, so
/// every party ever sent out was a farming party and the only mines in the world were farms that
/// happened to be founded on rock. Nothing failed. The map simply had no reason in it.</para>
///
/// <para>Measured over whole runs rather than on a constructed world, because what is being
/// asserted is a property of histories — that a purpose founding is a minority, that it lands on
/// the deposit, and that the crown's appetite is visible in how many of them a realm has.</para>
/// </remarks>
public sealed class ColonisationTests
{
    private static readonly ulong[] Seeds = { 1, 7, 42, 99, 123, 777, 2024, 31337 };

    /// <summary>
    /// Realms go out for ore, and the country they went to has some.
    /// </summary>
    /// <remarks>
    /// The region rather than the point, because the region is what the search chose and this is
    /// the search's test — that a realm crossing its own border for a deposit arrives somewhere the
    /// deposit is. That the camp then stands on the ore rather than merely inside the patch
    /// containing it is the siting decision's promise, and
    /// <c>SiteSelectionTests.ASiteNeverLiesAboutItsGround</c> is where it is kept.
    /// </remarks>
    [Fact]
    public void RealmsGoOutForOreAndFindIt()
    {
        int mines = 0;
        int total = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Settlement settlement in world.Settlements)
            {
                total++;
                if (settlement.Site != SiteCharacter.Mine) continue;

                mines++;
                Region region = world.Regions[settlement.RegionId];

                Assert.True(
                    region.GeologicActivity >= Specializations.OreThreshold,
                    $"{settlement.Name} (seed {seed}) is a mine site in a region with geologic "
                    + $"activity {region.GeologicActivity:F3}, which no ore search would have "
                    + $"chosen — the gate is {Specializations.OreThreshold:F2}.");
            }
        }

        Assert.True(
            mines > 0,
            $"No realm in {Seeds.Length} worlds ever founded anything for its ore. Either the need "
            + "is never wanted or the search never finds a deposit; both make founding a ranking "
            + "again.");
    }

    /// <summary>
    /// Colonisation stays mostly ordinary.
    /// </summary>
    /// <remarks>
    /// <para>The counterweight to the test above, and the more important of the two. Historical
    /// colonisation is overwhelmingly surplus people walking to the next valley; a world in which
    /// every founding answers a stated need reads like a plan rather than like a country. Mines are
    /// the exception that makes the rest look intelligent, and they stop doing that as soon as they
    /// are common.</para>
    ///
    /// <para>Currently 10.7% across these seeds. The ceiling is set with room for the ordinary
    /// case to keep dominating rather than at the measurement, and with room left over: quarries,
    /// ports and frontier posts are each meant to take a share of foundings too, and the sum of
    /// them is what this bound really guards.</para>
    /// </remarks>
    [Fact]
    public void MostFoundingsAreStillNobodysErrand()
    {
        const double Ceiling = 0.20;

        int purposeful = 0;
        int total = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Settlement settlement in world.Settlements)
            {
                total++;
                if (SiteCharacters.Purpose(settlement.Site) is not null) purposeful++;
            }
        }

        double share = purposeful / (double)total;

        Assert.True(
            share <= Ceiling,
            $"{share:P1} of {total} settlements were founded for a stated purpose, against "
            + $"{Ceiling:P0} allowed. Ordinary expansion has to stay the ordinary case.");
    }

    /// <summary>
    /// A realm walks past better ground to reach a deposit.
    /// </summary>
    /// <remarks>
    /// The whole claim of a purpose founding, and the one thing a habitability sort can never do.
    /// If mine sites sat on land as good as everything else, the ore search would be finding
    /// deposits that happened to lie under good farmland — which is what a ranking would have found
    /// anyway, and the need would be decoration.
    /// </remarks>
    [Fact]
    public void OreIsWorthWorseGroundThanARealmWouldOtherwiseTake()
    {
        var mineLand = new List<double>();
        var ordinaryLand = new List<double>();

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Settlement settlement in world.Settlements)
            {
                double habitability = world.Regions[settlement.RegionId].Habitability;

                if (settlement.Site == SiteCharacter.Mine) mineLand.Add(habitability);
                else ordinaryLand.Add(habitability);
            }
        }

        Assert.True(mineLand.Count > 0, "No mine sites to compare.");

        double mine = Median(mineLand);
        double ordinary = Median(ordinaryLand);

        Assert.True(
            mine < ordinary,
            $"Mine sites stand on land of median habitability {mine:F3} against {ordinary:F3} "
            + "elsewhere. A realm that pays nothing to reach a deposit is not going out of its way "
            + "for one.");
    }

    /// <summary>
    /// A camp founded for the ore is usually known for it, and not always.
    /// </summary>
    /// <remarks>
    /// <para>Both halves matter. Without the prior in <see cref="Systems.SpecializationSystem"/>
    /// only 5.8% of these camps were recorded as mining towns and four in five became farming
    /// villages, because the scorer reads soil and geology and has no idea anybody was sent
    /// anywhere — so the map said "mine" and the chronicle said "farming", which is worse than
    /// either alone.</para>
    ///
    /// <para>The upper bound is the other half of the same claim: the character is why they stood
    /// there and the specialization is what the place became known for, and if every camp inherited
    /// its trade the two would be one field with two names.</para>
    /// </remarks>
    [Fact]
    public void MineCampsAreUsuallyButNotAlwaysKnownForTheirOre()
    {
        int camps = 0;
        int mining = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Settlement settlement in world.Settlements)
            {
                if (settlement.Site != SiteCharacter.Mine) continue;
                if (settlement.Specialization == SettlementSpecialization.None) continue;

                camps++;
                if (settlement.Specialization == SettlementSpecialization.Mining) mining++;
            }
        }

        Assert.True(camps > 0, "No mine camp lived long enough to be known for anything.");

        double share = mining / (double)camps;

        Assert.InRange(share, 0.55, 0.92);
    }

    /// <summary>
    /// A mercantile crown works more ore than an incurious one.
    /// </summary>
    /// <remarks>
    /// <para>What makes the need the crown's decision rather than a rule of the map. Every realm
    /// that grows into a state plants its first mine if there is ore within reach — that is the
    /// appetite comparing against zero — so what the crown actually decides is the second and the
    /// third, and this measures exactly there.</para>
    ///
    /// <para>Median rather than mean, and restricted to realms large enough to have had the choice:
    /// a realm of four settlements has not yet been asked the question this is about.</para>
    ///
    /// <para><b>Over its own, wider panel.</b> The eight seeds the rest of this class uses yield
    /// about fourteen grown realms on each side of the comparison, and two medians over fourteen
    /// points are not a measurement — they inverted the moment an unrelated change to the
    /// household rules moved which figures ended up governing. Twenty-four seeds give 34 against
    /// 53, and the gap comes back clearly: 0.601 against 0.471. The bound stays "the order
    /// holds", because the size of the gap still depends on how many realms happen to grow
    /// large.</para>
    /// </remarks>
    [Fact]
    public void AMercantileCrownWorksMoreOre()
    {
        const int GrownUp = 8;

        var hungry = new List<double>();
        var content = new List<double>();

        for (ulong seed = 1; seed <= 24; seed++)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Civilization civilization in world.ActiveCivilizations())
            {
                int held = 0;
                int mines = 0;

                foreach (Settlement settlement in world.ActiveSettlementsOf(civilization))
                {
                    held++;
                    if (settlement.Site == SiteCharacter.Mine
                        || settlement.Specialization == SettlementSpecialization.Mining)
                    {
                        mines++;
                    }
                }

                if (held < GrownUp) continue;

                double mercantile = world.ValuesFor(civilization).Mercantile;
                if (mines >= 2) hungry.Add(mercantile); else content.Add(mercantile);
            }
        }

        Assert.True(
            hungry.Count > 0 && content.Count > 0,
            $"Needed realms of {GrownUp}+ settlements on both sides of the comparison; got "
            + $"{hungry.Count} with two or more mines and {content.Count} with fewer.");

        Assert.True(
            Median(hungry) > Median(content),
            $"Realms holding two or more mines have a median mercantile value of "
            + $"{Median(hungry):F3}, against {Median(content):F3} for those holding fewer. The "
            + "crown's appetite has stopped reaching the decision.");
    }


    // ─── The march ────────────────────────────────────────────────────────────────

    /// <summary>
    /// A garrison is a minority of foundings, and never in a realm nobody threatened.
    /// </summary>
    /// <remarks>
    /// The bound that keeps the need a need. Purpose foundings are the exceptions that make the
    /// rest of a map look intelligent, so they have to stay exceptional: across twenty-four seeds
    /// posts are 5.3% of settlements and mines 11.3%, which leaves ordinary colonisation holding
    /// five sixths of the map. The second assertion is the one that makes it a *reason*: not one
    /// post in the panel stands in a realm that never fought anybody.
    /// </remarks>
    [Fact]
    public void AGarrisonIsAMinorityAndNeverUnprovoked()
    {
        int settlements = 0;
        int posts = 0;
        int unprovoked = 0;

        for (ulong seed = 1; seed <= 24; seed++)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            var fought = new HashSet<EntityId>();
            foreach (War war in world.Wars)
            {
                fought.Add(war.AggressorId);
                fought.Add(war.DefenderId);
            }

            foreach (Settlement settlement in world.Settlements)
            {
                settlements++;
                if (settlement.Site != SiteCharacter.Strategic) continue;

                posts++;
                if (!fought.Contains(settlement.CivilizationId)) unprovoked++;
            }
        }

        Assert.True(posts > 20, $"Only {posts} frontier posts were founded across the panel.");
        Assert.InRange(posts / (double)settlements, 0.01, 0.10);
        Assert.Equal(0, unprovoked);
    }

    /// <summary>
    /// A frontier post stands on the march, and on ground a farmer would not have chosen.
    /// </summary>
    /// <remarks>
    /// <para>The equivalent of the ore build's "mine sites stand on measurably worse land": a
    /// purpose founding has to be visible in the ground it took, or the purpose is a label. Two
    /// measures, both on the region rather than the site, because the region is where this need
    /// does its work — see the note in <c>SiteSelection</c> on the two siting terms that were
    /// built for this and cut.</para>
    ///
    /// <para>Measured: a post's region sits a median of 1 hop from a region another realm holds,
    /// against 3 for everything else, and carries a median ruggedness of 0.155 against 0.109.</para>
    /// </remarks>
    [Fact]
    public void AFrontierPostStandsOnTheMarch()
    {
        var postHops = new List<double>();
        var otherHops = new List<double>();
        var postRuggedness = new List<double>();
        var otherRuggedness = new List<double>();

        for (ulong seed = 1; seed <= 24; seed++)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Settlement settlement in world.Settlements)
            {
                if (!world.Regions.Contains(settlement.RegionId)) continue;

                bool post = settlement.Site == SiteCharacter.Strategic;
                double hops = HopsToAnotherRealm(world, settlement);
                double ruggedness = world.Regions[settlement.RegionId].Ruggedness;

                (post ? postHops : otherHops).Add(hops);
                (post ? postRuggedness : otherRuggedness).Add(ruggedness);
            }
        }

        Assert.True(postHops.Count > 20, $"Only {postHops.Count} posts to measure.");

        Assert.True(
            Median(postHops) < Median(otherHops),
            $"Frontier posts sit a median of {Median(postHops):F1} regions from another realm, "
            + $"against {Median(otherHops):F1} for ordinary foundings. They are no longer on a "
            + "march.");

        Assert.True(
            Median(postRuggedness) > Median(otherRuggedness),
            $"Frontier posts stand on median ruggedness {Median(postRuggedness):F3}, against "
            + $"{Median(otherRuggedness):F3} elsewhere. The search has stopped preferring ground "
            + "a small force can hold.");
    }

    /// <summary>
    /// A bellicose crown garrisons and a peaceable one does not.
    /// </summary>
    /// <remarks>
    /// What makes this the crown's decision rather than a rule of the map, and the exact
    /// counterpart of <see cref="AMercantileCrownWorksMoreOre"/>. The appetite floor is zero
    /// rather than a small positive, so a peaceable realm facing a neighbour it has fought answers
    /// by not building forts — which is why the comparison is against realms with none at all
    /// rather than between one post and two. Measured: 0.517 against 0.442.
    /// </remarks>
    [Fact]
    public void ABellicoseCrownGarrisonsTheMarch()
    {
        var garrisoned = new List<double>();
        var quiet = new List<double>();

        for (ulong seed = 1; seed <= 24; seed++)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Civilization civilization in world.ActiveCivilizations())
            {
                bool holdsAPost = false;
                foreach (Settlement settlement in world.ActiveSettlementsOf(civilization))
                {
                    if (settlement.Site != SiteCharacter.Strategic) continue;
                    holdsAPost = true;
                    break;
                }

                double aggression = world.ValuesFor(civilization).Aggression;
                (holdsAPost ? garrisoned : quiet).Add(aggression);
            }
        }

        Assert.True(
            garrisoned.Count > 20 && quiet.Count > 20,
            $"Needed realms on both sides; got {garrisoned.Count} holding a post and "
            + $"{quiet.Count} holding none.");

        Assert.True(
            Median(garrisoned) > Median(quiet),
            $"Realms holding a frontier post have a median aggression of {Median(garrisoned):F3}, "
            + $"against {Median(quiet):F3} for those holding none. The crown's temper has stopped "
            + "reaching the decision.");
    }

    /// <summary>
    /// How many regions from this settlement's own to the nearest one another realm holds.
    /// </summary>
    /// <remarks>
    /// Capped, and the cap is reported rather than skipped: a settlement with no foreign region
    /// within four hops is genuinely deep inside its realm, and dropping those would compare
    /// frontier posts only against other border towns.
    /// </remarks>
    private static double HopsToAnotherRealm(WorldState world, Settlement settlement)
    {
        const int Cap = 4;

        var seen = new Dictionary<EntityId, int> { [settlement.RegionId] = 0 };
        var queue = new List<EntityId> { settlement.RegionId };

        for (int head = 0; head < queue.Count; head++)
        {
            Region region = world.Regions[queue[head]];
            int distance = seen[region.Id];

            if (!region.Owner.IsNone && region.Owner != settlement.CivilizationId) return distance;
            if (distance >= Cap) continue;

            foreach (EntityId neighbourId in region.AdjacentRegions)
            {
                if (!world.Regions[neighbourId].IsLand) continue;
                if (seen.TryAdd(neighbourId, distance + 1)) queue.Add(neighbourId);
            }
        }

        return Cap + 1;
    }

    private static double Median(List<double> values)
    {
        values.Sort();
        return values[values.Count / 2];
    }
}
