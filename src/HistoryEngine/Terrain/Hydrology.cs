using HistoryEngine.Core;

namespace HistoryEngine.Terrain;

/// <summary>
/// Rivers, drainage and coastline, derived from the height lattice by D8 flow accumulation.
/// </summary>
/// <remarks>
/// <para><b>Why rivers are computed rather than sampled.</b> A point sampler cannot answer
/// "is there a river here" even in principle — whether water flows through a spot depends on
/// the entire catchment uphill of it, which is not local information. Vintage Story's terrain
/// sampler does not report rivers at all unless the Watersheds sampler is present, and
/// Phase 2's candidate generators each model rivers differently or not at all.</para>
///
/// <para>Deriving them from elevation sidesteps all of that. Rivers exist identically in every
/// phase and are guaranteed consistent with the terrain they cut through — a sampled river can
/// contradict a sampled heightmap, a derived one cannot. When Phase 2 or 3 does supply real river
/// data, this becomes the fallback rather than the only path; see
/// <see cref="TerrainCapabilities.Rivers"/>.</para>
///
/// <para><b>On its own grid, not the simulation lattice.</b> This was originally built from the
/// primed lattice, which made it free. It also made it useless: at the lattice's 256-unit stride a
/// 4096-unit world is 17×17 cells, and flow accumulation over that produced four disconnected
/// fragments rather than a river network. Drainage is simply a finer-grained phenomenon than the
/// regional scoring the lattice exists for, so it gets one bulk sampling pass at
/// <see cref="TerrainAtlas.HydrologyStride"/> — a one-off worldgen cost that Phase 3 can lower
/// deliberately rather than a per-year cost that would need eliminating.</para>
///
/// <para>Even so this locates river <em>valleys</em> rather than channels, which is the right scale
/// for the questions history asks — which cities sit on a trade river, where an army must ford.</para>
/// </remarks>
public sealed class Hydrology
{
    /// <summary>Fraction of land cells that carry a named river. The 4% of the map with the most drainage.</summary>
    private const double RiverFraction = 0.04;

    // Eight neighbours in a fixed order. The order is part of the determinism contract:
    // it breaks ties when two neighbours are equally downhill.
    private static readonly int[] OffsetX = { 1, 1, 0, -1, -1, -1, 0, 1 };
    private static readonly int[] OffsetZ = { 0, 1, 1, 1, 0, -1, -1, -1 };

    private readonly int _width;
    private readonly int _height;
    private readonly int _stride;
    private readonly TerrainBounds _bounds;
    private readonly bool _eastWestPeriodic;

    private readonly int[] _downstream;
    private readonly double[] _accumulation;
    private readonly bool[] _isRiver;
    private readonly bool[] _isCoast;
    private readonly double _maxAccumulation;

    private Hydrology(
        int width,
        int height,
        int stride,
        TerrainBounds bounds,
        bool eastWestPeriodic,
        int[] downstream,
        double[] accumulation,
        bool[] isRiver,
        bool[] isCoast,
        double maxAccumulation)
    {
        _width = width;
        _height = height;
        _stride = stride;
        _bounds = bounds;
        _eastWestPeriodic = eastWestPeriodic;
        _downstream = downstream;
        _accumulation = accumulation;
        _isRiver = isRiver;
        _isCoast = isCoast;
        _maxAccumulation = maxAccumulation;
    }

    /// <summary>
    /// Builds the drainage network on its own grid at <paramref name="stride"/>.
    /// </summary>
    /// <remarks>
    /// Deliberately not built on the simulation lattice. At the lattice's 256-unit stride a
    /// 4096-unit world is 17×17 cells, and flow accumulation over that yields a handful of
    /// disconnected fragments rather than a river network — drainage is a finer-grained phenomenon
    /// than the regional scoring the lattice exists for. One bulk sampling pass at this stride is a
    /// one-off worldgen cost, reported separately so Phase 3 can lower it deliberately.
    /// </remarks>
    public static Hydrology Build(TerrainAtlas atlas, int stride)
    {
        TerrainSample[] grid = atlas.SampleGrid(stride, out int w, out int h);
        int n = w * h;

        var heights = new double[n];
        var submerged = new bool[n];

        for (int i = 0; i < n; i++)
        {
            heights[i] = grid[i].Height;
            submerged[i] = grid[i].Height < 0f;
        }

        int[] downstream = ComputeFlowDirections(
            heights, w, h, stride, atlas.EastWestPeriodic);
        double[] accumulation = ComputeAccumulation(heights, downstream, n);
        bool[] isRiver = ClassifyRivers(accumulation, submerged, n);
        bool[] isCoast = ClassifyCoast(submerged, w, h, atlas.EastWestPeriodic);

        double max = 0.0;
        for (int i = 0; i < n; i++)
        {
            if (accumulation[i] > max) max = accumulation[i];
        }

        return new Hydrology(
            w,
            h,
            stride,
            atlas.Bounds,
            atlas.EastWestPeriodic,
            downstream,
            accumulation,
            isRiver,
            isCoast,
            max);
    }

