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
    public const string EngineVersion = "0.10.0";

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
            Series: BuildSeries(world),
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
            Name: world.Flavour.Name,
            Kind: world.Flavour.Kind,
            Designation: world.Flavour.Designation,
            ParentName: world.Flavour.ParentName,
            MoonIndex: world.Flavour.MoonIndex,
            Cosmology: BuildCosmology(world.Flavour.Cosmology, world.StartYear, world.EndYear),
            MinX: world.Terrain.Bounds.MinX,
            MinZ: world.Terrain.Bounds.MinZ,
            Width: world.Terrain.Bounds.Width,
            Height: world.Terrain.Bounds.Height,
            RegionSize: world.Config.RegionSize,
            TerrainStride: world.Config.TerrainStride,
            EastWestPeriodic: world.Config.EastWestPeriodic,
            Capabilities: world.Terrain.Capabilities.ToString(),
            Raster: raster,
            Rivers: rivers);
    }

    private static ExportCosmology BuildCosmology(
        WorldCosmology cosmology, int startYear, int endYear)
    {
        var checks = new List<ExportCosmologyCheck>(cosmology.Checks.Count);
        foreach (CosmologyCheck check in cosmology.Checks)
        {
            checks.Add(new ExportCosmologyCheck(check.Label, check.Passed, check.Detail));
        }

        return new ExportCosmology(
            Galaxy: BuildGalaxy(cosmology.Galaxy),
            StarClass: cosmology.StarClass,
            StarMassSolar: cosmology.StarMassSolar,
            StarRadiusSolar: cosmology.StarRadiusSolar,
            LuminositySolar: cosmology.LuminositySolar,
            StarLifespanGyr: cosmology.StarLifespanGyr,
            HabitableZoneInnerAu: cosmology.HabitableZoneInnerAu,
            HabitableZoneOuterAu: cosmology.HabitableZoneOuterAu,
            OrbitalDistanceAu: cosmology.OrbitalDistanceAu,
            OrbitalPeriodDays: cosmology.OrbitalPeriodDays,
            WorldMassEarth: cosmology.WorldMassEarth,
            WorldRadiusEarth: cosmology.WorldRadiusEarth,
            SurfaceGravityG: cosmology.SurfaceGravityG,
            EscapeVelocityKmS: cosmology.EscapeVelocityKmS,
            BondAlbedo: cosmology.BondAlbedo,
            GreenhouseDeltaC: cosmology.GreenhouseDeltaC,
            EquilibriumTempK: cosmology.EquilibriumTempK,
            SurfaceTempK: cosmology.SurfaceTempK,
            ParentGiantMassEarth: cosmology.ParentGiantMassEarth,
            MoonOrbitalDistanceEarthRadii: cosmology.MoonOrbitalDistanceEarthRadii,
            MoonDayLengthDays: cosmology.MoonDayLengthDays,
            RocheLimitEarthRadii: cosmology.RocheLimitEarthRadii,
            SnowLineAu: cosmology.SnowLineAu,
            Companions: BuildCompanions(cosmology.Companions),
            Moons: BuildMoons(cosmology.Moons),
            HabitableMoonIndex: cosmology.HabitableMoonIndex,
            Comets: BuildComets(cosmology.Comets),
            Apparitions: BuildApparitions(cosmology, startYear, endYear),
            IsHabitable: cosmology.IsHabitable,
            Checks: checks);
    }

    private static ExportGalaxy BuildGalaxy(HostGalaxy galaxy)
    {
        GalaxyBlueprint blueprint = galaxy.Blueprint;
        GalacticLocation location = galaxy.Location;
        return new ExportGalaxy(
            blueprint.Morphology,
            blueprint.StellarMassSolar,
            blueprint.DiskScaleLengthKpc,
            blueprint.ThinDiskScaleHeightPc,
            blueprint.BulgeToDiskMass,
            blueprint.SolarAnalogMetallicityFeH,
            blueprint.MetallicityGradientDexPerKpc,
            blueprint.MetallicityScatterDex,
            blueprint.SpiralArmCount,
            blueprint.SpiralPitchDeg,
            blueprint.InnerHabitableRadiusKpc,
            blueprint.OuterHabitableRadiusKpc,
            blueprint.SersicIndex,
            blueprint.AxisRatio,
            blueprint.MetallicityReferenceRadiusKpc,
            new ExportGalacticLocation(
                location.GalactocentricRadiusKpc,
                location.AzimuthRad,
                location.HeightPc,
                location.MetallicityFeH,
                location.InSpiralArm,
                location.LocalStellarDensityRelativeToSolar,
                location.SupernovaRateRelativeToSolar),
            galaxy.CanHostIronCore,
            galaxy.CanHostOres);
    }

    private static List<ExportComet> BuildComets(IReadOnlyList<SystemComet> comets)
    {
        var list = new List<ExportComet>(comets.Count);
        foreach (SystemComet comet in comets)
        {
            list.Add(new ExportComet(
                comet.Index,
                comet.PerihelionAu,
                comet.AphelionAu,
                comet.Eccentricity,
                comet.InclinationDeg,
                comet.ArgumentOfPeriapsisRad,
                comet.OrbitalPeriodDays,
                comet.NucleusRadiusKm,
                comet.MassEarth));
        }

        return list;
    }

    private static List<ExportCompanionPlanet> BuildCompanions(IReadOnlyList<CompanionPlanet> companions)
    {
        var list = new List<ExportCompanionPlanet>(companions.Count);
        foreach (CompanionPlanet body in companions)
        {
            list.Add(new ExportCompanionPlanet(
                body.Role,
                body.SemiMajorAxisAu,
                body.MassEarth,
                body.RadiusEarth,
                body.OrbitalPeriodDays));
        }

        return list;
    }

    private static List<ExportSystemMoon> BuildMoons(IReadOnlyList<SystemMoon> moons)
    {
        var list = new List<ExportSystemMoon>(moons.Count);
        foreach (SystemMoon moon in moons)
        {
            list.Add(new ExportSystemMoon(
                moon.Index,
                moon.OrbitalDistanceEarthRadii,
                moon.MassEarth,
                moon.RadiusEarth,
                moon.DayLengthDays,
                moon.Habitable));
        }

        return list;
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
                Fortunes: Snapshot(civilization.Fortunes),
                EffectiveValues: new ExportValues(
                    civilization.EffectiveValues.Aggression,
                    civilization.EffectiveValues.Expansionism,
                    civilization.EffectiveValues.Piety,
                    civilization.EffectiveValues.Tradition,
                    civilization.EffectiveValues.Mercantile,
                    civilization.EffectiveValues.Learning),
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
                Day: battle.Day,
                EndYear: battle.EndYear,
                EndDay: battle.EndDay,
                RegionId: battle.RegionId,
                SettlementId: OrNull(battle.SettlementId),
                WasSiege: battle.IsSiege,
                SiegeOutcome: battle.SiegeOutcome,
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

        // Both surveyed once for the whole table, as the yearly tick does, rather than per
        // settlement: each per-settlement query walks a whole table of its own.
        TradeTraffic traffic = TradeRoutes.TrafficBySettlement(world);
        Hinterland hinterland = Hinterland.Survey(world);

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
                ConvertedYear: settlement.ConvertedYear,
                Site: settlement.Site,
                Fortunes: Snapshot(settlement.Fortunes),
                Support: SupportOf(world, settlement, traffic, hinterland)));
        }

        return list;
    }

    /// <summary>
    /// What is feeding a settlement as the chronicle closes, or null if it stands empty.
    /// </summary>
    /// <remarks>
    /// Measured against the final year's harvest, traffic and neighbours, so it describes the world
    /// the export is a picture of. An abandoned settlement gets nothing rather than a stale reading
    /// from the year it emptied.
    /// </remarks>
    private static ExportSupport? SupportOf(
        WorldState world, Settlement settlement, TradeTraffic traffic, Hinterland hinterland)
    {
        if (!settlement.IsActive) return null;
        if (!world.Civilizations.Contains(settlement.CivilizationId)) return null;

        Civilization civilization = world.Civilizations[settlement.CivilizationId];
        Region region = world.Regions[settlement.RegionId];

        SettlementSupport support = PopulationSystem.SupportFor(
            world,
            civilization,
            world.CultureOf(civilization),
            settlement,
            region,
            world.Harvest.QualityAt(region, world.EndYear),
            traffic.At(settlement.Id),
            hinterland.ShareFor(world, settlement));

        return new ExportSupport(
            Capacity: (int)Math.Round(support.Capacity),
            FromSite: (int)Math.Round(support.FromSite),
            FromLand: (int)Math.Round(support.FromLand),
            FromTrade: (int)Math.Round(support.FromTrade),
            LandShare: Math.Round(support.LandShare, 3),
            RouteTraffic: Math.Round(support.RouteTraffic, 3),
            Principal: support.Principal);
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
                PeakTraffic: route.PeakTraffic,
                Road: ExportRoadOf(route)));
        }

        return list;
    }

    /// <summary>
    /// The route's road, flattened to an <c>[x, z, x, z, …]</c> run of coordinates.
    /// </summary>
    /// <remarks>
    /// A flat integer list rather than a list of point objects, because the viewer reads it as one
    /// polyline and a per-point record would spend the field names again at every corner. Rivers
    /// are exported as independent segments for the opposite reason: they are a graph rather than a
    /// line, and each reach carries a strength of its own.
    /// </remarks>
    private static ExportRoad? ExportRoadOf(TradeRoute route)
    {
        if (route.Road is not { } road) return null;

        var points = new List<int>(road.Points.Count * 2);
        foreach (RoadPoint point in road.Points)
        {
            points.Add(point.X);
            points.Add(point.Z);
        }

        return new ExportRoad(
            Grade: road.Grade,
            BuiltYear: road.BuiltYear,
            PavedYear: road.PavedYear,
            Length: Math.Round(road.Length, 1),
            Points: points);
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
                Character: ExportCharacter(religion.Character),
                PeakSettlements: religion.PeakSettlements,
                SettlementIds: religion.SettlementIds.ToArray()));
        }

        return list;
    }

    private static ExportFaithCharacter ExportCharacter(FaithCharacter character) => new(
        Deity: character.Deity,
        Afterlife: character.Afterlife,
        Soul: character.Soul,
        Authority: character.Authority,
        Clergy: character.Clergy,
        CelibateClergy: character.CelibateClergy,
        Wealth: character.Wealth,
        Dogma: character.Dogma,
        Prayer: character.Prayer,
        Diet: character.Diet,
        Dress: character.Dress,
        Festival: character.Festival,
        Fervour: character.Fervour,
        Zealotry: character.Zealotry,
        Tolerance: character.Tolerance,
        SchismProneness: character.SchismProneness,
        Syncretism: character.Syncretism);

    private static List<ExportHolySite> BuildHolySites(WorldState world)
    {
        var list = new List<ExportHolySite>(world.HolySites.Count);

        foreach (HolySite site in world.HolySites)
        {
            HolySiteDescription d = site.Description;
            list.Add(new ExportHolySite(
                Id: site.Id,
                Name: site.Name,
                Kind: site.Kind,
                ReligionId: site.ReligionId,
                RegionId: site.RegionId,
                SettlementId: OrNull(site.SettlementId),
                X: site.X,
                Z: site.Z,
                FoundedYear: site.FoundedYear,
                Description: new ExportHolySiteDescription(
                    Tradition: d.Tradition,
                    DedicationKind: d.DedicationKind,
                    Dedication: d.Dedication,
                    Style: d.Style,
                    Atmosphere: d.Atmosphere,
                    Scale: d.Scale,
                    Capacity: d.Capacity,
                    HasStatue: d.HasStatue,
                    FocalPoint: d.FocalPoint,
                    Offering: d.Offering,
                    DedicateeId: OrNull(d.DedicateeId),
                    DedicateeEventId: d.DedicateeEventId)));
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
                    OwnerId: OrNull(holding.OwnerId),
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
                OwnerId: OrNull(artifact.OwnerId),
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
                References: section.References.ToArray(),
                Year: section.Year));
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
            var titles = new List<ExportTitle>(figure.Offices.Count);
            foreach (OfficeHolding holding in figure.Offices)
            {
                titles.Add(new ExportTitle(
                    Kind: holding.Kind,
                    Title: holding.Title,
                    CivilizationId: holding.CivilizationId,
                    FromYear: holding.FromYear,
                    ToYear: holding.ToYear,
                    ScopeId: OrNull(holding.ScopeId),
                    GrantedBy: OrNull(holding.GrantedBy),
                    Claim: holding.Claim));
            }

            list.Add(new ExportFigure(
                Id: figure.Id,
                // The styled name, numeral and all — the viewer shows what the chronicle says.
                Name: figure.FullName,
                Sex: figure.Sex,
                CivilizationId: figure.CivilizationId,
                CultureId: figure.CultureId,
                ReligionId: OrNull(figure.ReligionId),
                DynastyId: OrNull(figure.DynastyId),
                BirthYear: figure.BirthYear,
                DeathYear: figure.DeathYear,
                DeathCause: figure.DeathCause,
                DeathDetail: figure.DeathDetail,
                BirthSettlementId: OrNull(figure.BirthSettlementId),
                ResidenceSettlementId: OrNull(figure.ResidenceSettlementId),
                Residences: BuildResidences(figure),
                Origin: figure.Origin,
                Background: BuildBackground(figure.Background),
                Occupation: figure.Occupation,
                Disposition: new ExportDisposition(
                    figure.Disposition.Values.Aggression,
                    figure.Disposition.Values.Expansionism,
                    figure.Disposition.Values.Piety,
                    figure.Disposition.Values.Tradition,
                    figure.Disposition.Values.Mercantile,
                    figure.Disposition.Values.Learning,
                    figure.Disposition.Centralism,
                    figure.Disposition.Independence),
                Titles: titles,
                Campaigns: BuildCampaigns(figure),
                Journeys: BuildJourneys(figure),
                Bonds: BuildBonds(figure),
                Memories: BuildMemories(figure, figure.DeathYear ?? world.EndYear),
                Feelings: BuildFeelings(figure, figure.DeathYear ?? world.EndYear),
                Injuries: BuildInjuries(figure),
                Undertakings: BuildUndertakings(figure),
                Disputes: BuildDisputes(figure),
                Plots: BuildPlots(world, figure),
                Guardianships: BuildGuardianships(figure),
                Mentorships: BuildMentorships(figure),
                Observations: BuildObservations(figure),
                Claims: BuildClaims(figure),
                MotherId: OrNull(figure.MotherId),
                FatherId: OrNull(figure.FatherId),
                ChildIds: figure.ChildIds.ToArray(),
                SpouseIds: figure.SpouseIds.ToArray()));
        }

        return list;
    }

    private static ExportBackground? BuildBackground(FigureBackground? background) =>
        background is null
            ? null
            : new ExportBackground(
                background.IntroducedYear,
                background.OriginSettlementId,
                background.CareerFamily,
                OrNull(background.InstitutionId),
                OrNull(background.SponsorId),
                OrNull(background.MentorId));

    private static List<ExportGuardianship> BuildGuardianships(Figure figure)
    {
        var list = new List<ExportGuardianship>(figure.Guardianships.Count);
        foreach (FigureGuardianship guardianship in figure.Guardianships)
        {
            list.Add(new ExportGuardianship(
                guardianship.GuardianId,
                guardianship.WardId,
                guardianship.StartYear,
                guardianship.EndYear,
                guardianship.End,
                guardianship.CauseKind,
                OrNull(guardianship.CauseEntityId),
                OrNull(guardianship.LocationId)));
        }

        return list;
    }

    private static List<ExportMentorship> BuildMentorships(Figure figure)
    {
        var list = new List<ExportMentorship>(figure.Mentorships.Count);
        foreach (FigureMentorship mentorship in figure.Mentorships)
        {
            list.Add(new ExportMentorship(
                mentorship.MentorId,
                mentorship.ApprenticeId,
                mentorship.StartYear,
                mentorship.CareerFamily,
                OrNull(mentorship.LocationId)));
        }

        return list;
    }

    private static List<ExportCampaign> BuildCampaigns(Figure figure)
    {
        var list = new List<ExportCampaign>(figure.Campaigns.Count);
        foreach (CampaignMemory memory in figure.Campaigns)
        {
            list.Add(new ExportCampaign(
                WarId: memory.WarId,
                BattleId: OrNull(memory.BattleId),
                SideId: memory.SideId,
                Year: memory.Year,
                Role: memory.Role,
                Triumphant: memory.Triumphant,
                Fate: memory.Fate,
                RenownGained: memory.RenownGained,
                Traumatized: memory.Traumatized,
                Deserted: memory.Deserted,
                PromotionYear: memory.PromotionYear));
        }

        return list;
    }

    private static List<ExportJourney> BuildJourneys(Figure figure)
    {
        var list = new List<ExportJourney>(figure.Journeys.Count);
        foreach (Journey journey in figure.Journeys)
        {
            list.Add(new ExportJourney(
                Kind: journey.Kind,
                Year: journey.Year,
                Day: journey.Day,
                FromSettlementId: journey.FromSettlementId,
                ToSettlementId: journey.ToSettlementId,
                ViaId: OrNull(journey.ViaId),
                DurationDays: journey.DurationDays,
                Outcome: journey.Outcome,
                ReturnSettlementId: OrNull(journey.ReturnSettlementId),
                ReturnYear: journey.ReturnYear,
                ReturnDay: journey.ReturnDay));
        }

        return list;
    }

    private static List<ExportBond> BuildBonds(Figure figure)
    {
        var list = new List<ExportBond>(figure.Bonds.Count);
        foreach (FigureBond bond in figure.Bonds)
        {
            var kinds = new List<BondKind>();
            foreach (BondKind kind in Enum.GetValues<BondKind>())
            {
                if (kind != BondKind.None && bond.Kinds.HasFlag(kind)) kinds.Add(kind);
            }

            list.Add(new ExportBond(
                bond.OtherId,
                kinds,
                bond.SinceYear,
                bond.LastChangedYear,
                bond.LastCause,
                bond.OriginEventKind,
                OrNull(bond.OriginEntityId),
                OrNull(bond.OriginLocationId),
                bond.LastEventKind,
                OrNull(bond.LastEntityId),
                OrNull(bond.LastLocationId),
                bond.Affection,
                bond.Trust,
                bond.Obligation,
                bond.Fear,
                bond.Grievance));
        }

        return list;
    }

    private static List<ExportResidence> BuildResidences(Figure figure)
    {
        var list = new List<ExportResidence>(figure.Residences.Count);
        foreach (Residence residence in figure.Residences)
        {
            list.Add(new ExportResidence(
                residence.SettlementId, residence.FromYear, residence.Reason));
        }

        return list;
    }

    private static List<ExportMemory> BuildMemories(Figure figure, int year)
    {
        var list = new List<ExportMemory>(figure.Memories.Count);
        foreach (SalientMemory memory in figure.Memories)
        {
            bool active = LifeStories.IsActive(memory, year);
            if (!active && !LifeStories.IsFormative(memory)) continue;

            list.Add(new ExportMemory(
                memory.Kind,
                memory.Valence,
                memory.Year,
                memory.LastReinforcedYear,
                memory.SourceKind,
                OrNull(memory.AboutId),
                OrNull(memory.LocationId),
                LifeStories.EffectiveIntensity(memory, year),
                active));
        }

        return list;
    }

    private static ExportFeelings BuildFeelings(Figure figure, int year)
    {
        FeelingState feelings = LifeStories.Feelings(figure, year);
        return new ExportFeelings(
            feelings.Grief,
            feelings.Fear,
            feelings.Anger,
            feelings.Pride,
            feelings.Loyalty);
    }

    private static List<ExportInjury> BuildInjuries(Figure figure)
    {
        var list = new List<ExportInjury>(figure.Injuries.Count);
        foreach (FigureInjury injury in figure.Injuries)
        {
            list.Add(new ExportInjury(
                injury.CauseId,
                injury.SourceKind,
                injury.Year,
                injury.Severity,
                injury.RecoveryYear,
                injury.Permanent,
                injury.Detail));
        }

        return list;
    }

    /// <summary>
    /// Writes each quarrel from the viewpoint of the page it is going on.
    /// </summary>
    /// <remarks>
    /// The two parties share one object in the engine, so the facts here cannot diverge; what
    /// differs between the two exports is only which id is called the other one.
    /// </remarks>
    /// <summary>The sky's own schedule, so a reader can check the register against it.</summary>
    private static List<ExportApparition> BuildApparitions(
        WorldCosmology cosmology, int startYear, int endYear)
    {
        List<Apparition> returns = Skywatch.Apparitions(cosmology, startYear, endYear);
        var list = new List<ExportApparition>(returns.Count);
        foreach (Apparition seen in returns)
        {
            list.Add(new ExportApparition(seen.CometIndex, seen.Year, seen.Grade));
        }

        return list;
    }

    private static List<ExportObservation> BuildObservations(Figure figure)
    {
        var list = new List<ExportObservation>(figure.Observations.Count);
        foreach (SkyObservation seen in figure.Observations)
        {
            list.Add(new ExportObservation(
                seen.CometIndex,
                seen.Year,
                OrNull(seen.RealmId),
                OrNull(seen.SettlementId),
                seen.PriorYear,
                seen.Interval,
                seen.Grade));
        }

        return list;
    }

    private static List<ExportSkyClaim> BuildClaims(Figure figure)
    {
        var list = new List<ExportSkyClaim>(figure.Claims.Count);
        foreach (SkyClaim claim in figure.Claims)
        {
            list.Add(new ExportSkyClaim(
                claim.Id,
                claim.CometIndex,
                claim.Year,
                OrNull(claim.RealmId),
                claim.Register,
                claim.Reading,
                claim.RestsOnYears.ToArray(),
                claim.IntervalYears,
                claim.PredictedYear,
                claim.Verdict,
                claim.SettledYear,
                claim.ClaimantSawTheAnswer));
        }

        return list;
    }

    private static List<ExportDispute> BuildDisputes(Figure figure)
    {
        var list = new List<ExportDispute>(figure.Disputes.Count);
        foreach (FigureDispute dispute in figure.Disputes)
        {
            var acts = new List<ExportDisputeAct>(dispute.Acts.Count);
            foreach (DisputeAct act in dispute.Acts)
            {
                acts.Add(new ExportDisputeAct(
                    act.Year,
                    act.SourceKind,
                    act.Stage,
                    OrNull(act.ActorId),
                    act.Detail));
            }

            list.Add(new ExportDispute(
                dispute.Id,
                dispute.Other(figure.Id),
                dispute.OpenerId == figure.Id,
                dispute.Cause,
                dispute.SourceKind,
                OrNull(dispute.SourceEntityId),
                OrNull(dispute.PlaceId),
                dispute.Stage,
                dispute.Outcome,
                dispute.Resolution,
                OrNull(dispute.ArbiterId),
                dispute.StartYear,
                dispute.EndYear,
                dispute.LastActionYear,
                acts));
        }

        return list;
    }

    /// <summary>
    /// Every conspiracy this person knew about, from their own side.
    /// </summary>
    /// <remarks>
    /// The retrospective truth, including the years it was secret. What a consumer must not do is
    /// present a secret act as contemporary knowledge, which is what
    /// <see cref="ExportPlot.PublicYear"/> and <see cref="ExportPlotAct.Known"/> are for.
    /// </remarks>
    private static List<ExportPlot> BuildPlots(WorldState world, Figure figure)
    {
        var plots = new List<FigurePlot>(figure.Plots);

        // A target learns a plot only when the world does. Keep the shared engine record off the
        // target while it is secret, then derive the revealed viewpoint at export rather than
        // mutating a list whose meaning is "plots this person knowingly joined".
        foreach (Figure leader in world.Figures)
        {
            foreach (FigurePlot plot in leader.Plots)
            {
                if (plot.LeaderId != leader.Id || plot.TargetId != figure.Id || !plot.WasKnown) continue;
                if (!plots.Contains(plot)) plots.Add(plot);
            }
        }

        plots.Sort((left, right) =>
        {
            int byYear = left.StartYear.CompareTo(right.StartYear);
            if (byYear != 0) return byYear;
            int byLeader = left.LeaderId.CompareTo(right.LeaderId);
            return byLeader != 0 ? byLeader : left.Id.CompareTo(right.Id);
        });

        var list = new List<ExportPlot>(plots.Count);
        foreach (FigurePlot plot in plots)
        {
            var members = new List<ExportPlotMember>(plot.Members.Count);
            foreach (PlotMember member in plot.Members)
            {
                members.Add(new ExportPlotMember(
                    member.FigureId, member.JoinedYear, member.Tie, member.Witting));
            }

            var acts = new List<ExportPlotAct>(plot.Acts.Count);
            foreach (PlotAct act in plot.Acts)
            {
                acts.Add(new ExportPlotAct(
                    act.Year,
                    act.SourceKind,
                    act.Phase,
                    OrNull(act.ActorId),
                    act.Detail,
                    act.Known));
            }

            list.Add(new ExportPlot(
                plot.Id,
                plot.LeaderId,
                plot.TargetId,
                OrNull(plot.RealmId),
                plot.LeaderId == figure.Id
                    ? PlotViewpoint.Leader
                    : plot.TargetId == figure.Id
                        ? PlotViewpoint.Target
                        : PlotViewpoint.Member,
                plot.Objective,
                plot.Cause,
                plot.SourceKind,
                OrNull(plot.SourceEntityId),
                OrNull(plot.PlaceId),
                plot.Phase,
                plot.Outcome,
                plot.Resolution,
                OrNull(plot.BetrayerId),
                plot.StartYear,
                plot.EndYear,
                plot.PublicYear,
                plot.Progress,
                plot.RequiredProgress,
                plot.Secrecy,
                plot.Suspicion,
                plot.Access,
                members,
                acts));
        }

        return list;
    }

    private static List<ExportUndertaking> BuildUndertakings(Figure figure)
    {
        var list = new List<ExportUndertaking>(figure.Undertakings.Count);
        foreach (FigureUndertaking undertaking in figure.Undertakings)
        {
            var steps = new List<ExportUndertakingStep>(undertaking.Steps.Count);
            foreach (UndertakingStep step in undertaking.Steps)
            {
                steps.Add(new ExportUndertakingStep(
                    step.Year,
                    step.SourceKind,
                    OrNull(step.PlaceId),
                    OrNull(step.SubjectId),
                    step.Outcome));
            }

            list.Add(new ExportUndertaking(
                undertaking.Id,
                undertaking.Kind,
                undertaking.State,
                undertaking.StartYear,
                undertaking.EndYear,
                undertaking.Outcome,
                undertaking.Objective,
                OrNull(undertaking.TargetId),
                OrNull(undertaking.DestinationId),
                OrNull(undertaking.ViaId),
                undertaking.Progress,
                undertaking.RequiredProgress,
                undertaking.Motive,
                OrNull(undertaking.MotiveEntityId),
                undertaking.MotiveSourceKind,
                undertaking.DeadlineYear,
                undertaking.LastProgressYear,
                OrNull(undertaking.SponsorId),
                undertaking.RequiredOffice,
                undertaking.ParticipantIds.ToArray(),
                steps));
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
                Day: entry.Day,
                Kind: entry.Kind,
                Significance: entry.Significance,
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

    /// <summary>
    /// The yearly series, as the run sampled them.
    /// </summary>
    /// <remarks>
    /// Counts are exported whole and dials to three decimals. The rounding is what keeps this
    /// affordable: a realm's eleven tracks and a settlement's five across three centuries are a
    /// few kilobytes at three decimals and roughly three times that at full double precision, for
    /// a difference no chart can draw and no reader can see.
    /// </remarks>
    private static IReadOnlyList<ExportSeries> BuildSeries(WorldState world)
    {
        var series = new List<ExportSeries>(world.Series.All.Count);

        foreach (SeriesLog.Series track in world.Series.All)
        {
            int decimals = track.Measure.Unit == MeasureUnit.Count ? 0 : 3;

            var values = new double[track.Values.Count];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = Math.Round(track.Values[i], decimals);
            }

            series.Add(new ExportSeries(
                Entity: track.Entity,
                Metric: track.Measure.Name,
                Group: track.Measure.Group,
                Unit: track.Measure.Unit.ToString(),
                FromYear: track.FromYear,
                Values: values));
        }

        return series;
    }

    private static EntityId? OrNull(EntityId id) => id.IsNone ? null : id;

    private static ExportFortunes Snapshot(RealmFortunes fortunes) => new(
        fortunes.Weariness,
        fortunes.Calamity,
        fortunes.Triumph,
        fortunes.Grievance);

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
