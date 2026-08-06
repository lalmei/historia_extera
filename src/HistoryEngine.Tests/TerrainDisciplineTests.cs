using System.Reflection;
using HistoryEngine.Terrain;
using HistoryEngine.World;
using Xunit;

namespace HistoryEngine.Tests;

/// <summary>
/// Enforces the terrain access pattern Phase 3 depends on.
/// </summary>
/// <remarks>
/// These are the tests that make Milestone 1 worth doing before the Vintage Story work rather
/// than alongside it. Phase 1's noise sampler is free to call, so nothing about the code's
/// behaviour reveals a query pattern that would be catastrophic against a sampler costing
/// 1–2ms per point. Measuring the pattern now means a regression fails in the commit that
/// introduces it, instead of appearing years later as an unexplained multi-minute hang inside
/// the game.
/// </remarks>
public sealed class TerrainDisciplineTests
{
    /// <summary>
    /// Ceiling on terrain samples for a 300-year, 8-civilization run.
    /// </summary>
    /// <remarks>
    /// Set from the measured cost with headroom, not from a guess: the run this is written
    /// against spends roughly 1,600 samples, so 12,000 leaves room for the simulation to grow
    /// several more terrain-consuming systems while still costing well under 20 seconds inside
    /// Vintage Story. If a change pushes past this, the right response is to look at whether the
    /// new sampling is per-decision (fine) or per-entity-per-year (not fine) — not to raise the
    /// number.
    /// </remarks>
    private const long SimulationSampleBudget = 12_000;

    [Fact]
    public void AFullRunStaysWithinTheSampleBudget()
    {
        HistoryRun run = HistoryRun.Execute(TestWorlds.Standard());

        Assert.True(
            run.SimulationSamples <= SimulationSampleBudget,
            $"Simulation spent {run.SimulationSamples:N0} terrain samples, over the " +
            $"{SimulationSampleBudget:N0} budget. At {CountingTerrainSampler.GameSampleCostMs}ms " +
            $"per sample that is ~{run.SimulationSamples * CountingTerrainSampler.GameSampleCostMs / 1000.0:F0}s " +
            "of Vintage Story worldgen. Check for terrain queries inside the yearly tick — " +
            "broad questions belong on TerrainAtlas.SampleCoarse, which samples nothing.");
    }

    /// <summary>
    /// Maximum samples one siting decision may cost.
    /// </summary>
    /// <remarks>
    /// A capital evaluates 8×8 candidate positions, which is the largest single refinement the
    /// simulation performs.
    /// </remarks>
    private const int SamplesPerSitingDecision = 64;

    /// <summary>
    /// Sampling must scale with decisions made, never with years simulated.
    /// </summary>
    /// <remarks>
    /// <para>The sharpest form of the check, and the one that catches the actual mistake. A budget
    /// alone can be satisfied by a short run; this compares a 60-year run against a 600-year one on
    /// the same world.</para>
    ///
    /// <para>Longer runs legitimately sample more, because they found more settlements and each
    /// founding evaluates candidate sites — that is per-decision cost, and it is fine. What must
    /// never happen is cost per <em>year</em>: a terrain query inside the yearly tick would make
    /// samples scale with time, and a 300-year history would spend millions. So the assertion is on
    /// the ratio of extra samples to extra settlements, which stays flat under the correct pattern
    /// and explodes under the wrong one.</para>
    /// </remarks>
    [Fact]
    public void SamplingScalesWithDecisionsNotYears()
    {
        HistoryRun shortRun = HistoryRun.Execute(TestWorlds.Standard() with { Years = 60 });
        HistoryRun longRun = HistoryRun.Execute(TestWorlds.Standard() with { Years = 600 });

        long extraSamples = longRun.SimulationSamples - shortRun.SimulationSamples;
        int extraSettlements = longRun.World.Settlements.Count - shortRun.World.Settlements.Count;

        Assert.True(extraSettlements > 0, "The longer run founded no extra settlements to measure.");

        double samplesPerSettlement = extraSamples / (double)extraSettlements;

        Assert.True(
            samplesPerSettlement <= SamplesPerSitingDecision,
            $"A 10x longer run spent {extraSamples:N0} extra samples founding {extraSettlements} " +
            $"extra settlements — {samplesPerSettlement:F1} per settlement, above the " +
            $"{SamplesPerSitingDecision} one siting decision may cost. Terrain sampling is " +
            "scaling with time rather than with decisions: something queries terrain inside the " +
            "yearly tick.");
    }

    [Fact]
    public void CoarseSamplingIsFreeAfterPriming()
    {
        var counter = new CountingTerrainSampler(
            new ProceduralTerrainSampler(1, TerrainBounds.Square(2048)));
        var atlas = new TerrainAtlas(counter, stride: 256);

        atlas.EnsurePrimed();
        long afterPriming = counter.SampleCount;

        for (int z = 0; z < 2048; z += 7)
        {
            for (int x = 0; x < 2048; x += 7)
            {
                atlas.SampleCoarse(x, z);
            }
        }

        Assert.Equal(afterPriming, counter.SampleCount);
    }

    [Fact]
    public void PrimingUsesASingleBatch()
    {
        var counter = new CountingTerrainSampler(
            new ProceduralTerrainSampler(1, TerrainBounds.Square(4096)));
        var atlas = new TerrainAtlas(counter, stride: 256);

        atlas.EnsurePrimed();

        // One batch, no one-at-a-time calls: a backend that can amortise per-region setup across
        // a batch gets the chance to, which is the whole reason SampleBatch exists.
        Assert.Equal(1, counter.BatchCount);
        Assert.Equal(0, counter.SingleSampleCount);
    }

