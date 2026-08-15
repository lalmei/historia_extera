using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.Terrain;
using HistoryEngine.World;

namespace HistoryEngine.Systems;

/// <summary>
/// Claims neighbouring regions and founds new settlements in them.
/// </summary>
/// <remarks>
/// Expansion pressure comes from a civilization's own success — population per settlement —
/// multiplied by how expansionist its culture is. So a crowded, restless civilization spreads
/// and a small or sedentary one does not, without either being scripted.
///
/// <para>Candidate regions are found by walking region adjacency outward from owned territory,
/// which means expansion follows the shape of the land: a civilization hemmed in by mountains
/// or coast runs out of room, and two civilizations growing toward each other eventually
/// contend for the same regions. That contention is the seam Milestone 6's diplomacy and war
/// plugs into.</para>
/// </remarks>
public sealed class ExpansionSystem : IYearSystem
{
    /// <summary>Baseline yearly chance of founding a settlement, before culture and pressure.</summary>
    private const double BaseChance = 0.06;

    /// <summary>Population per existing settlement at which pressure is considered full.</summary>
    private const double PressureReference = 2200.0;

    /// <summary>A new region must be at least this habitable to be worth claiming.</summary>
    private const double MinHabitability = 0.15;

    /// <summary>Starting population of a new settlement.</summary>
    private const int SettlerCount = 70;

    /// <summary>
    /// People a departure point keeps whatever else happens.
    /// </summary>
    /// <remarks>
    /// A settlement does not colonise itself out of existence. Without a floor, a realm reduced to
    /// one struggling village would send parties out of it until the abandonment threshold
    /// finished it — and expansion, which is supposed to be a sign of success, would become a way
    /// for a failing realm to kill itself.
    /// </remarks>
    private const int MinimumLeftBehind = 140;

    /// <summary>A party smaller than this is not worth sending, and would not survive.</summary>
    private const int MinimumParty = 25;

    /// <summary>Candidate sites per axis. Coarser than a capital's — a colony is a smaller bet.</summary>
    private const int SitesPerAxis = 4;

    public string Name => "expansion";

    public void Tick(WorldState world, int year)
    {
        IRng rng = world.Root.Fork(Name, year);

        foreach (Civilization civilization in world.ActiveCivilizations())
        {
            int settlementCount = CountActive(world, civilization);
            if (settlementCount == 0) continue;

            Culture culture = world.CultureOf(civilization);

            double pressure = DetMath.Clamp01(
                civilization.Population / (settlementCount * PressureReference));

            // Founding a colony is a decision of state, so it is the realm's effective
            // expansionism rather than its culture's: a plague year suppresses it, a run of
            // victories encourages it, and an unambitious king declines to do it at all.
            double chance = BaseChance
                * (0.25 + world.ValuesFor(civilization).Expansionism)
                * pressure;
            if (!rng.Chance(chance)) continue;

            Region? target = FindFrontierRegion(world, civilization);
            if (target is null) continue;

            Claim(world, civilization, culture, target, year, rng);
        }
    }

    private static int CountActive(WorldState world, Civilization civilization)
    {
        int count = 0;
        foreach (EntityId id in civilization.SettlementIds)
        {
            if (world.Settlements[id].IsActive) count++;
        }

        return count;
    }

    /// <summary>
    /// The most habitable unclaimed region adjacent to this civilization's territory.
    /// </summary>
    /// <remarks>
    /// Candidates are gathered by walking owned regions in id order and their neighbours in
    /// the fixed order <see cref="RegionGrid"/> linked them, so discovery order is
    /// reproducible. The final choice breaks ties on region id, since <see cref="List{T}.Sort"/>
    /// is unstable and equal habitability scores are common on uniform terrain.
    /// </remarks>
    private static Region? FindFrontierRegion(WorldState world, Civilization civilization)
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

                if (score > bestScore ||
                    (score == bestScore && best is not null && neighbour.Id.CompareTo(best.Id) < 0))
                {
                    bestScore = score;
                    best = neighbour;
                }
            }
        }

        return best;
    }

    private static void Claim(
        WorldState world,
        Civilization civilization,
        Culture culture,
        Region target,
        int year,
        IRng rng)
    {
        target.Owner = civilization.Id;
        civilization.TerritoryRegionIds.Add(target.Id);

        world.Chronicle.Record(
            year, EventKind.RegionClaimed, target.Id, obj: civilization.Id);

        Point2 site = SiteSelection.Best(world, target, SitesPerAxis);

        // Settlers come out of somewhere. Conjuring them made expansion free, so a realm could
        // seed a continent without ever feeling it; taking them from the nearest town means a
        // frontier is paid for by the places behind it.
        Settlement? parent = Departure(world, civilization, target);
        int settlers = SettlerCount;

        if (parent is not null)
        {
            settlers = Math.Min(SettlerCount, parent.Population - MinimumLeftBehind);
            if (settlers < MinimumParty) return;

            parent.Population -= settlers;
        }

        // A cadet ranked out of the line is precisely who is sent: the heir is needed at home,
        // and a fourth son with no prospect of a throne has every reason to take one. This is the
        // loop the offices design named and did not build — a house planted in a colony, whose
        // children are born there, is where a breakaway realm eventually comes from.
        List<Figure> spare = Offices.Courtiers(world, civilization, year);
        Figure? leader = spare.Count == 0 ? null : rng.Pick(spare);

        WorldBuilder.FoundSettlement(
            world, civilization, culture, target, site, year, settlers, rng, parent, leader);
    }

    /// <summary>
    /// The settlement a founding party comes out of: the realm's nearest active town to the site.
    /// </summary>
    /// <remarks>
    /// Nearest rather than largest, because a party walks. Distance goes through
    /// <see cref="WorldState.Distance"/> so it takes the short way across the seam on a periodic
    /// world, and ties break on id — two settlements equidistant from a third is not rare on a grid
    /// and <see cref="List{T}.Sort"/> would otherwise order them unpredictably.
    /// </remarks>
    private static Settlement? Departure(
        WorldState world, Civilization civilization, Region target)
    {
        Settlement? nearest = null;
        double best = double.PositiveInfinity;

        foreach (Settlement candidate in world.ActiveSettlementsOf(civilization))
        {
            if (candidate.Population <= MinimumLeftBehind) continue;

            double distance = world.Distance(
                candidate.X, candidate.Z, target.CenterX, target.CenterZ);

            if (distance < best
                || (distance == best && nearest is not null && candidate.Id.CompareTo(nearest.Id) < 0))
            {
                best = distance;
                nearest = candidate;
            }
        }

        return nearest;
    }
}
