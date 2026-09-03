namespace HistoryEngine.Core;

/// <summary>
/// Reproducible transcendentals, built from <c>+ - * /</c> and <c>sqrt</c> only.
/// </summary>
/// <remarks>
/// <para><see cref="DetMath"/> deliberately has none of these: nothing on a decision path needs
/// them, and the runtime's own <c>Math.Sin</c> or <c>Math.Pow</c> may differ by an ULP between
/// platforms, which is enough to fork a history. Cosmology is the exception. A spin axis drawn
/// uniformly over a sphere, a coordinate change into right ascension, and the reflected light of
/// a ring system are all genuinely transcendental; none of them can be written as a polynomial
/// the way a scoring curve can.</para>
///
/// <para>So they are written out here instead, by the same argument
/// <see cref="World.HostGalaxy"/> makes for its private copies: range-reduce, then a truncated
/// series in the four correctly rounded operations. Two runtimes running this code take exactly
/// the same path and get exactly the same bits. Accuracy is roughly 1e-12 relative across the
/// ranges cosmology uses, which is far tighter than the physics being modelled.</para>
/// </remarks>
public static class DetSeries
{
    public const double Pi = 3.14159265358979323846;
    public const double TwoPi = 2.0 * Pi;
    public const double HalfPi = 0.5 * Pi;

    private const double Ln2 = 0.693147180559945309417;
    private const double Ln10 = 2.302585092994045684018;
    private const double QuarterPi = 0.25 * Pi;
    private const double SixthPi = Pi / 6.0;
    private const double Sqrt3 = 1.732050807568877293527;

    /// <summary>e^x. Range-reduced by ln 2, then a 16-term Taylor series on the remainder.</summary>
    public static double Exp(double x)
    {
        if (x > 700.0) return double.PositiveInfinity;
        if (x < -700.0) return 0.0;

        double scaled = x / Ln2;
        int n = (int)(scaled >= 0.0 ? scaled + 0.5 : scaled - 0.5);
        double r = x - (n * Ln2);

        double term = 1.0;
        double sum = 1.0;
        for (int i = 1; i <= 16; i++)
        {
            term *= r / i;
            sum += term;
        }

        return sum * PowerOfTwo(n);
    }

    /// <summary>Natural log. Mantissa reduced to [1, 2), then the atanh series.</summary>
    public static double Log(double x)
    {
        if (x <= 0.0) return x == 0.0 ? double.NegativeInfinity : double.NaN;

        int exponent = 0;
        double m = x;
        while (m >= 2.0)
        {
            m *= 0.5;
            exponent++;
        }

        while (m < 1.0)
        {
            m *= 2.0;
            exponent--;
        }

        double s = (m - 1.0) / (m + 1.0);
        double s2 = s * s;
        double power = s;
        double sum = 0.0;
        for (int i = 0; i < 20; i++)
        {
            sum += power / ((2 * i) + 1);
            power *= s2;
        }

        return (2.0 * sum) + (exponent * Ln2);
    }

    public static double Log10(double x) => Log(x) / Ln10;

    /// <summary>x^y for positive x. Zero to any positive power is zero.</summary>
    public static double Pow(double x, double y)
    {
        if (y == 0.0) return 1.0;
        if (x <= 0.0) return 0.0;
        return Exp(y * Log(x));
    }

    /// <summary>Cube root, seeded from the series and finished with Newton steps.</summary>
    public static double Cbrt(double value)
    {
        if (value == 0.0) return 0.0;

        double magnitude = value < 0.0 ? -value : value;
        double guess = Exp(Log(magnitude) / 3.0);
        for (int i = 0; i < 4; i++)
        {
            guess = ((2.0 * guess) + (magnitude / (guess * guess))) / 3.0;
        }

        return value < 0.0 ? -guess : guess;
    }

    /// <summary>sin x. Reduced onto [-π, π], then folded into [-π/4, π/4].</summary>
    public static double Sin(double radians) => CosSin(radians).Sin;

    /// <summary>cos x, sharing the reduction with <see cref="Sin"/>.</summary>
    public static double Cos(double radians) => CosSin(radians).Cos;

    public static double Tan(double radians)
    {
        (double cos, double sin) = CosSin(radians);
        return cos == 0.0 ? double.PositiveInfinity : sin / cos;
    }

    /// <summary>arcsin, via <see cref="Atan"/>. Inputs are clamped to [-1, 1].</summary>
    public static double Asin(double value)
    {
        double x = DetMath.Clamp(value, -1.0, 1.0);
        if (x == 1.0) return HalfPi;
        if (x == -1.0) return -HalfPi;
        return Atan(x / DetMath.Sqrt(1.0 - (x * x)));
    }

    public static double Acos(double value) => HalfPi - Asin(value);

