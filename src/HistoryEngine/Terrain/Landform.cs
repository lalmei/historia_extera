using HistoryEngine.Core;

namespace HistoryEngine.Terrain;

/// <summary>
/// How broken the country is, and where the ways through it are — derived from the same height
/// grid <see cref="Hydrology"/> is built on.
/// </summary>
/// <remarks>
/// <para><b>Landscape scale, deliberately.</b> This answers "what kind of country is this" at the
/// stride hydrology already pays for. It does not answer "how steep is the ground under this
/// candidate", because a siting decision refines its own grid four times finer and can see that
/// for itself — see <see cref="World.SiteSelection"/>. Two prominence measures at two scales would
/// be one too many, so each question is asked once, at the resolution that can actually answer
/// it.</para>
///
/// <para><b>Free.</b> <see cref="TerrainAtlas.SampleGrid"/> memoises every point it returns into the
/// same cache the three access tiers use, so building this after hydrology re-reads a grid that is
/// already in memory and samples nothing. That is the whole reason the measures worth having here
/// are the ones computable from elevation alone.</para>
/// </remarks>
public sealed class Landform
{
    /// <summary>
    /// Mean grade at which country reads as broken rather than rolling.
    /// </summary>
    /// <remarks>
    /// The normalising range for <see cref="RuggednessAt"/>, not a threshold anything tests
    /// against. Set from the terrain the engine actually produces rather than chosen: measured
    /// across eight seeds, mean grade over land runs a median of 0.08–0.35 with a long tail to
    /// above 4. The top of the range deliberately sits below that tail — past nine tenths of a
    /// metre risen per metre travelled, "more mountainous" is a distinction nothing downstream
    /// needs, and stretching the scale to fit it would flatten the range where every real
    /// judgement is made.
    /// </remarks>
    private const double RollingGrade = 0.04;

    private const double BrokenGrade = 0.90;

    /// <summary>
    /// How high the ridge either side of a col must stand before it is a pass, in metres.
    /// </summary>
    /// <remarks>
    /// A pass is worth something only in proportion to what going around it would cost, so what is
    /// measured is the barrier, not the col. Measured over eight seeds the rise either side of a
    /// saddle has a median of 3–21 m and a maximum of 24–164 m, so 25 m selects the saddles that
    /// genuinely cut through something and leaves a world as flat as seed 777 — whose steepest
    /// barrier is 24 m — with no passes at all, which is the truthful answer for it.
    /// </remarks>
    private const double PassBarrierRise = 25.0;

    /// <summary>How broken the country a col cuts through must be, over <see cref="PassContextRadius"/>.</summary>
    private const double PassContextRuggedness = 0.30;

    /// <summary>How far around a col counts as the country it cuts through.</summary>
    private const int PassContextRadius = 2;

    // Eight neighbours as a closed ring — east, south-east, south, and so on round to north-east.
    // The saddle test below walks this cyclically, so the order is not merely conventional here:
    // it is what makes "how many times does the ground rise and fall around this point" a
    // meaningful question.
    private static readonly int[] OffsetX = { 1, 1, 0, -1, -1, -1, 0, 1 };
    private static readonly int[] OffsetZ = { 0, 1, 1, 1, 0, -1, -1, -1 };

    private readonly int _width;
    private readonly int _height;
    private readonly int _stride;
    private readonly TerrainBounds _bounds;
    private readonly bool _eastWestPeriodic;

    private readonly double[] _ruggedness;
    private readonly bool[] _isPass;

    private Landform(
        int width,
        int height,
        int stride,
        TerrainBounds bounds,
        bool eastWestPeriodic,
        double[] ruggedness,
        bool[] isPass)
    {
        _width = width;
        _height = height;
        _stride = stride;
        _bounds = bounds;
        _eastWestPeriodic = eastWestPeriodic;
        _ruggedness = ruggedness;
        _isPass = isPass;
    }

    /// <summary>Derives the landform planes on the grid at <paramref name="stride"/>.</summary>
    public static Landform Build(TerrainAtlas atlas, int stride)
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

