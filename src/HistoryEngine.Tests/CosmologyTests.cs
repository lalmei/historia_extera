using HistoryEngine.Core;
using HistoryEngine.Naming;
using HistoryEngine.Serialization;
using HistoryEngine.World;
using Xunit;

namespace HistoryEngine.Tests;

/// <summary>
/// Host-star and habitable-body physics rolled from the seed.
/// </summary>
public sealed class CosmologyTests
{
    [Fact]
    public void SameSeedAlwaysProducesTheSameCosmology()
    {
        WorldCosmology first = WorldCosmology.From(42);
        WorldCosmology again = WorldCosmology.From(42);

        Assert.Equal(first.StarClass, again.StarClass);
        Assert.Equal(first.OrbitalDistanceAu, again.OrbitalDistanceAu);
        Assert.Equal(first.Galaxy, again.Galaxy);
        Assert.Equal(first.Comets, again.Comets);
        Assert.Equal(first.Companions.Count, again.Companions.Count);
        for (int i = 0; i < first.Companions.Count; i++)
        {
            Assert.Equal(first.Companions[i], again.Companions[i]);
        }
    }

    [Fact]
    public void CosmologyKindMatchesFlavour()
    {
        for (ulong seed = 1; seed <= 64; seed++)
        {
            WorldFlavour flavour = WorldFlavour.From(seed, new MarkovNameGenerator(seed));
            Assert.Equal(flavour.Kind, flavour.Cosmology.Kind);
        }
    }

    [Theory]
    [InlineData(StarSpectralClass.M, 0.08, 0.45)]
    [InlineData(StarSpectralClass.K, 0.45, 0.80)]
    [InlineData(StarSpectralClass.G, 0.80, 1.04)]
    [InlineData(StarSpectralClass.F, 1.04, 1.40)]
    public void SpectralClassMassRangesCoverExpectedBands(
        StarSpectralClass starClass,
        double min,
        double max)
    {
        (double lo, double hi) = WorldCosmology.MassRange(starClass);
        Assert.Equal(min, lo, 3);
        Assert.Equal(max, hi, 3);
    }

    [Fact]
    public void EveryGeneratedWorldPassesHabitabilityChecks()
    {
        var starClasses = new HashSet<StarSpectralClass>();

        for (ulong seed = 1; seed <= 256; seed++)
        {
            WorldCosmology cosmology = WorldCosmology.From(seed);
            starClasses.Add(cosmology.StarClass);

            Assert.True(
                cosmology.IsHabitable,
                $"Seed {seed} failed: {string.Join("; ", cosmology.Checks.Where(c => !c.Passed).Select(c => c.Detail))}");

            Assert.InRange(
                cosmology.OrbitalDistanceAu,
                cosmology.HabitableZoneInnerAu,
                cosmology.HabitableZoneOuterAu);

            Assert.True(cosmology.StarLifespanGyr >= WorldCosmology.MinStarLifespanGyr);
            Assert.True(cosmology.EscapeVelocityKmS >= WorldCosmology.MinEscapeVelocityKmS);
            Assert.InRange(cosmology.SurfaceTempK, WorldCosmology.MinSurfaceTempK, WorldCosmology.MaxSurfaceTempK);

            if (cosmology.Kind == WorldKind.Moon)
            {
                Assert.NotNull(cosmology.MoonDayLengthDays);
                Assert.True(cosmology.MoonDayLengthDays <= WorldCosmology.MaxMoonDayDays);
                Assert.NotNull(cosmology.MoonOrbitalDistanceEarthRadii);
                Assert.NotNull(cosmology.RocheLimitEarthRadii);
                Assert.True(cosmology.MoonOrbitalDistanceEarthRadii > cosmology.RocheLimitEarthRadii);
                Assert.True(cosmology.Moons.Count >= 1);
                Assert.All(cosmology.Moons, moon =>
                    Assert.True(
                        moon.DayLengthDays <= WorldCosmology.MaxMoonDayDays + 1e-9,
                        $"Moon {moon.Index} day {moon.DayLengthDays:F1} exceeds the 7-day habitable-moon limit."));
                Assert.Equal(1, cosmology.Moons.Count(moon => moon.Habitable));
                Assert.Equal(cosmology.HabitableMoonIndex, cosmology.Moons.Single(moon => moon.Habitable).Index);
            }
            else
            {
                Assert.Empty(cosmology.Moons);
            }

            CompanionPlanet shepherd = Assert.Single(
                cosmology.Companions,
                body => body.Role == CompanionRole.ShepherdGiant);
            Assert.True(shepherd.SemiMajorAxisAu > cosmology.SnowLineAu);
            Assert.True(shepherd.SemiMajorAxisAu > cosmology.HabitableZoneOuterAu);
            double habitableMass = cosmology.Kind == WorldKind.Moon
                ? cosmology.ParentGiantMassEarth ?? cosmology.WorldMassEarth
                : cosmology.WorldMassEarth;
            Assert.True(
                WorldCosmology.HillSeparated(
                    cosmology.OrbitalDistanceAu,
                    habitableMass,
                    shepherd.SemiMajorAxisAu,
                    shepherd.MassEarth,
                    cosmology.StarMassSolar),
                $"Seed {seed}: shepherd at {shepherd.SemiMajorAxisAu:F2} AU is too close to the habitable orbit.");
        }

        Assert.Equal(4, starClasses.Count);
    }

