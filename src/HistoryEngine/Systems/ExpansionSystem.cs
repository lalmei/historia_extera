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

            double chance = BaseChance * (0.25 + culture.Values.Expansionism) * pressure;
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

        WorldBuilder.FoundSettlement(
            world, civilization, culture, target, site, year, SettlerCount, rng);
    }
}
