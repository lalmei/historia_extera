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
