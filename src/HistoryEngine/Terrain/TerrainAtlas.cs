using HistoryEngine.Core;

namespace HistoryEngine.Terrain;

/// <summary>
/// The simulation's only view of terrain: a cached sparse lattice with interpolation
/// and on-demand local refinement.
/// </summary>
/// <remarks>
/// <para><b>The access pattern, in three tiers.</b></para>
/// <list type="number">
///   <item><description><see cref="SampleCoarse"/> — bilinear interpolation of the primed
///   lattice. Costs nothing and never samples. This is what broad questions use: is this
///   half of the continent habitable, which direction is warmer, roughly how good is this
///   region. The overwhelming majority of the simulation's terrain queries.</description></item>
///   <item><description><see cref="Refine"/> — batch-samples a small rectangle at a finer
///   stride. Used when a handful of candidate sites need comparing against each other,
///   which is a bounded number of times per founding decision, not per year.</description></item>
///   <item><description><see cref="SampleExact"/> — one true sample, memoised forever.
///   Used only for points that become permanent: a settlement's actual position, a
///   battlefield. Tens to low hundreds per run.</description></item>
/// </list>
///
/// <para><b>Why the tiers are the interesting part.</b> Any of the three phases could be
/// implemented with tier 3 alone, and in Phase 1 it would even be fast. The tiers exist so
/// that when the sampler underneath starts costing 1–2ms per point, the simulation's query
/// pattern does not have to change at all — which is the actual claim being tested, and
/// the reason this lands in Milestone 1 rather than alongside the Vintage Story work it is
/// for.</para>
///
/// <para><b>Priming is lazy but bulk.</b> The whole lattice is filled in a single
/// <see cref="ITerrainSampler.SampleBatch"/> call on first use, so a backend that can
/// amortise setup across a batch gets the chance to. On a 4096-unit world at the default
/// 256 stride that is 289 samples — about half a second in game, once.</para>
/// </remarks>
public sealed class TerrainAtlas
{
    /// <summary>Lattice spacing in world units. Must be a power of two.</summary>
    public const int DefaultStride = 256;

    /// <summary>
    /// Default hydrology grid spacing.
    /// </summary>
    /// <remarks>
    /// Chosen as the cheapest resolution that produces a recognisable river network: on a
    /// 4096-unit world this is a 65×65 grid, about 4,200 samples — roughly six seconds against
    /// Vintage Story's sampler, once, at world creation. Coarser than this and rivers break into
    /// disconnected fragments; finer buys detail the simulation cannot use.
    /// </remarks>
    public const int DefaultHydrologyStride = 64;

    private readonly ITerrainSampler _sampler;
    private readonly TerrainSample[] _lattice;
    private readonly Dictionary<long, TerrainSample> _exact = new();

    private bool _primed;
    private Hydrology? _hydrology;

    public TerrainAtlas(
        ITerrainSampler sampler,
        int stride = DefaultStride,
        int hydrologyStride = DefaultHydrologyStride)
    {
        if (stride <= 0 || (stride & (stride - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stride), stride, "Stride must be a positive power of two.");
        }

        if (hydrologyStride <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(hydrologyStride), hydrologyStride, "Hydrology stride must be positive.");
        }

        _sampler = sampler;
        Stride = stride;
        HydrologyStride = hydrologyStride;

