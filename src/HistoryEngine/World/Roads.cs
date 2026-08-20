using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Terrain;

namespace HistoryEngine.World;

/// <summary>
/// Cuts the physical way a trade route takes, as a least-cost path over the ground.
/// </summary>
/// <remarks>
/// <para><b>Why this lives under <c>World/</c> and not <c>Systems/</c>.</b> A path is a question
/// about terrain, and a system may not hold terrain — <c>TerrainDisciplineTests</c> fails the build
/// if anything under <c>Systems/</c> so much as names <c>ITerrainSampler</c>. So the trade system
/// decides <em>when</em> a road is worth building, from traffic it already knows, and this decides
/// <em>where</em> it goes. The split is the same one <c>SiteSelection</c> makes for founding.</para>
///
/// <para><b>It samples nothing.</b> The grid it searches is the one hydrology already paid for at
/// world creation: <see cref="TerrainAtlas.SampleGrid"/> memoises every point it returns, so
/// re-reading it here costs a cache hit per cell and no backend call. That is the whole reason the
/// terms this can afford to use are the ones derivable from elevation — height, drainage,
/// ruggedness, passes — rather than anything needing a fresh look at the ground.</para>
///
/// <para><b>Integer costs, and a total order on the frontier.</b> Every toll below is an integer,
/// so no two candidate paths can tie on a rounding difference; and the queue is keyed on
/// <c>cost × cells + index</c>, which makes the minimum unique. Dijkstra is then reproducible
/// whatever heap the runtime happens to implement underneath — which matters, because the engine
/// compiles for net7.0 and runs its tests on net10.0. A float cost with ties broken by pop order
/// would pass every test on one framework and quietly reroute a road on the other.</para>
///
/// <para><b>The grid is 64 units, so this finds valleys and not verges.</b> A road here means "the
/// way through this country", at the same resolution the engine's rivers and passes are known to.
/// It is not a claim about which side of a hedge the surface runs.</para>
/// </remarks>
public sealed class Roadbed
{
    /// <summary>What a step over easy, flat, dry ground costs.</summary>
    /// <remarks>
    /// The unit everything else is measured against. It is not zero, so that distance still counts:
    /// with a free baseline a path would take any detour that avoided a single rough cell.
    /// </remarks>
    private const int FlatToll = 10;

    /// <summary>What a river cell costs a route that travels <em>along</em> it.</summary>
    /// <remarks>
    /// Below <see cref="FlatToll"/>, and that is the point: a river route's path has to prefer the
    /// water to the dry ground beside it, or "River" would be a label on a line that ignored the
    /// river. Water carries goods more cheaply than any land surface in this period, so the number
    /// is not merely a drawing convenience.
    /// </remarks>
    private const int TowpathToll = 4;

    /// <summary>What crossing a watercourse costs a worn track, and an engineered road.</summary>
    /// <remarks>
    /// A ford is the expensive thing a track goes a long way round to avoid; a bridge is most of
    /// what an engineered road buys. The gap between these two numbers is the largest single reason
    /// an upgraded road takes a different line from the track it replaces.
    /// </remarks>
    private const int FordToll = 60;

    private const int BridgedFordToll = 15;

    /// <summary>How much of the country's ruggedness a track pays, and a paved road pays.</summary>
    /// <remarks>
    /// Percentages of the full grade penalty. A track is at the mercy of the ground and contours
    /// around it; a paved road cuts and embanks, so broken country costs it half as much and it
    /// takes the straighter line. Halved rather than removed — no amount of engineering makes a
    /// mountain the same as a plain.
    /// </remarks>
    private const int TrackSlopeWeight = 90;

    private const int PavedSlopeWeight = 45;

    /// <summary>What a col is worth against the ridge either side of it.</summary>
    /// <remarks>
    /// A pass keeps its ruggedness — it is still in the mountains — but pays a third of it, which
    /// is what draws a path through the saddle rather than over the shoulder beside it. Without the
    /// discount the search still crosses ridges at their low points most of the time, because low
    /// points are less steep; with it, the crossing lands on the col the terrain layer has already
    /// named, so the map and the model agree about where the way through is.
    /// </remarks>
    private const int PassTollDivisor = 3;

    // Eight neighbours in the same fixed order the terrain layer uses. The order is part of the
    // determinism contract everywhere it appears; here it is belt and braces, since the frontier's
    // key already makes every pop unique.
    private static readonly int[] OffsetX = { 1, 1, 0, -1, -1, -1, 0, 1 };
    private static readonly int[] OffsetZ = { 0, 1, 1, 1, 0, -1, -1, -1 };

