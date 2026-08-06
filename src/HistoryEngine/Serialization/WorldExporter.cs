using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.Systems;
using HistoryEngine.Terrain;
using HistoryEngine.World;

namespace HistoryEngine.Serialization;

/// <summary>
/// Turns a simulated <see cref="WorldState"/> into the export document.
/// </summary>
public static class WorldExporter
{
    /// <summary>Reported in the export so a world file records which engine produced it.</summary>
    public const string EngineVersion = "0.1.0";

    public static WorldExport Build(
        WorldState world,
        Simulator simulator,
        long simulationSamples,
        CountingTerrainSampler? counter = null)
    {
        // Built before the sample stats are read, so raster cost is attributed to the raster.
        ExportRaster raster = TerrainRaster.Build(world, world.Config.MapRasterResolution);

        long rasterSamples = counter is null
            ? 0
            : Math.Max(0, counter.SampleCount - simulationSamples);

        IReadOnlyList<ExportEvent> events = BuildEvents(world);

        return new WorldExport(
            SchemaVersion: WorldExport.CurrentSchemaVersion,
            Meta: new ExportMeta(
                Seed: world.Config.Seed,
                ConfigHash: world.Config.ConfigHash,
                SystemOrderHash: simulator.SystemOrderHash,
                SystemOrder: simulator.SystemOrder,
                EngineVersion: EngineVersion,
                NarrationSyntaxVersion: Narration.SyntaxVersion,
                StartYear: world.StartYear,
                EndYear: world.EndYear,
                YearsSimulated: world.Config.Years,
                EventCount: world.Chronicle.Count,
                TerrainSampling: new ExportSampleStats(
                    SimulationSamples: simulationSamples,
                    RasterSamples: rasterSamples,
                    EstimatedGameSecondsSimulation:
                        simulationSamples * CountingTerrainSampler.GameSampleCostMs / 1000.0,
                    EstimatedGameSecondsRaster:
                        rasterSamples * CountingTerrainSampler.GameSampleCostMs / 1000.0)),
            World: BuildWorld(world, raster),
            Regions: BuildRegions(world),
            Cultures: BuildCultures(world),
            Civilizations: BuildCivilizations(world),
            Settlements: BuildSettlements(world),
            Figures: BuildFigures(world),
            Events: events,
            Indices: BuildIndices(world, events),
            Narration: ToDictionary(Narration.Templates));
    }

    /// <summary>Serialises to the canonical compact form.</summary>
    public static string ToJson(WorldExport export, bool readable = false) =>
        JsonSerializer.Serialize(export, readable ? Json.Readable : Json.Compact);

    public static WorldExport FromJson(string json) =>
        JsonSerializer.Deserialize<WorldExport>(json, Json.Compact)
        ?? throw new InvalidOperationException("World export deserialised to null.");

    /// <summary>
    /// SHA-256 of the canonical serialisation.
    /// </summary>
    /// <remarks>
    /// The value the golden determinism test pins. Because the export carries no timestamp and
    /// every collection in it has a defined order, identical inputs must produce an identical
    /// digest — and any accidental nondeterminism anywhere in the engine surfaces here rather
    /// than as a subtly different history nobody notices.
    /// </remarks>
    public static string Fingerprint(WorldExport export)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(ToJson(export));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static ExportWorld BuildWorld(WorldState world, ExportRaster raster) =>
        new(
            MinX: world.Terrain.Bounds.MinX,
            MinZ: world.Terrain.Bounds.MinZ,
            Width: world.Terrain.Bounds.Width,
            Height: world.Terrain.Bounds.Height,
            RegionSize: world.Config.RegionSize,
            TerrainStride: world.Config.TerrainStride,
            Capabilities: world.Terrain.Capabilities.ToString(),
            Raster: raster);

    private static List<ExportRegion> BuildRegions(WorldState world)
    {
        var list = new List<ExportRegion>(world.Regions.Count);

        foreach (Region region in world.Regions)
        {
            list.Add(new ExportRegion(
                Id: region.Id,
                Name: world.NameOf(region.Id),
                MinX: region.Bounds.MinX,
                MinZ: region.Bounds.MinZ,
                Width: region.Bounds.Width,
                Height: region.Bounds.Height,
                Biome: region.Biome,
                Fertility: region.Fertility,
                Habitability: region.Habitability,
                MeanHeight: region.MeanHeight,
                IsLand: region.IsLand,
                HasRiver: region.HasRiver,
                IsCoastal: region.IsCoastal,
                Owner: OrNull(region.Owner),
                Adjacent: region.AdjacentRegions.ToArray()));
        }

        return list;
    }

    private static List<ExportCulture> BuildCultures(WorldState world)
    {
        var list = new List<ExportCulture>(world.Cultures.Count);

        foreach (Culture culture in world.Cultures)
        {
            list.Add(new ExportCulture(
                Id: culture.Id,
                Name: culture.Name,
                Government: culture.Government,
                RulerTitle: culture.RulerTitle,
                Aggression: culture.Values.Aggression,
                Expansionism: culture.Values.Expansionism,
                Piety: culture.Values.Piety,
                Tradition: culture.Values.Tradition,
                Mercantile: culture.Values.Mercantile));
        }

        return list;
    }

