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
///
/// <para><b>The grid answers more than "is there a river here".</b> Once the flow graph and the
/// submerged mask exist, confluences, river mouths, sheltered water and the distance to either kind
/// of water all fall out of them for no further sampling. That matters because the flags alone are
/// quantised to a stride far coarser than a siting decision: sixteen candidates can share one cell
/// and therefore one answer. The graded planes — <see cref="ShelterAt"/>,
/// <see cref="RiverDistance"/>, <see cref="CoastDistance"/> — are read bilinearly and vary
/// everywhere, which is what lets a score rank sites rather than blocks.</para>
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
    private readonly bool[] _isConfluence;
    private readonly double[] _shelter;
    private readonly double[] _riverDistance;
    private readonly double[] _coastDistance;
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
        bool[] isConfluence,
        double[] shelter,
        double[] riverDistance,
        double[] coastDistance,
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
        _isConfluence = isConfluence;
        _shelter = shelter;
        _riverDistance = riverDistance;
        _coastDistance = coastDistance;
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

        // Flow is derived from the filled surface; everything else keeps the real one. A
        // basin's floor is still at its real elevation for siting and fertility — it is only
        // the question "where does water leave here" that needs the spill route.
        double[] drainage = FillDepressions(heights, submerged, w, h, atlas.EastWestPeriodic);

        int[] downstream = ComputeFlowDirections(
            drainage, w, h, stride, atlas.EastWestPeriodic);
        double[] accumulation = ComputeAccumulation(drainage, downstream, n);
        bool[] isRiver = ClassifyRivers(accumulation, submerged, n);
        bool[] isCoast = ClassifyCoast(submerged, w, h, atlas.EastWestPeriodic);
        bool[] isConfluence = ClassifyConfluences(isRiver, downstream, n);
        double[] shelter = ComputeShelter(submerged, w, h, atlas.EastWestPeriodic);

        double[] riverDistance = DistanceTo(
            isRiver, w, h, stride, atlas.EastWestPeriodic);
        double[] coastDistance = DistanceTo(
            submerged, w, h, stride, atlas.EastWestPeriodic);

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
            isConfluence,
            shelter,
            riverDistance,
            coastDistance,
            max);
    }

    /// <summary>
    /// The height each cell drains at, with closed basins raised to their spill point.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this is here.</b> D8 gives a cell the steepest downhill neighbour or nothing
    /// at all, and "nothing at all" is a sink: water arrives and never leaves. Real terrain is
    /// full of them — a resampled lattice turns every closed hollow, every flat, and every
    /// coastal shelf into one — and the sinks do not merely lose their own drainage. Flow
    /// accumulates <em>into</em> them, so the cells with the most water on them are the pits, and
    /// <see cref="ClassifyRivers"/>, which names the wettest few percent of the land, names the
    /// pits. The Phase 2 terrain trial measured 26 of 41 river cells on an external generator's
    /// terrain to be sinks: two thirds of that world's rivers were puddles, in a world that
    /// otherwise looked entirely reasonable.</para>
    ///
    /// <para>Phase 1's value noise hid this for eight milestones because it is smooth by
    /// construction and produced few enough sinks to pass for weather. Nothing about Vintage
    /// Story's terrain will be smooth, so this belongs in front of Phase 3 rather than behind it.</para>
    ///
    /// <para><b>Priority flood.</b> The sea is the outlet; the flood works inward from it, always
    /// from the lowest frontier cell, and each cell it reaches is raised to at least the level the
    /// water arrived at. A cell higher than that keeps its own elevation, so real relief is
    /// untouched and only the hollows fill. One pass, one visit per cell.</para>
    ///
    /// <para><b>Why the epsilon.</b> Filling a basin flat solves the sink and creates a plateau,
    /// which is the same problem wearing a hat: every cell on a flat has no downhill neighbour
    /// either. Raising each cell a hair above the one that reached it leaves a monotone ramp
    /// toward the outlet instead, so every land cell has somewhere to send its water and the
    /// network is connected by construction rather than by luck. <see cref="SpillEpsilon"/> is a
    /// micrometre: over the longest path a lattice this size can hold, the accumulated tilt is
    /// millimetres, which is below anything the simulation can see and far below the metre or two
    /// that raster quantisation already costs.</para>
    ///
    /// <para><b>Determinism.</b> <see cref="PriorityQueue{TElement, TPriority}"/> makes no promise
    /// about equal priorities, and a lattice has ties everywhere — a flat sea floor is thousands
    /// of them. <see cref="SpillOrder"/> therefore orders by level and then by index, which is a
    /// total order, so the flood visits cells in one fixed sequence whatever the heap does
    /// internally.</para>
    /// </remarks>
    private static double[] FillDepressions(
        double[] heights, bool[] submerged, int w, int h, bool eastWestPeriodic)
    {
        int n = w * h;
        var filled = new double[n];
        var reached = new bool[n];

        var frontier = new PriorityQueue<int, Spill>(SpillOrder.Instance);

        // The sea is where water leaves the world. A world with no sea at all — small, high, or
        // simply dry — drains off its edges instead, since the alternative is a lattice with no
        // outlet, which fills to its own highest point and drains nowhere.
        bool hasSea = false;
        for (int i = 0; i < n; i++)
        {
            if (submerged[i]) { hasSea = true; break; }
        }

        for (int j = 0; j < h; j++)
        {
            for (int i = 0; i < w; i++)
            {
                int idx = (j * w) + i;

                bool outlet = hasSea
                    ? submerged[idx]
                    : j == 0 || j == h - 1 || (!eastWestPeriodic && (i == 0 || i == w - 1));

                if (!outlet) continue;

                filled[idx] = heights[idx];
                reached[idx] = true;
                frontier.Enqueue(idx, new Spill(filled[idx], idx));
            }
        }

        while (frontier.TryDequeue(out int index, out _))
        {
            int i = index % w;
            int j = index / w;

            for (int d = 0; d < 8; d++)
            {
                if (!TryNeighbour(i, j, d, w, h, eastWestPeriodic, out int nIdx)) continue;
                if (reached[nIdx]) continue;

                reached[nIdx] = true;
                filled[nIdx] = Math.Max(heights[nIdx], filled[index] + SpillEpsilon);
                frontier.Enqueue(nIdx, new Spill(filled[nIdx], nIdx));
            }
        }

        // A cell the flood never reached has no path to any outlet, which on a fully connected
        // lattice cannot happen. Keeping its own height rather than zero means that if it ever
        // does, the result is terrain that is merely undrained rather than terrain at sea level.
        for (int i = 0; i < n; i++)
        {
            if (!reached[i]) filled[i] = heights[i];
        }

        return filled;
    }

    /// <summary>The tilt given to a filled flat, in metres, so that it still runs downhill.</summary>
    private const double SpillEpsilon = 1e-6;

    /// <summary>A cell on the flood frontier: the level water reached it at, and which cell it is.</summary>
    private readonly record struct Spill(double Level, int Index);

    /// <summary>
    /// Lowest first, and on a tie the lower index first.
    /// </summary>
    /// <remarks>
    /// The index is not a tiebreak of convenience; it is what makes the flood reproducible. A
    /// heap's behaviour among equal keys is an implementation detail, and this lattice is full of
    /// equal keys.
    /// </remarks>
    private sealed class SpillOrder : IComparer<Spill>
    {
        public static readonly SpillOrder Instance = new();

        public int Compare(Spill a, Spill b)
        {
            int byLevel = a.Level.CompareTo(b.Level);
            return byLevel != 0 ? byLevel : a.Index.CompareTo(b.Index);
        }
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
                    if (!TryNeighbour(i, j, d, w, h, eastWestPeriodic, out int nIdx)) continue;

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
                    if (!TryNeighbour(i, j, d, w, h, eastWestPeriodic, out int nIdx)) continue;

                    if (submerged[nIdx])
                    {
                        isCoast[idx] = true;
                        break;
                    }
                }
            }
        }

        return isCoast;
    }

    /// <summary>River cells that two or more river cells drain into.</summary>
    /// <remarks>
    /// Counted off the flow graph rather than looked for geometrically, so a confluence is where
    /// water actually meets rather than where two channels happen to pass near each other. Only
    /// river tributaries count: every cell has upstream neighbours, but a river joined by two
    /// hillsides is not Koblenz.
    /// </remarks>
    private static bool[] ClassifyConfluences(bool[] isRiver, int[] downstream, int n)
    {
        var tributaries = new int[n];

        for (int i = 0; i < n; i++)
        {
            if (!isRiver[i]) continue;

            int next = downstream[i];
            if (next >= 0 && isRiver[next]) tributaries[next]++;
        }

        var isConfluence = new bool[n];
        for (int i = 0; i < n; i++)
        {
            isConfluence[i] = isRiver[i] && tributaries[i] >= 2;
        }

        return isConfluence;
    }

    /// <summary>
    /// How far around the water beside a shore cell is ringed by land, over
    /// <see cref="ShelterRadius"/> cells.
    /// </summary>
    /// <remarks>
    /// A radius rather than the immediate neighbours, and this is the whole difference between the
    /// measure working and not. Enclosure counted over the eight touching cells puts every shore in
    /// the world between a third and two thirds, because what it actually measures is "this water is
    /// next to a shoreline" — which is true of a bay and an exposed headland alike. Shelter is a
    /// question about the shape of a coast over a few kilometres, so it has to be asked over a few
    /// kilometres.
    /// </remarks>
    private const int ShelterRadius = 3;

    /// <summary>
    /// The enclosure range a shore actually occupies, which is not [0, 1].
    /// </summary>
    /// <remarks>
    /// Water touching a shore is about half ringed by land more or less by definition — that is
    /// what makes it a shore — so the raw fraction lands between 0.38 and 0.83 across every seed
    /// measured, with the bulk within a few hundredths of a half. Reported raw it discriminates
    /// almost nothing, which is the same trap <c>Fertility</c> fell into when its ramps plateaued
    /// at 1.0. Stretching the occupied range over [0, 1] is what turns a statistic that is
    /// technically correct into one a score can rank sites by.
    /// </remarks>
    private const double OpenShore = 0.35;

    private const double EnclosedShore = 0.80;

    /// <summary>How sheltered the water beside each shore cell is, in [0, 1].</summary>
    /// <remarks>
    /// <para>Whether a place is worth landing at is a property of the water, not of the shore: a
    /// headland and the bay behind it are both "coastal" and are not both harbours. So enclosure is
    /// measured on each <em>water</em> cell — how much of the sea room around it is land — and a
    /// shore cell takes the best enclosure among the water it touches.</para>
    ///
    /// <para>Open sea scores near nothing, a straight coast about a half, and an inlet or bay well
    /// above that. Land with no water beside it scores zero, which is what makes this safe to add
    /// unconditionally to an inland site's score.</para>
    /// </remarks>
    private static double[] ComputeShelter(
        bool[] submerged, int w, int h, bool eastWestPeriodic)
    {
        int n = w * h;
        var enclosure = new double[n];

        for (int j = 0; j < h; j++)
        {
            for (int i = 0; i < w; i++)
            {
                int idx = (j * w) + i;
                if (!submerged[idx]) continue;

                int land = 0;
                int considered = 0;

                // A disc rather than a square window, so a coast running diagonally is not
                // measured as more open than the same coast running north to south.
                for (int dz = -ShelterRadius; dz <= ShelterRadius; dz++)
                {
                    int nj = j + dz;
                    if (nj < 0 || nj >= h) continue;

                    for (int dx = -ShelterRadius; dx <= ShelterRadius; dx++)
                    {
                        if ((dx * dx) + (dz * dz) > ShelterRadius * ShelterRadius) continue;

                        int ni = i + dx;
                        if (eastWestPeriodic)
                        {
                            ni = WrapIndex(ni, w);
                        }
                        else if (ni < 0 || ni >= w)
                        {
                            continue;
                        }

                        considered++;
                        if (!submerged[(nj * w) + ni]) land++;
                    }
                }

                enclosure[idx] = considered == 0 ? 0.0 : land / (double)considered;
            }
        }

        var shelter = new double[n];

        for (int j = 0; j < h; j++)
        {
            for (int i = 0; i < w; i++)
            {
                int idx = (j * w) + i;
                if (submerged[idx]) continue;

                double best = 0.0;
                bool touchesWater = false;

                for (int d = 0; d < 8; d++)
                {
                    if (!TryNeighbour(i, j, d, w, h, eastWestPeriodic, out int nIdx)) continue;
                    if (!submerged[nIdx]) continue;

                    touchesWater = true;
                    if (enclosure[nIdx] > best) best = enclosure[nIdx];
                }

                shelter[idx] = touchesWater
                    ? DetMath.InverseLerp(OpenShore, EnclosedShore, best)
                    : 0.0;
            }
        }

        return shelter;
    }

    /// <summary>Distance from every cell to the nearest cell of <paramref name="source"/>, in world units.</summary>
    /// <remarks>
    /// <para><b>Why a distance and not a flag.</b> The grid is far coarser than a siting decision —
    /// sixteen candidates can share one cell — so a boolean answer cannot rank them and the whole
    /// premium collapses onto whichever block happens to hold a river. A distance varies everywhere
    /// and gives the score something to say between two points a stride apart.</para>
    ///
    /// <para><b>Integer 3-4 chamfer.</b> An orthogonal step costs 3 and a diagonal 4, so the
    /// propagation carries no floating point at all and dividing by three at the end approximates
    /// Euclidean distance to within about eight percent — far finer than the grid it is measured on,
    /// and free of any question about reproducibility.</para>
    ///
    /// <para><b>Relaxed from a queue, not raster-scanned.</b> The textbook two-pass forward/backward
    /// scan does not converge in one round across an east/west seam, because a cell's nearest source
    /// can lie in the direction the pass has already left behind. A FIFO relaxation converges
    /// wherever the seam is, for the same neighbourhood work, and sources enter in index order so
    /// the result is reproducible.</para>
    /// </remarks>
    private static double[] DistanceTo(
        bool[] source, int w, int h, int stride, bool eastWestPeriodic)
    {
        const int OrthogonalStep = 3;
        const int DiagonalStep = 4;

        int n = w * h;
        var cost = new int[n];
        var pending = new Queue<int>();

        for (int i = 0; i < n; i++)
        {
            cost[i] = source[i] ? 0 : int.MaxValue;
            if (source[i]) pending.Enqueue(i);
        }

        while (pending.Count > 0)
        {
            int idx = pending.Dequeue();
            int i = idx % w;
            int j = idx / w;

            for (int d = 0; d < 8; d++)
            {
                if (!TryNeighbour(i, j, d, w, h, eastWestPeriodic, out int nIdx)) continue;

                int step = (OffsetX[d] != 0 && OffsetZ[d] != 0) ? DiagonalStep : OrthogonalStep;
                int relaxed = cost[idx] + step;

                if (relaxed < cost[nIdx])
                {
                    cost[nIdx] = relaxed;
                    pending.Enqueue(nIdx);
                }
            }
        }

        // A world with no source at all — no rivers, or no sea — leaves every cell unreached.
        // Report a finite distance larger than the world rather than infinity, so a consumer can
        // divide by it without special-casing a world that legitimately has no coast.
        double unreached = (w + h) * (double)stride;

        var distance = new double[n];
        for (int i = 0; i < n; i++)
        {
            distance[i] = cost[i] == int.MaxValue
                ? unreached
                : cost[i] * stride / 3.0;
        }

        return distance;
    }

    /// <summary>The neighbour in direction <paramref name="d"/>, or false where the grid ends.</summary>
    private static bool TryNeighbour(
        int i, int j, int d, int w, int h, bool eastWestPeriodic, out int index)
    {
        index = -1;

        int ni = i + OffsetX[d];
        int nj = j + OffsetZ[d];

        if (nj < 0 || nj >= h) return false;

        if (eastWestPeriodic)
        {
            ni = WrapIndex(ni, w);
        }
        else if (ni < 0 || ni >= w)
        {
            return false;
        }

        index = (nj * w) + ni;
        return index != (j * w) + i;
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

    /// <summary>Whether two or more rivers meet here.</summary>
    public bool IsConfluence(int x, int z) => _isConfluence[IndexOfWorld(x, z)];

    /// <summary>
    /// Whether a river reaches the sea here.
    /// </summary>
    /// <remarks>
    /// Not a stored plane: a river cell that is also a coast cell <em>is</em> a river mouth, since
    /// both already mean "land" and the two conditions together mean the watercourse touches the
    /// ocean. Deriving it keeps one definition rather than two that can drift apart.
    /// </remarks>
    public bool IsEstuary(int x, int z)
    {
        int index = IndexOfWorld(x, z);
        return _isRiver[index] && _isCoast[index];
    }

    /// <summary>Drainage at this location, normalised to [0, 1] against the world's largest.</summary>
    public double FlowAt(int x, int z) =>
        _maxAccumulation <= 0.0 ? 0.0 : _accumulation[IndexOfWorld(x, z)] / _maxAccumulation;

    /// <summary>How sheltered the water beside this location is, in [0, 1]. Zero inland.</summary>
    public double ShelterAt(int x, int z) => Interpolate(_shelter, x, z);

    /// <summary>Distance to the nearest river, in world units.</summary>
    public double RiverDistance(int x, int z) => Interpolate(_riverDistance, x, z);

    /// <summary>Distance to the nearest open water, in world units.</summary>
    public double CoastDistance(int x, int z) => Interpolate(_coastDistance, x, z);

    /// <summary>Distance within which water is on the doorstep, and beyond which it is a journey.</summary>
    /// <remarks>
    /// The grid resolves valleys rather than channels, so "on the river" cannot mean nearer than
    /// about one cell. Full credit inside that, fading to nothing at the distance a settlement
    /// would have to haul water rather than walk to it.
    /// </remarks>
    private const double WaterAtHand = 64.0;

    private const double WaterTooFar = 384.0;

    /// <summary>How much fresh water this spot has, in [0, 1].</summary>
    /// <remarks>
    /// Kept here rather than at each call site because two very different consumers need the same
    /// answer — siting ranks candidates by it and region habitability sorts whole regions by it —
    /// and two curves that were meant to agree and quietly drifted would be a bug nothing would
    /// catch, since each would look reasonable alone.
    /// </remarks>
    public double RiverAccess(int x, int z) => Nearness(RiverDistance(x, z));

    /// <summary>How much sea this spot has, in [0, 1], regardless of whether it is worth landing at.</summary>
    public double SeaAccess(int x, int z) => Nearness(CoastDistance(x, z));

    /// <summary>Water's worth at a distance, in [0, 1].</summary>
    private static double Nearness(double distance) =>
        DetMath.InverseLerp(WaterTooFar, WaterAtHand, distance);

    /// <summary>
    /// Bilinear read of a continuous plane, so it varies between grid cells rather than in blocks.
    /// </summary>
    /// <remarks>
    /// The reason the graded measures exist at all: a siting decision compares candidates a
    /// quarter of a stride apart, and a nearest-cell read would hand all sixteen of them the same
    /// number. Only the continuous planes are read this way — a flag cannot be averaged, so
    /// <see cref="IsRiver"/> and its kind stay nearest-cell and are combined with a distance
    /// instead.
    /// </remarks>
    private double Interpolate(double[] field, int x, int z)
    {
        int normalizedX = _eastWestPeriodic ? _bounds.WrapX(x) : x;

        double fx = (normalizedX - _bounds.MinX) / (double)_stride;
        double fz = (z - _bounds.MinZ) / (double)_stride;

        int i0 = (int)Math.Floor(fx);
        int j0 = (int)Math.Floor(fz);

        if (!_eastWestPeriodic)
        {
            if (i0 < 0) i0 = 0;
            if (i0 > _width - 2) i0 = Math.Max(0, _width - 2);
        }

        if (j0 < 0) j0 = 0;
        if (j0 > _height - 2) j0 = Math.Max(0, _height - 2);

        double tx = DetMath.Clamp01(fx - i0);
        double tz = DetMath.Clamp01(fz - j0);

        int c0 = NormalizeColumn(i0);
        int c1 = NormalizeColumn(i0 + 1);
        int r0 = Math.Clamp(j0, 0, _height - 1);
        int r1 = Math.Clamp(j0 + 1, 0, _height - 1);

        double top = DetMath.Lerp(field[(r0 * _width) + c0], field[(r0 * _width) + c1], tx);
        double bottom = DetMath.Lerp(field[(r1 * _width) + c0], field[(r1 * _width) + c1], tx);
        return DetMath.Lerp(top, bottom, tz);
    }

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