    /// <summary>Chamfer steps: an orthogonal move is 3, a diagonal 4, as in the distance planes.</summary>
    private const int OrthogonalStep = 3;

    private const int DiagonalStep = 4;

    private readonly int _width;
    private readonly int _height;
    private readonly int _stride;
    private readonly TerrainBounds _bounds;
    private readonly bool _eastWestPeriodic;

    private readonly bool[] _submerged;
    private readonly bool[] _isRiver;
    private readonly bool[] _isPass;
    private readonly int[] _ruggedness;

    private Roadbed(
        int width,
        int height,
        int stride,
        TerrainBounds bounds,
        bool eastWestPeriodic,
        bool[] submerged,
        bool[] isRiver,
        bool[] isPass,
        int[] ruggedness)
    {
        _width = width;
        _height = height;
        _stride = stride;
        _bounds = bounds;
        _eastWestPeriodic = eastWestPeriodic;
        _submerged = submerged;
        _isRiver = isRiver;
        _isPass = isPass;
        _ruggedness = ruggedness;
    }

    /// <summary>
    /// Reads the ground a road can be cut through, on the grid hydrology already primed.
    /// </summary>
    /// <remarks>
    /// Built once per world and held on <see cref="WorldState"/>, not per road: the planes are the
    /// same for every route, and rebuilding them per construction would turn a one-off read of a
    /// cached grid into one per event for no different answer.
    /// </remarks>
    public static Roadbed Build(TerrainAtlas atlas)
    {
        int stride = atlas.HydrologyStride;
        TerrainSample[] grid = atlas.SampleGrid(stride, out int w, out int h);

        Hydrology hydrology = atlas.Hydrology;
        Landform landform = atlas.Landform;

        int n = w * h;
        var submerged = new bool[n];
        var isRiver = new bool[n];
        var isPass = new bool[n];
        var ruggedness = new int[n];

        for (int j = 0; j < h; j++)
        {
            for (int i = 0; i < w; i++)
            {
                int idx = (j * w) + i;
                int x = atlas.Bounds.MinX + (i * stride);
                int z = atlas.Bounds.MinZ + (j * stride);

                submerged[idx] = grid[idx].Height < 0f;
                isRiver[idx] = hydrology.RiverAtNode(i, j);
                isPass[idx] = landform.IsPass(x, z);

                // Quantised to hundredths on the way in. Every toll downstream is an integer, so
                // the one place a real number is allowed to enter is here, where the rounding is
                // explicit and identical on every runtime.
                ruggedness[idx] = (int)(DetMath.Clamp01(landform.RuggednessAt(x, z)) * 100.0);
            }
        }

        return new Roadbed(
            w, h, stride, atlas.Bounds, atlas.EastWestPeriodic,
            submerged, isRiver, isPass, ruggedness);
    }

    /// <summary>
    /// The cheapest way over the ground between two points, or null if there is none.
    /// </summary>
    /// <remarks>
    /// <para>Null is a real answer, not a failure: two towns can trade across a strait — the route
    /// system measures straight-line distance, not walking distance — and no road can be built
    /// between them. The caller records nothing in that case. No qualifying pair in the five
    /// standard seeds has actually been unreachable, so this is a guard against a world that can
    /// happen rather than a fix for one that did.</para>
    ///
    /// <para>Only the endpoint cells may be under water, so that a quayside town whose grid cell is
    /// mostly sea can still be an endpoint. Every interior vertex is dry land by construction,
    /// which is what makes "an overland road never crosses deep water" an invariant rather than a
    /// hope.</para>
    /// </remarks>
    public Road? Cut(
        int fromX, int fromZ, int toX, int toZ, TradeRouteMode mode, RoadGrade grade, int year)
    {
        int source = IndexOfWorld(fromX, fromZ);
        int target = IndexOfWorld(toX, toZ);

        var points = new List<RoadPoint> { new(fromX, fromZ) };

        if (source != target)
        {
            int[]? cells = Search(source, target, mode, grade);
            if (cells is null) return null;

            AppendInterior(points, cells);
        }

        points.Add(new RoadPoint(toX, toZ));

        return new Road(
            points,
            grade,
            builtYear: year,
            pavedYear: grade == RoadGrade.Paved ? year : null,
            length: LengthOf(points));
    }

