using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Terrain;
using HistoryEngine.World;
using Xunit;

namespace HistoryEngine.Tests;

public sealed class EntityIdTests
{
    [Theory]
    [InlineData(EntityKind.Civilization, 3, "civ:3")]
    [InlineData(EntityKind.Figure, 1204, "fig:1204")]
    [InlineData(EntityKind.Settlement, 0, "set:0")]
    [InlineData(EntityKind.Region, 99, "reg:99")]
    public void FormatsAndParses(EntityKind kind, int index, string expected)
    {
        var id = new EntityId(kind, index);

        Assert.Equal(expected, id.ToString());
        Assert.Equal(id, EntityId.Parse(expected));
    }

    [Fact]
    public void NoneRoundTrips()
    {
        Assert.Equal("none", EntityId.None.ToString());
        Assert.Equal(EntityId.None, EntityId.Parse("none"));
        Assert.True(EntityId.None.IsNone);
    }

    [Theory]
    [InlineData("")]
    [InlineData("civ")]
    [InlineData("civ:")]
    [InlineData(":3")]
    [InlineData("nope:3")]
    [InlineData("civ:-1")]
    [InlineData("civ:abc")]
    public void RejectsMalformedInput(string text) =>
        Assert.False(EntityId.TryParse(text, out _));

    /// <summary>
    /// A mistyped reference must fail loudly rather than resolve to something else.
    /// </summary>
    /// <remarks>
    /// The reason ids carry a kind prefix at all. With bare integers, reading a figure id as a
    /// settlement id returns an unrelated but perfectly valid entity, and the chronicle quietly
    /// says the wrong thing.
    /// </remarks>
    [Fact]
    public void TableRejectsIdsOfTheWrongKind()
    {
        var table = new EntityTable<Culture>(EntityKind.Culture);
        table.Add(new Culture(EntityId.Culture(0), "test", 1, CultureValues.Roll(new Pcg32(1)),
            GovernmentForm.Monarchy));

        Assert.Throws<ArgumentException>(() => table[EntityId.Figure(0)]);
    }

    [Fact]
    public void OrderIsByKindThenIndex()
    {
        var ids = new List<EntityId>
        {
            EntityId.Settlement(5), EntityId.Civilization(2),
            EntityId.Settlement(1), EntityId.Civilization(10),
        };

        ids.Sort();

        Assert.Equal(
            new[]
            {
                EntityId.Civilization(2), EntityId.Civilization(10),
                EntityId.Settlement(1), EntityId.Settlement(5),
            },
            ids);
    }
}

public sealed class RngTests
{
    [Fact]
    public void SameSeedProducesSameStream()
    {
        var a = new Pcg32(12345);
        var b = new Pcg32(12345);

        for (int i = 0; i < 100; i++) Assert.Equal(a.NextUInt(), b.NextUInt());
    }

    [Fact]
    public void BoundedIntegersStayInRange()
    {
        var rng = new Pcg32(5);

        for (int i = 0; i < 10_000; i++)
        {
            Assert.InRange(rng.NextInt(7), 0, 6);
            Assert.InRange(rng.NextInt(-3, 4), -3, 3);
        }
    }

    /// <summary>
    /// Bounded draws must be unbiased.
    /// </summary>
    /// <remarks>
    /// Plain modulo would over-represent low values. A slightly loaded die is not a visible bug —
    /// it just produces a world with subtly too many hamlets and too few wars, and nothing ever
    /// points at the RNG.
    /// </remarks>
    [Fact]
    public void BoundedDrawsAreNotBiased()
    {
        var rng = new Pcg32(77);
        var buckets = new int[6];
        const int draws = 600_000;

        for (int i = 0; i < draws; i++) buckets[rng.NextInt(6)]++;

        int expected = draws / 6;
        foreach (int count in buckets)
        {
            Assert.InRange(count, expected * 0.97, expected * 1.03);
        }
    }

