using HistoryEngine.Core;
using HistoryEngine.Serialization;
using HistoryEngine.Systems;
using HistoryEngine.World;
using Xunit;

namespace HistoryEngine.Tests;

/// <summary>
/// The project's central promise: identical seed and config produce identical history.
/// </summary>
public sealed class DeterminismTests
{
    [Fact]
    public void SameSeedProducesByteIdenticalExport()
    {
        WorldConfig config = TestWorlds.Standard();

        string first = WorldExporter.Fingerprint(HistoryRun.Execute(config).ToExport());
        string second = WorldExporter.Fingerprint(HistoryRun.Execute(config).ToExport());

        Assert.Equal(first, second);
    }

    [Fact]
    public void DifferentSeedProducesDifferentHistory()
    {
        string a = WorldExporter.Fingerprint(HistoryRun.Execute(TestWorlds.Standard(1)).ToExport());
        string b = WorldExporter.Fingerprint(HistoryRun.Execute(TestWorlds.Standard(2)).ToExport());

        Assert.NotEqual(a, b);
    }

    /// <summary>
    /// A run split in two must equal one continuous run.
    /// </summary>
    /// <remarks>
    /// The strongest test in the suite. Simply running twice proves only that the engine is
    /// consistent with itself; this proves that <see cref="WorldState"/> holds the <em>entire</em>
    /// simulation state. Anything cached in a static, memoised on a system instance, or derived
    /// from call ordering rather than world state diverges the moment the loop is interrupted and
    /// resumed — and this is what makes the future save/resume feature safe to build.
    /// </remarks>
    [Fact]
    public void SplittingARunDoesNotChangeIt()
    {
        WorldConfig config = TestWorlds.Standard();

        HistoryRun continuous = HistoryRun.Execute(config);

        // The same work, interrupted midway.
        WorldState split = WorldBuilder.Create(config);
        var simulator = new Simulator();
        simulator.Advance(split, 150);
        simulator.Advance(split, 150);

        Assert.Equal(continuous.World.Settlements.Count, split.Settlements.Count);
        Assert.Equal(continuous.World.TradeRoutes.Count, split.TradeRoutes.Count);
        Assert.Equal(continuous.World.Figures.Count, split.Figures.Count);
        Assert.Equal(continuous.World.Chronicle.Count, split.Chronicle.Count);

        // Compared by signature rather than by object, so a divergence names the year and event
        // it happened in instead of reporting that two events differ somehow.
        for (int i = 0; i < continuous.World.Chronicle.Count; i++)
        {
            string expected = continuous.World.Chronicle.Events[i].Signature();
            string actual = split.Chronicle.Events[i].Signature();

            Assert.True(
                expected == actual,
                $"Histories diverged at event {i}:\n  continuous: {expected}\n  split:      {actual}");
        }
    }

    /// <summary>
    /// Forked substreams must not depend on the parent's position.
    /// </summary>
    /// <remarks>
    /// The property the whole forking design exists for. If <see cref="IRng.Fork"/> consumed from
    /// its parent, then adding one die roll to any system would shift every name, birth and battle
    /// downstream of it — and every golden test would fail on every unrelated change, which trains
    /// people to regenerate goldens without reading them.
    /// </remarks>
    [Fact]
    public void ForkIsIndependentOfParentPosition()
    {
        var untouched = new Pcg32(99);
        string before = Draw(untouched.Fork("naming", 5));

        var advanced = new Pcg32(99);
        for (int i = 0; i < 1000; i++) advanced.NextUInt();
        string after = Draw(advanced.Fork("naming", 5));

        Assert.Equal(before, after);
    }

    [Fact]
    public void ForksWithDifferentPurposesDiverge()
    {
        var rng = new Pcg32(99);

        Assert.NotEqual(Draw(rng.Fork("naming", 1)), Draw(rng.Fork("warfare", 1)));
        Assert.NotEqual(Draw(rng.Fork("naming", 1)), Draw(rng.Fork("naming", 2)));
    }

    /// <summary>
    /// Hashing a fork label must be a pure function of its characters.
    /// </summary>
    /// <remarks>
    /// <see cref="string.GetHashCode()"/> is seeded per process, so a fork label hashed with it
    /// would produce a different history on every run — the single easiest way to break this
    /// project, and one that presents as a simulation bug rather than a hashing bug. The
    /// cross-process guarantee is pinned by <see cref="GoldenExportTests"/>; this covers the
    /// in-process properties.
    /// </remarks>
    [Fact]
    public void StringHashingIsPure()
    {
        Assert.Equal(Hash.OfString("population"), Hash.OfString("population"));
        Assert.NotEqual(Hash.OfString("population"), Hash.OfString("populatioN"));
        Assert.NotEqual(Hash.OfString("expansion"), Hash.OfString("expansion "));

        // Order must matter, or (x, z) and (z, x) lattice coordinates would collide.
        Assert.NotEqual(Hash.OfCoord(1, 3, 5), Hash.OfCoord(1, 5, 3));
    }

    private static string Draw(IRng rng)
    {
        var values = new uint[8];
        for (int i = 0; i < values.Length; i++) values[i] = rng.NextUInt();
        return string.Join(",", values);
    }
}