    private static List<ExportCivilization> BuildCivilizations(WorldState world)
    {
        var list = new List<ExportCivilization>(world.Civilizations.Count);

        foreach (Civilization civilization in world.Civilizations)
        {
            list.Add(new ExportCivilization(
                Id: civilization.Id,
                Name: civilization.Name,
                CultureId: civilization.CultureId,
                FoundedYear: civilization.FoundedYear,
                EndedYear: civilization.EndedYear,
                CapitalId: OrNull(civilization.CapitalId),
                CurrentRulerId: OrNull(civilization.CurrentRulerId),
                Population: civilization.Population,
                PeakPopulation: civilization.PeakPopulation,
                RulerIds: civilization.RulerIds.ToArray(),
                SettlementIds: civilization.SettlementIds.ToArray(),
                TerritoryRegionIds: civilization.TerritoryRegionIds.ToArray()));
        }

        return list;
    }

    private static List<ExportSettlement> BuildSettlements(WorldState world)
    {
        var list = new List<ExportSettlement>(world.Settlements.Count);

        foreach (Settlement settlement in world.Settlements)
        {
            list.Add(new ExportSettlement(
                Id: settlement.Id,
                Name: settlement.Name,
                CivilizationId: settlement.CivilizationId,
                FoundedBy: OrNull(settlement.FoundedBy),
                RegionId: settlement.RegionId,
                X: settlement.X,
                Z: settlement.Z,
                Tier: settlement.Tier,
                Population: settlement.Population,
                PeakPopulation: settlement.PeakPopulation,
                FoundedYear: settlement.FoundedYear,
                AbandonedYear: settlement.AbandonedYear,
                IsCapital: settlement.IsCapital,
                IsFortified: settlement.IsFortified));
        }

        return list;
    }

    private static List<ExportFigure> BuildFigures(WorldState world)
    {
        var list = new List<ExportFigure>(world.Figures.Count);

        foreach (Figure figure in world.Figures)
        {
            var titles = new List<ExportTitle>(figure.Titles.Count);
            foreach (TitleHolding holding in figure.Titles)
            {
                titles.Add(new ExportTitle(
                    holding.Title, holding.CivilizationId, holding.FromYear, holding.ToYear));
            }

            list.Add(new ExportFigure(
                Id: figure.Id,
                Name: figure.Name,
                CivilizationId: figure.CivilizationId,
                CultureId: figure.CultureId,
                BirthYear: figure.BirthYear,
                DeathYear: figure.DeathYear,
                DeathCause: figure.DeathCause,
                BirthSettlementId: OrNull(figure.BirthSettlementId),
                Titles: titles,
                ParentIds: figure.ParentIds.ToArray(),
                SpouseIds: figure.SpouseIds.ToArray()));
        }

        return list;
    }

    private static List<ExportEvent> BuildEvents(WorldState world)
    {
        var list = new List<ExportEvent>(world.Chronicle.Count);

        foreach (HistoryEvent entry in world.Chronicle.Events)
        {
            list.Add(new ExportEvent(
                Id: entry.Id,
                Year: entry.Year,
                Kind: entry.Kind,
                Subject: OrNull(entry.Subject),
                Object: OrNull(entry.Object),
                Location: OrNull(entry.Location),
                Extra: entry.Extra is null ? null : new List<EntityId>(entry.Extra),
                Data: entry.Data is null ? null : ToDictionary(entry.Data)));
        }

        return list;
    }

    /// <summary>
    /// Builds the denormalised lookups the viewer navigates by.
    /// </summary>
    /// <remarks>
    /// Every dictionary here is sorted with an explicit ordinal comparer.
    /// <see cref="SortedDictionary{TKey,TValue}"/> keyed by string would otherwise use
    /// <see cref="Comparer{T}.Default"/>, which for strings is culture-sensitive — so the export's
    /// byte layout would depend on the machine's locale, and the determinism test would pass here
    /// and fail on someone else's laptop.
    /// </remarks>
    private static ExportIndices BuildIndices(WorldState world, IReadOnlyList<ExportEvent> events)
    {
        var byEntity = new Dictionary<EntityId, List<int>>();
        var byYear = new SortedDictionary<int, List<int>>();
        var countsByKind = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (HistoryEvent entry in world.Chronicle.Events)
        {
            foreach (EntityId reference in entry.References())
            {
                if (!byEntity.TryGetValue(reference, out List<int>? bucket))
                {
                    bucket = new List<int>();
                    byEntity[reference] = bucket;
                }

                // An event mentioning the same entity twice should appear once in its page.
                if (bucket.Count == 0 || bucket[bucket.Count - 1] != entry.Id)
                {
                    bucket.Add(entry.Id);
                }
            }

            if (!byYear.TryGetValue(entry.Year, out List<int>? yearBucket))
            {
                yearBucket = new List<int>();
                byYear[entry.Year] = yearBucket;
            }

            yearBucket.Add(entry.Id);

            string kind = entry.Kind.ToString();
            countsByKind[kind] = countsByKind.TryGetValue(kind, out int count) ? count + 1 : 1;
        }

        var entityIndex = new SortedDictionary<string, int[]>(StringComparer.Ordinal);
        foreach (KeyValuePair<EntityId, List<int>> pair in byEntity)
        {
            entityIndex[pair.Key.ToString()] = pair.Value.ToArray();
        }

        var yearIndex = new SortedDictionary<string, int[]>(StringComparer.Ordinal);
        foreach (KeyValuePair<int, List<int>> pair in byYear)
        {
            yearIndex[pair.Key.ToString(CultureInfo.InvariantCulture)] = pair.Value.ToArray();
        }

        return new ExportIndices(entityIndex, yearIndex, countsByKind);
    }

    private static EntityId? OrNull(EntityId id) => id.IsNone ? null : id;

    private static SortedDictionary<string, string> ToDictionary(DetMap<string, string> map)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> pair in map)
        {
            result[pair.Key] = pair.Value;
        }

        return result;
    }
}