    [Fact]
    public void DoublesStayInUnitInterval()
    {
        var rng = new Pcg32(9);

        for (int i = 0; i < 100_000; i++)
        {
            double value = rng.NextDouble();
            Assert.True(value >= 0.0 && value < 1.0, $"NextDouble returned {value}");
        }
    }

    [Fact]
    public void ChanceHandlesCertainty()
    {
        var rng = new Pcg32(3);

        Assert.False(rng.Chance(0.0));
        Assert.False(rng.Chance(-1.0));
        Assert.True(rng.Chance(1.0));
        Assert.True(rng.Chance(2.0));
    }

    [Fact]
    public void ZeroOrNegativeBoundsAreRejected()
    {
        var rng = new Pcg32(1);

        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(-5));
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(5, 5));
    }
}

public sealed class DetMapTests
{
    /// <summary>
    /// String keys must order ordinally, not by culture.
    /// </summary>
    /// <remarks>
    /// <c>Comparer&lt;string&gt;.Default</c> delegates to a culture-sensitive comparison, so a
    /// sorted map keyed by string would enumerate differently under different locales — and the
    /// export's byte layout would depend on the machine that produced it. This is the specific
    /// back door that would have defeated the whole point of the type.
    /// </remarks>
    [Fact]
    public void StringKeysOrderOrdinally()
    {
        var map = new DetMap<string, int>();

        foreach (string key in new[] { "b", "A", "a", "B", "_z", "Ä" }) map[key] = 1;

        var keys = map.Keys.ToArray();
        var expected = new[] { "b", "A", "a", "B", "_z", "Ä" }
            .OrderBy(k => k, StringComparer.Ordinal).ToArray();

        Assert.Equal(expected, keys);
    }

    [Fact]
    public void EnumerationIsSortedRegardlessOfInsertionOrder()
    {
        var forward = new DetMap<EntityId, double>();
        var backward = new DetMap<EntityId, double>();

        for (int i = 0; i < 20; i++) forward[EntityId.Civilization(i)] = i;
        for (int i = 19; i >= 0; i--) backward[EntityId.Civilization(i)] = i;

        Assert.Equal(forward.Keys.ToArray(), backward.Keys.ToArray());
    }

    [Fact]
    public void OverwritesRatherThanDuplicates()
    {
        var map = new DetMap<string, int> { ["x"] = 1, ["x"] = 2 };

        Assert.Equal(1, map.Count);
        Assert.Equal(2, map["x"]);
    }

    [Fact]
    public void RemovesAndReports()
    {
        var map = new DetMap<string, int> { ["x"] = 1 };

        Assert.True(map.Remove("x"));
        Assert.False(map.Remove("x"));
        Assert.Equal(0, map.Count);
        Assert.Throws<KeyNotFoundException>(() => map["x"]);
    }
}

public sealed class DetMathTests
{
    [Fact]
    public void IntPowMatchesRepeatedMultiplication()
    {
        Assert.Equal(1.0, DetMath.IntPow(2.5, 0));
        Assert.Equal(2.5, DetMath.IntPow(2.5, 1));
        Assert.Equal(2.5 * 2.5 * 2.5, DetMath.IntPow(2.5, 3));
        Assert.Equal(1.0 / (2.0 * 2.0), DetMath.IntPow(2.0, -2));
    }

    [Fact]
    public void InterpolationClampsAtBothEnds()
    {
        Assert.Equal(0.0, DetMath.InverseLerp(10.0, 20.0, 5.0));
        Assert.Equal(1.0, DetMath.InverseLerp(10.0, 20.0, 25.0));
        Assert.Equal(0.5, DetMath.InverseLerp(10.0, 20.0, 15.0));

        // A zero-width range must not divide by zero.
        Assert.Equal(0.0, DetMath.InverseLerp(10.0, 10.0, 10.0));
    }