    /// <summary>Steepest-descent neighbour for each cell, or -1 where the cell is a sink.</summary>
    private static int[] ComputeFlowDirections(
        double[] heights, int w, int h, int stride, bool eastWestPeriodic)
    {
        var downstream = new int[w * h];

        // Diagonal steps are longer, so slope must be drop over distance or flow biases
        // toward diagonals. Both constants are exact in binary floating point at this
        // magnitude, and Sqrt is IEEE-correctly-rounded, so this stays reproducible.
        double straight = stride;
        double diagonal = DetMath.Sqrt(2.0) * stride;

        for (int j = 0; j < h; j++)
        {
            for (int i = 0; i < w; i++)
            {
                int idx = (j * w) + i;
                double own = heights[idx];

                int best = -1;
                double bestSlope = 0.0;

                for (int d = 0; d < 8; d++)
                {
                    int ni = i + OffsetX[d];
                    int nj = j + OffsetZ[d];

                    if (nj < 0 || nj >= h) continue;
                    if (eastWestPeriodic)
                    {
                        ni = WrapIndex(ni, w);
                    }
                    else if (ni < 0 || ni >= w)
                    {
                        continue;
                    }

                    int nIdx = (nj * w) + ni;
                    if (nIdx == idx) continue;
                    double drop = own - heights[nIdx];
                    if (drop <= 0.0) continue;

                    double distance = (OffsetX[d] != 0 && OffsetZ[d] != 0) ? diagonal : straight;
                    double slope = drop / distance;

                    // Strictly greater, so the fixed neighbour order breaks ties.
                    if (slope > bestSlope)
                    {
                        bestSlope = slope;
                        best = nIdx;
                    }
                }

                downstream[idx] = best;
            }
        }

        return downstream;
    }

    /// <summary>Cells drained by each cell, including itself.</summary>
    private static double[] ComputeAccumulation(double[] heights, int[] downstream, int n)
    {
        var accumulation = new double[n];
        for (int i = 0; i < n; i++) accumulation[i] = 1.0;

        // Processing from high to low guarantees a cell is finished before its
        // downstream neighbour is reached, since every downstream link goes strictly
        // downhill. Ties are broken by index so the order is total and reproducible —
        // an unstable sort over equal heights would otherwise be a determinism hole.
        var order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;

        Array.Sort(order, (a, b) =>
        {
            int byHeight = heights[b].CompareTo(heights[a]);
            return byHeight != 0 ? byHeight : a.CompareTo(b);
        });

        for (int k = 0; k < n; k++)
        {
            int idx = order[k];
            int next = downstream[idx];
            if (next >= 0)
            {
                accumulation[next] += accumulation[idx];
            }
        }

        return accumulation;
    }

    private static bool[] ClassifyRivers(double[] accumulation, bool[] submerged, int n)
    {
        var isRiver = new bool[n];

        // Threshold by percentile rather than an absolute figure, so river density stays
        // sensible whatever the world size or lattice stride.
        var landAccumulation = new List<double>();
        for (int i = 0; i < n; i++)
        {
            if (!submerged[i]) landAccumulation.Add(accumulation[i]);
        }

        if (landAccumulation.Count == 0) return isRiver;

        landAccumulation.Sort();
        int cutIndex = (int)((1.0 - RiverFraction) * (landAccumulation.Count - 1));
        double threshold = landAccumulation[Math.Clamp(cutIndex, 0, landAccumulation.Count - 1)];

        for (int i = 0; i < n; i++)
        {
            isRiver[i] = !submerged[i] && accumulation[i] >= threshold;
        }

        return isRiver;
    }