    /// <summary>
    /// arctan. Reduced to |x| ≤ tan 15° with the reciprocal and the 30° addition identities,
    /// where the alternating series converges in a dozen terms.
    /// </summary>
    public static double Atan(double value)
    {
        bool negative = value < 0.0;
        double x = negative ? -value : value;

        bool reciprocal = x > 1.0;
        if (reciprocal) x = 1.0 / x;

        bool shifted = x > 0.267949192431122706;
        if (shifted) x = ((x * Sqrt3) - 1.0) / (x + Sqrt3);

        double x2 = x * x;
        double power = x;
        double sum = 0.0;
        for (int i = 0; i < 16; i++)
        {
            double term = power / ((2 * i) + 1);
            sum = (i & 1) == 0 ? sum + term : sum - term;
            power *= x2;
        }

        if (shifted) sum += SixthPi;
        if (reciprocal) sum = HalfPi - sum;
        return negative ? -sum : sum;
    }

    /// <summary>Quadrant-aware arctan of y/x, in (-π, π].</summary>
    public static double Atan2(double y, double x)
    {
        if (x > 0.0) return Atan(y / x);
        if (x < 0.0) return y >= 0.0 ? Atan(y / x) + Pi : Atan(y / x) - Pi;
        if (y > 0.0) return HalfPi;
        if (y < 0.0) return -HalfPi;
        return 0.0;
    }

    /// <summary>An angle wrapped into [0, 2π).</summary>
    public static double WrapRadians(double radians)
    {
        double wrapped = radians - (TwoPi * Math.Floor(radians / TwoPi));
        return wrapped < 0.0 ? wrapped + TwoPi : wrapped;
    }

    /// <summary>An angle wrapped into [0, 360).</summary>
    public static double WrapDegrees(double degrees)
    {
        double wrapped = degrees - (360.0 * Math.Floor(degrees / 360.0));
        return wrapped < 0.0 ? wrapped + 360.0 : wrapped;
    }

    public static double ToRadians(double degrees) => degrees * (Pi / 180.0);

    public static double ToDegrees(double radians) => radians * (180.0 / Pi);

    /// <summary>
    /// Approximates N(0, 1) as twelve unit draws minus six — the Irwin–Hall construction, which
    /// needs no log and no cosine and so stays reproducible.
    /// </summary>
    public static double Gaussian(IRng rng)
    {
        ArgumentNullException.ThrowIfNull(rng);

        double sum = 0.0;
        for (int i = 0; i < 12; i++)
        {
            sum += rng.NextDouble();
        }

        return sum - 6.0;
    }

    /// <summary>A draw from N(mean, deviation), built on <see cref="Gaussian(IRng)"/>.</summary>
    public static double Gaussian(IRng rng, double mean, double deviation) =>
        mean + (Gaussian(rng) * deviation);

    /// <summary>Uniform in the logarithm between two positive bounds.</summary>
    public static double LogUniform(IRng rng, double min, double max)
    {
        ArgumentNullException.ThrowIfNull(rng);
        return Exp(Log(min) + (rng.NextDouble() * (Log(max) - Log(min))));
    }

    private static (double Cos, double Sin) CosSin(double radians)
    {
        double reduced = radians - (TwoPi * Math.Floor((radians + Pi) / TwoPi));

        // Sine is odd and cosine even, so the sign comes off first and the fold below only ever
        // has to handle a first-quadrant angle.
        double sinSign = 1.0;
        if (reduced < 0.0)
        {
            reduced = -reduced;
            sinSign = -1.0;
        }

        // [0, π] onto [0, π/2], where cosine changes sign and sine does not.
        bool negateCos = false;
        if (reduced > HalfPi)
        {
            reduced = Pi - reduced;
            negateCos = true;
        }

        // And [0, π/2] onto [0, π/4], where both series converge fastest, by swapping their roles.
        bool swapped = false;
        if (reduced > QuarterPi)
        {
            reduced = HalfPi - reduced;
            swapped = true;
        }

        double sin = SinSeries(reduced);
        double cos = CosSeries(reduced);
        if (swapped) (sin, cos) = (cos, sin);
        if (negateCos) cos = -cos;
        return (cos, sin * sinSign);
    }

    /// <summary>Taylor sine, accurate to machine precision on [-π/4, π/4].</summary>
    private static double SinSeries(double x)
    {
        double x2 = x * x;
        double term = x;
        double sum = x;
        for (int i = 1; i <= 8; i++)
        {
            term *= -x2 / ((2 * i) * ((2 * i) + 1));
            sum += term;
        }

        return sum;
    }

    /// <summary>Taylor cosine over the same interval.</summary>
    private static double CosSeries(double x)
    {
        double x2 = x * x;
        double term = 1.0;
        double sum = 1.0;
        for (int i = 1; i <= 8; i++)
        {
            term *= -x2 / (((2 * i) - 1) * (2 * i));
            sum += term;
        }

        return sum;
    }

    private static double PowerOfTwo(int n)
    {
        if (n == 0) return 1.0;
        return n > 0 ? DetMath.IntPow(2.0, n) : 1.0 / DetMath.IntPow(2.0, -n);
    }
}
