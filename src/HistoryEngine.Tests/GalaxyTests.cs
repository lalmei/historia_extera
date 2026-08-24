using HistoryEngine.Serialization;
using HistoryEngine.World;
using Xunit;

namespace HistoryEngine.Tests;

/// <summary>Host-galaxy placement rolled from the seed, independent of the local system.</summary>
public sealed class GalaxyTests
{
    [Fact]
    public void SameSeedAlwaysProducesTheSameGalaxy()
    {
        HostGalaxy first = HostGalaxy.From(42);
        HostGalaxy again = HostGalaxy.From(42);
        Assert.Equal(first, again);
    }

    [Fact]
    public void GalaxyStreamDoesNotReshuffleTheLocalSystem()
    {
        WorldCosmology cosmology = WorldCosmology.From(42);
        Assert.Equal(cosmology.Galaxy, HostGalaxy.From(42));
        Assert.Equal(cosmology.StarClass, WorldCosmology.From(42).StarClass);
        Assert.Equal(cosmology.OrbitalDistanceAu, WorldCosmology.From(42).OrbitalDistanceAu);
    }

    [Fact]
    public void ADifferentSeedProducesADifferentSite()
    {
        HostGalaxy first = HostGalaxy.From(1);
        HostGalaxy second = HostGalaxy.From(2);
        Assert.NotEqual(first.Location.GalactocentricRadiusKpc, second.Location.GalactocentricRadiusKpc);
        Assert.NotEqual(first.Location.AzimuthRad, second.Location.AzimuthRad);
    }

    [Fact]
    public void EveryRolledWorldSitsInAHabitableMetalRichSite()
    {
        for (ulong seed = 1; seed <= 250; seed++)
        {
            WorldCosmology cosmology = WorldCosmology.From(seed);
            HostGalaxy galaxy = cosmology.Galaxy;

            Assert.True(
                HostGalaxy.IsHabitable(galaxy.Blueprint, galaxy.Location),
                $"seed {seed} left the galactic habitable zone");
            Assert.True(galaxy.CanHostIronCore, $"seed {seed} lacked iron for a core");
            Assert.True(galaxy.CanHostOres, $"seed {seed} lacked metals for ores");
            Assert.InRange(
                HostGalaxy.StructuralRadiusKpc(galaxy.Blueprint, galaxy.Location),
                galaxy.Blueprint.InnerHabitableRadiusKpc,
                galaxy.Blueprint.OuterHabitableRadiusKpc);
        }
    }

    [Fact]
    public void EllipticalsAreRareSpheroidsWithoutArms()
    {
        HostGalaxy? elliptical = null;
        int ellipticals = 0;
        const int samples = 800;
        for (ulong seed = 1; seed <= samples; seed++)
        {
            HostGalaxy galaxy = HostGalaxy.From(seed);
            if (galaxy.Blueprint.Morphology != GalaxyMorphology.Elliptical)
            {
                continue;
            }

            ellipticals++;
            elliptical ??= galaxy;
            Assert.Equal(0, galaxy.Blueprint.SpiralArmCount);
            Assert.False(galaxy.Location.InSpiralArm);
            Assert.True(galaxy.Blueprint.SersicIndex >= 3.0);
            Assert.True(HostGalaxy.IsHabitable(galaxy.Blueprint, galaxy.Location));
            Assert.True(galaxy.CanHostOres);
        }

        Assert.NotNull(elliptical);
        Assert.InRange(ellipticals, 1, samples / 8);
    }

    [Fact]
    public void MeanIronFallsTowardTheOuterDisk()
    {
        var galaxy = new GalaxyBlueprint(
            GalaxyMorphology.BarredSpiral,
            StellarMassSolar: 6.0e10,
            DiskScaleLengthKpc: 3.0,
            ThinDiskScaleHeightPc: 300.0,
            BulgeToDiskMass: 0.3,
            SolarAnalogMetallicityFeH: 0.0,
            MetallicityGradientDexPerKpc: -0.06,
            MetallicityScatterDex: 0.1,
            SpiralArmCount: 4,
            SpiralPitchDeg: 12.0,
            InnerHabitableRadiusKpc: 6.0,
            OuterHabitableRadiusKpc: 12.0,
            SersicIndex: 1.0,
            AxisRatio: 0.1,
            MetallicityReferenceRadiusKpc: 8.0);

        Assert.Equal(0.0, HostGalaxy.MeanFeH(galaxy, 8.0), 6);
        Assert.True(HostGalaxy.MeanFeH(galaxy, 12.0) < HostGalaxy.MeanFeH(galaxy, 8.0));
        Assert.True(HostGalaxy.MeanFeH(galaxy, 4.0) > HostGalaxy.MeanFeH(galaxy, 8.0));
    }

    [Theory]
    [InlineData(-0.51, false, false)]
    [InlineData(-0.40, true, false)]
    [InlineData(-0.30, true, true)]
    [InlineData(0.00, true, true)]
    public void IronAndOreFloorsMatchTheGeologicalGates(double feH, bool iron, bool ores)
    {
        Assert.Equal(iron, HostGalaxy.CanHostIron(feH));
        Assert.Equal(ores, HostGalaxy.CanHostOre(feH));
    }

    [Fact]
    public void ExportCarriesTheHostGalaxy()
    {
        WorldExport export = HistoryRun.Execute(TestWorlds.Small()).ToExport();
        ExportGalaxy galaxy = export.World.Cosmology.Galaxy;
        Assert.True(galaxy.CanHostIronCore);
        Assert.True(galaxy.CanHostOres);
        Assert.InRange(
            galaxy.Location.GalactocentricRadiusKpc,
            galaxy.InnerHabitableRadiusKpc,
            galaxy.OuterHabitableRadiusKpc);
        Assert.Contains(export.World.Cosmology.Checks, check => check.Label == "Galactic habitable zone");
    }
}