        // The lattice spans the bounds inclusively at both edges, so interpolation
        // never needs to extrapolate.
        LatticeWidth = ((sampler.Bounds.Width + stride - 1) / stride) + 1;
        LatticeHeight = ((sampler.Bounds.Height + stride - 1) / stride) + 1;
        _lattice = new TerrainSample[LatticeWidth * LatticeHeight];
    }

    public TerrainBounds Bounds => _sampler.Bounds;

    public TerrainCapabilities Capabilities => _sampler.Capabilities;

    public int Stride { get; }

    public int LatticeWidth { get; }

    public int LatticeHeight { get; }

    /// <summary>Number of exact points memoised so far. Diagnostic.</summary>
    public int ExactCacheCount => _exact.Count;

    /// <summary>
    /// Rivers and drainage, derived from the height lattice.
    /// </summary>
    /// <remarks>
    /// Never asked of the sampler. A point sampler fundamentally cannot answer "is there a
    /// river here" — the answer depends on the whole upstream catchment — and Vintage
    /// Story's sampler does not report rivers at all unless the Watersheds sampler is
    /// installed. Deriving them from height means rivers exist in all three phases and are
    /// always consistent with the terrain they run over.
    ///
    /// <para>Built on its own grid at <see cref="HydrologyStride"/> rather than the simulation
    /// lattice, so unlike the rest of this class it does cost samples: one bulk batch at world
    /// creation, memoised thereafter. See <see cref="Terrain.Hydrology"/> for why the lattice is too
    /// coarse to derive a river network from.</para>
    /// </remarks>
    public Hydrology Hydrology
    {
        get
        {
            EnsurePrimed();
            return _hydrology ??= Terrain.Hydrology.Build(this, HydrologyStride);
        }
    }

    /// <summary>
    /// Grid spacing for hydrology, finer than <see cref="Stride"/>.
    /// </summary>
    /// <remarks>
    /// Rivers need their own resolution. The simulation lattice is deliberately coarse — at a
    /// 256-unit stride a 4096-unit world is a 17×17 grid, which cannot represent a river network at
    /// all; flow accumulation over it yields a handful of disconnected cells. Drainage is a
    /// fundamentally finer-grained phenomenon than "how good is this region", so it gets its own
    /// one-off grid rather than degrading the simulation lattice everywhere to suit it.
    /// </remarks>
    public int HydrologyStride { get; }

    /// <summary>
    /// Batch-samples a full-world grid at <paramref name="stride"/> and memoises every point.
    /// </summary>
    /// <remarks>
    /// One bulk call, so a backend that amortises setup across a batch gets the chance to. Points
    /// land in the same cache the three access tiers use, so a later refinement or raster pass over
    /// the same coordinates is free.
    /// </remarks>
    public TerrainSample[] SampleGrid(int stride, out int columns, out int rows)
    {
        columns = ((Bounds.Width + stride - 1) / stride) + 1;
        rows = ((Bounds.Height + stride - 1) / stride) + 1;

        var points = new Point2[columns * rows];
        for (int j = 0; j < rows; j++)
        {
            for (int i = 0; i < columns; i++)
            {
                points[(j * columns) + i] =
                    Bounds.Clamp(Bounds.MinX + (i * stride), Bounds.MinZ + (j * stride));
            }
        }

        var samples = new TerrainSample[points.Length];

        // Serve what is already cached; batch the rest in one call.
        var missing = new List<int>();
        for (int i = 0; i < points.Length; i++)
        {
            if (_exact.TryGetValue(PackKey(points[i].X, points[i].Z), out TerrainSample cached))
            {
                samples[i] = cached;
            }
            else
            {
                missing.Add(i);
            }
        }

        if (missing.Count > 0)
        {
            var toSample = new Point2[missing.Count];
            for (int i = 0; i < missing.Count; i++) toSample[i] = points[missing[i]];

            var fetched = new TerrainSample[missing.Count];
            _sampler.SampleBatch(toSample, fetched);

            for (int i = 0; i < missing.Count; i++)
            {
                samples[missing[i]] = fetched[i];
                _exact[PackKey(toSample[i].X, toSample[i].Z)] = fetched[i];
            }
        }

        return samples;
    }

    /// <summary>Fills the lattice if it has not been filled yet.</summary>
    public void EnsurePrimed()
    {
        if (_primed) return;

        int count = _lattice.Length;
        var points = new Point2[count];

        for (int j = 0; j < LatticeHeight; j++)
        {
            for (int i = 0; i < LatticeWidth; i++)
            {
                // Clamp so the inclusive far edge stays inside the sampler's bounds.
                Point2 p = Bounds.Clamp(
                    Bounds.MinX + (i * Stride),
                    Bounds.MinZ + (j * Stride));
                points[(j * LatticeWidth) + i] = p;
            }
        }

        _sampler.SampleBatch(points, _lattice);
        _primed = true;
    }

    /// <summary>The lattice node at grid position (i, j).</summary>
    public TerrainSample LatticeAt(int i, int j)
    {
        EnsurePrimed();

        int ci = i < 0 ? 0 : i >= LatticeWidth ? LatticeWidth - 1 : i;
        int cj = j < 0 ? 0 : j >= LatticeHeight ? LatticeHeight - 1 : j;
        return _lattice[(cj * LatticeWidth) + ci];
    }

    /// <summary>The world coordinate of lattice node (i, j).</summary>
    public Point2 LatticeToWorld(int i, int j) =>
        new(Bounds.MinX + (i * Stride), Bounds.MinZ + (j * Stride));

    /// <summary>
    /// Interpolated terrain. Free — never samples.
    /// </summary>
    /// <remarks>
    /// Continuous fields are bilinear. <see cref="WaterKind"/> cannot be averaged, so ocean
    /// follows the interpolated height and anything else takes the nearest node's value.
    /// That makes coarse water reliable at the scale of the lattice and vague below it,
    /// which is the right trade for the broad questions this tier answers — anything that
    /// turns into a permanent decision should use <see cref="SampleExact"/>.
    /// </remarks>
    public TerrainSample SampleCoarse(int x, int z)
    {
        EnsurePrimed();

        double fx = (x - Bounds.MinX) / (double)Stride;
        double fz = (z - Bounds.MinZ) / (double)Stride;

        int i0 = (int)Math.Floor(fx);
        int j0 = (int)Math.Floor(fz);

        if (i0 < 0) i0 = 0;
        if (j0 < 0) j0 = 0;
        if (i0 > LatticeWidth - 2) i0 = Math.Max(0, LatticeWidth - 2);
        if (j0 > LatticeHeight - 2) j0 = Math.Max(0, LatticeHeight - 2);

        double tx = DetMath.Clamp01(fx - i0);
        double tz = DetMath.Clamp01(fz - j0);

        TerrainSample s00 = LatticeAt(i0, j0);
        TerrainSample s10 = LatticeAt(i0 + 1, j0);
        TerrainSample s01 = LatticeAt(i0, j0 + 1);
        TerrainSample s11 = LatticeAt(i0 + 1, j0 + 1);

        float height = Bilinear(s00.Height, s10.Height, s01.Height, s11.Height, tx, tz);

        // Nearest node, for the fields that do not average meaningfully.
        TerrainSample nearest = (tx < 0.5, tz < 0.5) switch
        {
            (true, true) => s00,
            (false, true) => s10,
            (true, false) => s01,
            (false, false) => s11,
        };

        WaterKind water = height < 0f
            ? WaterKind.Ocean
            : nearest.Water == WaterKind.Ocean ? WaterKind.None : nearest.Water;

        return new TerrainSample(
            Height: height,
            Temperature: Bilinear(s00.Temperature, s10.Temperature, s01.Temperature, s11.Temperature, tx, tz),
            Rainfall: Bilinear(s00.Rainfall, s10.Rainfall, s01.Rainfall, s11.Rainfall, tx, tz),
            GeologicActivity: Bilinear(s00.GeologicActivity, s10.GeologicActivity, s01.GeologicActivity, s11.GeologicActivity, tx, tz),
            ForestDensity: Bilinear(s00.ForestDensity, s10.ForestDensity, s01.ForestDensity, s11.ForestDensity, tx, tz),
            ShrubDensity: Bilinear(s00.ShrubDensity, s10.ShrubDensity, s01.ShrubDensity, s11.ShrubDensity, tx, tz),
            Water: water);
    }

    /// <summary>
    /// A true sample of one point, memoised permanently.
    /// </summary>
    /// <remarks>
    /// Reserve this for coordinates that become part of the historical record. Calling it
    /// in a loop over settlements or years is exactly the pattern the sample budget test
    /// exists to catch.
    /// </remarks>
    public TerrainSample SampleExact(int x, int z)
    {
        Point2 p = Bounds.Clamp(x, z);
        long key = PackKey(p.X, p.Z);

        if (_exact.TryGetValue(key, out TerrainSample cached))
        {
            return cached;
        }

        TerrainSample sample = _sampler.Sample(p.X, p.Z);
        _exact[key] = sample;
        return sample;
    }

    /// <summary>
    /// Batch-samples a rectangle at <paramref name="stride"/> and memoises the results, so
    /// subsequent <see cref="SampleExact"/> calls inside it are free.
    /// </summary>
    /// <remarks>
    /// The intended shape of a siting decision: refine once around a candidate area, then
    /// score many positions inside it at no further cost. Points already cached are skipped,
    /// so overlapping refinements do not re-sample.
    /// </remarks>
    /// <returns>How many points were newly sampled.</returns>
    public int Refine(TerrainBounds area, int stride)
    {
        if (stride <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stride), stride, "Stride must be positive.");
        }

        var points = new List<Point2>();

        for (int z = area.MinZ; z < area.MaxZ; z += stride)
        {
            for (int x = area.MinX; x < area.MaxX; x += stride)
            {
                Point2 p = Bounds.Clamp(x, z);
                if (!_exact.ContainsKey(PackKey(p.X, p.Z)))
                {
                    points.Add(p);
                }
            }
        }

        if (points.Count == 0) return 0;

        var results = new TerrainSample[points.Count];
        Point2[] array = points.ToArray();
        _sampler.SampleBatch(array, results);

        for (int i = 0; i < array.Length; i++)
        {
            _exact[PackKey(array[i].X, array[i].Z)] = results[i];
        }

        return array.Length;
    }

    /// <summary>
    /// Refines <paramref name="area"/> and yields every point in it with its sample.
    /// </summary>
    /// <remarks>
    /// The intended way to evaluate candidate sites, and the reason it exists as one call:
    /// refining and then scoring at coordinates that do not land on the refined grid would
    /// miss the cache on every point and issue a fresh sample each time — the exact mistake
    /// the tiered design is meant to prevent, made while apparently using the tiers correctly.
    /// Iterating what was refined makes that impossible.
    /// </remarks>
    public IEnumerable<KeyValuePair<Point2, TerrainSample>> RefinedPoints(
        TerrainBounds area, int stride)
    {
        Refine(area, stride);

        for (int z = area.MinZ; z < area.MaxZ; z += stride)
        {
            for (int x = area.MinX; x < area.MaxX; x += stride)
            {
                Point2 p = Bounds.Clamp(x, z);
                yield return new KeyValuePair<Point2, TerrainSample>(p, _exact[PackKey(p.X, p.Z)]);
            }
        }
    }

    private static float Bilinear(float v00, float v10, float v01, float v11, double tx, double tz)
    {
        double top = DetMath.Lerp(v00, v10, tx);
        double bottom = DetMath.Lerp(v01, v11, tx);
        return (float)DetMath.Lerp(top, bottom, tz);
    }

    private static long PackKey(int x, int z) => ((long)(uint)x << 32) | (uint)z;
}
