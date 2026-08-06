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
/// <para>Deriving them from elevation sidesteps all of that. Rivers exist identically in
/// every phase, they are guaranteed consistent with the terrain they cut through (a sampled
/// river can contradict a sampled heightmap; a derived one cannot), and they cost zero
/// samples because the lattice is already primed. When Phase 2 or 3 does supply real river
/// data, this becomes the fallback rather than the only path — see
/// <see cref="TerrainCapabilities.Rivers"/>.</para>
///
/// <para>Resolution is the lattice stride, so this locates river <em>valleys</em>, not
/// channels. That is the right scale for the questions history asks of it — which cities sit
/// on a trade river, where an army must ford — and Phase 2 can refine locally if a battle
/// needs a specific crossing.</para>
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
        _downstream = downstream;
        _accumulation = accumulation;
        _isRiver = isRiver;
        _isCoast = isCoast;
        _maxAccumulation = maxAccumulation;
    }

    public static Hydrology FromLattice(TerrainAtlas atlas)
    {
        int w = atlas.LatticeWidth;
        int h = atlas.LatticeHeight;
        int n = w * h;

        var heights = new double[n];
        var submerged = new bool[n];

        for (int j = 0; j < h; j++)
        {
            for (int i = 0; i < w; i++)
            {
                TerrainSample s = atlas.LatticeAt(i, j);
                int idx = (j * w) + i;
                heights[idx] = s.Height;
                submerged[idx] = s.Height < 0f;
            }
        }

        int[] downstream = ComputeFlowDirections(heights, w, h, atlas.Stride);
        double[] accumulation = ComputeAccumulation(heights, downstream, n);
        bool[] isRiver = ClassifyRivers(accumulation, submerged, n);
        bool[] isCoast = ClassifyCoast(submerged, w, h);

        double max = 0.0;
        for (int i = 0; i < n; i++)
        {
            if (accumulation[i] > max) max = accumulation[i];
        }

        return new Hydrology(
            w, h, atlas.Stride, atlas.Bounds, downstream, accumulation, isRiver, isCoast, max);
    }

    /// <summary>Steepest-descent neighbour for each cell, or -1 where the cell is a sink.</summary>
    private static int[] ComputeFlowDirections(double[] heights, int w, int h, int stride)
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

                    if (ni < 0 || ni >= w || nj < 0 || nj >= h) continue;

                    int nIdx = (nj * w) + ni;
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

    private static bool[] ClassifyCoast(bool[] submerged, int w, int h)
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

                    if (ni < 0 || ni >= w || nj < 0 || nj >= h) continue;

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
        int i = Math.Clamp((x - _bounds.MinX + (_stride / 2)) / _stride, 0, _width - 1);
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
        _isRiver[(Math.Clamp(j, 0, _height - 1) * _width) + Math.Clamp(i, 0, _width - 1)];

    public bool CoastAtNode(int i, int j) =>
        _isCoast[(Math.Clamp(j, 0, _height - 1) * _width) + Math.Clamp(i, 0, _width - 1)];

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
}