    /// <summary>Dijkstra over the cell graph, returning the cells from source to target.</summary>
    private int[]? Search(int source, int target, TradeRouteMode mode, RoadGrade grade)
    {
        int n = _width * _height;

        var cost = new int[n];
        var previous = new int[n];
        var settled = new bool[n];

        for (int i = 0; i < n; i++)
        {
            cost[i] = int.MaxValue;
            previous[i] = -1;
        }

        cost[source] = 0;

        // Keyed on cost × cells + index, so the frontier has a total order and the minimum is
        // unique. That is what makes the result independent of the heap's internal tie-breaking,
        // which is not part of any framework's contract.
        var frontier = new PriorityQueue<int, long>();
        frontier.Enqueue(source, ((long)cost[source] * n) + source);

        while (frontier.TryDequeue(out int idx, out _))
        {
            if (settled[idx]) continue;
            settled[idx] = true;
            if (idx == target) break;

            int i = idx % _width;
            int j = idx / _width;

            for (int d = 0; d < 8; d++)
            {
                if (!TryNeighbour(i, j, d, out int next)) continue;
                if (settled[next]) continue;

                // Water is impassable except where the road has to start or finish.
                if (_submerged[next] && next != source && next != target) continue;

                int step = (OffsetX[d] != 0 && OffsetZ[d] != 0) ? DiagonalStep : OrthogonalStep;
                int relaxed = cost[idx] + (step * (Toll(idx, mode, grade) + Toll(next, mode, grade)));

                if (relaxed >= cost[next]) continue;

                cost[next] = relaxed;
                previous[next] = idx;
                frontier.Enqueue(next, ((long)relaxed * n) + next);
            }
        }

        if (cost[target] == int.MaxValue) return null;

        var reversed = new List<int>();
        for (int at = target; at >= 0; at = previous[at]) reversed.Add(at);
        reversed.Reverse();
        return reversed.ToArray();
    }

    /// <summary>What one cell costs to travel over, for this transport and this grade.</summary>
    private int Toll(int idx, TradeRouteMode mode, RoadGrade grade)
    {
        int weight = grade == RoadGrade.Paved ? PavedSlopeWeight : TrackSlopeWeight;
        int toll = FlatToll + (_ruggedness[idx] * weight / 100);

        if (_isPass[idx]) toll = FlatToll + ((toll - FlatToll) / PassTollDivisor);

        if (_isRiver[idx])
        {
            // The same cell is a highway or a barrier depending on which way the traffic means to
            // go over it. A river route follows the water; an overland route has to get across it.
            toll = mode == TradeRouteMode.River
                ? TowpathToll
                : toll + (grade == RoadGrade.Paved ? BridgedFordToll : FordToll);
        }

        return toll;
    }

    /// <summary>
    /// Adds a vertex wherever the way turns, dropping the endpoint cells and every straight run.
    /// </summary>
    /// <remarks>
    /// The cells are a dense trail at one vertex per 64 units; the road is the shape of that trail.
    /// Keeping only the turns cuts a typical path from around thirty points to under ten, which is
    /// export bytes and viewer geometry saved for a line that draws identically. The first and last
    /// cells are dropped because the caller has the settlements' exact coordinates, which are truer
    /// than the grid cell containing them.
    /// </remarks>
    private void AppendInterior(List<RoadPoint> points, int[] cells)
    {
        for (int k = 1; k < cells.Length - 1; k++)
        {
            int previousStep = StepBetween(cells[k - 1], cells[k]);
            int nextStep = StepBetween(cells[k], cells[k + 1]);
            if (previousStep == nextStep) continue;

            int i = cells[k] % _width;
            int j = cells[k] / _width;
            points.Add(new RoadPoint(
                _bounds.MinX + (i * _stride), _bounds.MinZ + (j * _stride)));
        }
    }

    /// <summary>Direction from one cell to the next, as a packed pair. Only equality is asked of it.</summary>
    private int StepBetween(int from, int to)
    {
        int di = (to % _width) - (from % _width);
        int dj = (to / _width) - (from / _width);

        // A wrapped step reads as a jump the width of the world; normalise it back to one cell so
        // a road crossing the seam is not given a spurious corner there.
        if (_eastWestPeriodic && di > 1) di -= _width;
        if (_eastWestPeriodic && di < -1) di += _width;

        return ((di + 1) * 3) + (dj + 1);
    }