        double[] ruggedness = ComputeRuggedness(
            heights, submerged, w, h, stride, atlas.EastWestPeriodic);
        bool[] isPass = ClassifyPasses(
            heights, submerged, ruggedness, w, h, atlas.EastWestPeriodic, out int saddles);

        return new Landform(
            w, h, stride, atlas.Bounds, atlas.EastWestPeriodic, ruggedness, isPass)
        {
            SaddleCount = saddles,
        };
    }

    /// <summary>Mean grade against the neighbours, normalised to [0, 1].</summary>
    /// <remarks>
    /// The mean rather than the steepest single step: one cliff among eight gentle neighbours is a
    /// feature of a place, while eight steep neighbours are a mountain range, and it is the range
    /// this measure exists to recognise. Submerged neighbours are skipped so a coastal shelf does
    /// not read as broken country merely because the sea floor drops away.
    /// </remarks>
    private static double[] ComputeRuggedness(
        double[] heights, bool[] submerged, int w, int h, int stride, bool eastWestPeriodic)
    {
        double straight = stride;
        double diagonal = DetMath.Sqrt(2.0) * stride;

        var ruggedness = new double[w * h];

        for (int j = 0; j < h; j++)
        {
            for (int i = 0; i < w; i++)
            {
                int idx = (j * w) + i;
                if (submerged[idx]) continue;

                double gradeSum = 0.0;
                int considered = 0;

                for (int d = 0; d < 8; d++)
                {
                    if (!TryNeighbour(i, j, d, w, h, eastWestPeriodic, out int nIdx)) continue;
                    if (submerged[nIdx]) continue;

                    double distance = (OffsetX[d] != 0 && OffsetZ[d] != 0) ? diagonal : straight;
                    gradeSum += Math.Abs(heights[idx] - heights[nIdx]) / distance;
                    considered++;
                }

                ruggedness[idx] = considered == 0
                    ? 0.0
                    : DetMath.InverseLerp(RollingGrade, BrokenGrade, gradeSum / considered);
            }
        }

        return ruggedness;
    }

    /// <summary>
    /// Saddles in high broken country: the low way across a ridge.
    /// </summary>
    /// <remarks>
    /// <para>A pass is not "somewhere low" or "somewhere high" but a point that is <em>both</em>,
    /// depending on which way you face — lower than the ground either side of the ridge, higher
    /// than the valleys it joins. That is exactly a saddle, and a saddle is recognisable without
    /// any notion of direction: walk the eight neighbours as a closed ring and count how many times
    /// the ground crosses from above the centre to below it. A summit or a hollow crosses zero
    /// times, an ordinary hillside twice, and a saddle four or more.</para>
    ///
    /// <para><b>The col is not the mountain, and this design originally confused the two.</b> The
    /// first version of this asked for a saddle that was itself high and itself steep. Measuring it
    /// showed both conditions to be backwards: across eight seeds a saddle's median height is
    /// 58–344 m against a base land height of 520, and its mean grade is consistently *lower* than
    /// the land around it. Of course it is — a col is the low smooth spot in a ridge, which is
    /// precisely why anyone crosses there. Under the original gates seed 42 found 87 saddles and
    /// called none of them a pass.</para>
    ///
    /// <para>So what is tested is the <em>surroundings</em>: how far the ridge rises either side of
    /// the col, and how broken the country it cuts through is. Both are properties of what the pass
    /// gets you past, which is the only thing that makes a pass worth holding.</para>
    /// </remarks>
    private static bool[] ClassifyPasses(
        double[] heights,
        bool[] submerged,
        double[] ruggedness,
        int w,
        int h,
        bool eastWestPeriodic,
        out int saddles)
    {
        var isPass = new bool[w * h];
        saddles = 0;

        for (int j = 0; j < h; j++)
        {
            for (int i = 0; i < w; i++)
            {
                int idx = (j * w) + i;

                if (submerged[idx]) continue;

                // The ring has to be whole for the crossing count to mean anything, so cells on
                // the north and south edges are never passes rather than being guessed at.
                var above = new bool[8];
                bool complete = true;

                for (int d = 0; d < 8; d++)
                {
                    if (!TryNeighbour(i, j, d, w, h, eastWestPeriodic, out int nIdx)
                        || submerged[nIdx])
                    {
                        complete = false;
                        break;
                    }

                    above[d] = heights[nIdx] > heights[idx];
                }

                if (!complete) continue;

                int crossings = 0;
                for (int d = 0; d < 8; d++)
                {
                    if (above[d] != above[(d + 1) % 8]) crossings++;
                }

                if (crossings < 4) continue;

                saddles++;

                double riseSum = 0.0;
                int rising = 0;
                for (int d = 0; d < 8; d++)
                {
                    if (!above[d]) continue;

                    TryNeighbour(i, j, d, w, h, eastWestPeriodic, out int nIdx);
                    riseSum += heights[nIdx] - heights[idx];
                    rising++;
                }

                if (rising == 0 || riseSum / rising < PassBarrierRise) continue;

                isPass[idx] = SurroundingRuggedness(ruggedness, submerged, i, j, w, h, eastWestPeriodic)
                              >= PassContextRuggedness;
            }
        }

        return isPass;
    }

    /// <summary>Mean ruggedness of the land within <see cref="PassContextRadius"/> cells.</summary>
    private static double SurroundingRuggedness(
        double[] ruggedness, bool[] submerged, int i, int j, int w, int h, bool eastWestPeriodic)
    {
        double sum = 0.0;
        int considered = 0;

        for (int dz = -PassContextRadius; dz <= PassContextRadius; dz++)
        {
            int nj = j + dz;
            if (nj < 0 || nj >= h) continue;

            for (int dx = -PassContextRadius; dx <= PassContextRadius; dx++)
            {
                int ni = i + dx;
                if (eastWestPeriodic)
                {
                    ni = WrapIndex(ni, w);
                }
                else if (ni < 0 || ni >= w)
                {
                    continue;
                }

                int idx = (nj * w) + ni;
                if (submerged[idx]) continue;

                sum += ruggedness[idx];
                considered++;
            }
        }

        return considered == 0 ? 0.0 : sum / considered;
    }

    /// <summary>How broken the country here is, in [0, 1]. Read bilinearly, so it varies everywhere.</summary>
    public double RuggednessAt(int x, int z)
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

        double top = DetMath.Lerp(
            _ruggedness[(r0 * _width) + c0], _ruggedness[(r0 * _width) + c1], tx);
        double bottom = DetMath.Lerp(
            _ruggedness[(r1 * _width) + c0], _ruggedness[(r1 * _width) + c1], tx);
        return DetMath.Lerp(top, bottom, tz);
    }

    /// <summary>Whether this is the low way across a ridge.</summary>
    public bool IsPass(int x, int z)
    {
        int normalizedX = _eastWestPeriodic ? _bounds.WrapX(x) : x;
        int i = Math.Clamp(
            (normalizedX - _bounds.MinX + (_stride / 2)) / _stride, 0, _width - 1);
        int j = Math.Clamp((z - _bounds.MinZ + (_stride / 2)) / _stride, 0, _height - 1);
        return _isPass[(j * _width) + i];
    }

    /// <summary>Total cells classified as passes. Diagnostic.</summary>
    public int PassCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < _isPass.Length; i++)
            {
                if (_isPass[i]) count++;
            }

            return count;
        }
    }

    /// <summary>Saddles found before the height and ruggedness gates. Diagnostic, for calibration.</summary>
    public int SaddleCount { get; private init; }

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

    private int NormalizeColumn(int i) =>
        _eastWestPeriodic ? WrapIndex(i, _width) : Math.Clamp(i, 0, _width - 1);

    private static int WrapIndex(int i, int width)
    {
        int wrapped = i % width;
        return wrapped < 0 ? wrapped + width : wrapped;
    }
}
