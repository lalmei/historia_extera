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
        Dynasties = new EntityTable<Dynasty>(EntityKind.Dynasty);
        Wars = new EntityTable<War>(EntityKind.War);
        Battles = new EntityTable<Battle>(EntityKind.Battle);
        Regions = new EntityTable<Region>(EntityKind.Region);
        Religions = new EntityTable<Religion>(EntityKind.Religion);
        Artifacts = new EntityTable<Artifact>(EntityKind.Artifact);
        TradeRoutes = new EntityTable<TradeRoute>(EntityKind.TradeRoute);
        HolySites = new EntityTable<HolySite>(EntityKind.HolySite);

        Chronicle = new Chronicle();
        Harvest = new HarvestModel(config.Seed);
        Outbreaks = new List<Outbreak>();
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

    /// <summary>
    /// The ruling houses.
    /// </summary>
    /// <remarks>
    /// A table of their own rather than a field on <see cref="Civilization"/>, because a house
    /// outlives the reign that made it and can hold a different throne afterwards — or none.
    /// </remarks>
    public EntityTable<Dynasty> Dynasties { get; }

    /// <summary>Every war ever declared, running or settled.</summary>
    public EntityTable<War> Wars { get; }

    /// <summary>Every battle fought, in the order they were fought.</summary>
    public EntityTable<Battle> Battles { get; }

    public EntityTable<Region> Regions { get; }

    /// <summary>Every faith ever preached, living or forgotten.</summary>
    public EntityTable<Religion> Religions { get; }

    /// <summary>Every made thing the chronicle follows, held or lost.</summary>
    public EntityTable<Artifact> Artifacts { get; }

    /// <summary>Every commercial connection ever established, including closed routes.</summary>
    public EntityTable<TradeRoute> TradeRoutes { get; }

    /// <summary>Every house of worship and independent sacred place ever established.</summary>
    public EntityTable<HolySite> HolySites { get; }

    /// <summary>
    /// Epidemics currently running.
    /// </summary>
    /// <remarks>
    /// A plain list rather than an entity table, because an outbreak is not something history
    /// refers to by name after the fact the way a war or a house is — it is a state the plague
    /// system carries between years. What survives it is the events it wrote.
    /// </remarks>
    public List<Outbreak> Outbreaks { get; }

    /// <summary>The year currently being simulated.</summary>
    public int Year { get; set; }

    public int StartYear => Config.StartYear;

    public int EndYear => Config.StartYear + Config.Years - 1;

    /// <summary>The culture of a given civilization.</summary>
    public Culture CultureOf(Civilization civilization) => Cultures[civilization.CultureId];

    /// <summary>The culture of a given figure.</summary>
    public Culture CultureOf(Figure figure) => Cultures[figure.CultureId];

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
        // The styled name, so a chronicle distinguishes the second Spysl from the first.
        EntityKind.Figure when Figures.Contains(id) => Figures[id].FullName,
        // Houses are spoken of as "the Vethric", so the article and the plural live in the
        // narration template rather than in the stored name.
        EntityKind.Dynasty when Dynasties.Contains(id) => Dynasties[id].Name,
        // Wars and battles carry the whole phrase — "Second Siege of Ekallatograd" — because
        // unlike every other name here it is composed rather than generated, and composing it
        // once at creation is what keeps two references to the same battle worded identically.
        EntityKind.War when Wars.Contains(id) => Wars[id].Name,
        EntityKind.Battle when Battles.Contains(id) => Battles[id].Name,
        // Regions are generated in bulk before any culture exists, so unlike other
        // entities they have no owning culture to name them — their labels come from
        // biome instead.
        EntityKind.Region when Regions.Contains(id) => Names.ForRegion(id, Regions[id].Biome),
        // Faiths are spoken of as "the Semnoi", like houses: the article lives in the template.
        EntityKind.Religion when Religions.Contains(id) => Religions[id].Name,
        // Artifact names are composed at creation — "the Crown of Aigionanvos" — so that two
        // references to the same object are worded identically, as with wars and battles.
        EntityKind.Artifact when Artifacts.Contains(id) => Artifacts[id].Name,
        EntityKind.TradeRoute when TradeRoutes.Contains(id) =>
            $"{NameOf(TradeRoutes[id].SettlementAId)}–{NameOf(TradeRoutes[id].SettlementBId)} route",
        EntityKind.HolySite when HolySites.Contains(id) => HolySites[id].Name,
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

    /// <summary>Wars still being fought, in the order they were declared.</summary>
    public IEnumerable<War> ActiveWars()
    {
        foreach (War war in Wars)
        {
            if (war.IsActive) yield return war;
        }
    }

    /// <summary>Trade routes still carrying traffic, in founding order.</summary>
    public IEnumerable<TradeRoute> ActiveTradeRoutes()
    {
        foreach (TradeRoute route in TradeRoutes)
        {
            if (route.IsActive) yield return route;
        }
    }

    /// <summary>Faiths still followed somewhere, in the order they were founded.</summary>
    public IEnumerable<Religion> ActiveReligions()
    {
        foreach (Religion religion in Religions)
        {
            if (religion.IsActive) yield return religion;
        }
    }

    /// <summary>
    /// The faith of a realm: whatever its seat of government followed when the year began.
    /// </summary>
    /// <remarks>
    /// Stored on the realm and synced from the capital once a year by the religion system, rather
    /// than read live from the capital on every call. Two reasons: a realm whose seat is taken in
    /// war does not change religion the instant the walls fall, and every consumer asking the same
    /// question inside one year gets the same answer regardless of what has converted since.
    /// </remarks>
    public EntityId FaithOf(Civilization civilization) => civilization.StateReligionId;

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