    [Fact]
    public void ExactSamplesAreMemoised()
    {
        var counter = new CountingTerrainSampler(
            new ProceduralTerrainSampler(1, TerrainBounds.Square(1024)));
        var atlas = new TerrainAtlas(counter, stride: 256);

        atlas.EnsurePrimed();
        long baseline = counter.SampleCount;

        for (int i = 0; i < 50; i++) atlas.SampleExact(300, 400);

        Assert.Equal(baseline + 1, counter.SampleCount);
    }

    [Fact]
    public void RefiningTwiceDoesNotResample()
    {
        var counter = new CountingTerrainSampler(
            new ProceduralTerrainSampler(1, TerrainBounds.Square(1024)));
        var atlas = new TerrainAtlas(counter, stride: 256);

        var area = new TerrainBounds(0, 0, 128, 128);

        int first = atlas.Refine(area, stride: 32);
        int second = atlas.Refine(area, stride: 32);

        Assert.True(first > 0);
        Assert.Equal(0, second);
    }

    /// <summary>
    /// Rivers must never be asked of the sampler.
    /// </summary>
    /// <remarks>
    /// A point sampler cannot answer whether a river runs through a location — the answer depends
    /// on the whole upstream catchment. Vintage Story's sampler does not report rivers at all
    /// unless the Watersheds sampler is installed, so deriving them from the height lattice is the
    /// only approach that works in every phase.
    /// </remarks>
    [Fact]
    public void HydrologyCostsNoSamples()
    {
        var counter = new CountingTerrainSampler(
            new ProceduralTerrainSampler(3, TerrainBounds.Square(4096)));
        var atlas = new TerrainAtlas(counter, stride: 256);

        atlas.EnsurePrimed();
        long baseline = counter.SampleCount;

        Hydrology hydrology = atlas.Hydrology;

        Assert.Equal(baseline, counter.SampleCount);
        Assert.False(atlas.Capabilities.HasFlag(TerrainCapabilities.Rivers));
        Assert.True(hydrology.RiverNodeCount > 0, "Derived hydrology produced no rivers at all.");
    }

    /// <summary>
    /// Nothing under <c>Systems/</c> may touch <see cref="ITerrainSampler"/>.
    /// </summary>
    /// <remarks>
    /// The structural half of the discipline. The budget tests catch a costly pattern after it is
    /// written; this stops the pattern being reachable at all, because a system holding a raw
    /// sampler has bypassed every cache and interpolation the atlas provides. World construction
    /// legitimately needs the sampler to build the atlas, which is exactly why
    /// <see cref="WorldBuilder"/> lives under <c>World/</c> and not here.
    /// </remarks>
    [Fact]
    public void YearlySystemsCannotReachTheRawSampler()
    {
        string systemsDirectory = Path.Combine(EngineSource.Root, "Systems");
        Assert.True(Directory.Exists(systemsDirectory), $"Missing {systemsDirectory}");

        var offenders = new List<string>();

        foreach (string file in Directory.GetFiles(systemsDirectory, "*.cs"))
        {
            string source = EngineSource.StripComments(File.ReadAllText(file));
            if (source.Contains("ITerrainSampler", StringComparison.Ordinal))
            {
                offenders.Add(Path.GetFileName(file));
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Yearly systems must consume terrain through TerrainAtlas, never ITerrainSampler " +
            "directly. Offending files: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// Every declared capability must be genuinely readable, and undeclared ones must not be assumed.
    /// </summary>
    [Fact]
    public void PlaceholderSamplerDeclaresWhatItActuallyProvides()
    {
        var sampler = new ProceduralTerrainSampler(1, TerrainBounds.Square(512));

        Assert.True(sampler.Supports(TerrainCapabilities.Height));
        Assert.True(sampler.Supports(TerrainCapabilities.Temperature));
        Assert.True(sampler.Supports(TerrainCapabilities.Rainfall));
        Assert.True(sampler.Supports(TerrainCapabilities.Lakes));

        // Rivers come from Hydrology in every phase, so no backend should claim them here.
        Assert.False(sampler.Supports(TerrainCapabilities.Rivers));
    }
}

/// <summary>Locates the engine's source tree for the guard tests that read it.</summary>
internal static class EngineSource
{
    public static string Root { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<EngineSourcePathAttribute>()?.Path
        ?? throw new InvalidOperationException(
            "EngineSourcePathAttribute is missing. It is set in HistoryEngine.Tests.csproj.");

    public static IEnumerable<string> AllFiles() =>
        Directory.GetFiles(Root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal);

    /// <summary>
    /// Removes comments so the guard tests scan code rather than prose.
    /// </summary>
    /// <remarks>
    /// Necessary because the engine's own documentation discusses the very constructs the guards
    /// ban — the doc comment on <c>DetMath</c> names <c>Math.Exp</c> in order to explain why it is
    /// forbidden. Without this, the guards would flag their own rationale.
    /// </remarks>
    public static string StripComments(string source)
    {
        var output = new System.Text.StringBuilder(source.Length);

        int i = 0;
        while (i < source.Length)
        {
            if (i + 1 < source.Length && source[i] == '/' && source[i + 1] == '/')
            {
                while (i < source.Length && source[i] != '\n') i++;
                continue;
            }

            if (i + 1 < source.Length && source[i] == '/' && source[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/')) i++;
                i += 2;
                continue;
            }

            output.Append(source[i]);
            i++;
        }

        return output.ToString();
    }
}
