using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.Naming;
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
    public const string EngineVersion = "0.9.1";

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
            Dynasties: BuildDynasties(world),
            Settlements: BuildSettlements(world),
            TradeRoutes: BuildTradeRoutes(world),
            Figures: BuildFigures(world),
            Wars: BuildWars(world),
            Battles: BuildBattles(world),
            Religions: BuildReligions(world),
            HolySites: BuildHolySites(world),
            Artifacts: BuildArtifacts(world),
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
    /// SHA-256 of the canonical serialisation, excluding every number that versions the file
    /// rather than describes the history.
    /// </summary>
    /// <remarks>
    /// <para>The value the golden determinism test pins. Because the export carries no timestamp
    /// and every collection in it has a defined order, identical inputs must produce an identical
    /// digest — and any accidental nondeterminism anywhere in the engine surfaces here rather
    /// than as a subtly different history nobody notices.</para>
    ///
    /// <para><b>The digest answers one question: did the history for this seed change?</b> So the
    /// three numbers that describe the <em>file</em> rather than the world are cleared first —
    /// <see cref="ExportMeta.EngineVersion"/>, <see cref="WorldExport.SchemaVersion"/> and
    /// <see cref="ExportMeta.NarrationSyntaxVersion"/>. None of them is part of a history. Leaving
    /// one in means a release, a new exported field, or a template-grammar change fails the golden
    /// test and is answered by regenerating the golden — which is exactly the reflex the test
    /// exists to prevent: regenerate often enough for reasons that are fine and you will
    /// regenerate the one time it was not.</para>
    ///
    /// <para>Only <see cref="ExportMeta.EngineVersion"/> was excluded originally, and the cost of
    /// the omission was visible: four consecutive milestones each bumped the schema and each
    /// regenerated this digest, so the golden moved five times for four real changes in
    /// behaviour and nobody could tell by looking which move was which. All three still travel in
    /// the export, where a viewer reads them to decide whether it understands the file.</para>
    ///
    /// <para>Adding a field to the export still moves the digest, and should: a world that carries
    /// new facts is a new export even when the simulation behind it is unchanged. What no longer
    /// moves it is renumbering the contract that describes those facts.</para>
    /// </remarks>
    public static string Fingerprint(WorldExport export)
    {
        WorldExport world = export with
        {
            SchemaVersion = 0,
            Meta = export.Meta with
            {
                EngineVersion = string.Empty,
                NarrationSyntaxVersion = 0,
            },
        };

        byte[] bytes = Encoding.UTF8.GetBytes(ToJson(world));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static ExportWorld BuildWorld(WorldState world, ExportRaster raster)
    {
        var rivers = new List<ExportRiver>();
        foreach (Hydrology.RiverSegment reach in world.Terrain.Hydrology.RiverSegments())
        {
            rivers.Add(new ExportRiver(
                reach.FromX, reach.FromZ, reach.ToX, reach.ToZ, reach.Strength));
        }

        return new ExportWorld(
            MinX: world.Terrain.Bounds.MinX,
            MinZ: world.Terrain.Bounds.MinZ,
            Width: world.Terrain.Bounds.Width,
            Height: world.Terrain.Bounds.Height,
            RegionSize: world.Config.RegionSize,
            TerrainStride: world.Config.TerrainStride,
            Capabilities: world.Terrain.Capabilities.ToString(),
            Raster: raster,
            Rivers: rivers);
    }

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

    /// <summary>How many example names per category the lexicon carries.</summary>
    private const int LexiconSampleCount = 6;

    private static List<ExportCulture> BuildCultures(WorldState world)
    {
        var list = new List<ExportCulture>(world.Cultures.Count);
        var markov = world.Names as MarkovNameGenerator;

        foreach (Culture culture in world.Cultures)
        {
            list.Add(new ExportCulture(
                Id: culture.Id,
                Name: culture.Name,
                Government: culture.Government,
                RulerTitle: culture.RulerTitle,
                SuccessionLaw: culture.Succession,
                TermYears: culture.TermYears,
                Aggression: culture.Values.Aggression,
                Expansionism: culture.Values.Expansionism,
                Piety: culture.Values.Piety,
                Tradition: culture.Values.Tradition,
                Mercantile: culture.Values.Mercantile,
                Learning: culture.Values.Learning,
                Lexicon: BuildLexicon(markov, culture)));
        }

        return list;
    }

    /// <summary>
    /// Describes a culture's language, or an empty lexicon when names are placeholders.
    /// </summary>
    /// <remarks>
    /// Sample names are drawn from a stream keyed on the culture and a fixed purpose, so they are
    /// reproducible, and from indices far outside the world's entity range so they cannot be
    /// mistaken for — or collide with — an entity that actually exists.
    /// </remarks>
    private static ExportLexicon BuildLexicon(MarkovNameGenerator? markov, Culture culture)
    {
        if (markov is null)
        {
            return new ExportLexicon(
                Array.Empty<ExportLexiconSource>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>());
        }

        NamingLanguage language = markov.LanguageOf(culture);

        var sources = new List<ExportLexiconSource>(language.Sources.Count);
        foreach (NamingLanguage.CorpusWeight source in language.Sources)
        {
            sources.Add(new ExportLexiconSource(source.Family, source.Weight));
        }

        var shifts = new List<string>(language.Mutations.Count);
        foreach (MutationRule rule in language.Mutations) shifts.Add(rule.ToString());

        var people = new List<string>(LexiconSampleCount);
        var places = new List<string>(LexiconSampleCount);

        IRng peopleStream = new Pcg32(language.Seed).Fork("lexicon.people", culture.Id.ToDiscriminator());
        IRng placeStream = new Pcg32(language.Seed).Fork("lexicon.places", culture.Id.ToDiscriminator());

        for (int i = 0; i < LexiconSampleCount; i++)
        {
            people.Add(language.Person(peopleStream));
            places.Add(language.Place(placeStream));
        }

        return new ExportLexicon(sources, shifts, people, places);
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
                RulingDynastyId: OrNull(civilization.RulingDynastyId),
                RegentId: OrNull(civilization.RegentId),
                StateReligionId: OrNull(civilization.StateReligionId),
                RulerSinceYear: civilization.RulerSinceYear,
                Population: civilization.Population,
                PeakPopulation: civilization.PeakPopulation,
                RulerIds: civilization.RulerIds.ToArray(),
                SettlementIds: civilization.SettlementIds.ToArray(),
                TerritoryRegionIds: civilization.TerritoryRegionIds.ToArray(),
                Relations: BuildRelations(civilization),
                Allies: BuildAlliances(civilization)));
        }

        return list;
    }

    /// <summary>
    /// A realm's opinion of everyone it has ever met, in id order.
    /// </summary>
    /// <remarks>
    /// The truce is folded in here rather than carried as a list of its own, because a truce is
    /// only ever read alongside the opinion it is holding in check — "hates them, and cannot do
    /// anything about it until 214" is one fact about one pair of realms.
    /// </remarks>
    private static List<ExportRelation> BuildRelations(Civilization civilization)
    {
        var list = new List<ExportRelation>(civilization.Relations.Count);

        foreach (KeyValuePair<EntityId, double> relation in civilization.Relations)
        {
            int truce = civilization.Truces.GetOrDefault(relation.Key, int.MinValue);

            list.Add(new ExportRelation(
                relation.Key, relation.Value, truce == int.MinValue ? null : truce));
        }

        return list;
    }

    private static List<ExportAlliance> BuildAlliances(Civilization civilization)
    {
        var list = new List<ExportAlliance>(civilization.Allies.Count);

        foreach (KeyValuePair<EntityId, int> pact in civilization.Allies)
        {
            list.Add(new ExportAlliance(pact.Key, pact.Value));
        }

        return list;
    }

    private static List<ExportWar> BuildWars(WorldState world)
    {
        var list = new List<ExportWar>(world.Wars.Count);

        foreach (War war in world.Wars)
        {
            list.Add(new ExportWar(
                Id: war.Id,
                Name: war.Name,
                Cause: war.Cause,
                ClaimedRelicId: OrNull(war.ClaimedRelicId),
                AggressorReligionId: OrNull(war.AggressorReligionId),
                DefenderReligionId: OrNull(war.DefenderReligionId),
                Outcome: war.Outcome,
                StartYear: war.StartYear,
                EndYear: war.EndYear,
                AggressorId: war.AggressorId,
                DefenderId: war.DefenderId,
                Attackers: war.Attackers.ToArray(),
                Defenders: war.Defenders.ToArray(),
                BattleIds: war.BattleIds.ToArray(),
                CededRegionIds: war.CededRegionIds.ToArray(),
                AttackerLosses: war.AttackerLosses,
                DefenderLosses: war.DefenderLosses));
        }

        return list;
    }

    private static List<ExportBattle> BuildBattles(WorldState world)
    {
        var list = new List<ExportBattle>(world.Battles.Count);

        foreach (Battle battle in world.Battles)
        {
            list.Add(new ExportBattle(
                Id: battle.Id,
                Name: battle.Name,
                WarId: battle.WarId,
                Year: battle.Year,
                RegionId: battle.RegionId,
                SettlementId: OrNull(battle.SettlementId),
                WasSiege: battle.IsSiege,
                AttackerId: battle.AttackerId,
                DefenderId: battle.DefenderId,
                VictorId: battle.VictorId,
                AttackerCommanderId: OrNull(battle.AttackerCommanderId),
                DefenderCommanderId: OrNull(battle.DefenderCommanderId),
                AttackerStrength: battle.AttackerStrength,
                DefenderStrength: battle.DefenderStrength,
                AttackerLosses: battle.AttackerLosses,
                DefenderLosses: battle.DefenderLosses,
                Sacked: battle.Sacked));
        }

        return list;
    }

    private static List<ExportDynasty> BuildDynasties(WorldState world)
    {
        var list = new List<ExportDynasty>(world.Dynasties.Count);

        foreach (Dynasty house in world.Dynasties)
        {
            list.Add(new ExportDynasty(
                Id: house.Id,
                Name: house.Name,
                CultureId: house.CultureId,
                FoundedYear: house.FoundedYear,
                EndedYear: house.EndedYear,
                FounderId: house.FounderId,
                OriginCivilizationId: OrNull(house.OriginCivilizationId),
                RulerIds: house.RulerIds.ToArray(),
                MemberIds: house.MemberIds.ToArray()));
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
                Specialization: settlement.Specialization,
                SpecializedYear: settlement.SpecializedYear,
                Population: settlement.Population,
                PeakPopulation: settlement.PeakPopulation,
                FoundedYear: settlement.FoundedYear,
                AbandonedYear: settlement.AbandonedYear,
                YearsDepressed: settlement.YearsDepressed,
                IsCapital: settlement.IsCapital,
                IsFortified: settlement.IsFortified,
                ReligionId: OrNull(settlement.ReligionId),
                ConvertedYear: settlement.ConvertedYear));
        }

        return list;
    }

    private static List<ExportTradeRoute> BuildTradeRoutes(WorldState world)
    {
        var list = new List<ExportTradeRoute>(world.TradeRoutes.Count);

        foreach (TradeRoute route in world.TradeRoutes)
        {
            list.Add(new ExportTradeRoute(
                Id: route.Id,
                SettlementAId: route.SettlementAId,
                SettlementBId: route.SettlementBId,
                Mode: route.Mode,
                Status: route.Status,
                FoundedYear: route.FoundedYear,
                EndedYear: route.EndedYear,
                Traffic: route.Traffic,
                PeakTraffic: route.PeakTraffic));
        }

        return list;
    }

    private static List<ExportReligion> BuildReligions(WorldState world)
    {
        var list = new List<ExportReligion>(world.Religions.Count);

        foreach (Religion religion in world.Religions)
        {
            list.Add(new ExportReligion(
                Id: religion.Id,
                Name: religion.Name,
                CultureId: religion.CultureId,
                FounderId: OrNull(religion.FounderId),
                OriginSettlementId: religion.OriginSettlementId,
                ParentId: OrNull(religion.ParentId),
                FoundedYear: religion.FoundedYear,
                EndedYear: religion.EndedYear,
                Fervour: religion.Fervour,
                PeakSettlements: religion.PeakSettlements,
                SettlementIds: religion.SettlementIds.ToArray()));
        }

        return list;
    }

    private static List<ExportHolySite> BuildHolySites(WorldState world)
    {
        var list = new List<ExportHolySite>(world.HolySites.Count);

        foreach (HolySite site in world.HolySites)
        {
            list.Add(new ExportHolySite(
                Id: site.Id,
                Name: site.Name,
                Kind: site.Kind,
                ReligionId: site.ReligionId,
                RegionId: site.RegionId,
                SettlementId: OrNull(site.SettlementId),
                X: site.X,
                Z: site.Z,
                FoundedYear: site.FoundedYear));
        }

        return list;
    }

    private static List<ExportArtifact> BuildArtifacts(WorldState world)
    {
        var list = new List<ExportArtifact>(world.Artifacts.Count);

        foreach (Artifact artifact in world.Artifacts)
        {
            var provenance = new List<ExportProvenance>(artifact.Provenance.Count);
            foreach (ArtifactHolding holding in artifact.Provenance)
            {
                provenance.Add(new ExportProvenance(
                    Year: holding.Year,
                    SettlementId: OrNull(holding.SettlementId),
                    How: holding.How));
            }

            list.Add(new ExportArtifact(
                Id: artifact.Id,
                Name: artifact.Name,
                Kind: artifact.Kind,
                CreatorId: OrNull(artifact.CreatorId),
                OriginSettlementId: artifact.OriginSettlementId,
                ReligionId: OrNull(artifact.ReligionId),
                TomeContents: BuildTomeContents(artifact.TomeContents),
                CreatedYear: artifact.CreatedYear,
                HolderId: OrNull(artifact.HolderId),
                LostYear: artifact.LostYear,
                Provenance: provenance));
        }

        return list;
    }

    private static ExportTomeContents? BuildTomeContents(TomeContents? contents)
    {
        if (contents is null) return null;

        var copies = new List<ExportTomeCopy>(contents.Copies.Count);
        foreach (TomeCopy copy in contents.Copies)
        {
            copies.Add(new ExportTomeCopy(
                Year: copy.Year,
                SettlementId: copy.SettlementId,
                SourceSettlementId: copy.SourceSettlementId));
        }

        var sections = new List<ExportTomeSection>(contents.Sections.Count);
        foreach (TomeSection section in contents.Sections)
        {
            sections.Add(new ExportTomeSection(
                Heading: section.Heading,
                Text: section.Text,
                References: section.References.ToArray()));
        }

        return new ExportTomeContents(
            Kind: contents.Kind,
            SubjectId: contents.SubjectId,
            ContextId: OrNull(contents.ContextId),
            CopyLimit: contents.CopyLimit,
            Copies: copies,
            Sections: sections);
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
                // The styled name, numeral and all — the viewer shows what the chronicle says.
                Name: figure.FullName,
                Sex: figure.Sex,
                CivilizationId: figure.CivilizationId,
                CultureId: figure.CultureId,
                DynastyId: OrNull(figure.DynastyId),
                BirthYear: figure.BirthYear,
                DeathYear: figure.DeathYear,
                DeathCause: figure.DeathCause,
                DeathDetail: figure.DeathDetail,
                BirthSettlementId: OrNull(figure.BirthSettlementId),
                Disposition: new ExportDisposition(
                    figure.Disposition.Values.Aggression,
                    figure.Disposition.Values.Expansionism,
                    figure.Disposition.Values.Piety,
                    figure.Disposition.Values.Tradition,
                    figure.Disposition.Values.Mercantile,
                    figure.Disposition.Values.Learning,
                    figure.Disposition.Centralism),
                Titles: titles,
                MotherId: OrNull(figure.MotherId),
                FatherId: OrNull(figure.FatherId),
                ChildIds: figure.ChildIds.ToArray(),
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
