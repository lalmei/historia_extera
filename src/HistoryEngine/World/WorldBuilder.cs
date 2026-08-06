using System.Globalization;
using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.Naming;
using HistoryEngine.Terrain;

namespace HistoryEngine.World;

/// <summary>
/// Builds the year-zero world: terrain, regions, founding cultures and their capitals.
/// </summary>
/// <remarks>
/// Founding placement is where the three-tier terrain access pattern is exercised end to end,
/// and it is worth reading as the worked example the rest of the simulation should imitate:
///
/// <list type="number">
///   <item><description>Every region already carries a habitability score computed from the
///   interpolated lattice, so ranking the entire world costs <b>zero samples</b>.</description></item>
///   <item><description>Only the handful of regions actually chosen get
///   <see cref="TerrainAtlas.RefinedPoints"/>, a bounded burst of real sampling — a few dozen
///   points each, once, at world creation.</description></item>
///   <item><description>The winning coordinate is then sampled exactly and memoised, because
///   it becomes a permanent part of the record.</description></item>
/// </list>
///
/// <para>The naive version — score every candidate coordinate in the world at full fidelity —
/// produces an almost identical result here and would take hours against Vintage Story's
/// sampler.</para>
/// </remarks>
public static class WorldBuilder
{
    /// <summary>Minimum habitability for a region to be considered as a homeland.</summary>
    private const double MinFoundingHabitability = 0.25;

    /// <summary>Candidate capital sites evaluated per axis within the chosen region.</summary>
    private const int SitesPerAxis = 8;

    public static WorldState Create(WorldConfig config, ITerrainSampler? sampler = null)
    {
        config.Validate();

        sampler ??= new ProceduralTerrainSampler(config.Seed, config.Bounds, config.Terrain);
        var atlas = new TerrainAtlas(sampler, config.TerrainStride, config.HydrologyStride);

        var world = new WorldState(
            config,
            atlas,
            new Pcg32(config.Seed),
            new PlaceholderNameGenerator());

        RegionGrid.Build(atlas, config.RegionSize, world.Regions);

        world.Chronicle.Record(config.StartYear, EventKind.WorldCreated, EntityId.None);

        FoundInitialCivilizations(world);
        return world;
    }

    private static void FoundInitialCivilizations(WorldState world)
    {
        WorldConfig config = world.Config;
        if (config.InitialCivilizations == 0) return;

        List<Region> homelands = ChooseHomelands(world, config.InitialCivilizations);

        for (int i = 0; i < homelands.Count; i++)
        {
            FoundCivilization(world, homelands[i], config.StartYear);
        }
    }

    /// <summary>
    /// Picks well-separated, habitable regions as founding homelands. Costs no terrain samples.
    /// </summary>
    private static List<Region> ChooseHomelands(WorldState world, int wanted)
    {
        var candidates = new List<Region>();
        foreach (Region region in world.Regions)
        {
            if (region.IsLand && region.Habitability >= MinFoundingHabitability)
            {
                candidates.Add(region);
            }
        }

        // Best land first. Region id breaks ties so the sort is a total order — List.Sort is
        // unstable, so equal scores would otherwise order unpredictably.
        candidates.Sort((a, b) =>
        {
            int byScore = b.Habitability.CompareTo(a.Habitability);
            return byScore != 0 ? byScore : a.Id.CompareTo(b.Id);
        });

        // Spread civilizations out, so they have room to expand into and eventually meet.
        // Relax the constraint rather than fail if the world is too cramped or too wet.
        double separation = world.Config.WorldSize / (DetMath.Sqrt(wanted) * 2.2);

        for (int attempt = 0; attempt < 8; attempt++)
        {
            List<Region> chosen = GreedyPick(candidates, wanted, separation);
            if (chosen.Count >= wanted || separation < 1.0)
            {
                return chosen;
            }

            separation *= 0.7;
        }

        return GreedyPick(candidates, wanted, 0.0);
    }