    [Fact]
    public void EveryGiantCarriesAFaceAndAFamilyThatSurvivesItsPrimary()
    {
        var withRings = 0;
        var withStorms = 0;
        var withMoons = 0;
        var roles = new HashSet<CompanionRole>();

        for (ulong seed = 1; seed <= 256; seed++)
        {
            WorldCosmology cosmology = WorldCosmology.From(seed);

            foreach (CompanionPlanet body in cosmology.Companions)
            {
                roles.Add(body.Role);

                if (!body.IsGiant)
                {
                    Assert.Null(body.Appearance);
                    Assert.Empty(body.Moons);
                    continue;
                }

                GiantAppearance face = Assert.IsType<GiantAppearance>(body.Appearance);
                Assert.InRange(face.ObliquityDeg, 0.0, 98.0);
                Assert.InRange(face.RotationPeriodHours, 8.0, 20.0);
                Assert.InRange(face.BandCount, 3, 17);
                Assert.InRange(face.AscendingNodeDeg, 0.0, 360.0);

                if (face.Storm is { } storm)
                {
                    withStorms++;
                    Assert.InRange(storm.LatitudeDeg, -62.0, 62.0);
                    Assert.True(storm.AgeYears > 0.0);
                }

                double ringEdge = 0.0;
                if (face.Ring is { } ring)
                {
                    withRings++;
                    Assert.InRange(
                        ring.InnerRadiusPlanetRadii,
                        GiantAppearances.MinRingInnerPlanetRadii,
                        GiantAppearances.MaxRingOuterPlanetRadii);
                    Assert.True(ring.OuterRadiusPlanetRadii > ring.InnerRadiusPlanetRadii);
                    Assert.True(ring.OuterRadiusPlanetRadii <= GiantAppearances.MaxRingOuterPlanetRadii);
                    Assert.InRange(ring.OpticalDepth, 0.0, 1.0);
                    Assert.True(
                        GiantAppearances.RingBrightnessBoostMagnitudes(face) <= 0.0,
                        "A ring can only add light, never take it away.");
                    ringEdge = ring.OuterRadiusPlanetRadii * body.RadiusEarth;
                }

                if (body.Moons.Count > 0) withMoons++;

                double roche = WorldCosmology.ComputeRocheLimitEarthRadii(body.RadiusEarth, 0.3);
                foreach (SystemMoon moon in body.Moons)
                {
                    Assert.True(
                        moon.OrbitalDistanceEarthRadii > roche,
                        $"Seed {seed}: a moon of the {body.RoleLabel} sits inside the Roche limit.");
                    Assert.True(
                        moon.OrbitalDistanceEarthRadii > ringEdge,
                        $"Seed {seed}: a moon of the {body.RoleLabel} sits inside its own ring.");
                    Assert.True(
                        moon.DayLengthDays <= WorldCosmology.MaxGiantMoonMonthDays * 1.2,
                        $"Seed {seed}: a moon of the {body.RoleLabel} takes {moon.DayLengthDays:F0} days to come round.");
                    Assert.False(moon.Habitable);
                    Assert.Equal(moon.DisplayName, moon.DisplayName);
                }

                Assert.Equal(
                    body.Moons.Select(moon => moon.Index).ToArray(),
                    Enumerable.Range(1, body.Moons.Count).ToArray());
            }
        }

        Assert.Contains(CompanionRole.OuterGasGiant, roles);
        Assert.Contains(CompanionRole.OuterIceGiant, roles);
        Assert.Contains(CompanionRole.InnerRocky, roles);
        Assert.True(withRings > 0, "No giant in 256 seeds kept a ring.");
        Assert.True(withStorms > 0, "No giant in 256 seeds held a storm.");
        Assert.True(withMoons > 0, "No giant in 256 seeds kept a moon.");
    }

