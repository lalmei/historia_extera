using HistoryEngine.Core;

namespace HistoryEngine.Terrain;

/// <summary>
/// One greyscale raster plane, normalised to [0, 1].
/// </summary>
/// <remarks>
/// <para><b>Values are points, not cells.</b> A W-wide plane spans the world extent with its
/// first and last columns sitting exactly on the edges, so <c>u = 0</c> reads column 0 and
/// <c>u = 1</c> reads column W-1. The alternative — treating each value as a cell covering a
/// span of world — leaves half a cell of undefined terrain outside the raster along every
/// edge, which is precisely where coastlines and therefore settlements are.
/// <see cref="TerrainAtlas"/> spans its bounds inclusively for the same reason.</para>
///
/// <para><b>Unitless on purpose.</b> A PGM file knows its own maximum value and nothing else;
/// only the manifest knows whether that maximum means 2,400 metres or 32 °C. Keeping the plane
/// normalised means one type serves height, temperature and a water mask alike, and every
/// decision about units lives in <see cref="TerrainRasterSet"/> rather than being spread across
/// a codec, a sampler and a loader.</para>
/// </remarks>
public sealed class RasterGrid
{
    private readonly float[] _values;

    public RasterGrid(int width, int height, float[] values)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be positive.");
        }

        if (values.Length != width * height)
        {
            throw new ArgumentException(
                $"Expected {width * height} values for a {width}x{height} raster, got {values.Length}.",
                nameof(values));
        }

        Width = width;
        Height = height;
        _values = values;
    }

    public int Width { get; }

    public int Height { get; }

    public ReadOnlySpan<float> Values => _values;

    /// <summary>The value at a grid position, clamped to the raster.</summary>
    public float At(int column, int row)
    {
        int c = column < 0 ? 0 : column >= Width ? Width - 1 : column;
        int r = row < 0 ? 0 : row >= Height ? Height - 1 : row;
        return _values[(r * Width) + c];
    }

    /// <summary>
    /// Bilinear interpolation at normalised coordinates.
    /// </summary>
    /// <remarks>
    /// <para><b>This is not the interpolation the sampler contract forbids.</b>
    /// <see cref="ITerrainSampler"/> tells backends to stay dumb — no caching, no interpolation
    /// — and that rule is about not duplicating <see cref="TerrainAtlas"/>, which decides
    /// <em>which</em> points are worth paying for. Reading between the values of a finite
    /// raster is a different job: it is how this backend answers the point it was asked about
    /// at all. Nearest-neighbour instead would quantise every coastline to the raster's stride
    /// and put settlements on a visible grid.</para>
    /// </remarks>
    public float Sample(double u, double v)
    {
        double fx = DetMath.Clamp01(u) * (Width - 1);
        double fz = DetMath.Clamp01(v) * (Height - 1);

        int c0 = (int)fx;
        int r0 = (int)fz;
        if (c0 > Width - 2) c0 = Width > 1 ? Width - 2 : 0;
        if (r0 > Height - 2) r0 = Height > 1 ? Height - 2 : 0;

        double tx = DetMath.Clamp01(fx - c0);
        double tz = DetMath.Clamp01(fz - r0);

        double top = DetMath.Lerp(At(c0, r0), At(c0 + 1, r0), tx);
        double bottom = DetMath.Lerp(At(c0, r0 + 1), At(c0 + 1, r0 + 1), tx);

        return (float)DetMath.Lerp(top, bottom, tz);
    }

    /// <summary>
    /// The nearest value, without interpolation.
    /// </summary>
    /// <remarks>
    /// For planes that do not average meaningfully. Halfway between lake and dry land is not
    /// half a lake, so a water mask reads nearest while height reads bilinear — the same split
    /// <see cref="TerrainAtlas.SampleCoarse"/> makes between its continuous fields and
    /// <see cref="WaterKind"/>.
    /// </remarks>
    public float Nearest(double u, double v)
    {
        double fx = DetMath.Clamp01(u) * (Width - 1);
        double fz = DetMath.Clamp01(v) * (Height - 1);

        return At((int)(fx + 0.5), (int)(fz + 0.5));
    }
}