    /// <summary>Length along the polyline, the short way round a periodic world.</summary>
    private double LengthOf(List<RoadPoint> points)
    {
        double length = 0.0;

        for (int i = 1; i < points.Count; i++)
        {
            length += _bounds.Distance(
                points[i - 1].X, points[i - 1].Z, points[i].X, points[i].Z, _eastWestPeriodic);
        }

        return length;
    }

    private bool TryNeighbour(int i, int j, int d, out int index)
    {
        index = -1;

        int ni = i + OffsetX[d];
        int nj = j + OffsetZ[d];

        if (nj < 0 || nj >= _height) return false;

        if (_eastWestPeriodic)
        {
            ni = WrapIndex(ni, _width);
        }
        else if (ni < 0 || ni >= _width)
        {
            return false;
        }

        index = (nj * _width) + ni;
        return index != (j * _width) + i;
    }

    private int IndexOfWorld(int x, int z)
    {
        int normalizedX = _eastWestPeriodic ? _bounds.WrapX(x) : x;
        int i = Math.Clamp(
            (normalizedX - _bounds.MinX + (_stride / 2)) / _stride, 0, _width - 1);
        int j = Math.Clamp((z - _bounds.MinZ + (_stride / 2)) / _stride, 0, _height - 1);

        if (_eastWestPeriodic) i = WrapIndex(i, _width);

        return (j * _width) + i;
    }

    private static int WrapIndex(int i, int width)
    {
        int wrapped = i % width;
        return wrapped < 0 ? wrapped + width : wrapped;
    }
}

/// <summary>When a commercial link has earned a physical way, and what it costs to give it one.</summary>
/// <remarks>
/// <para><b>Not every route becomes a road, on purpose.</b> Roads are prioritised by the traffic a
/// route has <em>sustained</em>, not the traffic it happens to carry this year, so a corridor that
/// was busy for a generation keeps its road through a bad decade and a link that flickered above
/// the line for one year never gets one. Measured across seeds 2, 7, 11, 42 and 99, the thresholds
/// below select roughly a quarter to a third of land routes — a network with hubs and trunk lines
/// rather than a road under every line on the map. The decision log carries the sweep.</para>
///
/// <para><b>Coastal routes get no road,</b> and that is a claim rather than an omission: a coastal
/// route is sailed. The engine models no hulls, no ports beyond access, and no sea lanes, so a
/// polyline hugging the shore would be geometry nothing in the simulation earned. When ships exist,
/// their courses can be added here as the thing they are.</para>
/// </remarks>
public static class Roads
{
    /// <summary>Sustained traffic at which a link is worth a made way.</summary>
    /// <remarks>
    /// Set from the measured distribution rather than chosen: peak traffic across five standard
    /// seeds runs a median near 0.62 with a maximum near 0.89, so 0.68 sits between the median and
    /// the top decile. Lowering it to 0.62 roads half the network and the map stops distinguishing
    /// trunk from spur; raising it to 0.76 leaves the sparsest seeds with a single road.
    /// </remarks>
    public const double BuildThreshold = 0.68;

    /// <summary>Sustained traffic at which a track is worth engineering.</summary>
    /// <remarks>
    /// <para>The engineered line differs from the track it replaced in only two of eighteen cases
    /// across the standard seeds — the rest are short roads over easy ground where cuttings and
    /// bridges have nothing to cut through. That was once read as grounds for deleting the grade,
    /// on the reasoning that a term moving no outcome is decoration.</para>
    ///
    /// <para><b>It was the wrong measure.</b> What paving produces is not a polyline but a dated
    /// fact: a way a town has used for three generations being bridged is history whether or not
    /// the course shifts a cell. The geometry is the smaller half of what this earns, so the grade
    /// is kept and the test it is held to is the chronicle. What actually needed fixing was
    /// <em>when</em> it fired — see <see cref="MinimumTrackYears"/>.</para>
    /// </remarks>
    public const double PaveThreshold = 0.76;

    /// <summary>Whether this route's traffic has earned it a first way over the ground.</summary>
    /// <remarks>
    /// False once the ground has been searched, whatever the search found. A pair with no way
    /// between them is answered once and not asked again, so a busy link across a strait costs one
    /// path search for its whole life rather than one a year.
    /// </remarks>
    public static bool DeservesRoad(TradeRoute route) =>
        route.Road is null
        && !route.RoadSurveyed
        && route.Mode != TradeRouteMode.Coastal
        && route.PeakTraffic >= BuildThreshold;