    [Fact]
    public void SmoothStepIsPinnedAtItsEnds()
    {
        Assert.Equal(0.0, DetMath.SmoothStep(0.0));
        Assert.Equal(1.0, DetMath.SmoothStep(1.0));
        Assert.Equal(0.5, DetMath.SmoothStep(0.5));

        Assert.Equal(0.0, DetMath.SmootherStep(0.0));
        Assert.Equal(1.0, DetMath.SmootherStep(1.0));
        Assert.Equal(0.5, DetMath.SmootherStep(0.5), 12);
    }
}

public sealed class ConfigTests
{
    /// <summary>
    /// A new simulation-affecting config field must be added to the hash.
    /// </summary>
    /// <remarks>
    /// Without this, adding a field is silently safe-looking: the build passes, the simulation
    /// honours the field, and two worlds generated with different values both claim the same
    /// config hash. Every provenance guarantee in the export quietly stops being true.
    ///
    /// <para>Reflection here is a deliberate exception to the engine's own ban on it — this runs in
    /// a test, not on a decision path, and the point is precisely to detect a field the
    /// hand-written hash does not know about.</para>
    /// </remarks>
    [Fact]
    public void EveryConfigFieldIsAccountedFor()
    {
        var simulationAffecting = new[]
        {
            nameof(WorldConfig.Years), nameof(WorldConfig.StartYear), nameof(WorldConfig.WorldSize),
            nameof(WorldConfig.RegionSize), nameof(WorldConfig.TerrainStride),
            nameof(WorldConfig.HydrologyStride), nameof(WorldConfig.InitialCivilizations),
        };

        // Excluded on purpose: Seed is exported separately as the run's other half, and
        // MapRasterResolution only affects the presentation raster.
        var excluded = new[] { nameof(WorldConfig.Seed), nameof(WorldConfig.MapRasterResolution) };

        var computed = new[] { nameof(WorldConfig.Bounds), nameof(WorldConfig.ConfigHash) };

        var actual = typeof(WorldConfig).GetProperties()
            .Select(p => p.Name)
            .Where(name => !computed.Contains(name))
            .Where(name => name != nameof(WorldConfig.Terrain))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var known = simulationAffecting.Concat(excluded)
            .OrderBy(name => name, StringComparer.Ordinal).ToArray();

        Assert.Equal(known, actual);

        int terrainFields = typeof(TerrainSettings).GetProperties().Length;
        Assert.Equal(WorldConfig.HashedFieldCount, simulationAffecting.Length + terrainFields);
    }

    [Fact]
    public void HashChangesWithSimulationAffectingFields()
    {
        var baseline = new WorldConfig();

        Assert.NotEqual(baseline.ConfigHash, (baseline with { Years = 301 }).ConfigHash);
        Assert.NotEqual(baseline.ConfigHash, (baseline with { WorldSize = 2048 }).ConfigHash);
        Assert.NotEqual(baseline.ConfigHash, (baseline with { InitialCivilizations = 9 }).ConfigHash);
        Assert.NotEqual(
            baseline.ConfigHash,
            (baseline with { Terrain = new TerrainSettings { ContinentScale = 1234 } }).ConfigHash);
    }

    /// <summary>The seed is not part of the config hash — the two are reported separately.</summary>
    [Fact]
    public void HashIgnoresSeedAndPresentation()
    {
        var baseline = new WorldConfig();

        Assert.Equal(baseline.ConfigHash, (baseline with { Seed = 999 }).ConfigHash);
        Assert.Equal(baseline.ConfigHash, (baseline with { MapRasterResolution = 512 }).ConfigHash);
    }

    [Theory]
    [InlineData(-1, 4096, 128, 256)]
    [InlineData(10, 0, 128, 256)]
    [InlineData(10, 4096, 0, 256)]
    [InlineData(10, 100, 200, 256)]
    [InlineData(10, 4096, 128, 100)]
    public void ValidationRejectsImpossibleConfigurations(
        int years, int size, int regionSize, int stride)
    {
        var config = new WorldConfig
        {
            Years = years,
            WorldSize = size,
            RegionSize = regionSize,
            TerrainStride = stride,
        };

        Assert.Throws<InvalidOperationException>(() => config.Validate());
    }
}
