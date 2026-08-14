using System.Text;
using HistoryEngine.Terrain;
using HistoryEngine.World;
using Xunit;

namespace HistoryEngine.Tests;

/// <summary>
/// Phase 2's proving ground: terrain that came from somewhere else entirely.
/// </summary>
/// <remarks>
/// <para>The whole three-phase plan rests on one claim — the simulation runs against an abstract
/// terrain interface, and the backend can be swapped without touching simulation code. Phase 1
/// cannot test that claim, because it has only ever had one backend, written alongside the
/// interface by the same hand, producing every field the interface asks for. These tests are the
/// first time the claim is put to a backend that was not designed around it: a set of rasters
/// with finite resolution, a foreign datum, units it does not carry, and quite possibly nothing
/// but elevation.</para>
///
/// <para>What is being checked is not that a raster world <em>reproduces</em> a noise world. It
/// cannot, and <see cref="BakedTerrainDoesNotReproduceTheHistoryItCameFrom"/> pins that it does
/// not. What is checked is that terrain survives the crossing — the shoreline is where the
/// generator said it was, the fields nobody measured are declared as such, and a history runs
/// over it at the same sampling cost as before.</para>
/// </remarks>
public sealed class RasterTerrainTests
{
    // ─── The codec ────────────────────────────────────────────────────────────────

    [Fact]
    public void BinaryAndAsciiRastersParseIdentically()
    {
        // The same 2x2 plane, both ways round. Generators emit whichever they please.
        RasterGrid binary = Netpbm.Read(BinaryPgm(2, 2, 255, new[] { 0, 85, 170, 255 }));
        RasterGrid ascii = Netpbm.Read(
            Encoding.ASCII.GetBytes("P2\n# painted by hand\n2 2\n255\n0 85\n170 255\n"));

        Assert.Equal(binary.Width, ascii.Width);

        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(binary.Values[i], ascii.Values[i], 6);
        }