    [Fact]
    public void PlanetWorldsGetTheirOwnMoonsAndMoonWorldsDoNot()
    {
        var moonCounts = new HashSet<int>();

        for (ulong seed = 1; seed <= 256; seed++)
        {
            WorldCosmology cosmology = WorldCosmology.From(seed);

            if (cosmology.Kind == WorldKind.Moon)
            {
                Assert.Empty(cosmology.HomeMoons);
                continue;
            }

            moonCounts.Add(cosmology.HomeMoons.Count);
            Assert.InRange(cosmology.HomeMoons.Count, 0, 3);

            double rocheFloor = WorldCosmology.RockyRocheLimitEarthRadii(cosmology.WorldRadiusEarth);
            double previousOrbit = 0.0;
            foreach (SystemMoon moon in cosmology.HomeMoons)
            {
                Assert.True(
                    moon.OrbitalDistanceEarthRadii > rocheFloor,
                    $"Seed {seed}: a moon inside the Roche limit would be a ring, not a moon.");
                Assert.InRange(
                    moon.DayLengthDays,
                    WorldCosmology.MinHomeMoonMonthDays * 0.99,
                    WorldCosmology.MaxHomeMoonMonthDays * 1.01);
                Assert.InRange(
                    moon.MassEarth,
                    WorldCosmology.MinHomeMoonMassEarth,
                    WorldCosmology.MaxHomeMoonMassEarth);
                Assert.False(moon.Habitable);

                if (previousOrbit > 0.0)
                {
                    Assert.True(
                        moon.OrbitalDistanceEarthRadii / previousOrbit
                            >= WorldCosmology.MinHomeMoonOrbitRatio - 1e-9,
                        $"Seed {seed}: two moons run all but the same track.");
                }

                previousOrbit = moon.OrbitalDistanceEarthRadii;
            }
        }

        Assert.Contains(0, moonCounts);
        Assert.Contains(1, moonCounts);
        Assert.True(moonCounts.Max() >= 2, "No planet world in 256 seeds kept more than one moon.");
    }

    [Fact]
    public void UnnamedMoonsAreWrittenAsNumerals()
    {
        Assert.Equal("I", new SystemMoon(1, 10.0, 0.01, 0.2, 20.0, false).DisplayName);
        Assert.Equal("IV", new SystemMoon(4, 10.0, 0.01, 0.2, 20.0, false).DisplayName);
        Assert.Equal("12", new SystemMoon(12, 10.0, 0.01, 0.2, 20.0, false).DisplayName);
        Assert.Equal("Selene", new SystemMoon(2, 10.0, 0.01, 0.2, 20.0, false, "Selene").DisplayName);
    }

    [Fact]
    public void RockyBodiesCarryAnIronBudgetTheirDensityAgreesWith()
    {
        for (ulong seed = 1; seed <= 128; seed++)
        {
            WorldCosmology cosmology = WorldCosmology.From(seed);

            Assert.InRange(cosmology.BulkIronMassFraction, 0.20, 0.42);
            Assert.InRange(cosmology.CoreMassFraction, 0.20, cosmology.BulkIronMassFraction + 0.04);
            Assert.InRange(cosmology.MeanDensityEarth, 0.5, 2.5);
            Assert.Equal(
                cosmology.WorldMassEarth / (cosmology.WorldRadiusEarth * cosmology.WorldRadiusEarth
                    * cosmology.WorldRadiusEarth),
                cosmology.MeanDensityEarth,
                9);
        }
    }

