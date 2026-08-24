using HistoryEngine;
using HistoryEngine.Events;
using HistoryEngine.Naming;
using HistoryEngine.Serialization;
using HistoryEngine.World;
using Xunit;

namespace HistoryEngine.Tests;

/// <summary>
/// The world's own name: planet or moon, unique to the seed, independent of the simulation.
/// </summary>
public sealed class WorldFlavourTests
{
    [Fact]
    public void SameSeedAlwaysProducesTheSameDesignation()
    {
        WorldFlavour first = WorldFlavour.From(42, new MarkovNameGenerator(42));
        WorldFlavour again = WorldFlavour.From(42, new MarkovNameGenerator(42));

        AssertSameFlavour(first, again);
        Assert.False(string.IsNullOrWhiteSpace(first.Designation));
        Assert.False(string.IsNullOrWhiteSpace(first.Name));
    }

    /// <summary>
    /// Stretching a run, or asking for a different number of civilizations, cannot rename the
    /// world. The designation is how a list of histories is labelled; if it moved with the
    /// knobs, the same seed would appear under two names.
    /// </summary>
    [Fact]
    public void DesignationDependsOnTheSeedAlone()
    {
        WorldFlavour shortRun = WorldFlavour.From(7, new MarkovNameGenerator(7));
        WorldFlavour longRun = WorldFlavour.From(7, new MarkovNameGenerator(7));

        AssertSameFlavour(shortRun, longRun);

        WorldFlavour built = WorldBuilder.Create(TestWorlds.Small(7) with { Years = 1 }).Flavour;
        AssertSameFlavour(shortRun, built);
    }

    [Fact]
    public void DifferentSeedsProduceDifferentDesignations()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (ulong seed = 1; seed <= 256; seed++)
        {
            WorldFlavour flavour = WorldFlavour.From(seed, new MarkovNameGenerator(seed));
            Assert.True(
                seen.Add(flavour.Designation),
                $"Seed {seed} reused {flavour.Designation}, which another seed already claimed.");
        }
    }

    [Fact]
    public void PlanetsAndMoonsAreBothReachableAndWellFormed()
    {
        var kinds = new HashSet<WorldKind>();
        int compoundPlanets = 0;

        for (ulong seed = 1; seed <= 80; seed++)
        {
            WorldFlavour flavour = WorldFlavour.From(seed, new MarkovNameGenerator(seed));
            kinds.Add(flavour.Kind);

            if (flavour.Kind == WorldKind.Planet)
            {
                Assert.Null(flavour.ParentName);
                Assert.Null(flavour.MoonIndex);
                Assert.StartsWith("The planet " + flavour.Name, flavour.Designation, StringComparison.Ordinal);

                if (flavour.Designation.Contains(" of the ", StringComparison.Ordinal)
                    && flavour.Designation.EndsWith(" system", StringComparison.Ordinal))
                {
                    compoundPlanets++;
                }
                else
                {
                    Assert.Equal("The planet " + flavour.Name, flavour.Designation);
                }
            }
            else
            {
                Assert.False(string.IsNullOrWhiteSpace(flavour.ParentName));
                Assert.Equal(flavour.Cosmology.HabitableMoonIndex, flavour.MoonIndex);
                Assert.InRange(flavour.MoonIndex ?? 0, 1, flavour.Cosmology.Moons.Count);
                Assert.True(flavour.Cosmology.Moons.Count >= flavour.MoonIndex);
                Assert.Equal(
                    flavour.Name + ", the " + WorldFlavour.Ordinal(flavour.MoonIndex!.Value)
                    + " moon of " + flavour.ParentName,
                    flavour.Designation);
            }
        }

        Assert.Contains(WorldKind.Planet, kinds);
        Assert.Contains(WorldKind.Moon, kinds);
        Assert.True(compoundPlanets > 0, "No planet was named as part of a system.");
    }

    [Theory]
    [InlineData(1, "1st")]
    [InlineData(2, "2nd")]
    [InlineData(3, "3rd")]
    [InlineData(4, "4th")]
    [InlineData(11, "11th")]
    [InlineData(12, "12th")]
    [InlineData(13, "13th")]
    [InlineData(21, "21st")]
    [InlineData(22, "22nd")]
    [InlineData(23, "23rd")]
    public void OrdinalsAreEnglishAndInvariant(int n, string expected)
    {
        Assert.Equal(expected, WorldFlavour.Ordinal(n));
    }

    [Fact]
    public void WorldCreatedNamesTheWorldAndExportCarriesIt()
    {
        HistoryRun run = HistoryRun.Execute(TestWorlds.Small());
        WorldExport export = run.ToExport();

        HistoryEvent created = Assert.Single(
            run.World.Chronicle.Events, entry => entry.Kind == EventKind.WorldCreated);

        Assert.Equal(run.World.Flavour.Designation, created.Data!["designation"]);
        Assert.Equal(run.World.Flavour.Name, created.Data["name"]);
        Assert.Equal(run.World.Flavour.Kind.ToString(), created.Data["kind"]);
        Assert.Contains(run.World.Flavour.Designation, run.World.Narrate(created));

        Assert.Equal(run.World.Flavour.Name, export.World.Name);
        Assert.Equal(run.World.Flavour.Kind, export.World.Kind);
        Assert.Equal(run.World.Flavour.Designation, export.World.Designation);
        Assert.Equal(run.World.Flavour.ParentName, export.World.ParentName);
        Assert.Equal(run.World.Flavour.MoonIndex, export.World.MoonIndex);
        Assert.Equal(run.World.Flavour.Cosmology.IsHabitable, export.World.Cosmology.IsHabitable);
        Assert.Equal(run.World.Flavour.Cosmology.Companions.Count, export.World.Cosmology.Companions.Count);
        Assert.Equal(WorldExport.CurrentSchemaVersion, export.SchemaVersion);
    }

    private static void AssertSameFlavour(WorldFlavour expected, WorldFlavour actual)
    {
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Designation, actual.Designation);
        Assert.Equal(expected.ParentName, actual.ParentName);
        Assert.Equal(expected.MoonIndex, actual.MoonIndex);
        Assert.Equal(expected.Cosmology.StarClass, actual.Cosmology.StarClass);
        Assert.Equal(expected.Cosmology.OrbitalDistanceAu, actual.Cosmology.OrbitalDistanceAu);
        Assert.Equal(expected.Cosmology.Galaxy, actual.Cosmology.Galaxy);
        Assert.Equal(expected.Cosmology.Comets, actual.Cosmology.Comets);
        Assert.Equal(expected.Cosmology.Companions.Count, actual.Cosmology.Companions.Count);
        for (int i = 0; i < expected.Cosmology.Companions.Count; i++)
        {
            Assert.Equal(expected.Cosmology.Companions[i], actual.Cosmology.Companions[i]);
        }
    }
}
