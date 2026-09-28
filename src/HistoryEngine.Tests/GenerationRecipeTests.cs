using System.Text.Json;
using HistoryEngine.Serialization;
using HistoryEngine.World;
using Xunit;

namespace HistoryEngine.Tests;

public sealed class GenerationRecipeTests
{
    [Fact]
    public void ExportRetainsInitialCivilizationsAndFullWidthSeed()
    {
        var config = new WorldConfig { Seed = ulong.MaxValue, Years = 0, InitialCivilizations = 3 };
        var export = HistoryRun.Execute(config).ToExport();
        string json = WorldExporter.ToJson(export);
        using var document = JsonDocument.Parse(json);
        var meta = document.RootElement.GetProperty("meta");
        Assert.Equal(ulong.MaxValue, meta.GetProperty("seed").GetUInt64());
        Assert.Equal(3, meta.GetProperty("generation").GetProperty("initialCivilizations").GetInt32());
        Assert.Equal(export.Meta.Generation, WorldExporter.FromJson(json).Meta.Generation);
        Assert.Null(export.Meta.Generation!.UnsupportedReason);
    }

    [Fact]
    public void FormParametersDoNotMakeARecipeUnsupported()
    {
        var config = new WorldConfig
        {
            Seed = 99, Years = 450, InitialCivilizations = 12,
            WorldSize = 8192, EastWestPeriodic = true,
        };
        var recipe = ExportGenerationRecipe.FromConfig(config);
        Assert.Null(recipe.UnsupportedReason);
        Assert.Equal(12, recipe.InitialCivilizations);
    }

    [Fact]
    public void EveryCustomConfigurationIsRefusedRatherThanDefaulted()
    {
        var baseline = new WorldConfig();
        WorldConfig[] custom =
        [
            baseline with { StartYear = 10 }, baseline with { RegionSize = 64 },
            baseline with { TerrainStride = 128 }, baseline with { HydrologyStride = 32 },
            baseline with { UnitsPerTravelDay = 12.00001 },
            baseline with { Calendar = new Calendar(365, 5) },
            baseline with { MapRasterResolution = 64 },
            baseline with { Terrain = baseline.Terrain with { ContinentScale = 1234 } },
        ];
        foreach (var config in custom)
        {
            Assert.NotEqual(baseline, config);
            Assert.Contains("original CLI", ExportGenerationRecipe.FromConfig(config).UnsupportedReason);
        }
    }

    [Fact]
    public void ExternalTerrainKeepsItsContentIdentityAndRefusesProceduralRerun()
    {
        var recipe = ExportGenerationRecipe.FromConfig(new WorldConfig { TerrainSource = "raster:abc123" });
        Assert.Equal("raster:abc123", recipe.TerrainSource);
        Assert.Contains("same terrain files", recipe.UnsupportedReason);
    }
}