    [Fact]
    public void TheSpinAxisIsDrawnOverTheWholeSphereAndHasItsOwnStream()
    {
        WorldCosmology first = WorldCosmology.From(42);
        Assert.Equal(first.Orientation, WorldCosmology.From(42).Orientation);

        var tilts = new List<double>();
        for (ulong seed = 1; seed <= 256; seed++)
        {
            CelestialOrientation orientation = WorldCosmology.From(seed).Orientation;
            Assert.InRange(orientation.PoleGalacticLatitudeRad, -DetSeries.HalfPi, DetSeries.HalfPi);
            Assert.InRange(orientation.PoleGalacticLongitudeRad, -DetSeries.Pi, DetSeries.Pi);
            Assert.InRange(orientation.RightAscensionOriginRollRad, 0.0, DetSeries.TwoPi);
            Assert.InRange(orientation.PoleTiltFromGalacticPoleDeg, 0.0, 90.0);
            tilts.Add(orientation.PoleTiltFromGalacticPoleDeg);
        }

        // A pole drawn uniformly over the sphere sits near the galactic plane more often than near
        // the pole, so the median tilt lands well above 45 degrees rather than at it.
        tilts.Sort();
        Assert.InRange(tilts[tilts.Count / 2], 50.0, 75.0);
    }

    [Fact]
    public void EquatorialCoordinatesRoundTripBackToGalacticOnes()
    {
        CelestialOrientation orientation = WorldCosmology.From(7).Orientation;

        for (int lon = -170; lon <= 170; lon += 37)
        {
            for (int lat = -80; lat <= 80; lat += 23)
            {
                double longitudeRad = DetSeries.ToRadians(lon);
                double latitudeRad = DetSeries.ToRadians(lat);

                (double ra, double dec) = orientation.ToEquatorial(longitudeRad, latitudeRad);
                Assert.InRange(ra, 0.0, 360.0);
                Assert.InRange(dec, -90.0, 90.0);

                (double backLon, double backLat) = orientation.ToGalactic(ra, dec);
                Assert.Equal(latitudeRad, backLat, 8);
                Assert.Equal(DetSeries.Sin(longitudeRad), DetSeries.Sin(backLon), 8);
                Assert.Equal(DetSeries.Cos(longitudeRad), DetSeries.Cos(backLon), 8);
            }
        }
    }

    [Fact]
    public void ExportCarriesCosmology()
    {
        WorldExport export = HistoryRun.Execute(TestWorlds.Small()).ToExport();

        Assert.Equal(WorldExport.CurrentSchemaVersion, export.SchemaVersion);
        ExportCosmology cosmology = export.World.Cosmology;
        Assert.True(cosmology.IsHabitable);
        Assert.NotEmpty(cosmology.Checks);
        Assert.Contains(cosmology.Companions, body => body.Role == CompanionRole.ShepherdGiant);
        Assert.NotNull(cosmology.Galaxy);
        Assert.True(cosmology.Galaxy.CanHostIronCore);
        Assert.NotNull(cosmology.Orientation);
        Assert.InRange(cosmology.Orientation.PoleTiltFromGalacticPoleDeg, 0.0, 90.0);
        Assert.True(cosmology.MeanDensityEarth > 0.0);
        Assert.All(cosmology.Companions, body =>
        {
            Assert.NotNull(body.RoleLabel);
            Assert.NotNull(body.Moons);
            if (body.Appearance is { Ring: { } ring })
            {
                Assert.NotEmpty(ring.CompositionLabel);
                Assert.True(body.Appearance.RingBrightnessBoostMagnitudes <= 0.0);
            }
        });
        Assert.All(cosmology.HomeMoons, moon => Assert.NotEmpty(moon.Name));
        Assert.InRange(cosmology.Comets.Count, 2, 5);
        Assert.All(cosmology.Comets, comet =>
        {
            Assert.True(comet.AphelionAu > comet.PerihelionAu);
            Assert.InRange(comet.Eccentricity, 0.05, 0.999);
        });
    }

