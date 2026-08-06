namespace HistoryEngine.Terrain;

/// <summary>
/// Wraps a sampler and counts how many points get sampled.
/// </summary>
/// <remarks>
/// <para>This is the instrument that turns a Phase 3 performance constraint into a
/// Phase 1 test. Vintage Story's sampler costs roughly 1–2ms per point, so a run's total
/// sample count multiplied by 1.5ms is, near enough, how long worldgen will block the
/// server. <c>TerrainDisciplineTests</c> asserts a budget on that count.</para>
///
/// <para>The value is in the failure mode. Without it, adding a per-settlement terrain
/// lookup inside the yearly tick is invisible: Phase 1 sampling is free, the tests stay
/// green, and the cost only appears years later as an unexplained multi-minute hang
/// inside the game, in code nobody remembers writing. With it, that same change fails a
/// test in the same commit that introduced it.</para>
///
/// <para><see cref="EstimatedGameSampleCost"/> reports the count in the unit that
/// actually matters, because "412,000 samples" does not read as a problem until it reads
/// as "ten minutes".</para>
/// </remarks>
public sealed class CountingTerrainSampler : ITerrainSampler
{
    /// <summary>Measured per-sample cost of Vintage Story's terrain sampler, mid-range.</summary>
    public const double GameSampleCostMs = 1.5;

    private readonly ITerrainSampler _inner;

    public CountingTerrainSampler(ITerrainSampler inner) => _inner = inner;

    /// <summary>Total points sampled through this instance.</summary>
    public long SampleCount { get; private set; }

    /// <summary>Number of <see cref="SampleBatch"/> calls. Batches are the cheap path.</summary>
    public long BatchCount { get; private set; }

    /// <summary>Number of points sampled one at a time. High values are the smell.</summary>
    public long SingleSampleCount { get; private set; }

    /// <summary>What this many samples would cost against Vintage Story's sampler.</summary>
    public TimeSpan EstimatedGameSampleCost =>
        TimeSpan.FromMilliseconds(SampleCount * GameSampleCostMs);

    public TerrainBounds Bounds => _inner.Bounds;

    public TerrainCapabilities Capabilities => _inner.Capabilities;

    public void Reset()
    {
        SampleCount = 0;
        BatchCount = 0;
        SingleSampleCount = 0;
    }

    public TerrainSample Sample(int x, int z)
    {
        SampleCount++;
        SingleSampleCount++;
        return _inner.Sample(x, z);
    }

    public void SampleBatch(ReadOnlySpan<Point2> points, Span<TerrainSample> destination)
    {
        SampleCount += points.Length;
        BatchCount++;
        _inner.SampleBatch(points, destination);
    }

    public override string ToString() =>
        $"{SampleCount:N0} samples ({SingleSampleCount:N0} single, {BatchCount:N0} batches) " +
        $"≈ {EstimatedGameSampleCost.TotalSeconds:F1}s in game";
}
