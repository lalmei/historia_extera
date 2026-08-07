using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.Naming;
using HistoryEngine.Terrain;

namespace HistoryEngine.World;

/// <summary>
/// The entire simulated world: terrain, entities, and the chronicle.
/// </summary>
/// <remarks>
/// <para>Systems receive this and mutate it. There is no separate "model" and "state" split
/// because there is no concurrency to protect against — the tick loop is strictly sequential,
/// which is itself a determinism requirement rather than a simplification.</para>
///
/// <para><b>Terrain is exposed as <see cref="TerrainAtlas"/>, never as
/// <see cref="ITerrainSampler"/>.</b> The atlas is the caching, interpolating, budget-bounded
/// consumer described in its own docs, and this property is the only handle systems get.
/// <c>TerrainDisciplineTests</c> fails the build if anything under <c>Systems/</c> so much as
/// mentions the sampler interface.</para>
/// </remarks>
public sealed class WorldState
{
    public WorldState(
        WorldConfig config,
        TerrainAtlas terrain,
        IRng root,
        INameGenerator names)
    {
        Config = config;
        Terrain = terrain;
        Root = root;
        Names = names;

        Cultures = new EntityTable<Culture>(EntityKind.Culture);
        Civilizations = new EntityTable<Civilization>(EntityKind.Civilization);
        Settlements = new EntityTable<Settlement>(EntityKind.Settlement);
        Figures = new EntityTable<Figure>(EntityKind.Figure);
        Regions = new EntityTable<Region>(EntityKind.Region);

        Chronicle = new Chronicle();
        Harvest = new HarvestModel(config.Seed);
        Year = config.StartYear;
    }

    public WorldConfig Config { get; }

    /// <summary>The simulation's only view of terrain.</summary>
    public TerrainAtlas Terrain { get; }

    /// <summary>
    /// The run's root RNG. Systems must <see cref="IRng.Fork"/> from it rather than draw from
    /// it directly, so that one system's consumption cannot shift another's.
    /// </summary>
    public IRng Root { get; }

    public INameGenerator Names { get; }

    public Chronicle Chronicle { get; }

    /// <summary>
    /// How each year went, per region.
    /// </summary>
    /// <remarks>
    /// Stateless and seeded, so it can be queried for any region and year without keeping history.
    /// It is what makes carrying capacity move, and therefore what makes decline, abandonment and
    /// the fall of civilizations reachable at all.
    /// </remarks>
    public HarvestModel Harvest { get; }

    public EntityTable<Culture> Cultures { get; }

    public EntityTable<Civilization> Civilizations { get; }

    public EntityTable<Settlement> Settlements { get; }

    public EntityTable<Figure> Figures { get; }

    public EntityTable<Region> Regions { get; }

    /// <summary>The year currently being simulated.</summary>
    public int Year { get; set; }

    public int StartYear => Config.StartYear;

    public int EndYear => Config.StartYear + Config.Years - 1;

    /// <summary>The culture of a given civilization.</summary>
    public Culture CultureOf(Civilization civilization) => Cultures[civilization.CultureId];

    /// <summary>
    /// Display name for any entity id. Backs <see cref="Narration.Render"/> and the CLI.
    /// </summary>
    /// <remarks>
    /// Returns the id itself for anything unresolvable rather than throwing, because a
    /// chronicle that cannot be printed is harder to debug than one with an odd name in it.
    /// </remarks>
    public string NameOf(EntityId id) => id.Kind switch
    {
        EntityKind.Culture when Cultures.Contains(id) => Cultures[id].Name,
        EntityKind.Civilization when Civilizations.Contains(id) => Civilizations[id].Name,
        EntityKind.Settlement when Settlements.Contains(id) => Settlements[id].Name,
        EntityKind.Figure when Figures.Contains(id) => Figures[id].Name,
        // Regions are generated in bulk before any culture exists, so unlike other
        // entities they have no owning culture to name them — their labels come from
        // biome instead.
        EntityKind.Region when Regions.Contains(id) => Names.ForRegion(id, Regions[id].Biome),
        _ => id.ToString(),
    };

    /// <summary>Renders one event to prose using this world's names.</summary>
    public string Narrate(HistoryEvent entry) => Narration.Render(entry, NameOf);

    /// <summary>Active civilizations, in id order.</summary>
    public IEnumerable<Civilization> ActiveCivilizations()
    {
        foreach (Civilization civ in Civilizations)
        {
            if (civ.IsActive) yield return civ;
        }
    }

    /// <summary>Active settlements of one civilization, in id order.</summary>
    public IEnumerable<Settlement> ActiveSettlementsOf(Civilization civilization)
    {
        // Iterates the civ's own id list, which is append-ordered and therefore stable.
        foreach (EntityId id in civilization.SettlementIds)
        {
            Settlement settlement = Settlements[id];
            if (settlement.IsActive) yield return settlement;
        }
    }
}