    [Fact]
    public void CometStreamDoesNotReshuffleTheLocalSystem()
    {
        WorldCosmology cosmology = WorldCosmology.From(42);
        Assert.Equal(cosmology.StarClass, WorldCosmology.From(42).StarClass);
        Assert.Equal(cosmology.OrbitalDistanceAu, WorldCosmology.From(42).OrbitalDistanceAu);
        Assert.Equal(cosmology.Companions, WorldCosmology.From(42).Companions);
        Assert.NotEmpty(cosmology.Comets);
    }

    [Fact]
    public void MassLuminosityAndLifespanMatchReferenceFormulas()
    {
        const double mass = 1.0;
        Assert.Equal(1.0, WorldCosmology.MassLuminosity(mass), 3);
        Assert.Equal(10.0, WorldCosmology.StarLifespan(mass), 3);

        (double inner, double outer) = WorldCosmology.HabitableZone(1.0);
        Assert.Equal(0.953, inner, 2);
        Assert.Equal(1.373, outer, 2);
        Assert.Equal(2.7, WorldCosmology.SnowLine(1.0), 2);
    }

    [Fact]
    public void DeepTimeIsOrderedAndLeavesRoomForEarlierStellarGenerations()
    {
        var stages = new HashSet<StellarNextStage>();

        for (ulong seed = 1; seed <= 256; seed++)
        {
            WorldCosmology cosmology = WorldCosmology.From(seed);
            CosmicChronology time = cosmology.Chronology;
            stages.Add(time.NextStage);

            Assert.Equal(CosmicChronology.ObservableUniverseAgeGyr, time.UniverseAgeGyr);
            Assert.True(time.UniverseAgeGyr > time.GalaxyFormationLookbackGyr);
            Assert.True(time.GalaxyFormationLookbackGyr > time.StarFormationLookbackGyr);
            Assert.True(time.StarFormationLookbackGyr > time.WorldFormationLookbackGyr);
            Assert.True(time.WorldFormationLookbackGyr > 0.0);
            Assert.True(
                time.PriorStellarEnrichmentGyr >= CosmicChronology.MinimumPriorEnrichmentGyr,
                $"Seed {seed} allowed only {time.PriorStellarEnrichmentGyr:F2} Gyr for enrichment.");
            Assert.InRange(
                time.WorldFormationDelayMyr,
                CosmicChronology.MinimumWorldFormationDelayMyr,
                CosmicChronology.MaximumWorldFormationDelayMyr);
            Assert.Equal(
                cosmology.StarLifespanGyr - time.StarFormationLookbackGyr,
                time.MainSequenceRemainingGyr,
                9);
            Assert.True(time.MainSequenceRemainingGyr > 0.0);
            Assert.Contains("white dwarf", time.StellarFuture, StringComparison.Ordinal);
            Assert.Contains("not explode as a supernova", time.StellarFuture, StringComparison.Ordinal);

            StellarNextStage expected = cosmology.StarMassSolar
                <= CosmicChronology.BlueDwarfMaximumMassSolar
                    ? StellarNextStage.BlueDwarf
                    : StellarNextStage.Subgiant;
            Assert.Equal(expected, time.NextStage);
        }

        Assert.Contains(StellarNextStage.BlueDwarf, stages);
        Assert.Contains(StellarNextStage.Subgiant, stages);
    }

    [Fact]
    public void ChronologyHasItsOwnStreamAndDoesNotMoveTheSystem()
    {
        WorldCosmology first = WorldCosmology.From(42);
        WorldCosmology again = WorldCosmology.From(42);

        Assert.Equal(first.Chronology, again.Chronology);
        Assert.Equal(first.StarClass, again.StarClass);
        Assert.Equal(first.OrbitalDistanceAu, again.OrbitalDistanceAu);
        Assert.Equal(first.Companions, again.Companions);
        Assert.Equal(first.Comets, again.Comets);
    }
}