    /// <summary>
    /// How long a track must have carried traffic before it is worth engineering.
    /// </summary>
    /// <remarks>
    /// <para><b>Paving is a later generation's decision, and without this it was not one.</b>
    /// <see cref="TradeRoute.PeakTraffic"/> is a high-water mark that only ever rises, so a link
    /// whose peak crossed <see cref="BuildThreshold"/> this year had usually crossed
    /// <see cref="PaveThreshold"/> by the next: thirteen of nineteen pavings in an eight-century
    /// run landed the year after the road was cut. A road bridged the spring after it was first
    /// trodden is not a road anybody has lived on — it reads as the same decision made twice, which
    /// is what a chronicle shows and a fingerprint cannot.</para>
    ///
    /// <para>Twenty-five years is about a generation and roughly two reigns at this engine's mean,
    /// so the track is worn in by people who did not choose it before anyone spends on bridging it.
    /// Longer reads better in isolation and is worse in practice: at forty, two of the five standard
    /// seeds finished three centuries with no paved road at all, and a tier absent from the default
    /// run length is not rare, it is invisible.</para>
    /// </remarks>
    public const int MinimumTrackYears = 25;

    /// <summary>
    /// How much longer than the minimum an unmercantile people takes to get round to it.
    /// </summary>
    /// <remarks>
    /// <para>A flat minimum fixes the "paved the year after it was cut" fault and introduces a
    /// smaller one in its place: the floor becomes the mode. Thirteen of nineteen pavings landed on
    /// exactly forty years, and a chronicle in which nine lines all say "after 40 years of use"
    /// reads as mechanically as the thing it replaced.</para>
    ///
    /// <para>So the wait is a reason rather than a constant. A trading people spends on its roads
    /// sooner; one with little interest in commerce leaves the track as it is for another lifetime.
    /// Taken from whichever end of the road wants it more, because one willing partner is enough to
    /// pay for bridges — and read from the realm's effective values, so a mercantile crown can
    /// hurry a road its people would have left alone.</para>
    /// </remarks>
    public const int PavingPatienceYears = 40;

    /// <summary>How long this particular road must stand before it is worth engineering.</summary>
    public static int PavingWait(WorldState world, TradeRoute route)
    {
        double keenest = Math.Max(
            MercantileAt(world, route.SettlementAId),
            MercantileAt(world, route.SettlementBId));

        return MinimumTrackYears + (int)((1.0 - keenest) * PavingPatienceYears);
    }

    /// <summary>How commercially minded the realm holding this settlement is, or the midpoint.</summary>
    private static double MercantileAt(WorldState world, EntityId settlementId)
    {
        if (!world.Settlements.Contains(settlementId)) return 0.5;

        Settlement settlement = world.Settlements[settlementId];
        if (!world.Civilizations.Contains(settlement.CivilizationId)) return 0.5;

        return DetMath.Clamp01(world.Civilizations[settlement.CivilizationId].EffectiveValues.Mercantile);
    }

    /// <summary>Whether an existing track has stood long enough, and carries enough, to be engineered.</summary>
    public static bool DeservesPaving(WorldState world, TradeRoute route, int year) =>
        route.Road is { Grade: RoadGrade.Track } road
        && year - road.BuiltYear >= PavingWait(world, route)
        && route.PeakTraffic >= PaveThreshold;

    /// <summary>
    /// Cuts the way for a route at the given grade, or returns null where none can be cut.
    /// </summary>
    /// <remarks>
    /// The route keeps its id, its founding year and its whole chronicle: only
    /// <see cref="TradeRoute.Road"/> changes, and an upgrade carries the original
    /// <see cref="Road.BuiltYear"/> forward, so the record still says when the way was first made.
    /// </remarks>
    public static Road? Cut(WorldState world, TradeRoute route, RoadGrade grade, int year)
    {
        Settlement a = world.Settlements[route.SettlementAId];
        Settlement b = world.Settlements[route.SettlementBId];

        Road? road = world.Roadbed.Cut(a.X, a.Z, b.X, b.Z, route.Mode, grade, year);
        if (road is null) return null;

        return route.Road is null
            ? road
            : new Road(
                road.Points,
                grade,
                builtYear: route.Road.BuiltYear,
                pavedYear: grade == RoadGrade.Paved ? year : route.Road.PavedYear,
                length: road.Length);
    }
}