    private static List<Region> GreedyPick(List<Region> candidates, int wanted, double separation)
    {
        var chosen = new List<Region>(wanted);
        double minSquared = separation * separation;

        foreach (Region candidate in candidates)
        {
            if (chosen.Count >= wanted) break;

            bool tooClose = false;
            for (int i = 0; i < chosen.Count; i++)
            {
                double distanceSquared = DetMath.DistanceSquared(
                    candidate.CenterX, candidate.CenterZ, chosen[i].CenterX, chosen[i].CenterZ);

                if (distanceSquared < minSquared)
                {
                    tooClose = true;
                    break;
                }
            }

            if (!tooClose) chosen.Add(candidate);
        }

        return chosen;
    }

    private static void FoundCivilization(WorldState world, Region homeland, int year)
    {
        // One fork per civilization, keyed on the homeland region, so founding order cannot
        // shift any civilization's rolled traits.
        IRng rng = world.Root.Fork("worldgen.found", homeland.Id.ToDiscriminator());

        EntityId cultureId = world.Cultures.NextId;
        var culture = new Culture(
            cultureId,
            world.Names.ForCulture(cultureId, rng),
            languageSeed: Hash.Combine(world.Config.Seed, (ulong)cultureId.ToDiscriminator()),
            values: CultureValues.Roll(rng),
            government: (GovernmentForm)rng.NextInt(Enum.GetValues(typeof(GovernmentForm)).Length));
        world.Cultures.Add(culture);

        EntityId civId = world.Civilizations.NextId;
        var civilization = new Civilization(
            civId,
            cultureId,
            world.Names.ForCivilization(civId, culture, rng),
            year);
        world.Civilizations.Add(civilization);

        Point2 site = SiteSelection.Best(world, homeland, SitesPerAxis);
        Settlement capital = FoundSettlement(
            world, civilization, culture, homeland, site, year, population: 120, rng);

        capital.IsCapital = true;
        civilization.CapitalId = capital.Id;

        homeland.Owner = civId;
        civilization.TerritoryRegionIds.Add(homeland.Id);

        world.Chronicle.Record(
            year, EventKind.CivilizationFounded, civId, location: capital.Id);

        CrownNewRuler(world, civilization, culture, year, rng);
    }

    /// <summary>Creates a settlement, wires it to its civilization, and records the founding.</summary>
    public static Settlement FoundSettlement(
        WorldState world,
        Civilization civilization,
        Culture culture,
        Region region,
        Point2 site,
        int year,
        int population,
        IRng rng)
    {
        EntityId id = world.Settlements.NextId;

        var settlement = new Settlement(
            id,
            civilization.Id,
            region.Id,
            world.Names.ForSettlement(id, culture, rng),
            site.X,
            site.Z,
            year,
            population)
        {
            FoundedBy = civilization.Id,
            PeakPopulation = population,
        };

        world.Settlements.Add(settlement);
        civilization.SettlementIds.Add(id);

        world.Chronicle.Record(
            year, EventKind.SettlementFounded, id, obj: civilization.Id, location: region.Id);

        return settlement;
    }

    /// <summary>
    /// Raises a new ruler and crowns them.
    /// </summary>
    /// <remarks>
    /// Rulers arrive as fully-grown adults with no parents. Milestone 5 replaces this with real
    /// dynasties, marriages and heirs; until then a succession is a new figure rather than a
    /// claim traced through a family tree.
    /// </remarks>
    public static Figure CrownNewRuler(
        WorldState world, Civilization civilization, Culture culture, int year, IRng rng)
    {
        EntityId id = world.Figures.NextId;

        int age = rng.NextInt(22, 46);
        var ruler = new Figure(
            id,
            civilization.Id,
            culture.Id,
            world.Names.ForFigure(id, culture, rng),
            birthYear: year - age)
        {
            BirthSettlementId = civilization.CapitalId,
        };

        world.Figures.Add(ruler);

        ruler.Titles.Add(new TitleHolding(culture.RulerTitle, civilization.Id, year, null));
        civilization.CurrentRulerId = id;
        civilization.RulerIds.Add(id);

        world.Chronicle.Record(
            year,
            EventKind.RulerCrowned,
            id,
            obj: civilization.Id,
            location: civilization.CapitalId,
            data: Chronicle.Data(
                ("title", culture.RulerTitle),
                ("age", age.ToString(CultureInfo.InvariantCulture))));

        return ruler;
    }
}
