using HistoryEngine.Core;
using HistoryEngine.Entities;

namespace HistoryEngine.World;

/// <summary>What a realm is short of when it sends a party out.</summary>
/// <remarks>
/// <para>Explicit values, because this reaches <see cref="SiteSelection.Best"/> and decides which
/// weights a siting decision uses. Renumbering it would silently re-site every colony.</para>
///
/// <para><b>Land is not a fallback, it is the answer nearly every time.</b> Historical colonisation
/// is mostly surplus people walking to the next valley, and a model in which every founding has a
/// purpose behind it produces a map that reads like a plan rather than like a country. The named
/// needs are the exceptions that make the rest look intelligent.</para>
/// </remarks>
public enum FoundingNeed
{
    /// <summary>Room for people. Ordinary expansion, and the great majority of it.</summary>
    Land = 0,

    /// <summary>Ore the realm has not got, worth walking past good soil to reach.</summary>
    Ore = 1,
}

/// <summary>Where a settling party is going, and what the realm wants from it.</summary>
public readonly record struct FrontierChoice(Region Region, FoundingNeed Need);

/// <summary>
/// Which country a realm settles next, and what for.
/// </summary>
/// <remarks>
/// <para><b>Need first, then search.</b> Expansion used to ask one question — which unclaimed
/// neighbour is most habitable — and habitability is fertility with water and footing on it, so
/// every party ever sent out was a farming party. That is right for most of them and wrong for the
/// ones a map is read for: nothing in the model could put a camp on a mountain because somebody
/// wanted the ore under it, so ore was a thing settlements were retrospectively found to be near
/// rather than a reason anyone went anywhere.</para>
///
/// <para><b>The crown picks the need; the search only finds the ground.</b> Whether a realm wants
/// ore is read from its effective values — culture, the reigning temperament, and the fortunes that
/// move both — so a mercantile crown works more of it and a plague year founds almost nothing at
/// all, through the expansion chance that was already there. The named leader of a party is who is
/// sent, not who decided the realm needed a mine.</para>
///
/// <para><b>Nothing here draws a random number, and nothing here samples terrain.</b> The need is a
/// function of state, and both searches rank regions on fields <see cref="RegionGrid"/> derived once
/// from the primed lattice. That is deliberate on both counts: the founding roll stays exactly the
/// roll it was, and a search that walks three hops in every direction costs the same as the search
/// that walked one — nothing. The only terrain a founding spends is the single
/// <see cref="SiteSelection.Best"/> inside the region that wins.</para>
/// </remarks>
public static class Colonisation
{
    /// <summary>A new region must be at least this habitable to be worth claiming.</summary>
    private const double MinHabitability = 0.15;

    /// <summary>
    /// How habitable ground taken for its ore has to be.
    /// </summary>
    /// <remarks>
    /// Lower than <see cref="MinHabitability"/>, because a mining camp is not a farm and the whole
    /// claim of a purpose founding is that the realm will accept worse ground to get the deposit.
    /// Not zero, and that matters: <see cref="Region.Habitability"/> returns exactly zero for water
    /// and for biomes nobody lives in, so a floor above zero is what keeps a mine off the ice
    /// without needing a second biome test that could drift away from the first.
    /// </remarks>
    private const double MinOreHabitability = 0.06;

    /// <summary>
    /// How far past its own border a realm will walk for ore, in regions.
    /// </summary>
    /// <remarks>
    /// One hop is the adjacent region a farming party would have taken anyway, so a reach of one
    /// would make an ore need nothing but a re-ranking of the same candidates and the deposit would
    /// only ever be found where it happened to lie next door. Three hops is 384 units on a
    /// 128-unit grid — half again the distance a settlement draws its fields from
    /// (<see cref="Hinterland"/>), which is the right order for somewhere you send people to work
    /// rather than somewhere you farm.
    /// </remarks>
    private const int OreReach = 3;

    /// <summary>What each extra hop out costs a candidate deposit.</summary>
    /// <remarks>
    /// Against an ore term spanning 0.65 this makes the far end of the reach worth about a third of
    /// the deposit's own range: a realm will pass a poor showing next door for a rich one three
    /// regions out, and will not cross the map for a marginal improvement. What going far actually
    /// costs a mine is not paid here — it is paid by supply, since a camp that far from the roads
    /// is fed by <see cref="Specializations.ImportReliance"/> and fails when the routes do.
    /// </remarks>
    private const double HopCost = 0.10;