    private static bool[] ClassifyCoast(
        bool[] submerged, int w, int h, bool eastWestPeriodic)
    {
        var isCoast = new bool[w * h];

        for (int j = 0; j < h; j++)
        {
            for (int i = 0; i < w; i++)
            {
                int idx = (j * w) + i;
                if (submerged[idx]) continue;

                for (int d = 0; d < 8; d++)
                {
                    int ni = i + OffsetX[d];
                    int nj = j + OffsetZ[d];

                    if (nj < 0 || nj >= h) continue;
                    if (eastWestPeriodic)
                    {
                        ni = WrapIndex(ni, w);
                    }
                    else if (ni < 0 || ni >= w)
                    {
                        continue;
                    }

                    if (submerged[(nj * w) + ni])
                    {
                        isCoast[idx] = true;
                        break;
                    }
                }
            }
        }

        return isCoast;
    }

    private int IndexOfWorld(int x, int z)
    {
        int normalizedX = _eastWestPeriodic ? _bounds.WrapX(x) : x;
        int i = Math.Clamp(
            (normalizedX - _bounds.MinX + (_stride / 2)) / _stride, 0, _width - 1);
        int j = Math.Clamp((z - _bounds.MinZ + (_stride / 2)) / _stride, 0, _height - 1);
        return (j * _width) + i;
    }

    /// <summary>Whether a river runs through this location, at lattice resolution.</summary>
    public bool IsRiver(int x, int z) => _isRiver[IndexOfWorld(x, z)];

    /// <summary>Whether this location is land adjacent to ocean.</summary>
    public bool IsCoast(int x, int z) => _isCoast[IndexOfWorld(x, z)];

    /// <summary>Drainage at this location, normalised to [0, 1] against the world's largest.</summary>
    public double FlowAt(int x, int z) =>
        _maxAccumulation <= 0.0 ? 0.0 : _accumulation[IndexOfWorld(x, z)] / _maxAccumulation;

    /// <summary>River flag per lattice node, row-major. For raster export.</summary>
    public bool RiverAtNode(int i, int j) =>
        _isRiver[(Math.Clamp(j, 0, _height - 1) * _width) + NormalizeColumn(i)];

    public bool CoastAtNode(int i, int j) =>
        _isCoast[(Math.Clamp(j, 0, _height - 1) * _width) + NormalizeColumn(i)];

    /// <summary>One reach of a river: where it runs from, where it runs to, and how much it carries.</summary>
    public readonly record struct RiverSegment(
        int FromX, int FromZ, int ToX, int ToZ, double Strength);

    /// <summary>
    /// The river network as line segments following the flow graph.
    /// </summary>
    /// <remarks>
    /// Exported for the map view instead of a per-cell river flag. A flag rasterises to a block the
    /// size of the lattice stride — 256 world units — so rivers would render as a scatter of squares
    /// that read as lakes rather than as watercourses. Segments follow the actual downstream links,
    /// so they draw as continuous lines at any zoom and carry a width from their drainage.
    /// </remarks>
    public IEnumerable<RiverSegment> RiverSegments()
    {
        for (int j = 0; j < _height; j++)
        {
            for (int i = 0; i < _width; i++)
            {
                int index = (j * _width) + i;
                if (!_isRiver[index]) continue;

                int downstream = _downstream[index];
                if (downstream < 0) continue;

                int di = downstream % _width;
                int dj = downstream / _width;

                int fromX = _bounds.MinX + (i * _stride);
                int toX = _bounds.MinX + (di * _stride);
                if (_eastWestPeriodic && Math.Abs(di - i) > 1)
                {
                    // A wrapped reach is short on the cylinder but would be drawn across the
                    // whole flat map. Draw the copy that meets the appropriate seam instead.
                    if (di < i)
                    {
                        toX = _bounds.MaxX;
                    }
                    else
                    {
                        fromX = _bounds.MaxX;
                    }
                }

                yield return new RiverSegment(
                    FromX: fromX,
                    FromZ: _bounds.MinZ + (j * _stride),
                    ToX: toX,
                    ToZ: _bounds.MinZ + (dj * _stride),
                    Strength: _maxAccumulation <= 0.0 ? 0.0 : _accumulation[index] / _maxAccumulation);
            }
        }
    }

    /// <summary>Total river nodes. Diagnostic.</summary>
    public int RiverNodeCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < _isRiver.Length; i++)
            {
                if (_isRiver[i]) count++;
            }

            return count;
        }
    }

    private int NormalizeColumn(int i) =>
        _eastWestPeriodic ? WrapIndex(i, _width) : Math.Clamp(i, 0, _width - 1);

    private static int WrapIndex(int i, int width)
    {
        int wrapped = i % width;
        return wrapped < 0 ? wrapped + width : wrapped;
    }
}