        Assert.Equal(0f, ascii.At(0, 0));
        Assert.Equal(1f, ascii.At(1, 1));
    }

    /// <summary>
    /// A written plane must read back as itself.
    /// </summary>
    /// <remarks>
    /// The tolerance is one step of the 16-bit scale, which is what the format costs and the
    /// reason it is written at 16 bits rather than 8 — at a byte per sample this would be
    /// 1/255, or thirteen metres of relief on a world with any mountains in it.
    /// </remarks>
    [Fact]
    public void WritingAndReadingAPlaneIsLossless()
    {
        var values = new float[64 * 48];
        for (int i = 0; i < values.Length; i++) values[i] = i / (float)(values.Length - 1);

        var original = new RasterGrid(64, 48, values);

        using var buffer = new MemoryStream();
        Netpbm.Write(buffer, original);
        RasterGrid reloaded = Netpbm.Read(buffer.ToArray());

        Assert.Equal(original.Width, reloaded.Width);
        Assert.Equal(original.Height, reloaded.Height);

        for (int i = 0; i < values.Length; i++)
        {
            Assert.True(
                Math.Abs(original.Values[i] - reloaded.Values[i]) <= 1.0 / Netpbm.WriteMaxValue,
                $"Value {i} drifted by more than one quantisation step.");
        }
    }

    [Theory]
    [InlineData("P3\n2 2\n255\n0 0 0 0", "P2 or P5")]
    [InlineData("", "magic")]
    [InlineData("P5\n2 2\n255\n", "Truncated")]
    public void AMalformedRasterSaysWhatIsWrongWithIt(string content, string expected)
    {
        FormatException error = Assert.Throws<FormatException>(
            () => Netpbm.Read(Encoding.ASCII.GetBytes(content)));

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    // ─── The datum contract ───────────────────────────────────────────────────────

    /// <summary>
    /// Sea level reads exactly zero, whatever scale the generator used.
    /// </summary>
    /// <remarks>
    /// <para>The single most important property of the raster backend, and the one that would
    /// fail silently. Heights are metres relative to sea level by contract, and every consumer
    /// leans on it: <c>IsSubmerged</c> is <c>Height &lt; 0</c>, ocean is negative height, region
    /// habitability and fertility both key off it. A generator that puts its shoreline at 20 on
    /// a 0..100 scale, mapped through a single linear range, would land the coastline hundreds of
    /// metres off — producing a world that looks entirely plausible with every settlement in the
    /// wrong place.</para>
    ///
    /// <para>So the manifest names the shoreline and the loader scales the two sides of it
    /// separately, which makes the contract hold by construction rather than by arithmetic
    /// coincidence.</para>
    /// </remarks>
    [Fact]
    public void SeaLevelIsZeroWhateverTheGeneratorsScaleWas()
    {
        // A shoreline at a fifth of the range, as a generator with a shallow ocean would write.
        using var fixture = RasterFixture.Create(
            nameof(SeaLevelIsZeroWhateverTheGeneratorsScaleWas),
            width: 5,
            height: 1,
            heightValues: new[] { 0, 0.1, 0.2, 0.6, 1.0 },
            manifest: """
                { "worldSize": 5,
                  "height": { "file": "height.pgm", "min": -1000, "max": 3000, "seaLevel": 0.2 } }
                """);

        RasterTerrainSampler sampler = fixture.Load();

        Assert.Equal(-1000.0, sampler.Sample(0, 0).Height, 1);
        Assert.Equal(-500.0, sampler.Sample(1, 0).Height, 1);

        // The join. Not "close to zero" — exactly the value every downstream test keys off.
        Assert.Equal(0.0, sampler.Sample(2, 0).Height, 3);
        Assert.False(sampler.Sample(2, 0).IsSubmerged);

        // Halfway up the land range: (0.6 - 0.2) / (1 - 0.2).
        Assert.Equal(1500.0, sampler.Sample(3, 0).Height, 1);
        Assert.Equal(3000.0, sampler.Sample(4, 0).Height, 1);
    }

    /// <summary>The plane's edges land on the world's edges, so no coastline falls outside it.</summary>
    [Fact]
    public void TheRasterSpansTheWorldInclusively()
    {
        using var fixture = RasterFixture.Create(
            nameof(TheRasterSpansTheWorldInclusively),
            width: 2,
            height: 1,
            heightValues: new[] { 0.0, 1.0 },
            manifest: """
                { "worldSize": 512,
                  "height": { "file": "height.pgm", "min": -100, "max": 100, "seaLevel": 0.5 } }
                """);

        RasterTerrainSampler sampler = fixture.Load();

        Assert.Equal(-100.0, sampler.Sample(0, 0).Height, 1);
        Assert.Equal(100.0, sampler.Sample(511, 0).Height, 1);

        // Halfway across the world is halfway between the two values it was given.
        Assert.Equal(0.0, sampler.Sample(255, 0).Height, 0);
    }

    // ─── Honest capabilities ──────────────────────────────────────────────────────

    /// <summary>
    /// A bare heightmap runs a world, and says out loud that it is only a heightmap.
    /// </summary>
    /// <remarks>
    /// <para>The case that justifies <see cref="TerrainCapabilities"/> existing. Until Phase 2
    /// every backend declared <see cref="TerrainCapabilities.Standard"/>, because the noise
    /// sampler synthesises six fields as easily as one — which is precisely the trap the flags
    /// were written against. Real generators export elevation and, with luck, one climate layer.
    /// Refusing those would make the raster route useless, so the missing fields are modelled
    /// from latitude and elevation and deliberately left out of the declaration.</para>
    ///
    /// <para>The assertion that matters is the negative one: a modelled field must never be
    /// reported as measured.</para>
    /// </remarks>
    [Fact]
    public void AHeightOnlySetDeclaresOnlyHeight()
    {
        using var fixture = RasterFixture.Create(
            nameof(AHeightOnlySetDeclaresOnlyHeight),
            width: 4,
            height: 4,
            heightValues: new[] { 0.0, 0.3, 0.6, 1.0, 0.1, 0.4, 0.7, 0.9, 0.2, 0.5, 0.8, 0.95, 0.0, 0.35, 0.65, 1.0 },
            manifest: """
                { "worldSize": 256,
                  "height": { "file": "height.pgm", "min": -500, "max": 2500, "seaLevel": 0.25 } }
                """);

        RasterTerrainSampler sampler = fixture.Load();

        Assert.True(sampler.Supports(TerrainCapabilities.Height));

        Assert.False(sampler.Supports(TerrainCapabilities.Temperature));
        Assert.False(sampler.Supports(TerrainCapabilities.Rainfall));
        Assert.False(sampler.Supports(TerrainCapabilities.GeologicActivity));
        Assert.False(sampler.Supports(TerrainCapabilities.ForestDensity));
        Assert.False(sampler.Supports(TerrainCapabilities.ShrubDensity));
        Assert.False(sampler.Supports(TerrainCapabilities.Lakes));
        Assert.False(sampler.Supports(TerrainCapabilities.Rivers));

        // Undeclared does not mean unusable: the modelled fields still have to be in range, or
        // biome classification and fertility would produce nonsense from them.
        for (int x = 0; x < 256; x += 17)
        {
            TerrainSample sample = sampler.Sample(x, x);

            Assert.InRange(sample.Rainfall, 0f, 1f);
            Assert.InRange(sample.Temperature, -60f, 60f);
            Assert.InRange(sample.ForestDensity, 0f, 1f);
            Assert.InRange(sample.ShrubDensity, 0f, 1f);
            Assert.InRange(sample.GeologicActivity, 0f, 1f);
            Assert.InRange(sample.Fertility, 0f, 1f);
        }
    }

    /// <summary>A field with no defensible default range must be declared, not guessed.</summary>
    [Fact]
    public void ATemperatureLayerWithoutARangeIsRejected()
    {
        using var fixture = RasterFixture.Create(
            nameof(ATemperatureLayerWithoutARangeIsRejected),
            width: 2,
            height: 2,
            heightValues: new[] { 0.0, 1.0, 0.5, 0.5 },
            manifest: """
                { "worldSize": 128,
                  "height": { "file": "height.pgm", "min": -100, "max": 100, "seaLevel": 0.5 },
                  "temperature": { "file": "height.pgm" } }
                """);

        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(() => fixture.Load());

        Assert.Contains("temperature", error.Message, StringComparison.Ordinal);
        Assert.Contains("min", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AHeightLayerWithoutAShorelineIsRejected()
    {
        using var fixture = RasterFixture.Create(
            nameof(AHeightLayerWithoutAShorelineIsRejected),
            width: 2,
            height: 2,
            heightValues: new[] { 0.0, 1.0, 0.5, 0.5 },
            manifest: """
                { "worldSize": 128,
                  "height": { "file": "height.pgm", "min": -100, "max": 100 } }
                """);

        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(() => fixture.Load());

        Assert.Contains("seaLevel", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AManifestWithNoHeightLayerIsRejected()
    {
        using var fixture = RasterFixture.Create(
            nameof(AManifestWithNoHeightLayerIsRejected),
            width: 2,
            height: 2,
            heightValues: new[] { 0.0, 1.0, 0.5, 0.5 },
            manifest: """{ "worldSize": 128 }""");

        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(() => fixture.Load());

        Assert.Contains("height", error.Message, StringComparison.Ordinal);
    }

    // ─── The sampler contract ─────────────────────────────────────────────────────

    /// <summary>
    /// A batch must return exactly what the same points return one at a time.
    /// </summary>
    /// <remarks>
    /// <see cref="TerrainAtlas"/> primes its whole lattice through <c>SampleBatch</c> and takes
    /// single samples for permanent coordinates, so a backend whose two paths disagreed would
    /// give a settlement one terrain when it was sited and a different one forever after.
    /// </remarks>
    [Fact]
    public void BatchedAndSingleSamplesAgree()
    {
        using RasterFixture fixture = BakedFixture(nameof(BatchedAndSingleSamplesAgree), seed: 5);

        // Through the interface: SampleBatch is a default member, and the atlas only ever sees
        // the backend this way.
        ITerrainSampler sampler = fixture.Load();

        var points = new Point2[64];
        for (int i = 0; i < points.Length; i++) points[i] = new Point2(i * 37, i * 53);

        var batched = new TerrainSample[points.Length];
        sampler.SampleBatch(points, batched);

        for (int i = 0; i < points.Length; i++)
        {
            Assert.Equal(sampler.Sample(points[i].X, points[i].Z), batched[i]);
        }
    }

    // ─── The round trip ───────────────────────────────────────────────────────────

    /// <summary>
    /// Terrain baked to rasters and read back is the same terrain.
    /// </summary>
    /// <remarks>
    /// <para>The sharpest test available without shipping somebody else's map in the repository:
    /// the same world reached by two completely different routes — evaluated as noise, and read
    /// out of a file — has to agree. Any disagreement beyond quantisation is the abstraction
    /// leaking, and it would show up as a coastline in the wrong place rather than as an
    /// exception.</para>
    ///
    /// <para>The tolerance is the bake's own resolution, not the codec's. Baking at 256 over a
    /// 1024-unit world stores one value every four units and interpolates between them, so a
    /// point between two stored values is as accurate as the relief there is smooth. Height is
    /// compared as a fraction of the world's total relief for that reason; what is checked
    /// exactly is the shoreline, because land and sea is a decision rather than a measurement.</para>
    /// </remarks>
    [Fact]
    public void BakedTerrainReloadsAsTheSameTerrain()
    {
        var bounds = TerrainBounds.Square(1024);
        var source = new ProceduralTerrainSampler(11, bounds);

        using RasterFixture fixture = BakedFixture(
            nameof(BakedTerrainReloadsAsTheSameTerrain), seed: 11, size: 1024, resolution: 256);

        RasterTerrainSampler reloaded = fixture.Load();

        Assert.Equal(bounds, reloaded.Bounds);

        double worstHeight = 0.0;
        int landDisagreements = 0;
        int compared = 0;

        for (int z = 0; z < 1024; z += 13)
        {
            for (int x = 0; x < 1024; x += 13)
            {
                TerrainSample noise = source.Sample(x, z);
                TerrainSample raster = reloaded.Sample(x, z);

                worstHeight = Math.Max(worstHeight, Math.Abs(noise.Height - raster.Height));
                if (noise.IsSubmerged != raster.IsSubmerged) landDisagreements++;
                compared++;
            }
        }

        Assert.True(
            worstHeight < 60.0,
            $"Height drifted by up to {worstHeight:F1}m across the round trip, which is more " +
            "than the bake's resolution can explain. The raster is not spanning the world the " +
            "way it was written.");

        // A handful of cells straddling the shoreline may land either side of it; a systematic
        // datum error would put whole coastlines on the wrong side.
        Assert.True(
            landDisagreements <= compared / 100,
            $"{landDisagreements} of {compared} points disagreed about land versus sea. " +
            "That is a datum error, not quantisation.");
    }

    /// <summary>The reloaded set declares exactly what the source declared.</summary>
    [Fact]
    public void BakingPreservesTheDeclaredCapabilities()
    {
        var source = new ProceduralTerrainSampler(11, TerrainBounds.Square(512));

        using RasterFixture fixture = BakedFixture(
            nameof(BakingPreservesTheDeclaredCapabilities), seed: 11, size: 512, resolution: 128);

        Assert.Equal(source.Capabilities, fixture.Load().Capabilities);
    }

    /// <summary>
    /// The round trip carries the terrain, not the history.
    /// </summary>
    /// <remarks>
    /// <para>Worth pinning as a fact rather than discovering as a surprise. The two worlds are
    /// the same landscape to within a few metres, and they still produce different histories:
    /// quantisation moves a candidate site's score by a fraction, one founding lands a region
    /// over, and three centuries compound it. Nothing is broken — this is what it means for a
    /// simulation to be sensitive to its terrain, and it is the reason
    /// <see cref="WorldConfig.TerrainSource"/> has to name the rasters. A world file that
    /// recorded only the seed and the numeric config would claim two different histories were
    /// the same run.</para>
    /// </remarks>
    [Fact]
    public void BakedTerrainDoesNotReproduceTheHistoryItCameFrom()
    {
        WorldConfig config = TestWorlds.Small(seed: 11) with { WorldSize = 1024 };

        using RasterFixture fixture = BakedFixture(
            nameof(BakedTerrainDoesNotReproduceTheHistoryItCameFrom),
            seed: 11,
            size: 1024,
            resolution: 256);

        RasterTerrainSampler raster = fixture.Load();

        HistoryRun noise = HistoryRun.Execute(config);
        HistoryRun rasterRun = HistoryRun.Execute(config.WithTerrain(raster), raster);

        Assert.NotEqual(config.ConfigHash, config.WithTerrain(raster).ConfigHash);

        // Recognisably the same world: it supports a comparable number of towns.
        Assert.InRange(
            rasterRun.World.Settlements.Count,
            noise.World.Settlements.Count / 2,
            noise.World.Settlements.Count * 2);
    }

    // ─── Provenance ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A raster world's config hash identifies the rasters, and a procedural one is untouched.
    /// </summary>
    /// <remarks>
    /// Two halves of one guarantee. A raster backend's input is a set of files, so the digest has
    /// to be in the hash or "identical seed and config" stops covering the terrain. But adding it
    /// must not shift the hash of every world generated before Phase 2 existed, because those
    /// files' provenance is a claim already made — hence an empty source contributing nothing.
    /// </remarks>
    [Fact]
    public void ARasterWorldRecordsWhichRasterMadeIt()
    {
        using RasterFixture first = BakedFixture(
            nameof(ARasterWorldRecordsWhichRasterMadeIt) + "-a", seed: 3, size: 512, resolution: 128);
        using RasterFixture second = BakedFixture(
            nameof(ARasterWorldRecordsWhichRasterMadeIt) + "-b", seed: 4, size: 512, resolution: 128);

        RasterTerrainSampler a = first.Load();
        RasterTerrainSampler b = second.Load();

        var baseline = new WorldConfig();

        Assert.NotEqual(a.Digest, b.Digest);
        Assert.NotEqual(baseline.WithTerrain(a).ConfigHash, baseline.WithTerrain(b).ConfigHash);

        // Same files, loaded twice: the same world, so the same hash.
        Assert.Equal(a.Digest, first.Load().Digest);

        // And a world with no foreign backend hashes to what it always hashed to.
        Assert.Equal(baseline.ConfigHash, (baseline with { TerrainSource = string.Empty }).ConfigHash);
    }

    /// <summary>Pointing a config at a raster set adopts the extent the rasters actually cover.</summary>
    [Fact]
    public void AdoptingARasterSetAdoptsItsExtent()
    {
        using RasterFixture fixture = BakedFixture(
            nameof(AdoptingARasterSetAdoptsItsExtent), seed: 3, size: 512, resolution: 128);

        WorldConfig config = new WorldConfig { WorldSize = 4096 }.WithTerrain(fixture.Load());

        Assert.Equal(512, config.WorldSize);
    }

    // ─── The discipline, on a foreign backend ─────────────────────────────────────

    /// <summary>
    /// A history over raster terrain costs what a history over noise costs.
    /// </summary>
    /// <remarks>
    /// The claim the three-tier access pattern was built on, tested against a second backend for
    /// the first time. The sample budget is a property of how the simulation asks questions, not
    /// of who answers them, so swapping the backend must not move it — and if it did, the tiering
    /// would be measuring something about the noise sampler rather than about the simulation.
    /// </remarks>
    [Fact]
    public void AHistoryOverRasterTerrainCostsTheSameAsOverNoise()
    {
        WorldConfig config = TestWorlds.Small(seed: 11) with { WorldSize = 1024 };

        using RasterFixture fixture = BakedFixture(
            nameof(AHistoryOverRasterTerrainCostsTheSameAsOverNoise),
            seed: 11,
            size: 1024,
            resolution: 256);

        RasterTerrainSampler raster = fixture.Load();

        HistoryRun noise = HistoryRun.Execute(config);
        HistoryRun rasterRun = HistoryRun.Execute(config.WithTerrain(raster), raster);

        Assert.InRange(
            rasterRun.SimulationSamples,
            noise.SimulationSamples / 2,
            noise.SimulationSamples * 2);
    }

    /// <summary>The same rasters and the same seed produce a byte-identical export.</summary>
    [Fact]
    public void RasterTerrainIsDeterministic()
    {
        WorldConfig config = TestWorlds.Small(seed: 11) with { WorldSize = 1024 };

        using RasterFixture fixture = BakedFixture(
            nameof(RasterTerrainIsDeterministic), seed: 11, size: 1024, resolution: 128);

        string First()
        {
            RasterTerrainSampler sampler = fixture.Load();
            return Serialization.WorldExporter.Fingerprint(
                HistoryRun.Execute(config.WithTerrain(sampler), sampler).ToExport());
        }

        Assert.Equal(First(), First());
    }

    /// <summary>
    /// A partial source bakes a partial set: modelled fields do not become measured ones.
    /// </summary>
    /// <remarks>
    /// <see cref="BakingPreservesTheDeclaredCapabilities"/> bakes the procedural sampler, which
    /// declares every field, so it cannot see the difference between writing what was measured
    /// and writing whatever the sampler answered. A sampler answers every field either way — the
    /// absent ones modelled from elevation and latitude — so serialising all of them turns the
    /// model into a measurement and the reloaded set declares capabilities its source never had.
    /// </remarks>
    [Fact]
    public void BakingAPartialSamplerDoesNotInventCapabilities()
    {
        var source = new PartialSampler(
            new ProceduralTerrainSampler(11, TerrainBounds.Square(256)),
            TerrainCapabilities.Height);

        using var fixture = RasterFixture.Empty(nameof(BakingAPartialSamplerDoesNotInventCapabilities));
        TerrainRasterBake.Write(source, fixture.Directory, resolution: 64);

        Assert.Equal(TerrainCapabilities.Height, fixture.Load().Capabilities);
        Assert.False(File.Exists(Path.Combine(fixture.Directory, "temperature.pgm")));
        Assert.False(File.Exists(Path.Combine(fixture.Directory, "water.pgm")));
    }

    /// <summary>A source measuring some fields bakes exactly those, and no others.</summary>
    [Fact]
    public void BakingCarriesEverySourceCapabilityAndNoMore()
    {
        const TerrainCapabilities declared =
            TerrainCapabilities.Height | TerrainCapabilities.Temperature | TerrainCapabilities.Lakes;

        var source = new PartialSampler(
            new ProceduralTerrainSampler(11, TerrainBounds.Square(256)), declared);

        using var fixture = RasterFixture.Empty(nameof(BakingCarriesEverySourceCapabilityAndNoMore));
        TerrainRasterBake.Write(source, fixture.Directory, resolution: 64);

        Assert.Equal(declared, fixture.Load().Capabilities);
        Assert.True(File.Exists(Path.Combine(fixture.Directory, "temperature.pgm")));
        Assert.False(File.Exists(Path.Combine(fixture.Directory, "rainfall.pgm")));
    }

    /// <summary>
    /// Bounds the format cannot express are refused rather than quietly remapped.
    /// </summary>
    /// <remarks>
    /// The manifest carries one <c>worldSize</c> and the loader rebuilds a square at the origin,
    /// so a rectangular or offset sampler used to come back with its Z extent rescaled and its
    /// origin discarded — terrain that is subtly the wrong shape in the wrong place, which
    /// nothing downstream can detect.
    /// </remarks>
    [Theory]
    [InlineData(0, 0, 512, 256)]
    [InlineData(0, 0, 256, 512)]
    [InlineData(64, 0, 256, 256)]
    [InlineData(0, -128, 256, 256)]
    public void BakingRefusesBoundsTheFormatCannotCarry(int minX, int minZ, int width, int height)
    {
        var source = new PartialSampler(
            new ProceduralTerrainSampler(11, new TerrainBounds(minX, minZ, width, height)),
            TerrainCapabilities.Standard);

        using var fixture = RasterFixture.Empty(nameof(BakingRefusesBoundsTheFormatCannotCarry));

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => TerrainRasterBake.Write(source, fixture.Directory, resolution: 64));

        Assert.Contains("square world at the origin", error.Message);
        Assert.False(File.Exists(Path.Combine(fixture.Directory, "terrain.json")));
    }

    /// <summary>
    /// The summary lists a field as modelled only when something actually models it.
    /// </summary>
    /// <remarks>
    /// The CLI used to subtract measured capabilities from <see cref="TerrainCapabilities.Standard"/>,
    /// which includes lakes — so a height-only world reported lakes as modelled while the raster
    /// backend was reporting <see cref="WaterKind.None"/> above sea level and synthesising
    /// nothing. A heightmap cannot tell you whether water stands in a depression.
    /// </remarks>
    [Fact]
    public void AHeightOnlyWorldModelsClimateButNeverLakes()
    {
        TerrainCapabilities modelled = TerrainFields.ModelledFor(TerrainCapabilities.Height);

        Assert.Equal(TerrainFields.Modelled, modelled);
        Assert.False(modelled.HasFlag(TerrainCapabilities.Lakes));
        Assert.True(modelled.HasFlag(TerrainCapabilities.Temperature));
        Assert.True(modelled.HasFlag(TerrainCapabilities.ShrubDensity));

        // A backend that measures everything models nothing, and the summary line is skipped.
        Assert.Equal(
            TerrainCapabilities.None, TerrainFields.ModelledFor(TerrainCapabilities.Standard));
    }

    /// <summary>
    /// Nothing above sea level is inland water when no layer supplied any — the claim
    /// <see cref="TerrainFields.Modelled"/> rests on.
    /// </summary>
    [Fact]
    public void AHeightOnlySetReportsNoLakesAnywhere()
    {
        using RasterFixture fixture = BakedFixture(
            nameof(AHeightOnlySetReportsNoLakesAnywhere), seed: 11, size: 512, resolution: 128);

        var source = new PartialSampler(
            new ProceduralTerrainSampler(11, TerrainBounds.Square(512)), TerrainCapabilities.Height);

        using var bare = RasterFixture.Empty(nameof(AHeightOnlySetReportsNoLakesAnywhere) + "-bare");
        TerrainRasterBake.Write(source, bare.Directory, resolution: 128);

        RasterTerrainSampler sampler = bare.Load();

        for (int z = 0; z < 512; z += 16)
        {
            for (int x = 0; x < 512; x += 16)
            {
                Assert.NotEqual(WaterKind.Lake, sampler.Sample(x, z).Water);
            }
        }
    }

    // ─── Fixtures ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Wraps a sampler and declares less than it measures.
    /// </summary>
    /// <remarks>
    /// The project has exactly one honestly-partial backend — a bare raster set — and it cannot
    /// be the source of a bake test, because producing one is what the test is checking. This
    /// stands in for the third-party sampler the capability flags were written for.
    /// </remarks>
    private sealed class PartialSampler : ITerrainSampler
    {
        private readonly ITerrainSampler _inner;

        public PartialSampler(ITerrainSampler inner, TerrainCapabilities capabilities)
        {
            _inner = inner;
            Capabilities = capabilities;
        }

        public TerrainBounds Bounds => _inner.Bounds;

        public TerrainCapabilities Capabilities { get; }

        public TerrainSample Sample(int x, int z) => _inner.Sample(x, z);
    }

    private static RasterFixture BakedFixture(
        string name, ulong seed, int size = 512, int resolution = 128)
    {
        var fixture = RasterFixture.Empty(name);

        TerrainRasterBake.Write(
            new ProceduralTerrainSampler(seed, TerrainBounds.Square(size)),
            fixture.Directory,
            resolution);

        return fixture;
    }

    private static byte[] BinaryPgm(int width, int height, int maxValue, int[] values)
    {
        var bytes = new List<byte>(
            Encoding.ASCII.GetBytes($"P5\n{width} {height}\n{maxValue}\n"));

        foreach (int value in values) bytes.Add((byte)value);

        return bytes.ToArray();
    }

    /// <summary>
    /// A raster set on disk, cleaned up afterwards.
    /// </summary>
    /// <remarks>
    /// Written out per test rather than committed to the repository: the sets are megabytes of
    /// binary that would have to be regenerated whenever the noise sampler changed, and a fixture
    /// nobody can regenerate from a seed is a fixture nobody dares touch.
    /// </remarks>
    private sealed class RasterFixture : IDisposable
    {
        private RasterFixture(string directory) => Directory = directory;

        public string Directory { get; }

        public static RasterFixture Empty(string name)
        {
            string path = Path.Combine(Path.GetTempPath(), "historia-extera-rasters", name);

            if (System.IO.Directory.Exists(path)) System.IO.Directory.Delete(path, recursive: true);
            System.IO.Directory.CreateDirectory(path);

            return new RasterFixture(path);
        }

        public static RasterFixture Create(
            string name, int width, int height, double[] heightValues, string manifest)
        {
            RasterFixture fixture = Empty(name);

            var values = new float[width * height];
            for (int i = 0; i < heightValues.Length; i++) values[i] = (float)heightValues[i];

            Netpbm.WriteFile(
                Path.Combine(fixture.Directory, "height.pgm"),
                new RasterGrid(width, height, values));

            File.WriteAllText(Path.Combine(fixture.Directory, "terrain.json"), manifest);

            return fixture;
        }

        public RasterTerrainSampler Load() =>
            RasterTerrainSampler.Load(Path.Combine(Directory, "terrain.json"));

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
    }
}