    // What an ore search wants beyond the deposit itself. Height and broken country are where rock
    // is exposed and where nobody is farming anyway; neither is allowed to outweigh the ore.
    private const double OreHeightWeight = 0.35;
    private const double OreRuggednessWeight = 0.20;

    /// <summary>
    /// Settlements a realm holds before it will spend a party on anything but food.
    /// </summary>
    /// <remarks>
    /// A realm of two villages has no use for ore it has nobody to work into anything, and sending
    /// a third of its people to a hillside is how it stops being a realm. Purpose foundings are a
    /// thing a state does once it is a state.
    /// </remarks>
    private const int SettlementsBeforeOre = 3;

    /// <summary>
    /// How much of its map a realm is willing to spend on mines, from an incurious crown to a
    /// hungry one.
    /// </summary>
    /// <remarks>
    /// <para>Read against the count the realm already holds, so the need switches itself off once
    /// it is met and comes back when the realm outgrows it. Across eight seeds this settles at
    /// <b>10.7%</b> of all foundings, against 0% before — a minority, which is the claim: ordinary
    /// colonisation is people walking to the next valley, and the purpose foundings are the
    /// exceptions that make the rest of the map look intelligent. There is room in that figure for
    /// the quarries, ports and frontier posts that would each take a share of their own.</para>
    ///
    /// <para><b>Every realm that becomes a state gets its first mine; the crown decides the
    /// rest.</b> Below one whole settlement's worth of appetite the comparison is against zero, so
    /// any realm past <see cref="SettlementsBeforeOre"/> with ore in reach will plant one. What
    /// separates crowns is the second and the third: realms of eight settlements or more holding
    /// two or more mines have a median mercantile value of <b>0.75</b>, against <b>0.51</b> for
    /// those holding fewer.</para>
    /// </remarks>
    private const double LeastOreShare = 0.03;

    private const double MostOreShare = 0.14;

    /// <summary>
    /// Where this civilization's next settling party is going, and what the realm wants from it.
    /// </summary>
    /// <returns>Null when there is nowhere left worth going.</returns>
    public static FrontierChoice? Frontier(WorldState world, Civilization civilization)
    {
        if (WantsOre(world, civilization))
        {
            Region? deposit = BestOre(world, civilization);
            if (deposit is not null) return new FrontierChoice(deposit, FoundingNeed.Ore);
        }

        Region? land = BestLand(world, civilization);
        return land is null ? null : new FrontierChoice(land, FoundingNeed.Land);
    }

    /// <summary>
    /// Whether the realm is short of ore, and grown enough to mind.
    /// </summary>
    /// <remarks>
    /// Counted on the character of the ground as well as on the trade, because the two answer
    /// different questions and both are "we have a mine": <see cref="SiteCharacter.Mine"/> is a camp
    /// sent out for the deposit and still too small to be known for anything, and
    /// <see cref="SettlementSpecialization.Mining"/> is a place that grew into the work without
    /// having been founded for it. A realm with either is not short.
    /// </remarks>
    private static bool WantsOre(WorldState world, Civilization civilization)
    {
        int settlements = 0;
        int mines = 0;

        foreach (Settlement settlement in world.ActiveSettlementsOf(civilization))
        {
            settlements++;
            if (settlement.Site == SiteCharacter.Mine
                || settlement.Specialization == SettlementSpecialization.Mining)
            {
                mines++;
            }
        }

        if (settlements < SettlementsBeforeOre) return false;

        double appetite = DetMath.Lerp(
            LeastOreShare, MostOreShare, world.ValuesFor(civilization).Mercantile);

        return mines < settlements * appetite;
    }

