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

    public static WorldState Create(
        WorldConfig config,
        ITerrainSampler? sampler = null,
        INameGenerator? names = null)
    {
        config.Validate();

        sampler ??= new ProceduralTerrainSampler(
            config.Seed, config.Bounds, config.Terrain, config.EastWestPeriodic);
        var atlas = new TerrainAtlas(
            sampler,
            config.TerrainStride,
            config.HydrologyStride,
            config.EastWestPeriodic);

        var world = new WorldState(
            config,
            atlas,
            new Pcg32(config.Seed),
            names ?? new MarkovNameGenerator(config.Seed));

        RegionGrid.Build(atlas, config.RegionSize, world.Regions);

        world.Chronicle.Record(
            config.StartYear,
            EventKind.WorldCreated,
            EntityId.None,
            data: Chronicle.Data(
                ("name", world.Flavour.Name),
                ("kind", world.Flavour.Kind.ToString()),
                ("designation", world.Flavour.Designation)));

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
            List<Region> chosen = GreedyPick(world, candidates, wanted, separation);
            if (chosen.Count >= wanted || separation < 1.0)
            {
                return chosen;
            }

            separation *= 0.7;
        }

        return GreedyPick(world, candidates, wanted, 0.0);
    }

    private static List<Region> GreedyPick(
        WorldState world, List<Region> candidates, int wanted, double separation)
    {
        var chosen = new List<Region>(wanted);
        double minSquared = separation * separation;

        foreach (Region candidate in candidates)
        {
            if (chosen.Count >= wanted) break;

            bool tooClose = false;
            for (int i = 0; i < chosen.Count; i++)
            {
                double distanceSquared = world.DistanceSquared(
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
            world.Names.ForCulture(cultureId),
            languageSeed: world.Names.LanguageSeedFor(cultureId),
            values: CultureValues.Roll(rng),
            government: (GovernmentForm)rng.NextInt(Enum.GetValues(typeof(GovernmentForm)).Length));
        world.Cultures.Add(culture);

        EntityId civId = world.Civilizations.NextId;
        var civilization = new Civilization(
            civId,
            cultureId,
            world.Names.ForCivilization(civId, culture),
            year)
        {
            // The founding year runs before the first tick, so the crown system has not settled
            // anything yet. A realm founded this morning is governed by its people and nothing else.
            EffectiveValues = culture.Values,
        };
        world.Civilizations.Add(civilization);

        SiteChoice site = SiteSelection.Best(world, homeland, SitesPerAxis);
        Settlement capital = FoundSettlement(
            world, civilization, culture, homeland, site, year, population: 120, rng);

        capital.IsCapital = true;
        civilization.CapitalId = capital.Id;

        homeland.Owner = civId;
        civilization.TerritoryRegionIds.Add(homeland.Id);

        world.Chronicle.Record(
            year, EventKind.CivilizationFounded, civId, location: capital.Id);

        // The homeland is claimed like any other region. Recording it means every acre a realm
        // ever held entered the chronicle by an event, so territory at any year can be replayed
        // from the log rather than inferred from where a realm happened to put its first town.
        // Routine: ownership is the fact; "extended its reach" is not history worth a spine line.
        world.Chronicle.Record(
            year, EventKind.RegionClaimed, homeland.Id, obj: civId,
            significance: Significance.Routine);

        Figure founder = Houses.FoundDynasty(world, civilization, culture, year, rng);
        Houses.Enthrone(world, civilization, culture, founder, year, "by the founding of the realm");
    }

    /// <summary>Creates a settlement, wires it to its civilization, and records the founding.</summary>
    /// <param name="from">
    /// The settlement the party came out of, where one did. A capital founded at the beginning of
    /// the world came from nowhere, and says so by leaving this null.
    /// </param>
    /// <param name="leader">Whoever led them out, where the court could spare somebody.</param>
    public static Settlement FoundSettlement(
        WorldState world,
        Civilization civilization,
        Culture culture,
        Region region,
        SiteChoice site,
        int year,
        int population,
        IRng rng,
        Settlement? from = null,
        Figure? leader = null)
    {
        EntityId id = world.Settlements.NextId;

        var settlement = new Settlement(
            id,
            civilization.Id,
            region.Id,
            world.Names.ForSettlement(id, culture),
            site.At.X,
            site.At.Z,
            year,
            population)
        {
            FoundedBy = civilization.Id,
            PeakPopulation = population,
            Site = site.Character,
        };

        world.Settlements.Add(settlement);
        civilization.SettlementIds.Add(id);

        var data = new DetMap<string, string>();
        if (from is not null)
        {
            data["settlers"] = population.ToString(CultureInfo.InvariantCulture);
            data["from"] = from.Name;
        }

        // Read off the character rather than passed in beside it, because the character is already
        // the answer: a site is recorded as a mine when the party sent for ore found ore, and a
        // party that went for ore and settled on ordinary ground has nothing to claim about why.
        string? purpose = SiteCharacters.Purpose(site.Character);
        if (purpose is not null) data["purpose"] = purpose;

        world.Chronicle.Record(
            year,
            EventKind.SettlementFounded,
            id,
            obj: civilization.Id,
            location: region.Id,
            extra: leader is null ? null : new[] { leader.Id },
            data: data.Count == 0 ? null : data);

        // The leader of the party governs what they founded, whatever its size. The office is
        // normally reserved for towns, and a colony of seventy is not one — but somebody led these
        // people here, and they are who a chronicle names when it writes about the place. When
        // they die the colony is left to itself until it grows enough to warrant a court
        // appointment, which is the honest shape of how a frontier settlement is administered.
        if (leader is not null)
        {
            Offices.Grant(
                world,
                civilization,
                culture,
                leader,
                OfficeKind.Governor,
                settlement.Id,
                EntityId.None,
                "by the founding of the town",
                year);
        }

        return settlement;
    }

}
