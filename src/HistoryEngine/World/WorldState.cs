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
        Flavour = WorldFlavour.From(config.Seed, names);

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
        Harvest = new HarvestModel(config);
        Outbreaks = new List<Outbreak>();
        PendingHardships = new List<PendingHardship>();
        Series = new SeriesLog();
        Docket = new Docket(config.Calendar);
        Now = Stamp.Opening(config.StartYear);
    }

    public WorldConfig Config { get; }

    /// <summary>The simulation's only view of terrain.</summary>
    public TerrainAtlas Terrain { get; }

    /// <summary>
    /// The ground a road can be cut through, derived once and only if a road is ever built.
    /// </summary>
    /// <remarks>
    /// Lazy for the same reason hydrology is: a world in which no link ever earns a road — a
    /// short run, or an entirely maritime one — should not pay to read the planes. Once built it is
    /// held for the run, because the ground does not move and every road asks it the same
    /// questions. It samples no terrain; see <see cref="Roadbed"/>.
    /// </remarks>
    public Roadbed Roadbed => _roadbed ??= Roadbed.Build(Terrain);

    private Roadbed? _roadbed;

    /// <summary>
    /// The run's root RNG. Systems must <see cref="IRng.Fork"/> from it rather than draw from
    /// it directly, so that one system's consumption cannot shift another's.
    /// </summary>
    public IRng Root { get; }

    public INameGenerator Names { get; }

    /// <summary>
    /// The world's own name, and whether it is a planet or a moon.
    /// </summary>
    /// <remarks>
    /// Derived from the seed at construction, independently of <see cref="Root"/>, so adding
    /// this flavour cannot shift a founding, a battle, or any other name. The seed remains the
    /// thing you type to reproduce the history; this is the thing you read to recognise it.
    /// </remarks>
    public WorldFlavour Flavour { get; }

    public Chronicle Chronicle { get; }

    /// <summary>
    /// The measures that move, sampled once a year.
    /// </summary>
    /// <remarks>
    /// The chronicle records what happened; this records what it left the world looking like. The
    /// two answer different questions — "why is this realm exhausted" is in the events, and "how
    /// exhausted, in which decade" is here — and neither is recoverable from the other.
    /// </remarks>
    public SeriesLog Series { get; }

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

    /// <summary>
    /// Episodes recorded this year whose effect on the people living through them is not yet
    /// resolved.
    /// </summary>
    /// <remarks>
    /// <para>The buffered-intent escape hatch <see cref="Systems.ISystem"/> describes, taken for
    /// the reason it describes. A famine is recorded by <c>population</c> and a plague arrives
    /// under <c>plague</c>, both of which run near the top of the year; whether a given person was
    /// at home to suffer it is not known until <c>travel</c> has run, near the bottom. Resolving
    /// the consequence where the episode is written would therefore have to ignore the year's own
    /// journeys, and put a famine memory on the page of a man who demonstrably spent that year on
    /// the road.</para>
    ///
    /// <para>Held here rather than on the system for the same reason <see cref="Outbreaks"/> is:
    /// state on a system instance is invisible to the <c>Advance</c>-versus-<c>Run</c> determinism
    /// test. The list is drained every year and is empty between steps, so it is not exported.</para>
    /// </remarks>
    public List<PendingHardship> PendingHardships { get; }

    /// <summary>
    /// Work scheduled for a date, waiting for it.
    /// </summary>
    /// <remarks>
    /// Held here rather than on the system that scheduled it, for the reason
    /// <see cref="Outbreaks"/> gives and one more: state on a system instance is invisible to the
    /// <c>Advance</c>-versus-<c>Run</c> split determinism test, which is the strongest test in the
    /// suite precisely because everything the simulation knows has to be reachable from this
    /// object.
    /// </remarks>
    public Docket Docket { get; }

    /// <summary>The instant currently being simulated.</summary>
    /// <remarks>
    /// Set by the tick loop and read by everything else. Between two ticks it holds the opening of
    /// the next step to run, so <see cref="Systems.Simulator.Run"/> and
    /// <see cref="Systems.Simulator.Advance"/> resume from the same place.
    /// </remarks>
    public Stamp Now { get; set; }

    /// <summary>
    /// The year currently being simulated.
    /// </summary>
    /// <remarks>
    /// Derived from <see cref="Now"/> rather than stored, so there is one clock and not two. Kept
    /// as a property because the great majority of this engine reasons in years and should go on
    /// doing so — a figure's age, a harvest, a truce — and rewriting those call sites to say
    /// <c>Now.Year</c> would be churn dressed as progress.
    /// </remarks>
    public int Year => Now.Year;

    public int StartYear => Config.StartYear;

    public int EndYear => Config.StartYear + Config.Years - 1;

    /// <summary>Who is within reach of whom, by realm, as of <see cref="_reachYear"/>.</summary>
    private readonly DetMap<EntityId, DetMap<EntityId, double>> _reach = new();

    /// <summary>The year <see cref="_reach"/> describes. Before any is published, no year.</summary>
    private int _reachYear = int.MinValue;

    /// <summary>
    /// Publishes the year's contact map, so systems after the one that paid for it can read it.
    /// </summary>
    /// <remarks>
    /// <para>Proximity is every settlement of one realm against every settlement of another, and it
    /// is the most expensive question the engine asks each year. Diplomacy already resolves it for
    /// every realm; cultural drift needs the same answer a few systems later, and recomputing it
    /// there doubled the cost of a long run for an identical result.</para>
    ///
    /// <para>Cached against the year it describes rather than kept indefinitely: a reader that asks
    /// in a later year — or in a run whose system list has no diplomacy in it — is told nothing and
    /// falls back to computing its own, which keeps this an optimisation rather than a dependency
    /// between two systems.</para>
    /// </remarks>
    public void PublishReach(
        int year, IReadOnlyList<Civilization> civilizations, IReadOnlyList<DetMap<EntityId, double>> reach)
    {
        _reach.Clear();
        for (int i = 0; i < civilizations.Count && i < reach.Count; i++)
        {
            _reach[civilizations[i].Id] = reach[i];
        }

        _reachYear = year;
    }

    /// <summary>
    /// The realms within reach of this one as resolved this year, or null if nobody published any.
    /// </summary>
    public DetMap<EntityId, double>? ReachOf(EntityId civilizationId, int year) =>
        _reachYear == year && _reach.TryGetValue(civilizationId, out DetMap<EntityId, double>? found)
            ? found
            : null;

    /// <summary>Distance respecting this world's east/west boundary condition.</summary>
    public double Distance(double x1, double z1, double x2, double z2) =>
        Config.Bounds.Distance(x1, z1, x2, z2, Config.EastWestPeriodic);

    /// <summary>Squared distance respecting this world's east/west boundary condition.</summary>
    public double DistanceSquared(double x1, double z1, double x2, double z2) =>
        Config.Bounds.DistanceSquared(x1, z1, x2, z2, Config.EastWestPeriodic);

    /// <summary>The culture of a given civilization.</summary>
    public Culture CultureOf(Civilization civilization) => Cultures[civilization.CultureId];

    /// <summary>The culture of a given figure.</summary>
    public Culture CultureOf(Figure figure) => Cultures[figure.CultureId];

    /// <summary>
    /// The values a realm decides by: its people, whoever governs them, and its recent past.
    /// </summary>
    /// <remarks>
    /// <para><b>Use this wherever a system rolls a decision the realm is taking</b> — declaring
    /// war, sacking a town, founding a colony, raising walls, commissioning something. Use
    /// <see cref="CultureOf(Civilization)"/> directly where the thing being modelled is a standing
    /// property of the people instead: how they farm, how they resist conversion, how attached
    /// they are to a dying town, and above all which succession law they follow.</para>
    ///
    /// <para>That line is not stylistic. A ruler who could move Tradition could change how their
    /// own successor is chosen, mid-reign, by having opinions — agnatic one year and absolute the
    /// next. Constitutional change is a real thing to model one day and it is not a side effect of
    /// a personality.</para>
    /// </remarks>
    public CultureValues ValuesFor(Civilization civilization) => civilization.EffectiveValues;

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

    /// <summary>
    /// Writes a person's name into a narration payload under <paramref name="key"/>, if there is
    /// a person. Reports whether it wrote, so a caller can index the event against them too.
    /// </summary>
    /// <remarks>
    /// <para><b>Why a data string and not a slot.</b> The three entity slots on an event are the
    /// three things a template can turn into a link, and on the events that most want an actor
    /// they are already spent: a battle is subject, its victor is object, the town it was fought
    /// over is location. Rather than displace one of those — every one of which the viewer's
    /// filters and indices depend on — the actor's name goes in as text, which is what
    /// <see cref="Events.EventKind.StateFaithChanged"/> has always done for the ruler who
    /// converted a realm.</para>
    ///
    /// <para>Nothing is lost by it except a clickable word. Callers pass the same id into the
    /// event's <c>extra</c>, which is what drives the per-entity index — so the battle still
    /// appears on the commander's page, and the reader who wants them can get there from the
    /// page rather than from the sentence.</para>
    ///
    /// <para>Absent people are absent rather than "unknown": an army that marched without its
    /// king should read as though nobody in particular led it, and the optional segments in the
    /// templates drop the clause entirely when this writes nothing.</para>
    /// </remarks>
    public bool NamePerson(DetMap<string, string> data, string key, EntityId figureId)
    {
        if (!Figures.Contains(figureId)) return false;

        data[key] = Figures[figureId].FullName;
        return true;
    }

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

    /// <summary>The faith a figure holds, or <see cref="EntityId.None"/> if none has reached them.</summary>
    public EntityId FaithOf(Figure figure) => figure.ReligionId;

    /// <summary>
    /// Where a figure actually is: their recorded residence, or their realm's seat.
    /// </summary>
    /// <remarks>
    /// <para><b>A resolver rather than a stored answer, because the stored one goes stale in ways
    /// no writer can be expected to chase.</b> A residence can be abandoned, taken in a war, or
    /// outlive the realm that held it, and every system that moves a settlement between owners
    /// would otherwise have to know about everyone living in it. Falling back to the capital
    /// keeps the invariant that matters — a living figure is somewhere their own realm holds —
    /// without spreading residence bookkeeping across the whole engine.</para>
    ///
    /// <para>The recorded field is therefore best-effort and this is the only thing worth reading.
    /// Exposure — who a disaster or a sack can reach — goes through here, so a governor whose town
    /// has just changed hands is at court rather than in a city that is no longer theirs.</para>
    /// </remarks>
    public EntityId ResidenceOf(Figure figure)
    {
        if (Settlements.Contains(figure.ResidenceSettlementId))
        {
            Settlement recorded = Settlements[figure.ResidenceSettlementId];
            if (recorded.IsActive && recorded.CivilizationId == figure.CivilizationId)
            {
                return recorded.Id;
            }
        }

        if (Civilizations.Contains(figure.CivilizationId))
        {
            Civilization realm = Civilizations[figure.CivilizationId];
            if (Settlements.Contains(realm.CapitalId) && Settlements[realm.CapitalId].IsActive)
            {
                return realm.CapitalId;
            }
        }

        return EntityId.None;
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
