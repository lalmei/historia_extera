namespace HistoryEngine.Terrain;

/// <summary>
/// Raw point access to a terrain backend. Deliberately minimal.
/// </summary>
/// <remarks>
/// <para><b>The simulation must never hold one of these.</b> It talks to
/// <see cref="TerrainAtlas"/>, which owns the only reference. That is not a style
/// preference; it is the constraint the whole three-phase plan rests on, and
/// <c>TerrainDisciplineTests</c> enforces it.</para>
///
/// <para><b>Why.</b> In Phase 3 this interface is backed by Vintage Story's terrain
/// sampler, where a single call costs roughly 1–2ms — and more than that for the first
/// call in a fresh region, which has to generate a region map first. A simulation that
/// asks terrain a question per settlement per year would issue millions of calls and
/// take hours. The fix is caching, interpolation and selective refinement, and the
/// point of putting all of it in the consumer is that it gets written once, in Phase 1,
/// against a sampler that costs nothing — rather than three times, or once in a panic
/// inside the game.</para>
///
/// <para>So implementations stay dumb: answer the point asked about, declare what you
/// can measure, and offer a batch entry point if you can do better than one at a time.
/// No caching, no interpolation, no cleverness.</para>
/// </remarks>
public interface ITerrainSampler
{
    /// <summary>The extent this sampler can answer for.</summary>
    TerrainBounds Bounds { get; }

    /// <summary>Which fields this backend genuinely measures.</summary>
    TerrainCapabilities Capabilities { get; }

    /// <summary>
    /// Samples one point. May be expensive — assume it is.
    /// </summary>
    TerrainSample Sample(int x, int z);

    /// <summary>
    /// Samples many points at once.
    /// </summary>
    /// <remarks>
    /// The default implementation just loops. Backends that can amortise setup across a
    /// batch — sorting by region to avoid repeated region-map generation, for instance —
    /// should override it, because <see cref="TerrainAtlas"/> primes its lattice through
    /// this method and that is the single largest burst of sampling in a run.
    /// </remarks>
    void SampleBatch(ReadOnlySpan<Point2> points, Span<TerrainSample> destination)
    {
        if (destination.Length < points.Length)
        {
            throw new ArgumentException(
                "Destination is shorter than the point list.", nameof(destination));
        }

        for (int i = 0; i < points.Length; i++)
        {
            destination[i] = Sample(points[i].X, points[i].Z);
        }
    }
}

public static class TerrainSamplerExtensions
{
    public static bool Supports(this ITerrainSampler sampler, TerrainCapabilities capability) =>
        (sampler.Capabilities & capability) == capability;
}