    /// <summary>
    /// The most habitable unclaimed region adjacent to this civilization's territory.
    /// </summary>
    /// <remarks>
    /// <para>Unchanged from what expansion has always done, and deliberately so: ordinary
    /// colonisation is people walking to the next valley for the soil, and habitability is the
    /// measure of soil with water and footing already on it. Adjacent is correct here — a farming
    /// party has no reason to pass good ground to reach other good ground.</para>
    ///
    /// <para>Candidates are gathered by walking owned regions in id order and their neighbours in
    /// the fixed order <see cref="RegionGrid"/> linked them, so discovery order is reproducible. The
    /// final choice breaks ties on region id, since equal habitability scores are common on uniform
    /// terrain.</para>
    /// </remarks>
    private static Region? BestLand(WorldState world, Civilization civilization)
    {
        Region? best = null;
        double bestScore = double.NegativeInfinity;

        foreach (EntityId ownedId in civilization.TerritoryRegionIds)
        {
            Region owned = world.Regions[ownedId];

            foreach (EntityId neighbourId in owned.AdjacentRegions)
            {
                Region neighbour = world.Regions[neighbourId];

                if (!neighbour.Owner.IsNone) continue;
                if (!neighbour.IsLand) continue;

                double score = neighbour.Habitability;
                if (score < MinHabitability) continue;

                if (Beats(score, bestScore, neighbour, best))
                {
                    bestScore = score;
                    best = neighbour;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// The best unclaimed deposit within reach, however awkward the ground it sits on.
    /// </summary>
    /// <remarks>
    /// <para>A breadth-first walk outward from owned territory to <see cref="OreReach"/> hops,
    /// which is what separates this from the land search: the deposit is where it is, and a realm
    /// that will only look next door will only ever find the ore next door. Everything else in the
    /// engine's spatial reasoning walks region adjacency, so this does too — a mine three regions
    /// out is reachable by land, not across a strait.</para>
    ///
    /// <para><b>The walk crosses its own ground and empty ground, never a neighbour's.</b> Passing
    /// through another realm's territory to plant a camp beyond it is a claim about borders that
    /// nothing here is entitled to make, and it would put enclaves in the middle of somebody else's
    /// country for the diplomacy system to inherit.</para>
    ///
    /// <para>Deterministic without a sort: regions enter the queue from territory in claim order
    /// and expand in the fixed neighbour order the grid linked them, each is scored the first time
    /// it is reached — which is at its shortest hop distance — and ties break on region id. The
    /// visited map is a lookup and never an iteration, for the reason
    /// <see cref="SiteSelection.Best"/> gives.</para>
    /// </remarks>
    private static Region? BestOre(WorldState world, Civilization civilization)
    {
        var reached = new Dictionary<EntityId, int>();
        var queue = new List<EntityId>();

        foreach (EntityId ownedId in civilization.TerritoryRegionIds)
        {
            if (reached.TryAdd(ownedId, 0)) queue.Add(ownedId);
        }

        Region? best = null;
        double bestScore = double.NegativeInfinity;

        for (int head = 0; head < queue.Count; head++)
        {
            EntityId id = queue[head];
            int hops = reached[id];
            if (hops >= OreReach) continue;

            foreach (EntityId neighbourId in world.Regions[id].AdjacentRegions)
            {
                if (!reached.TryAdd(neighbourId, hops + 1)) continue;

                Region neighbour = world.Regions[neighbourId];
                if (!neighbour.IsLand) continue;

                // Ours or nobody's: walkable, and worth scoring if nobody has it.
                bool open = neighbour.Owner.IsNone;
                if (!open && neighbour.Owner != civilization.Id) continue;

                queue.Add(neighbourId);
                if (!open) continue;

                double score = OreScore(neighbour, hops + 1);
                if (double.IsNegativeInfinity(score)) continue;

                if (Beats(score, bestScore, neighbour, best))
                {
                    bestScore = score;
                    best = neighbour;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// What a deposit is worth to a realm standing <paramref name="hops"/> regions away from it.
    /// </summary>
    /// <remarks>
    /// <para>The gate is <see cref="Specializations.OreThreshold"/> — the same line specialization
    /// draws, so a camp founded to work ore can never be somewhere its own trade would be
    /// impossible. There is no deposit map underneath this and there should not be one until
    /// something else needs it: a region above the threshold is where there is ore.</para>
    ///
    /// <para>Height and ruggedness are read as where rock is exposed and where nobody is farming,
    /// not as ore in themselves. They are capped well below the deposit term so that a bare
    /// mountain never outranks a workable one — the realm is going for the ore, and the mountain is
    /// what it puts up with to get it.</para>
    /// </remarks>
    private static double OreScore(Region region, int hops)
    {
        if (region.GeologicActivity < Specializations.OreThreshold) return double.NegativeInfinity;
        if (region.Habitability < MinOreHabitability) return double.NegativeInfinity;

        double score = region.GeologicActivity;
        score += DetMath.InverseLerp(400.0, 1800.0, region.MeanHeight) * OreHeightWeight;
        score += region.Ruggedness * OreRuggednessWeight;
        score -= (hops - 1) * HopCost;

        return score;
    }

    /// <summary>Strictly better, or equal on a lower region id.</summary>
    private static bool Beats(double score, double bestScore, Region candidate, Region? best) =>
        score > bestScore
        || (score == bestScore && best is not null && candidate.Id.CompareTo(best.Id) < 0);
}
